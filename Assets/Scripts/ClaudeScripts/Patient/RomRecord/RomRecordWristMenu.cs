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

    // ── 탭 판(2026-09-22 사용자 지시 "다음·이전 누르는 게 번거롭다 · 텍스트뿐이라 직관적이지 않다") ──
    // ★전부 0/null이면 종전 항목과 똑같이 그려진다 — 기존 배치는 손대지 않아도 된다.
    public float rows;          // 세로 줄 수(0이면 1). 탭은 그림이 들어가 두 줄 가까이 쓴다
    public bool locked;         // 보이되 못 누른다(대추를 찍기 전의 동작 탭)
    public Texture2D icon;      // 버튼 위쪽에 그림, 글자는 아래로 내린다
    public string key;          // 글자 칸을 나중에 SetText로 바꿀 때 찾는 이름
    public float textScale;     // 글자 크기 배수(0이면 1)
    public bool alignLeft;      // 글자 칸을 왼쪽 정렬

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
///
/// ★★3판 모양내기(2026-09-21 사용자 지시 "UI 디자인 진행ROOT나 세팅 팝업 같은 느낌으로",
///   앞선 지적 "UI 너무 조잡해 · 버튼 디자인도 투박하고").
///   <b>이 프로젝트가 실제로 쓰는 UI를 재서 그대로 옮겼다</b>(아래는 전부 실측이다):
///   - 설정 팝업 = 씬 루트 <c>Setting</c>(TrainingScene, 668x304px). 진행Root = <c>UI Group/진행Root/진행/CanvasRoot/Guide</c>
///     (월드 캔버스 1024x310px, 캔버스 스케일 0.0005 → <b>1px = 0.5mm</b>).
///   - 두 판 <b>모두</b> 같은 재질을 쓴다: Meta Interaction SDK의 <c>RoundedBoxGradientUIDark.mat</c>
///     + <c>RoundedBoxUIProperties.borderRadius = 20px</c>, VerticalLayoutGroup 여백 24px·간격 12px.
///     재질 실측: 안쪽 그라데이션 0.153(아래) → 0.255(위), 테두리 2.5px에 0.329(위) → 0.141(아래).
///   - 버튼 = Meta UISet <c>TextTileButton_IconAndLabel_Toggle</c>(모서리 16px, 칸 176x106px, 사이 8px).
///     상태색은 애니메이터 <c>ToggleButton_Dark.controller</c> 실측:
///     보통 0.294 · 손가락 들어옴 0.365 · 눌림 0.435(그리고 크기 0.95배) · 고름 흰색 a=0.698 + 글자 0.153.
///     글자는 흰색 a=0.902, 보조 글자는 흰색 a=0.698.
///   ★<b>둥근 모서리를 스프라이트로는 못 낸다</b> — 저쪽 UI는 스프라이트를 안 쓰고
///     셰이더(<c>Unlit/RoundedBoxWithGradientUI</c>)가 <c>uv0.zw</c>(칸 크기)와 <c>uv1</c>(모서리 반지름)을 읽어서
///     그린다. 그 값은 <c>RoundedBoxUIProperties</c>가 <b>uGUI 메시에</b> 실어 준다(Image 전용이다).
///     그래서 여기서는 <b>둥근 모서리 메시를 직접 만들어</b> 같은 모양을 낸다. 판의 그라데이션도
///     정점 색으로 굽는다(Sprites/Default가 정점 색을 곱해 준다).
/// </summary>
public class RomRecordWristMenu : IRomRecordMenu
{
    private class Btn
    {
        public Transform quad;
        public Renderer rend;
        public Mesh mesh;         // ★버튼마다 제 크기로 구운 둥근 사각형. 단계가 바뀔 때만 다시 굽는다
        public TextMeshPro label;
        public string id;
        public bool repeat;
        public bool inside;
        public bool selected;     // 고른 상태 — 밝히는 방식이 다르다(색이 아니라 알파를 올린다)
        public float nextRepeat;
        public float pressedAt = -99f;
        public Color baseColor;
        public Vector2 half;      // 판 로컬 반폭·반높이
        public Vector3 local;     // 판 로컬 중심
        public Vector3 scale;     // 논리 크기(가로·세로). ★그리기는 메시가 하고, 이 값은 판정·막대에 쓴다
        // ★길게 누르기(09-21). holdSeconds가 0보다 크면 닿아 있는 시간이 그만큼 쌓여야 실행된다.
        public float holdSeconds;
        public float holdStart;   // 닿기 시작한 시각
        public bool holdFired;    // 이번 접촉에서 이미 실행했다 — 손이 나갈 때까지 다시 안 쏜다
        public Transform fill;    // 차오르는 막대(hold 버튼에만 보인다)
        public Renderer fillRend;
        public string key;        // SetText로 찾는 이름(09-22)
        public bool locked;       // 보이되 못 누른다(09-22)
    }

