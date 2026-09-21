using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>한 칸의 버튼(또는 글자). id가 null이면 누를 수 없는 글자다.</summary>
public struct RomMenuItem
{
    public string id;
    public string label;
    public bool repeat;       // 누르고 있으면 반복
    public Color tint;
    public float col, row;    // 판 안의 칸 좌표(왼쪽 위가 0,0 · 0행은 머리줄)
    public float span;        // 가로 칸 수
    public bool selected;     // 고른 상태(대상 선택 등) — 밝게 칠한다
    // ★길게 눌러야 먹는 버튼(2026-09-21 사용자 지시). 0이면 닿는 즉시 실행된다.
    //   09-21 증상: "양손으로 환자를 지탱하다가 [나가기]가 눌린다" — 스쳐 지나간 손이 곧바로 실행시켰다.
    public float holdSeconds;

    public static RomMenuItem Button(string id, string label, float col, float row, float span, Color tint,
                                     bool repeat = false, bool selected = false, float holdSeconds = 0f)
        => new RomMenuItem { id = id, label = label, col = col, row = row, span = span, tint = tint, repeat = repeat, selected = selected, holdSeconds = holdSeconds };

    public static RomMenuItem Text(string label, float col, float row, float span)
        => new RomMenuItem { id = null, label = label, col = col, row = row, span = span };
}

/// <summary>
/// 실측 기록의 손목 메뉴 — <b>양 손목에 같은 판</b>이 뜨고 <b>반대 손 검지</b>로 누른다(2026-09-18 사용자 안).
///
/// ★2판(09-18 사용자 결정 "그룹 판넬"): 어두운 판 위에 머리줄(단계·진행·[나가기]) / 조작 묶음 / 이동 줄.
///   버튼은 글자가 안에 들어간 납작한 사각형이고, 칸 좌표로 배치한다.
/// ★가림 대응(09-18 사용자 결정 "다가오면 제자리 고정"): 누르는 손이 다가오면 판이 손목을 따라가지 않고 멈춘다.
///   누르는 손이 메뉴 쪽 손목을 가려 그 손목 추적이 끊기면, 종전엔 판이 숨었다가 튀었다("UI가 도망간다").
///   추적이 잠깐 끊겨도 <see cref="grace"/>초는 숨기지 않는다.
/// ★눌림 표시(09-18 사용자: "누른 건지 구분이 안 간다"): 손가락이 들어오면 밝아지고, 눌리는 순간 움찔 줄며 번쩍인다.
///   소리는 본체(<see cref="RomRecordSession"/>)가 낸다.
/// ★매 프레임 하는 일은 판 루트 이동과 버튼 색·크기뿐이다. 배치·글자는 단계가 바뀔 때만 다시 쓴다.
/// </summary>
public class RomRecordWristMenu : IRomRecordMenu
{
    private class Btn
    {
        public Transform quad;
        public Renderer rend;
        public TextMeshPro label;
        public string id;
        public bool repeat;
        public bool inside;
        public float nextRepeat;
        public float pressedAt = -99f;
        public Color baseColor;
        public Vector2 half;      // 판 로컬 반폭·반높이
        public Vector3 local;     // 판 로컬 중심
        public Vector3 scale;
        // ★길게 누르기(09-21). holdSeconds가 0보다 크면 닿아 있는 시간이 그만큼 쌓여야 실행된다.
        public float holdSeconds;
        public float holdStart;   // 닿기 시작한 시각
        public bool holdFired;    // 이번 접촉에서 이미 실행했다 — 손이 나갈 때까지 다시 안 쏜다
        public Transform fill;    // 차오르는 막대(hold 버튼에만 보인다)
        public Renderer fillRend;
    }

    private readonly List<Btn> pool = new List<Btn>();
    private int activeCount;
    private Transform root, plate;
    private TextMeshPro header;
    private float cooldownUntil;
    private float panelW, panelH;
    private bool placedOnce;
    private float lastValidTime = -99f;
    private string displayName;

