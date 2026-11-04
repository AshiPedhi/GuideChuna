using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

/// <summary>
/// 런타임에 만드는 손가락 누름 버튼(사각판 + 글) — 환자 옆 안내판 · 옆 영상 화면이 같이 쓴다(2026-11-03).
/// 좌표는 <b>cm 공간</b>(스케일 0.01인 부모) 기준이고 사용자 쪽이 −Z다.
/// 손가락 끝(ISDK Hand · <see cref="RomRecordHands"/>)이 판 앞 <see cref="ArmFrontCm"/> 안에서 다가와 판을 지나면 한 번 눌린다.
/// ★컨트롤러 레이는 받지 않는다 — 런타임 생성이라 ISDK 포인터 캔버스를 못 붙인다(안내판 버튼과 같은 제약).
/// </summary>
public sealed class SidePokeButton
{
    const float ArmFrontCm = 8f, FireDepthCm = 0.5f, PressCooldown = 0.6f;

    public readonly Transform Quad;
    public readonly TextMeshPro Label;
    public float W { get; private set; }
    public float H { get; private set; }
    private readonly Material mat;
    private Transform icon;
    private Material iconMat;
    private Color idle, hover;
    private bool armed, lastHover;
    private float lastPress = -10f;

    public SidePokeButton(string name, Transform cmSpace, TMP_FontAsset font, float w, float h, float fontSize, Color idle, Color hover, int queue)
    {
        this.idle = idle; this.hover = hover; W = w; H = h;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name; q.hideFlags = HideFlags.DontSave;
        Object.Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(cmSpace, false);
        q.transform.localScale = new Vector3(w, h, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        Shader sh = Shader.Find("Sprites/Default"); if (sh == null) sh = Shader.Find("Standard");
        mat = new Material(sh) { color = idle, renderQueue = queue };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        Quad = q.transform;

        var t = new GameObject(name + " 글") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(cmSpace, false);
        Label = t.AddComponent<TextMeshPro>();
        if (font != null) Label.font = font;
        Label.fontSize = fontSize;
        Label.alignment = TextAlignmentOptions.Center;
        Label.textWrappingMode = TextWrappingModes.NoWrap;
        Label.rectTransform.sizeDelta = new Vector2(w, h);
        Label.raycastTarget = false;
        Label.fontStyle = FontStyles.Bold;
    }

    public bool Visible => Quad.gameObject.activeSelf;

    /// <summary>글 대신 아이콘(흰색 PNG · <c>Resources/Icons/Media</c>). 판 크기의 <paramref name="frac"/>만큼 가운데에.</summary>
    public void SetIcon(Texture2D tex, float frac = 0.7f)
    {
        if (tex == null) { Debug.LogWarning($"[누름버튼] 아이콘 없음 — {Quad.name}"); return; }
        if (icon == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = Quad.name + " 아이콘"; q.hideFlags = HideFlags.DontSave;
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(Quad.parent, false);
            var mr = q.GetComponent<MeshRenderer>();
            iconMat = new Material(mat.shader) { color = Color.white, renderQueue = mat.renderQueue + 1 };
            mr.sharedMaterial = iconMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            icon = q.transform;
            icon.gameObject.SetActive(Quad.gameObject.activeSelf);
        }
        if (iconMat.mainTexture != tex) iconMat.mainTexture = tex;
        float s = Mathf.Min(W, H) * frac;
        icon.localScale = new Vector3(s, s, 1f);
        icon.localPosition = new Vector3(Quad.localPosition.x, Quad.localPosition.y, 0.2f);
        Label.text = "";
    }

    /// <summary>
    /// 둥근 알약 모양(11-04 사용자 «다음 버튼 디자인도 크기도 우리 규격에 안 맞고 거슬려»). 상단 메뉴 · 팝업과 같은 꼴 — 양 끝이 반원.
    /// 판 비율(W:H)에 맞춘 흰 알약 마스크를 한 번 만들어 색(idle · hover)과 곱한다.
    /// </summary>
    public void MakePill()
    {
        mat.mainTexture = PillTexture(W / Mathf.Max(0.01f, H));
    }

    private static Texture2D PillTexture(float aspect)
    {
        int h = 64, w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), h, 512);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        float r = h * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = r;
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);   // 가장자리 1px 부드럽게
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    public void SetVisible(bool on)
    {
        if (Quad.gameObject.activeSelf != on) Quad.gameObject.SetActive(on);
        if (Label.gameObject.activeSelf != on) Label.gameObject.SetActive(on);
        if (icon != null && icon.gameObject.activeSelf != on) icon.gameObject.SetActive(on);
        if (!on) { armed = false; if (lastHover) { mat.color = idle; lastHover = false; } }
    }

    public void SetText(string s) { if (Label.text != s) Label.text = s; }

    public void SetIdleColor(Color c) { idle = c; if (!lastHover) mat.color = c; }

    public void Place(float x, float y)
    {
        Quad.localPosition = new Vector3(x, y, 0.3f);
        Label.rectTransform.localPosition = new Vector3(x, y, 0f);
        if (icon != null) icon.localPosition = new Vector3(x, y, 0.2f);
    }

    /// <summary>눌렸으면 true. <paramref name="fx"/> = 누른 자리의 가로 위치(0 = 왼끝 · 1 = 오른끝) — 재생 막대용.</summary>
    public bool Poll(Transform cmSpace, RomRecordHands hands, out float fx)
    {
        fx = 0f;
        if (!Visible) return false;
        bool hov = false, fire = false;
        Vector3 c = Quad.localPosition;
        for (int h = 0; h < 2; h++)
        {
            if (!hands.TryJoint(h == 0, HandJointId.HandIndexTip, out Vector3 tip)) continue;
            Vector3 l = cmSpace.InverseTransformPoint(tip);
            if (Mathf.Abs(l.x - c.x) > W * 0.5f + 1f || Mathf.Abs(l.y - c.y) > H * 0.5f + 1f) continue;
            float z = l.z - c.z;
            if (z < -ArmFrontCm * 1.5f) continue;
            hov = true;
            if (z < -1.5f && z > -ArmFrontCm) armed = true;            // 앞에서 다가왔다
            else if (armed && z > -FireDepthCm) { fire = true; fx = Mathf.Clamp01((l.x - c.x) / W + 0.5f); }   // 판을 눌렀다
        }
        if (!hov) armed = false;
        if (hov != lastHover) { mat.color = hov ? hover : idle; lastHover = hov; }
        if (fire && Time.time - lastPress > PressCooldown) { lastPress = Time.time; armed = false; return true; }
        return false;
    }
}