    private readonly List<Btn> pool = new List<Btn>();
    private int activeCount;
    private Transform root, plate, border;
    private Mesh plateMesh, borderMesh;
    private TextMeshPro header;
    private float cooldownUntil;
    private float panelW, panelH;
    private bool placedOnce;
    private float lastValidTime = -99f;
    private string displayName;

    // 모양 — 크기
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
    public float cooldown = 0.5f;          // ★09-21에 0.35에서 늘렸다 — "다다다다 눌린다"는 지적
    public float pressAnim = 0.18f;        // 눌림 애니메이션 길이(초)

    // ── 모양내기(2026-09-21) ─────────────────────────────────────────
    // ★전부 설정 팝업·진행Root 실측에서 가져왔다. 출처는 클래스 주석에 적어 뒀다.
    //   ★이 클래스는 MonoBehaviour가 아니라 <b>인스펙터에 안 뜬다</b>. 본체 RomRecordSession이
    //   덮어쓰는 것은 labelSize·headerSize·cellW·rowH·buttonH 다섯뿐이고, 아래 값들은 코드 기본값이 그대로 산다.
    [Tooltip("판 위쪽 색 — 실측 RoundedBoxGradientUIDark._ColorB")]
    public Color plateTopColor = new Color(0.255f, 0.255f, 0.255f, 1f);
    [Tooltip("판 아래쪽 색 — 실측 _ColorA")]
    public Color plateBottomColor = new Color(0.153f, 0.153f, 0.153f, 1f);
    [Tooltip("테두리 위쪽 색 — 실측 _BorderColorA")]
    public Color borderTopColor = new Color(0.329f, 0.329f, 0.329f, 1f);
    [Tooltip("테두리 아래쪽 색 — 실측 _BorderColorB")]
    public Color borderBottomColor = new Color(0.141f, 0.141f, 0.141f, 1f);
    [Tooltip("테두리 두께(m). 실측은 668px 판에 2.5px(=0.37%)라 손목 판 폭에서는 약 0.5mm다")]
    public float borderWidth = 0.0008f;
    [Tooltip("버튼 바탕색 — 실측 ToggleButton_Dark 'Normal' 0.294 회색")]
    public Color buttonBaseColor = new Color(0.294f, 0.294f, 0.294f, 1f);
    [Tooltip("항목이 들고 온 tint를 바탕색에 섞는 정도. 0이면 저쪽 UI처럼 완전 무채색이 된다")]
    public float buttonTintStrength = 0.5f;
    [Tooltip("손가락이 들어왔을 때 밝히는 양 — 실측 0.365-0.294")]
    public float hoverLift = 0.071f;
    [Tooltip("눌린 순간 밝히는 양 — 실측 0.435-0.294")]
    public float pressLift = 0.141f;
    [Tooltip("눌린 순간 줄어드는 비율 — 실측 ToggleButton_Dark 'SelectedPressed' 0.95배")]
    public float pressShrink = 0.05f;
    [Tooltip("고른 버튼 — 실측 'SelectedSelected' 흰색 a=0.698")]
    public Color selectedColor = new Color(1f, 1f, 1f, 0.698f);
    [Tooltip("고른 버튼의 글자 — 실측 0.153 어두운 회색(판 바탕과 같은 색이다)")]
    public Color selectedLabelColor = new Color(0.153f, 0.153f, 0.153f, 1f);
    [Tooltip("버튼 글자 — 실측 Label 흰색 a=0.902")]
    public Color labelColor = new Color(1f, 1f, 1f, 0.902f);
    [Tooltip("누를 수 없는 설명 글자 — 실측 보조 Label 흰색 a=0.698")]
    public Color captionColor = new Color(1f, 1f, 1f, 0.698f);
    [Tooltip("길게 누르기 막대 색 — ★저쪽 UI에 대응물이 없어 이건 실측이 아니다")]
    public Color fillColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("판 모서리 반지름(m). 실측 20px × 0.5mm/px = 1.0cm")]
    public float plateRadius = 0.010f;
    [Tooltip("버튼 모서리 반지름(m). 실측은 16px/106px = 버튼 높이의 30%다")]
    public float buttonRadius = 0.005f;
    [Tooltip("모서리 한 귀퉁이를 몇 조각으로 나눠 그리나. 4면 충분히 둥글다")]
    public int cornerSegments = 4;
    [Tooltip("판 안쪽 여백(m). 실측 24px/668px = 3.6%")]
    public float padding = 0.006f;
    [Tooltip("버튼과 칸 사이의 틈(m). 실측 8px/176px = 4.5%")]
    public float buttonGap = 0.004f;
    [Tooltip("머리줄 글자를 버튼 글자의 몇 배로 할까 — ★실측 설정 팝업은 20/14=1.43이지만 손목 판은 좁아 1.15로 뒀다(실측 아님)")]
    public float headerScale = 1.15f;

    // ★공간 고정 모드(2026-09-21 사용자 지시 "진행ROOT 패널처럼 따로 분리").
    //   손목을 따라다니면 <b>기록하는 손과 판이 같은 자리</b>에 있어 서로 간섭한다 —
    //   09-21 로그 실측에서 핀치를 막은 주범이 판 자신이었다(판 근처 3,084 · 다가옴 3,708 프레임).
    //   고정 모드에서는 판이 환자 옆 허공에 멈춰 있고, 조작할 때만 손을 뻗는다.
    public bool fixedInSpace;
    public bool FixedInSpace { get => fixedInSpace; set => fixedInSpace = value; }
    public bool Placed => placedOnce;

    public bool Visible => root != null && root.gameObject.activeSelf;
    public bool LastRepeat { get; private set; }   // 방금 눌린 것이 반복 입력인가 — 소리를 가볍게 낸다

    public void Build(Transform parent, string name, int capacity, TMP_FontAsset font, Material mat)
    {
        displayName = name;
        root = new GameObject("[실측기록] " + name).transform;
        root.SetParent(parent, false);

        // ★그리는 순서: 테두리(-1) → 판(0) → 버튼(1) → 차오름(2) → 글자(3).
        //   테두리는 판보다 조금 크게 구워 뒤에 깔아 둔다 — 저쪽 UI의 2.5px 테두리를 이렇게 흉내 낸다.
        borderMesh = new Mesh { name = "판테두리" };
        plateMesh = new Mesh { name = "판" };
        border = MakeRect("판테두리", mat, Color.white, -1, borderMesh);
        border.localPosition = new Vector3(0f, 0f, 0.0025f);
        plate = MakeRect("판", mat, Color.white, 0, plateMesh);
        plate.localPosition = new Vector3(0f, 0f, 0.002f);   // 버튼보다 조금 뒤(눈에서 먼 쪽)

        header = MakeLabel(font, headerSize * headerScale, TextAlignmentOptions.MidlineLeft, 3);
        header.color = labelColor;

        Mesh unit = MakeUnitQuadMesh();   // 차오름 막대는 크기를 매 프레임 바꾸므로 공용 단위 사각형을 쓴다
        for (int i = 0; i < capacity; i++)
        {
            var mesh = new Mesh { name = "버튼" };
            Transform q = MakeRect("버튼", mat, buttonBaseColor, 1, mesh);
            Transform fl = MakeRect("차오름", mat, fillColor, 2, unit);   // ★버튼과 글자 사이(1 < 2 < 3)
            fl.gameObject.SetActive(false);
            pool.Add(new Btn
            {
                quad = q,
                rend = q.GetComponent<Renderer>(),
                mesh = mesh,
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
        // ★칸이 모자라면 뒤쪽 항목이 <b>조용히 잘린다</b> — 09-22에 [정면 ▶]·[정면 다시]가 그렇게 사라졌다.
        if (items.Length > pool.Count)
            Debug.LogWarning($"[실측기록] ★{displayName} — 항목 {items.Length}개인데 칸이 {pool.Count}개다. " +
                             $"뒤쪽 {items.Length - pool.Count}개(첫째 '{items[pool.Count].label}')가 안 보인다 — MenuCapacity를 올린다.");

        // ★판 폭을 <b>내용에 맞춘다</b>(09-21). 종전엔 columns(6칸)로 고정이라 항목이 적어도
        //   판이 안 줄었다 — 사용자가 "빈 공간이 너무 많다"고 한 것이 이것이다.
        // ★09-22 탭 판: 여러 줄짜리 칸이 생겨 높이는 <b>가장 아래 칸의 바닥</b>이 정한다.
        //   ★이 판은 그림(icon)을 그리지 않는다 — 지금 쓰는 판은 Meta 판(씬 useMetaUI=true)이다.
        float maxBottom = 1f, maxCol = 1f;
        for (int i = 0; i < activeCount; i++)
        {
            maxBottom = Mathf.Max(maxBottom, items[i].row + RowsOf(items[i]));
            maxCol = Mathf.Max(maxCol, items[i].col + items[i].span);
        }
        panelW = maxCol * cellW + padding * 2f;
        panelH = maxBottom * rowH + padding * 2f;
        // ★판·테두리는 <b>제 크기로 구운 메시</b>다. 스케일로 늘리면 모서리가 타원이 된다.
        FillRoundedRect(plateMesh, panelW, panelH, plateRadius, cornerSegments, plateBottomColor, plateTopColor);
        FillRoundedRect(borderMesh, panelW + borderWidth * 2f, panelH + borderWidth * 2f,
                        plateRadius + borderWidth, cornerSegments, borderBottomColor, borderTopColor);

        header.text = headerText;
        header.fontSize = headerSize * headerScale;
        header.rectTransform.sizeDelta = new Vector2(Mathf.Max(panelW - padding * 2f, 0.02f), rowH);
        header.transform.localPosition = new Vector3(0f, panelH * 0.5f - padding - rowH * 0.5f, -0.002f);

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
            b.id = it.locked ? null : it.id;   // ★잠긴 칸은 못 누른다 — Poll·Tick이 id null을 건너뛴다
            b.key = it.key;
            b.locked = it.locked;
            b.repeat = it.repeat;
            b.selected = it.selected;
            b.holdSeconds = it.holdSeconds;
            b.holdStart = -99f;
            b.holdFired = false;
            b.inside = false;
            b.pressedAt = -99f;
            b.label.text = it.label;

            float rows = RowsOf(it);
            float x = -panelW * 0.5f + padding + (it.col + it.span * 0.5f) * cellW;
            float y = panelH * 0.5f - padding - (it.row + rows * 0.5f) * rowH;
            float w = it.span * cellW - buttonGap;
            float bh = isButton ? buttonH + (rows - 1f) * rowH : rows * rowH;
            b.label.fontSize = labelSize * (it.textScale > 0f ? it.textScale : 1f);
            b.label.alignment = it.alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            b.label.richText = !isButton;
            if (isButton)
            {
                // 글자가 칸보다 넓으면 버튼을 넓힌다(옆과 겹칠 수 있어 로그로 알린다)
                float need = b.label.GetPreferredValues(it.label).x + 0.006f;
                if (need > w)
                {
                    Debug.Log($"[실측기록] {displayName} — '{it.label}' 글자가 칸보다 넓어 버튼을 {w * 100f:F1}→{need * 100f:F1}cm로 늘렸다");
                    w = need;
                }
                // ★색 실측: 고르지 않은 버튼은 0.294 회색에 항목 색을 섞고, 고른 버튼은 흰색 a=0.698이다.
                b.baseColor = it.selected ? selectedColor : MixTint(it.tint);
                b.scale = new Vector3(w, bh, 1f);
                b.quad.localPosition = new Vector3(x, y, 0f);
                b.quad.localScale = Vector3.one;     // ★크기는 메시가 들고 있다. 스케일은 눌림 연출에만 쓴다
                if (it.locked) b.baseColor.a *= 0.45f;
                FillRoundedRect(b.mesh, w, bh, buttonRadius, cornerSegments, Color.white, Color.white);
                b.rend.material.color = b.baseColor;
                b.label.color = it.selected ? selectedLabelColor : labelColor;
                if (it.locked) b.label.color = new Color(labelColor.r, labelColor.g, labelColor.b, labelColor.a * 0.35f);
                b.label.fontStyle = it.selected ? FontStyles.Bold : FontStyles.Normal;
            }
            else
            {
                b.label.color = it.key != null ? labelColor : captionColor;   // key 칸은 값이라 밝게
                b.label.fontStyle = FontStyles.Normal;
            }
            b.local = new Vector3(x, y, 0f);
            b.half = new Vector2(w * 0.5f, bh * 0.5f);
            b.label.rectTransform.sizeDelta = new Vector2(Mathf.Max(w, 0.02f), bh);
            b.label.transform.localPosition = new Vector3(x, y, -0.001f);   // 버튼보다 조금 앞(눈 쪽)
        }
    }

    public void SetHeader(string text)
    {
        if (header != null && header.text != text) header.text = text;
    }

    public void SetText(string key, string text)
    {
        if (key == null) return;
        for (int i = 0; i < activeCount; i++)
        {
            Btn b = pool[i];
            if (b.key != key) continue;
            if (b.label.text != text) b.label.text = text;
            return;
        }
    }

    private static float RowsOf(RomMenuItem it) => it.rows > 0f ? it.rows : 1f;

    /// <summary>
    /// 판을 손목 위에 둔다. <paramref name="hold"/>가 참이면(누르는 손이 다가옴) <b>그 자리에 멈춘다</b>.
    /// 손목 추적이 끊겨도 grace초 동안은 숨기지 않는다.
    /// </summary>
    /// <summary>
    /// 공간 고정 모드에서 판을 그 자리에 세운다(2026-09-21). 위치는 <b>한 번만</b> 정하고,
    /// 회전만 천천히 눈 쪽으로 돌린다 — 자리가 바뀌면 손이 헛짚는다.
    /// </summary>
    public void PlaceAt(Vector3 pos, Transform eye)
    {
        if (root == null || eye == null) return;
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        root.position = pos;
        root.rotation = Quaternion.LookRotation(pos - eye.position, Vector3.up);
        placedOnce = true;
        lastValidTime = Time.unscaledTime;
    }

    /// <summary>공간 고정 모드의 매 프레임 — 자리는 그대로 두고 회전만 눈을 따라간다.</summary>
    public void FaceEye(Transform eye)
    {
        if (root == null || eye == null || !placedOnce) return;
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        Vector3 away = root.position - eye.position;
        if (away.sqrMagnitude < 1e-4f) return;
        Quaternion rot = Quaternion.LookRotation(away, Vector3.up);
        float k = 1f - Mathf.Exp(-followSharpness * 0.35f * Time.unscaledDeltaTime);
        root.rotation = Quaternion.Slerp(root.rotation, rot, k);
    }

    /// <summary>판을 통째로 옮긴다(잡아 끌기).</summary>
    public void MoveTo(Vector3 pos)
    {
        if (root != null) root.position = pos;
    }

    public Vector3 Position => root != null ? root.position : Vector3.zero;

    public void Follow(bool wristValid, Vector3 wrist, Transform eye, bool hold)
    {
        if (root == null || eye == null) return;
        if (fixedInSpace) { FaceEye(eye); return; }   // ★고정 모드에서는 손목을 따라가지 않는다
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
            // ★실측 그대로: 밝히는 양은 색에 더한다(0.294 → 0.365 → 0.435).
            //   고른 버튼은 이미 흰색이라 더할 데가 없어 <b>알파</b>를 올린다(실측 0.698 → 0.8).
            float up = (b.inside ? hoverLift : 0f) + pressLift * pulse;
            Color c = b.baseColor;
            if (b.selected) c.a = Mathf.Min(1f, c.a + up);
            else { c.r += up; c.g += up; c.b += up; }
            b.rend.material.color = c;
            b.quad.localScale = Vector3.one * (1f - pressShrink * pulse);   // 눌린 순간 움찔(실측 0.95배)

            // ★길게 누르는 버튼 — 닿아 있는 동안 막대가 왼쪽에서 차오른다. 얼마나 더 있어야 하는지 눈에 보인다.
            if (b.holdSeconds <= 0f) continue;
            float fillP = b.inside && b.holdStart > 0f ? Mathf.Clamp01((now - b.holdStart) / b.holdSeconds) : 0f;
            bool showFill = fillP > 0.002f;
            if (b.fill.gameObject.activeSelf != showFill) b.fill.gameObject.SetActive(showFill);
            if (!showFill) continue;
            // ★버튼 모서리가 둥글어졌으니 막대를 그 안쪽으로 물려 둔다 — 안 그러면 귀퉁이 밖으로 삐져나온다.
            float usable = Mathf.Max(b.scale.x - buttonRadius, 0.001f);
            float fw = usable * fillP;
            b.fill.localScale = new Vector3(fw, b.scale.y * 0.7f, 1f);
            b.fill.localPosition = new Vector3(b.local.x - usable * 0.5f + fw * 0.5f, b.local.y, -0.0005f);
            b.fillRend.material.color = fillColor;
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
            // ★★추적이 잠깐 끊긴 것을 «나갔다»로 보지 않는다(09-21) — 손가락 추적이 깜박일 때마다
            //   «나갔다 들어왔다»가 되어 쿨다운이 풀릴 때마다 다시 눌렸다. 오래 끊기면 그때 푼다.
            bool outNow = tipValid
                ? (dx > b.half.x + pressMargin * 3f || dy > b.half.y + pressMargin * 3f || dz > pressDepth * 1.5f)
                : (now - b.pressedAt > 1.5f);

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

    /// <summary>항목 색을 실측 바탕색(0.294 회색)에 섞는다. 알파는 바탕색 쪽을 쓴다.</summary>
    private Color MixTint(Color tint)
    {
        float k = Mathf.Clamp01(buttonTintStrength);
        return new Color(Mathf.Lerp(buttonBaseColor.r, tint.r, k),
                         Mathf.Lerp(buttonBaseColor.g, tint.g, k),
                         Mathf.Lerp(buttonBaseColor.b, tint.b, k),
                         buttonBaseColor.a);
    }

    private Transform MakeRect(string name, Material mat, Color c, int order, Mesh mesh)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (mat != null) r.sharedMaterial = mat;       // Sprites/Default — 양면(컬링 없음)·투명·정점색을 곱한다
        r.material.color = c;
        r.sortingOrder = order;                        // ★같은 투명 큐라 그리는 순서를 정해 준다: 테두리 -1 · 판 0 · 버튼 1 · 차오름 2 · 글자 3
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
        t.color = labelColor;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(0.2f, size * 2f);
        t.sortingOrder = order;
        return t;
    }

    // ── 둥근 사각형 메시 ────────────────────────────────────────────
    // ★저쪽 UI는 셰이더로 모서리를 깎지만 그건 uGUI Image 전용이다(클래스 주석 참고).
    //   여기서는 모서리를 <b>메시로</b> 만든다. 단계가 바뀔 때만 굽고, 매 프레임 경로에서는 굽지 않는다.
    //   리스트는 정적으로 돌려 써서 다시 굽더라도 새로 할당하지 않는다.
    private static readonly List<Vector3> gVtx = new List<Vector3>(128);
    private static readonly List<Color> gCol = new List<Color>(128);
    private static readonly List<Vector2> gUv = new List<Vector2>(128);
    private static readonly List<int> gTri = new List<int>(384);

    /// <summary>가로 w·세로 h·모서리 radius인 둥근 사각형을 굽는다. 색은 아래→위로 정점에 굽는다.</summary>
    private static void FillRoundedRect(Mesh mesh, float w, float h, float radius, int seg, Color bottom, Color top)
    {
        float hw = Mathf.Max(w, 0.0001f) * 0.5f;
        float hh = Mathf.Max(h, 0.0001f) * 0.5f;
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(hw, hh));
        seg = Mathf.Max(1, seg);

        gVtx.Clear(); gCol.Clear(); gUv.Clear(); gTri.Clear();
        gVtx.Add(Vector3.zero);
        gCol.Add(Color.Lerp(bottom, top, 0.5f));
        gUv.Add(new Vector2(0.5f, 0.5f));

        // 오른위 → 왼위 → 왼아래 → 오른아래 순으로 네 귀퉁이를 돈다
        for (int c = 0; c < 4; c++)
        {
            float cx = (c == 0 || c == 3) ? hw - radius : -hw + radius;
            float cy = (c == 0 || c == 1) ? hh - radius : -hh + radius;
            for (int s = 0; s <= seg; s++)
            {
                float a = (c * 90f + 90f * s / seg) * Mathf.Deg2Rad;
                float x = cx + Mathf.Cos(a) * radius;
                float y = cy + Mathf.Sin(a) * radius;
                gVtx.Add(new Vector3(x, y, 0f));
                gCol.Add(Color.Lerp(bottom, top, Mathf.InverseLerp(-hh, hh, y)));
                gUv.Add(new Vector2((x + hw) / (hw * 2f), (y + hh) / (hh * 2f)));
            }
        }

        int n = gVtx.Count - 1;
        for (int i = 0; i < n; i++)
        {
            gTri.Add(0);
            gTri.Add(1 + i);
            gTri.Add(1 + (i + 1) % n);
        }

        mesh.Clear();
        mesh.SetVertices(gVtx);
        mesh.SetColors(gCol);
        mesh.SetUVs(0, gUv);
        mesh.SetTriangles(gTri, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>차오름 막대용 단위 사각형(1x1). 크기는 스케일로 준다 — 매 프레임 바뀌므로 굽지 않는다.</summary>
    private static Mesh MakeUnitQuadMesh()
    {
        var m = new Mesh { name = "단위사각형" };
        m.SetVertices(new List<Vector3>
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
        });
        m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
        m.SetUVs(0, new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
        });
        m.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
        m.RecalculateBounds();
        return m;
    }
}