    // 모양
    public float cellW = 0.036f;       // 칸 너비(m)
    public float rowH = 0.03f;         // 줄 높이(m)
    public float buttonH = 0.022f;     // 버튼 높이(m)
    public float labelSize = 0.026f;   // ★TMP 폰트 크기(스케일 1). 첫 판 0.9는 버튼을 통째로 가렸다
    public float headerSize = 0.026f;
    public int columns = 6;
    // 자리
    public float lift = 0.06f;
    public float towardEye = 0.02f;
    public float followSharpness = 14f;   // 손목을 따라가는 부드러움(클수록 빠름)
    public float grace = 1.5f;             // 손목 추적이 끊겨도 이만큼은 판을 그대로 둔다(초)
    // 누르기
    public float pressDepth = 0.02f;       // 판 앞뒤로 이 안에 들어오면 누른 것
    public float pressMargin = 0.003f;
    public float holdDelay = 0.5f;
    public float repeatInterval = 0.12f;
    public float cooldown = 0.35f;
    public float pressAnim = 0.18f;        // 눌림 애니메이션 길이(초)

    public bool Visible => root != null && root.gameObject.activeSelf;
    public bool LastRepeat { get; private set; }   // 방금 눌린 것이 반복 입력인가 — 소리를 가볍게 낸다

    private static readonly Color PlateColor = new Color(0.06f, 0.08f, 0.12f, 0.8f);

    public void Build(Transform parent, string name, int capacity, TMP_FontAsset font, Material mat)
    {
        displayName = name;
        root = new GameObject("[실측기록] " + name).transform;
        root.SetParent(parent, false);

        plate = MakeQuad("판", mat, PlateColor, 0);
        plate.localPosition = new Vector3(0f, 0f, 0.002f);   // 버튼보다 조금 뒤(눈에서 먼 쪽)

        header = MakeLabel(font, headerSize, TextAlignmentOptions.MidlineLeft, 3);

        for (int i = 0; i < capacity; i++)
        {
            Transform q = MakeQuad("버튼", mat, Color.gray, 1);
            Transform fl = MakeQuad("차오름", mat, Color.white, 2);   // ★버튼과 글자 사이(1 < 2 < 3)
            fl.gameObject.SetActive(false);
            pool.Add(new Btn
            {
                quad = q,
                rend = q.GetComponent<Renderer>(),
                fill = fl,
                fillRend = fl.GetComponent<Renderer>(),
                label = MakeLabel(font, labelSize, TextAlignmentOptions.Center, 3),
            });
        }
        root.gameObject.SetActive(false);
    }

    /// <summary>단계가 바뀔 때(또는 고른 상태가 바뀔 때) 부른다. 칸 좌표로 판을 다시 짠다.</summary>
    public void SetLayout(string headerText, RomMenuItem[] items)
    {
        cooldownUntil = Time.unscaledTime + cooldown;   // ★방금 누른 손가락이 새 버튼 위에 남아 곧바로 눌리지 않게
        activeCount = Mathf.Min(items.Length, pool.Count);

        float maxRow = 0f;
        for (int i = 0; i < activeCount; i++) maxRow = Mathf.Max(maxRow, items[i].row);
        panelW = columns * cellW + 0.01f;
        panelH = (maxRow + 1f) * rowH + 0.01f;
        plate.localScale = new Vector3(panelW, panelH, 1f);

        header.text = headerText;
        header.rectTransform.sizeDelta = new Vector2(panelW - 0.012f, rowH);
        header.transform.localPosition = new Vector3(0f, panelH * 0.5f - 0.005f - rowH * 0.5f, -0.002f);

        for (int i = 0; i < pool.Count; i++)
        {
            Btn b = pool[i];
            bool on = i < activeCount;
            b.label.gameObject.SetActive(on);
            if (!on) { b.quad.gameObject.SetActive(false); b.fill.gameObject.SetActive(false); b.id = null; continue; }

            RomMenuItem it = items[i];
            bool isButton = it.id != null;
            b.quad.gameObject.SetActive(isButton);
            b.fill.gameObject.SetActive(false);
            b.id = it.id;
            b.repeat = it.repeat;
            b.holdSeconds = it.holdSeconds;
            b.holdStart = -99f;
            b.holdFired = false;
            b.inside = false;
            b.pressedAt = -99f;
            b.label.text = it.label;

            float x = -panelW * 0.5f + 0.005f + (it.col + it.span * 0.5f) * cellW;
            float y = panelH * 0.5f - 0.005f - (it.row + 0.5f) * rowH;
            float w = it.span * cellW - 0.004f;
            if (isButton)
            {
                // 글자가 칸보다 넓으면 버튼을 넓힌다(옆과 겹칠 수 있어 로그로 알린다)
                float need = b.label.GetPreferredValues(it.label).x + 0.006f;
                if (need > w)
                {
                    Debug.Log($"[실측기록] {displayName} — '{it.label}' 글자가 칸보다 넓어 버튼을 {w * 100f:F1}→{need * 100f:F1}cm로 늘렸다");
                    w = need;
                }
                b.baseColor = it.selected ? Color.Lerp(it.tint, Color.white, 0.35f) : it.tint * new Color(0.6f, 0.6f, 0.6f, 1f);
                b.scale = new Vector3(w, buttonH, 1f);
                b.quad.localPosition = new Vector3(x, y, 0f);
                b.quad.localScale = b.scale;
                b.rend.material.color = b.baseColor;
                b.label.color = Color.white;
                b.label.fontStyle = it.selected ? FontStyles.Bold : FontStyles.Normal;
            }
            else
            {
                b.label.color = new Color(0.75f, 0.8f, 0.85f, 1f);
                b.label.fontStyle = FontStyles.Normal;
            }
            b.local = new Vector3(x, y, 0f);
            b.half = new Vector2(w * 0.5f, buttonH * 0.5f);
            b.label.rectTransform.sizeDelta = new Vector2(Mathf.Max(w, 0.02f), buttonH);
            b.label.transform.localPosition = new Vector3(x, y, -0.001f);   // 버튼보다 조금 앞(눈 쪽)
        }
    }

    public void SetHeader(string text)
    {
        if (header != null && header.text != text) header.text = text;
    }

    /// <summary>
    /// 판을 손목 위에 둔다. <paramref name="hold"/>가 참이면(누르는 손이 다가옴) <b>그 자리에 멈춘다</b>.
    /// 손목 추적이 끊겨도 grace초 동안은 숨기지 않는다.
    /// </summary>
    public void Follow(bool wristValid, Vector3 wrist, Transform eye, bool hold)
    {
        if (root == null || eye == null) return;
        float now = Time.unscaledTime;
        if (wristValid) lastValidTime = now;

        bool show = wristValid || (placedOnce && (hold || now - lastValidTime < grace));
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) { placedOnce = false; return; }

        // ★멈춘다 — 가림으로 튀는 손목을 따라가지 않는다. 단 한 번도 자리를 안 잡았으면 먼저 잡는다.
        if (!wristValid || (hold && placedOnce)) return;

        Vector3 toEye = eye.position - wrist;
        Vector3 anchor = wrist + Vector3.up * lift + toEye.normalized * towardEye;
        Vector3 target = anchor + Vector3.up * (panelH * 0.5f);
        Quaternion rot = Quaternion.LookRotation(target - eye.position, Vector3.up);

        if (!placedOnce)
        {
            root.SetPositionAndRotation(target, rot);
            placedOnce = true;
            return;
        }
        float k = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        root.SetPositionAndRotation(Vector3.Lerp(root.position, target, k), Quaternion.Slerp(root.rotation, rot, k));
    }

    /// <summary>눌림·손가락 들어옴 표시. 매 프레임 부른다 — 할당은 없다.</summary>
    public void Tick()
    {
        if (!Visible) return;
        float now = Time.unscaledTime;
        for (int i = 0; i < activeCount; i++)
        {
            Btn b = pool[i];
            if (b.id == null) continue;
            float t = (now - b.pressedAt) / pressAnim;                 // 0 → 1
            float pulse = t < 1f ? 1f - t : 0f;
            Color c = b.inside ? Color.Lerp(b.baseColor, Color.white, 0.3f) : b.baseColor;
            c = Color.Lerp(c, Color.white, pulse * 0.8f);              // 눌린 순간 번쩍
            b.rend.material.color = c;
            b.quad.localScale = b.scale * (1f - 0.18f * pulse);         // 눌린 순간 움찔

            // ★길게 누르는 버튼 — 닿아 있는 동안 막대가 왼쪽에서 차오른다. 얼마나 더 있어야 하는지 눈에 보인다.
            if (b.holdSeconds <= 0f) continue;
            float fillP = b.inside && b.holdStart > 0f ? Mathf.Clamp01((now - b.holdStart) / b.holdSeconds) : 0f;
            bool showFill = fillP > 0.002f;
            if (b.fill.gameObject.activeSelf != showFill) b.fill.gameObject.SetActive(showFill);
            if (!showFill) continue;
            float fw = b.scale.x * fillP;
            b.fill.localScale = new Vector3(fw, b.scale.y * 0.82f, 1f);
            b.fill.localPosition = new Vector3(b.local.x - b.scale.x * 0.5f + fw * 0.5f, b.local.y, -0.0005f);
            b.fillRend.material.color = Color.Lerp(b.baseColor, Color.white, 0.55f);
        }
    }

    /// <summary>판 중심까지 거리 — 누르는 손이 다가오는지 보는 데 쓴다.</summary>
    public float Distance(Vector3 p) => Visible ? Vector3.Distance(root.position, p) : float.MaxValue;

    /// <summary>이 자리가 판 근처인가 — 핀치를 막는 데 쓴다.</summary>
    public bool Near(Vector3 tip, float margin)
    {
        if (!Visible) return false;
        Vector3 l = root.InverseTransformPoint(tip);
        return Mathf.Abs(l.x) < panelW * 0.5f + margin && Mathf.Abs(l.y) < panelH * 0.5f + margin && Mathf.Abs(l.z) < margin;
    }

    /// <summary>반대 손 검지로 눌린 버튼 id. 없으면 null.</summary>
    public string Poll(Vector3 tip, bool tipValid)
    {
        if (!Visible) return null;
        float now = Time.unscaledTime;
        Vector3 l = tipValid ? root.InverseTransformPoint(tip) : Vector3.zero;

        for (int i = 0; i < activeCount; i++)
        {
            Btn b = pool[i];
            if (b.id == null) continue;
            float dx = Mathf.Abs(l.x - b.local.x), dy = Mathf.Abs(l.y - b.local.y), dz = Mathf.Abs(l.z);
            bool inNow = tipValid && dx < b.half.x + pressMargin && dy < b.half.y + pressMargin && dz < pressDepth;
            // ★나갈 때는 조금 더 벗어나야 나간 것으로 본다 — 경계에서 떨려 연타되지 않게.
            bool outNow = !tipValid || dx > b.half.x + pressMargin * 3f || dy > b.half.y + pressMargin * 3f || dz > pressDepth * 1.5f;

            if (!b.inside)
            {
                if (!inNow || now < cooldownUntil) continue;
                b.inside = true;
                b.holdStart = now;
                b.holdFired = false;
                b.nextRepeat = now + holdDelay;
                if (b.holdSeconds > 0f) continue;   // ★길게 누르는 버튼은 닿는 것만으로는 안 먹는다
                b.pressedAt = now;
                if (!b.repeat) cooldownUntil = now + cooldown;
                LastRepeat = false;
                return b.id;
            }
            if (outNow) { b.inside = false; b.holdStart = -99f; continue; }
            if (b.holdSeconds > 0f)
            {
                // 닿아 있는 동안만 쌓인다 — 손이 나가면 위에서 holdStart가 풀려 처음부터 다시다.
                if (b.holdFired || now - b.holdStart < b.holdSeconds) continue;
                b.holdFired = true;
                b.pressedAt = now;
                cooldownUntil = now + cooldown;
                LastRepeat = false;
                return b.id;
            }
            if (b.repeat && now >= b.nextRepeat)
            {
                b.nextRepeat = now + repeatInterval;
                b.pressedAt = now;
                LastRepeat = true;
                return b.id;
            }
        }
        return null;
    }

    // ── 만드는 도구 ──────────────────────────────────────────────────
    private Transform MakeQuad(string name, Material mat, Color c, int order)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());   // 물리는 안 쓴다 — 판 로컬 좌표로 판정한다
        go.transform.SetParent(root, false);
        var r = go.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat;       // Sprites/Default — 양면(컬링 없음)·투명
        r.material.color = c;
        r.sortingOrder = order;                        // ★같은 투명 큐라 그리는 순서를 정해 준다: 판 0 · 버튼 1 · 글자 3
        return go.transform;
    }

    private TextMeshPro MakeLabel(TMP_FontAsset font, float size, TextAlignmentOptions align, int order)
    {
        var go = new GameObject("글자");
        go.transform.SetParent(root, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(0.2f, size * 2f);
        t.sortingOrder = order;
        return t;
    }
}
