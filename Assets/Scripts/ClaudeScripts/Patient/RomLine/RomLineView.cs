using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 표시(2026-10-01 신설). 점·선·각도기, 컨트롤러 끝 이름표, 멀리 세우는 메인 화면과 그 아래의 탭 창(10-12).
/// 계산은 하지 않는다 — <see cref="RomLineSession"/>이 준 값만 그린다.
/// ★글자는 상태가 바뀔 때만 바꾼다(매 프레임 문자열을 만들지 않는다). 매 프레임 하는 일은 자리 옮기기뿐이다.
/// </summary>
public class RomLineView
{
    // ── 크기(m) ─────────────────────────────────────────────────────
    public const float PanelW = 0.80f, PanelH = 0.38f;   // 메인 화면의 «배율 전» 크기. 실제 폭은 PlacePanel이 받는다(기본 2.5m → 배율 3.1)
    // ★10-13 사용자 지시: 공간의 선은 얇게(3mm → 1.5mm), 구슬도 조금 작게(반지름 5mm → 3.5mm).
    internal const float PointRadius = 0.0035f;
    private const float LineWidth = 0.0015f;
    // ★10-13 사용자 지시: 선은 <b>찍은 두 점 사이만</b> 긋는다. 종전에는 두 선의 교점까지 늘이고(끝은 4cm 더) 교점에 각도기 호를 세웠다 —
    //   9/30 방식(«두 직선의 교점이 각도기의 축»)을 공간에 그대로 보이려던 것이다. 호는 뺐다(늘이지 않으면 교점이 선 밖에 뜬다). 값은 그대로다.
    // ★10-14 사용자 지시: 순서 측정(여섯 동작)은 공간에 각 글자를 띄우지 않고 선마다 <b>길이(mm)</b>를 선 위에 띄운다. 자유 측정은 종전 그대로 각 글자다.
    //   (같은 날 선을 폭 있는 띠로 바꿨다가 사용자 지시로 직선으로 되돌렸다.)
    private const float LengthRise = 0.012f;   // 길이 글자를 선 가운데에서 위로 띄우는 거리(자유 측정과 같다)

    // ── 색 ──────────────────────────────────────────────────────────
    //   ★10-12 사용자 지시: 색은 <b>역할</b>마다 하나다 — 중립 · 능동 · 압박. 환자 위의 점 · 선 · 호 · 값 글자, 메인 화면의 값 글자 · 칸 머리글 · 각도기 선,
    //     자유 측정의 선, 모니터 화면(monitor.html의 --neutral · --active · --pressure)이 전부 이 세 값을 쓴다. 다른 주황 계열을 새로 만들지 않는다.
    //     알림 글자(NoteColor)도 주황이 아니다 — 주황은 측정값에만 쓴다.
    public static readonly Color NeutralColor = new Color(0.92f, 0.92f, 0.92f, 1f);     // 중립 #EBEBEB
    public static readonly Color ActiveColor = new Color(1f, 0.42f, 0.02f, 1f);       // 능동 #FF6B05
    public static readonly Color PressureColor = new Color(0.95f, 0.60f, 0.30f, 1f);   // 압박 #F2994D — 종전 공간의 #9E4203은 어두운 화면에서 안 보여 화면만 따로 밝은 색을 썼다. 밝은 쪽으로 합쳤다
    private static readonly Color TipColor = new Color(1f, 0.95f, 0.2f, 1f);
    private static readonly Color TipLostColor = new Color(1f, 0.30f, 0.30f, 0.85f);   // 헤드셋이 컨트롤러를 못 보고 있다 — 자리는 추정값
    private static readonly Color PlateColor = new Color(0.07f, 0.09f, 0.12f, 0.92f);
    private static readonly Color TabOn = new Color(0.30f, 0.52f, 0.85f, 1f);
    private static readonly Color TabOff = new Color(0.16f, 0.20f, 0.26f, 1f);
    private static readonly Color CompareColor = new Color(0.35f, 0.80f, 1f, 1f);
    private static readonly Color CompareNeutral = new Color(0.75f, 0.92f, 1f, 1f);
    private static readonly Color ComparePressure = new Color(0.10f, 0.45f, 0.75f, 1f);

    private Transform root;
    private Material mat;
    /// <summary>Sprites/Default 공용 머티리얼 — 공간 각도기(<see cref="RomLineSpaceGauge"/>)의 눈금 메시가 같이 쓴다.</summary>
    internal Material Mat => mat;
    private TMP_FontAsset font;

    // 판
    private Transform panel;
    private Transform guidedArea, freeArea;   // 순서 측정(안내 두 줄 · 결과표) / 자유 측정(갈래 · 안내 · 선 목록)
    private TextMeshPro footer;
    private readonly TextMeshPro[] compareNames = new TextMeshPro[2];    // 비교 시험 줄 이름: 굴곡 · 신전
    private readonly TextMeshPro[] compareValues = new TextMeshPro[8];   // 줄마다 능동 면·직접, 압박 면·직접
    private readonly TextMeshPro[] values = new TextMeshPro[12];        // 면 위에서 잰 값(동작 × 능동 · 압박). «직접» 값은 10-12부터 판에 내지 않는다(로그 · CSV에만)

    // 환자 위 표시
    private readonly Transform[] spheres = new Transform[RomLineSession.PointCount];
    private readonly Transform[] extraSpheres = new Transform[3];        // 비교 시험의 Gn(중립·능동·압박)
    private readonly TextMeshPro[] neutralLabels = new TextMeshPro[2];   // 기준(양쪽 Tr) 이름표
    private LineRenderer neutralLine, activeLine, pressureLine;            // 순서 측정의 세 선
    private LineRenderer cmpNeutralLine, cmpActiveLine, cmpPressureLine;   // 비교 시험의 Gl ~ Gn 선
    private TextMeshPro neutralText, activeText, pressureText, cmpText;    // 앞의 셋 = 선마다 길이(mm, 10-14 — 종전에는 능동 · 압박의 각이었다)

    // 컨트롤러 끝
    private Transform tip;
    private Renderer tipRenderer;
    private bool tipDrawnTracked = true;
    private TextMeshPro tipLabel;

    public bool PanelPlaced { get; private set; }
    public Transform Panel => panel;
    /// <summary>자유 측정의 화면 글자를 붙일 자리와, 그 선 목록이 시작하는 높이(화면 좌표).</summary>
    public Transform FreeArea => freeArea;
    public float FreeTableTop { get; private set; }

    public void Build(Transform parent, TMP_FontAsset f, Material m)
    {
        // ★11-17 글꼴 = KoPubWorld 돋움(환자 옆 안내판과 같은 묶음 Resources/UI/SideGuideFonts) — 없으면 넘어온 글꼴 하나로
        var kp = SideGuideFonts.Get();
        fontM = kp != null && kp.medium != null ? kp.medium : f;
        fontB = kp != null && kp.bold != null ? kp.bold : fontM;
        fontL = kp != null && kp.light != null ? kp.light : fontM;
        font = fontM;
        if (kp != null && kp.medium != null) Debug.Log($"[선실측] 글꼴 — KoPub(B {fontB?.name} · M {fontM?.name} · L {fontL?.name}) · OS 글자 ×{OsDisplayScale.Font:F2}");
        else Debug.LogWarning($"[선실측] 글꼴 — ★KoPub 묶음(Resources/UI/SideGuideFonts)이 없다 · 기본 '{(f != null ? f.name : "없음")}'으로 그린다");
        mat = m;
        root = new GameObject("선 실측 표시").transform;
        root.SetParent(parent, false);

        // ── 점 ──
        for (int i = 0; i < spheres.Length; i++)
        {
            // 0·1 = 기준(양쪽 Tr) · 그 뒤로 동작마다 중립 2 · 능동 2 · 압박 2
            int phase = i < 2 ? 0 : ((i - 2) / 2) % 3;
            Color c = phase == 0 ? NeutralColor : phase == 1 ? ActiveColor : PressureColor;
            spheres[i] = MakeSphere("점", PointRadius, c);
            spheres[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < extraSpheres.Length; i++)
        {
            extraSpheres[i] = MakeSphere("점(비교 턱끝점)", PointRadius, i == 0 ? NeutralColor : i == 1 ? ActiveColor : PressureColor);
            extraSpheres[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < neutralLabels.Length; i++)
        {
            neutralLabels[i] = MakeText(root, RomLineSession.PointName[i], 0.07f, TextAlignmentOptions.Center, 5);
            neutralLabels[i].gameObject.SetActive(false);
        }

        neutralLine = MakeLine("중립 기준선", NeutralColor, 2);
        activeLine = MakeLine("능동 측정선", ActiveColor, 2);
        pressureLine = MakeLine("압박 측정선", PressureColor, 2);
        // 길이 글자 — 흰 글자 + 검은 외곽선(선의 색과 겹치지 않게 · 자유 측정의 길이 글자와 같은 크기)
        neutralText = Outlined(MakeText(root, "", 0.07f, TextAlignmentOptions.Center, 5));
        activeText = Outlined(MakeText(root, "", 0.07f, TextAlignmentOptions.Center, 5));
        pressureText = Outlined(MakeText(root, "", 0.07f, TextAlignmentOptions.Center, 5));
        HideMeasure();

        // 비교 시험의 Gl ~ Gn 선 — Tr ~ Gl 선과 헷갈리지 않게 하늘색 계열로 그린다
        cmpNeutralLine = MakeLine("비교 중립선", CompareNeutral, 2);
        cmpActiveLine = MakeLine("비교 능동선", CompareColor, 2);
        cmpPressureLine = MakeLine("비교 압박선", ComparePressure, 2);
        cmpText = MakeText(root, "", 0.10f, TextAlignmentOptions.Center, 5);
        cmpText.color = CompareColor;
        HideCompare();

        // ── 컨트롤러 끝 ──
        tip = MakeSphere("컨트롤러 끝", PointRadius * 0.8f, TipColor);
        tipRenderer = tip.GetComponent<Renderer>();
        tipLabel = MakeText(root, "", 0.08f, TextAlignmentOptions.Left, 6);
        tipLabel.color = TipColor;
        tipLabel.rectTransform.sizeDelta = new Vector2(0.10f, 0.16f);   // 왼쪽 정렬 — 글자가 끝점 오른쪽에서 시작하게 폭을 좁힌다
        tip.gameObject.SetActive(false);
        tipLabel.gameObject.SetActive(false);

        BuildPanel();
        panel.gameObject.SetActive(false);
    }

    // ── 화면 ────────────────────────────────────────────────────────
    //   ★10-12 저녁 사용자 지시: 메인 화면은 <b>크고 넓게, 뒤쪽 멀리</b> 세운다. 탭(굴곡 · 신전 / 측굴 / 회전 / 리셋 / 자유 측정)은
    //     화면에서 떼어 그 <b>아래에 뜨는 창</b>으로 만든다(A/X로 부른다). 누르는 버튼은 화면에 하나도 없다 — 조작은 전부 컨트롤러다.
    //   좌표는 «배율 전» 값이다(PanelW × PanelH). 실제 크기는 PlacePanel이 판 전체에 배율을 걸어 맞춘다.
    private void BuildPanel()
    {
        panel = new GameObject("메인 화면").transform;
        panel.SetParent(root, false);
        MakeQuad(panel, "판", Vector2.zero, new Vector2(PanelW, PanelH), PlateColor, 0);

        guidedArea = new GameObject("순서 측정").transform;
        guidedArea.SetParent(panel, false);
        resultArea = new GameObject("결과표").transform;
        resultArea.SetParent(guidedArea, false);
        freeArea = new GameObject("자유 측정").transform;
        freeArea.SetParent(panel, false);
        popup = new GameObject("탭 창").transform;
        popup.SetParent(panel, false);
        popup.localPosition = new Vector3(0f, -(PanelH * 0.5f + PopupGap + PopupH * 0.5f), 0f);
        Transform retired = new GameObject("안 쓰는 글자").transform;   // 비교 시험 줄 — 10-08부터 늘 꺼져 있다
        retired.SetParent(panel, false);
        retired.gameObject.SetActive(false);

        float top = PanelH * 0.5f;

        // ★10-12 사용자 지시: 화면 맨 위(top ~ top − 0.085)는 <b>비워 둔다</b> — 프로그램 이름과 회사 아이콘으로 꾸밀 타이틀 자리다.
        //   거기 있던 «지금 찍을 점 · 알림» 두 줄은 안내창(아래 guide)으로 옮겼다.

        // 자유 측정 — 맨 위에 동작 갈래 셋(보여 주기만 한다 — 스틱 좌우로 바꾼다), 그 아래 안내 · 알림 · 선 목록
        for (int i = 0; i < 3; i++)
        {
            var c = new Vector2((i - 1f) * 0.16f, top - 0.104f);
            freeTabPlates[i] = MakeQuad(freeArea, "갈래 " + FreeTabNames[i], c, new Vector2(0.15f, 0.038f), TabOff, 1).GetComponent<Renderer>();
            Place(MakeText(freeArea, FreeTabNames[i], 0.13f, TextAlignmentOptions.Center, 3), c, 0.15f);
        }
        FreeTableTop = top - 0.142f;   // 갈래 줄 아래

        BuildResults(top);
        BuildPopup();

        for (int r = 0; r < 2; r++)
        {
            compareNames[r] = MakeText(retired, RomLineSession.MotionName[r] + " 미간점~턱끝점", 0.07f, TextAlignmentOptions.Left, 3);
            for (int c = 0; c < 4; c++) compareValues[r * 4 + c] = MakeText(retired, "—", 0.08f, TextAlignmentOptions.Center, 3);
            SetCompare(r, false, "", "", "", "");
        }
        freeArea.gameObject.SetActive(false);   // 탭 줄은 늘 보인다(10-14 — A로 여닫던 탭 창을 없앴다)

        // ── 안내 띠(10-14 사용자 지시) ── 탭 줄 바로 아래 — 컨트롤러 위의 안내와 <b>같은 글</b>을 메인 화면 폭에 맞춰 한 줄로 펼친다.
        Transform band = new GameObject("안내 띠").transform;
        band.SetParent(panel, false);
        band.localPosition = new Vector3(0f, -(PanelH * 0.5f + PopupGap + PopupH + PopupGap + BandH * 0.5f), 0f);
        MakeQuad(band, "바탕", Vector2.zero, new Vector2(PanelW, BandH), PopupColor, 0);
        wideLine = Place(MakeText(band, "", 0.26f, TextAlignmentOptions.Center, 3), new Vector2(0f, 0.014f), PanelW - 0.03f);
        wideNote = Place(MakeText(band, "", 0.17f, TextAlignmentOptions.Center, 3), new Vector2(0f, -0.021f), PanelW - 0.03f);
        wideNote.color = NoteColor;
        wideNote.enableAutoSizing = true;   // 긴 알림은 줄여서 한 줄에 넣는다(글이 바뀔 때만 다시 잰다)
        wideLine.enableAutoSizing = true;   // 이어 붙인 한 줄이 바보다 길면 줄여서 넣는다(바의 높이는 그대로)
        wideLine.fontSizeMin = 0.15f;
        wideLine.fontSizeMax = 0.26f;
        wideNote.fontSizeMin = 0.10f;
        wideNote.fontSizeMax = 0.17f;

        // ── 안내(10-13 사용자 지시) ── 판을 없앴다. 반투명 테두리 안에 글만 띄우고, <b>헤드셋이 보는 쪽을 따라온다</b>(Tick).
        //   윗줄 = 지금 할 일 한 문장(«굴곡 · 중립을 측정하세요» · «굴곡 측정 완료 …») · 아랫줄 = 알림이 있을 때만.
        guide = new GameObject("안내").transform;
        guide.SetParent(root, false);
        // ★10-14 사용자 지시: 빈 공간이 많다 → 테두리 높이를 <b>글 높이에 맞춘다</b>(SetGuide가 글이 바뀔 때마다 다시 잰다 · LayoutGuide).
        //   긴 알림도 이제 테두리 밖으로 흐르지 않고 테두리가 늘어난다.
        borderTop = MakeQuad(guide, "테두리 위", Vector2.zero, new Vector2(GuideW, GuideBorderT), GuideBorder, 0);
        borderBottom = MakeQuad(guide, "테두리 아래", Vector2.zero, new Vector2(GuideW, GuideBorderT), GuideBorder, 0);
        borderLeft = MakeQuad(guide, "테두리 왼쪽", new Vector2(-GuideW * 0.5f, 0f), new Vector2(GuideBorderT, 1f), GuideBorder, 0);
        borderRight = MakeQuad(guide, "테두리 오른쪽", new Vector2(GuideW * 0.5f, 0f), new Vector2(GuideBorderT, 1f), GuideBorder, 0);
        guideLine = Place(MakeText(guide, "", Fs(PxGuideLine), TextAlignmentOptions.Center, 3, fontB), Vector2.zero, GuideW - 0.02f);
        guideLine.textWrappingMode = TextWrappingModes.Normal;   // 긴 줄(«트리거 : 컨트롤러 끝점 보정» 등)은 테두리 안에서 줄을 바꾼다 — 높이는 LayoutGuide가 맞춘다
        guideNote = Place(MakeText(guide, "", Fs(PxNote), TextAlignmentOptions.Center, 3, fontL), Vector2.zero, GuideW - 0.02f);
        guideNote.textWrappingMode = TextWrappingModes.Normal;
        guideNote.color = NoteColor;
        // 바탕이 없어 밝은 배경(패스스루)에서 글자가 묻힌다 — 검은 외곽선을 두른다
        guideLine.outlineColor = guideNote.outlineColor = new Color32(0, 0, 0, 255);
        guideLine.outlineWidth = guideNote.outlineWidth = 0.22f;

        // 탭 줄의 복제(10-14 사용자 지시) — 안내 테두리 바로 위, <b>테두리 폭에 맞춘다</b>.
        //   ★같은 날 사용자 지시: 메인 화면 탭을 그대로 옮긴 첫 판은 너무 튀었고, 글자만 둔 둘째 판은 눈에 안 들어왔다 → 같은 디자인에 알파만 낮춘다(NearTabAlpha).
        float cellW = (GuideW - NearTabGap * (PopupTabs - 1)) / PopupTabs;
        float tabSize = Fs(PxTab);
        nearTabH = tabSize * 0.2f;   // 글자 높이(≈ 크기 × 0.1m)의 두 배 — 종전 0.092 글자 · 0.027 칸과 비슷한 여유
        for (int i = 0; i < PopupTabs; i++)
        {
            var c = new Vector2((i - 2f) * (cellW + NearTabGap), 0f);
            nearTabTexts[i] = Place(MakeText(guide, "", tabSize, TextAlignmentOptions.Center, 3, fontB), c, cellW);
            nearTabTexts[i].rectTransform.sizeDelta = new Vector2(cellW, nearTabH);
            // 칸이 좁아 «굴곡·신전»이 넘칠 수 있다 — 규격 크기를 최대로 두고 칸에 맞춰 줄인다(최소 70%)
            nearTabTexts[i].enableAutoSizing = true;
            nearTabTexts[i].fontSizeMax = tabSize;
            nearTabTexts[i].fontSizeMin = tabSize * 0.7f;
            nearTabTexts[i].outlineColor = new Color32(0, 0, 0, 255);
            nearTabTexts[i].outlineWidth = 0.2f;
            nearTabPlates[i] = MakeQuad(guide, "탭 " + i, c, new Vector2(cellW, nearTabH), TabOff, 1).GetComponent<Renderer>();
        }

        // ★11-17 사용자 지시: 홈 메뉴(측정값 초기화 · 끝점 보정 · 나가기)는 <b>안내 테두리 안</b>에 작게 — 왼쪽 항목 셋, 고른 항목 오른쪽에 설명.
        //   스틱 위아래로 고른다(세션). 초기화 · 나가기는 트리거 길게 — 설명 아래 막대가 찬다. 커서가 없으면 이 묶음은 꺼지고 안내 두 줄이 그대로 보인다.
        menuRoot = new GameObject("홈 메뉴").transform;
        menuRoot.SetParent(guide, false);
        float itemSize = Fs(PxMenuItem);
        menuRowH = itemSize * 0.22f;   // 종전 0.075 글자 · 0.017 줄과 같은 비율
        menuLeftW = -1f;               // 항목 이름을 받으면 가장 긴 이름에 맞춘다(SetHomeMenu)
        menuRightW = GuideW - GuidePad * 2f - 0.08f - MenuGap;
        menuHi = MakeQuad(menuRoot, "커서", Vector2.zero, new Vector2(0.08f, menuRowH - 0.002f), new Color(TabOn.r, TabOn.g, TabOn.b, NearTabAlpha), 1);
        for (int i = 0; i < MenuRows; i++)
        {
            menuItems[i] = Outlined(Place(MakeText(menuRoot, "", itemSize, TextAlignmentOptions.Midline, 3, fontM), Vector2.zero, 0.08f));
            menuItems[i].rectTransform.sizeDelta = new Vector2(0.08f, menuRowH);
        }
        menuDesc = Outlined(Place(MakeText(menuRoot, "", Fs(PxNote), TextAlignmentOptions.TopLeft, 3, fontL), Vector2.zero, menuRightW));
        menuDesc.textWrappingMode = TextWrappingModes.Normal;
        menuDesc.color = NoteColor;   // Outlined가 흰색으로 둔다 — 설명은 #CCDBED
        holdBack = MakeQuad(menuRoot, "길게 바탕", Vector2.zero, new Vector2(menuRightW, MenuBarH), GaugeDim, 1);
        holdFill = MakeQuad(menuRoot, "길게 막대", Vector2.zero, new Vector2(menuRightW, MenuBarH), TipColor, 2);
        menuRoot.gameObject.SetActive(false);
        LayoutGuide(0f, 0f);
        guide.gameObject.SetActive(false);

        // 맨 아래 한 줄 — 모니터 화면의 주소
        footer = Place(MakeText(panel, "", 0.075f, TextAlignmentOptions.Center, 3), new Vector2(0f, -PanelH * 0.5f + 0.010f), PanelW - 0.02f);
        footer.color = new Color(0.60f, 0.66f, 0.72f, 1f);

        // 배터리(10-21 사용자 지시) — 벽면UI는 오른쪽 위 모서리(타이틀 자리의 끝), 컨트롤러UI는 탭 줄 바로 위 오른쪽(LayoutGuide가 높이를 맞춘다)
        wallBattery = Place(MakeText(panel, "", 0.13f, TextAlignmentOptions.Right, 3), new Vector2(PanelW * 0.5f - 0.01f - 0.08f, PanelH * 0.5f - 0.03f), 0.16f);
        nearBattery = Outlined(Place(MakeText(guide, "", Fs(PxSmall), TextAlignmentOptions.Right, 3, fontM), Vector2.zero, GuideW));
        SetBattery(-1, false);
        LayoutGuide(0f, 0f);
    }

    // ── 배터리 ── 색은 잔량 구간마다 하나: 50% 초과 초록 · 20% 초과 노랑 · 20% 이하 빨강. 관제 화면(mirror.html · chuna_mirror.html)도 같은 구간이다.
    //   ★주황은 측정값 색이라 쓰지 않는다(10-12 색 규칙).
    private TextMeshPro wallBattery, nearBattery;
    private static readonly Color BatteryHigh = new Color(0.45f, 0.90f, 0.55f, 1f);   // #73E68C(다 잰 탭과 같다)
    private static readonly Color BatteryMid = new Color(1f, 0.95f, 0.20f, 1f);       // #FFF233
    private static readonly Color BatteryLow = new Color(1f, 0.42f, 0.42f, 1f);       // #FF6B6B

    /// <summary>잔량(0~100). 읽을 수 없으면 -1(에디터 · 배터리 없는 PC).</summary>
    public static int BatteryPercent() => SystemInfo.batteryLevel < 0f ? -1 : Mathf.RoundToInt(SystemInfo.batteryLevel * 100f);
    /// <summary>전원에 꽂혀 있다(충전 중 · 완충).</summary>
    public static bool BatteryCharging() => SystemInfo.batteryStatus == BatteryStatus.Charging || SystemInfo.batteryStatus == BatteryStatus.Full;
    /// <summary>색 구간: -1 모름 · 0 낮음(≤20) · 1 중간(≤50) · 2 높음.</summary>
    public static int BatteryBand(int pct) => pct < 0 ? -1 : pct <= 20 ? 0 : pct <= 50 ? 1 : 2;

    /// <summary>두 화면의 배터리 글자(바뀔 때만 부른다). «배터리» 글자는 흐린 색, 숫자만 구간 색이다.</summary>
    public void SetBattery(int pct, bool charging)
    {
        string text;
        if (pct < 0) text = "배터리 —";
        else
        {
            int band = BatteryBand(pct);
            Color c = band == 2 ? BatteryHigh : band == 1 ? BatteryMid : BatteryLow;
            text = "배터리 <color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + pct + "%</color>" + (charging ? " · 충전 중" : "");
        }
        wallBattery.text = nearBattery.text = text;
        wallBattery.color = DimColor;
        nearBattery.color = NoteColor;
    }

    // ── 결과표 ── ★10-13 사용자 지시: 3 × 2 칸을 풀어 <b>왼쪽에 여섯 줄</b>(측정 명칭 | 능동 | 압박), <b>오른쪽에 각도기 셋</b>으로 나눈다.
    //   같은 단면의 두 동작(굴곡 · 신전 / 우측굴 · 좌측굴 / 우회전 · 좌회전)은 <b>한 각도기</b>에 좌우로 그린다 — 각도기는 그 두 줄 옆에 크게 선다.
    //   각도기 그림은 «보는 방향»을 정해 그린 약도다(찍은 점의 좌표를 그대로 옮긴 그림이 아니다). 값은 세션이 준 «면 위 각» 그대로다.
    //   ★10-13 저녁 사용자 지시: 그림의 좌우를 뒤집었다 — 양옆 표시가 «뒤 · 앞» → «앞 · 뒤», «우 · 좌» → «좌 · 우»다. 글자만 바꾼 것이 아니라 선이 기우는 쪽(GaugeSide)도 같이 뒤집었다.
    //     굴곡 · 신전 = 옆에서 본 그림(왼쪽이 환자 앞) · 측굴 = 환자 왼쪽이 그림 왼쪽 · 회전 = 위에서 본 그림(위가 환자 앞 · 환자 왼쪽이 그림 왼쪽)
    //     ★모니터 화면의 세 그림은 종전 방향 그대로다(헤드셋 결과표와 좌우가 반대다).
    //   줄의 순서도 «좌측굴 · 우측굴», «좌회전 · 우회전»으로 바꿨었다(DisplayRow) — ★10-16에 재는 순서(우 → 좌)로 되돌렸다(사용자 지시 · 각도기는 그대로).
    private const float RowH = 0.041f, GaugeR = 0.068f;
    // ★10-15 사용자 지시: 압박과 각도기 사이에 «차이»(압박 − 능동 — 능동에서 압박으로 얼마나 더 갔나) 칸을 넣었다. 그 자리를 내려고 능동 · 압박 칸을 왼쪽으로 당기고 지금 줄 띠를 넓혔다.
    private const float XName = -0.31f, XActive = -0.19f, XPressure = -0.08f, XDiff = 0.03f, GaugeX = 0.20f;   // 판 가운데에서 잰 자리
    private const float LeftX = -0.16f, LeftW = 0.46f;   // 왼쪽 표(지금 줄을 밝히는 띠)의 가운데와 폭
    private const float PopupH = 0.088f, PopupGap = 0.015f;   // 탭 한 줄뿐이다(아래의 설명 세 줄은 뺐다, 10-12)
    private const float BandH = 0.075f;                       // 탭 줄 아래 안내 띠의 높이(배율 전, 10-14)
    private const float NearTabGap = 0.002f;   // 안내 위 탭 줄 칸 사이(m, 10-14) — 칸은 보이는 탭 수로 안내 테두리 폭(GuideW)을 채운다(11-17)
    private float nearTabH = 0.027f;           // 탭 칸 높이 — 탭 글자 크기에 맞춰 Build에서 정한다(종전 고정 0.027)

    // ── 컨트롤러UI 글자 규격 ── ★11-17 사용자 결정: 실습 씬 UI 규격 V0.1 8절 «환자 옆 안내판»(D:/CoCoChuna_실습씬_UI규격_V0.1_20261111)을 따른다.
    //   ★규격은 <b>글자 사이 비율</b>만 맞춘다(사용자 정정) — 설계 px × 기준 배율 하나 × OS 글자 배율. 절대 크기는 OS 설정을 따라 변해도 된다.
    //   기준 배율 = 안내 큰 글 20px이 종전 크기 0.16이 되게(지금 크기 감각 유지). 글꼴 = KoPubWorld 돋움 B · M · L(SideGuideFonts).
    //   벽면UI는 글꼴만 KoPub M(굵게 쓰던 곳은 B)이고 크기는 종전 그대로다.
    private const float PxGuideLine = 20f;   // 안내 큰 글(지금 할 일) — B · 흰
    private const float PxTab = 18f;         // 탭 — B · 탭 색 규칙(파란 칸 + 노란 글자) 유지
    private const float PxMenuItem = 16f;    // 홈 메뉴 항목 — M(고른 항목 B)
    private const float PxNote = 14f;        // 알림 · 메뉴 설명 — L · #CCDBED
    private const float PxSmall = 12f;       // 조작 문구 · 배터리 — M
    private const float PxToSize = 0.16f / PxGuideLine;
    /// <summary>설계 px → RomLine TMP 글자 크기(OS 글자 배율 포함).</summary>
    private static float Fs(float px) => px * PxToSize * OsDisplayScale.Font;
    /// <summary>메뉴 설명 안 조작 문구(12px)를 설명 글(14px)에 대한 비율로 줄이는 리치 텍스트 태그 — 세션이 설명 끝줄에 쓴다.</summary>
    public const string SmallTagOpen = "<size=86%>", SmallTagClose = "</size>";   // 12 / 14 ≈ 0.86

    private TMP_FontAsset fontB, fontM, fontL;   // KoPub 세 굵기(없으면 셋 다 Build에 넘어온 글꼴)
    /// <summary>굵은 글꼴 — 공간 각도기의 굵은 숫자가 쓴다.</summary>
    internal TMP_FontAsset BoldFont => fontB;
    private const float GuidePad = 0.008f, GuideNoteGap = 0.004f, GuideMinH = 0.040f, GuideBorderT = 0.0015f;   // 안내 테두리 안쪽 여백 · 두 줄 사이 · 최소 높이 · 테두리 굵기
    private const float GuideW = 0.24f;                       // 안내 테두리의 폭(m) — ★10-13 오후 사용자 지시: 길쭉한 띠(0.40 × 0.085) 말고 조금 넓은 직사각형. 높이는 10-14부터 글에 맞춘다(LayoutGuide)
    private const float GuideFollow = 10f;                   // 안내가 컨트롤러를 따라오는 빠르기(1/초) — 손 떨림은 걸러 내되 손을 옮기면 바로 따라붙는다
    private const float NearTabAlpha = 0.55f;   // ★10-14 사용자 지시: 안내 위 탭은 메인 화면 탭 줄과 <b>같은 디자인</b>(칸 바탕 · 지금 탭 = 파란 칸 + 노란 글자)에 알파만 낮춰 안내의 반투명 분위기에 맞춘다
    private static readonly Color GuideBorder = new Color(1f, 1f, 1f, 0.35f);
    private const float GaugeMaxDraw = 100f;     // 지움: 이보다 큰 각은 그림에서 여기까지만 그린다(반원 밖으로 넘어간다) — 숫자는 그대로 나온다
    private const int GaugeArcSegments = 12;
    private static readonly float[] GaugeZeroY = { 1f, 1f, 1f, 1f, 1f, 1f };         // 0° 선이 향하는 쪽(위 +1 · 아래 −1) — 회전도 위로 세웠다(10-13 저녁 사용자 지시 · 종전에는 아래가 환자 앞이라 뒤집혀 보였다)
    private static readonly float[] GaugeSide = { -1f, 1f, 1f, -1f, 1f, -1f };       // 그 동작이 제 방향으로 움직였을 때 선이 기우는 쪽(그림 오른쪽 +1) — 10-13 저녁에 전부 뒤집었다
    // ★10-16 사용자 지시: 줄 순서는 다시 <b>재는 순서</b>(굴곡 · 신전 · 우측굴 · 좌측굴 · 우회전 · 좌회전)로 되돌렸다. 각도기(좌우 뒤집기 · GaugeSide)는 10-13 그대로다.
    private static readonly int[] DisplayRow = { 0, 1, 2, 3, 4, 5 };                  // 동작 번호 → 결과표의 줄(10-13~10-15에는 { 0, 1, 3, 2, 5, 4 } — 좌측굴 · 좌회전을 먼저)
    private static readonly string[] GaugeLeft = { "앞", "앞", "좌", "좌", "좌", "좌" };
    private static readonly string[] GaugeRight = { "뒤", "뒤", "우", "우", "우", "우" };
    private static readonly string[] FreeTabNames = { "3점", "2선", "연속" };   // 자유 측정의 방식(10-13) — RomLineSession.FreeTabName과 같다
    private static readonly Color NoteColor = new Color(0.80f, 0.86f, 0.93f, 1f);     // 알림 · 안내 — 주황(측정값)과 섞이지 않게 푸른 회색
    private static readonly Color DimColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    private static readonly Color GaugeDim = new Color(0.42f, 0.48f, 0.56f, 1f);
    private static readonly Color OppositeColor = new Color(1f, 0.45f, 0.45f, 1f);    // 반대쪽으로 움직인 값
    private static readonly Color RowNowColor = new Color(0.15f, 0.21f, 0.29f, 1f);
    private static readonly Color PopupColor = new Color(0.09f, 0.12f, 0.17f, 0.96f);
    private static readonly Color TabCurrent = new Color(0.20f, 0.29f, 0.40f, 1f);     // 지금 재는 탭(스틱이 다른 탭 위에 있을 때)
    private static readonly Color ResetColor = new Color(0.42f, 0.13f, 0.13f, 1f);

    private Transform resultArea, popup, rowNow, resetCard;
    private TextMeshPro askTitle, askKeys;
    private Transform guide;                 // 안내 — 헤드셋이 보는 쪽을 따라온다
    private float guideDistance, guideDrop, guideRise;
    private bool guideSnapped;
    private TextMeshPro guideLine, guideNote, wideLine, wideNote;
    private readonly Renderer[] nearTabPlates = new Renderer[PopupTabs];
    private readonly TextMeshPro[] nearTabTexts = new TextMeshPro[PopupTabs];
    private Transform borderTop, borderBottom, borderLeft, borderRight;
    private float guideH = GuideMinH;        // 지금 안내 테두리의 높이(글에 맞춘다 — LayoutGuide)
    private readonly Vector2[] cellPos = new Vector2[6], gaugePivot = new Vector2[6];
    private readonly LineRenderer[] gaugeLines = new LineRenderer[12];   // 동작 × (능동 · 압박)
    private readonly LineRenderer[] gaugeArcs = new LineRenderer[6];     // 0° 선에서 능동 선까지의 호
    private readonly TextMeshPro[] diffs = new TextMeshPro[6];           // 동작마다 «차이»(압박 − 능동, 10-15)
    private static readonly Color DiffColor = new Color(0.92f, 0.92f, 0.92f, 1f);   // 능동 · 압박 색과 섞이지 않게 흰색
    private readonly Renderer[] freeTabPlates = new Renderer[3], popupTabPlates = new Renderer[PopupTabs];
    private readonly TextMeshPro[] popupTabTexts = new TextMeshPro[PopupTabs];

    private void BuildResults(float top)
    {
        float headY = top - 0.098f, rowTop = top - 0.112f;
        Place(MakeText(resultArea, "측정", 0.12f, TextAlignmentOptions.Left, 3), new Vector2(XName, headY), 0.14f).color = DimColor;
        Place(MakeText(resultArea, "능동", 0.15f, TextAlignmentOptions.Center, 3), new Vector2(XActive, headY), 0.10f).color = ActiveColor;
        Place(MakeText(resultArea, "압박", 0.15f, TextAlignmentOptions.Center, 3), new Vector2(XPressure, headY), 0.10f).color = PressureColor;
        TextMeshPro diffHeadText = Place(MakeText(resultArea, "차이", 0.15f, TextAlignmentOptions.Center, 3), new Vector2(XDiff, headY), 0.10f);
        diffHeadText.color = DiffColor;
        // ★10-14 사용자 지시: «각도기» 머리글과 각도기 옆의 «옆에서 / 앞에서 / 위에서 본 그림» 글자를 뺐다(양옆의 앞 · 뒤 / 좌 · 우 표시는 둔다).

        rowNow = MakeQuad(resultArea, "지금 줄", Vector2.zero, new Vector2(LeftW, RowH - 0.004f), RowNowColor, 1);
        rowNow.gameObject.SetActive(false);

        // 움직이지 않는 그림(반원 · 눈금 · 0° 선 · 줄 사이 선)은 메시 하나로 만든다 — 그리는 호출이 하나다.
        var verts = new System.Collections.Generic.List<Vector3>(800);
        var cols = new System.Collections.Generic.List<Color>(800);
        var tris = new System.Collections.Generic.List<int>(1200);
        for (int m = 0; m < 6; m++)
        {
            float y = rowTop - RowH * (DisplayRow[m] + 0.5f);
            float pairY = rowTop - RowH * (m / 2 * 2 + 1);   // 같은 단면 두 줄의 가운데 — 각도기가 여기 선다
            cellPos[m] = new Vector2(LeftX, y);
            gaugePivot[m] = new Vector2(GaugeX, pairY - GaugeZeroY[m] * GaugeR * 0.5f);   // 두 동작이 같은 축을 쓴다

            Place(MakeText(resultArea, RomLineSession.MotionName[m], 0.22f, TextAlignmentOptions.Left, 3), new Vector2(XName, y), 0.14f);
            values[m * 2] = Place(MakeText(resultArea, "—", 0.26f, TextAlignmentOptions.Center, 3), new Vector2(XActive, y), 0.10f);
            values[m * 2 + 1] = Place(MakeText(resultArea, "—", 0.26f, TextAlignmentOptions.Center, 3), new Vector2(XPressure, y), 0.10f);
            diffs[m] = Place(MakeText(resultArea, "—", 0.26f, TextAlignmentOptions.Center, 3), new Vector2(XDiff, y), 0.10f);
            diffs[m].color = DiffColor;

            // ★10-15 사용자 지시: 각도기의 측정 선(능동 · 압박 · 능동 호 · 중립 0° 선)을 절반 굵기로. 반원 테두리 · 눈금은 그대로다.
            gaugeArcs[m] = MakePanelLine("각도기 호", ActiveColor, GaugeArcSegments + 1, 0.0010f);
            gaugeLines[m * 2 + 1] = MakePanelLine("각도기 압박선", PressureColor, 2, 0.0015f);
            gaugeLines[m * 2] = MakePanelLine("각도기 능동선", ActiveColor, 2, 0.0018f);
            if (m % 2 == 1) continue;   // 아래는 단면마다 한 번만 그린다

            Place(MakeText(resultArea, GaugeLeft[m], 0.12f, TextAlignmentOptions.Center, 3), gaugePivot[m] + Vector2.left * (GaugeR + 0.014f), 0.02f).color = GaugeDim;
            Place(MakeText(resultArea, GaugeRight[m], 0.12f, TextAlignmentOptions.Center, 3), gaugePivot[m] + Vector2.right * (GaugeR + 0.014f), 0.02f).color = GaugeDim;

            Seg(verts, cols, tris, new Vector2(-PanelW * 0.5f + 0.01f, pairY + RowH), new Vector2(PanelW * 0.5f - 0.01f, pairY + RowH), 0.0012f, GaugeDim);   // 두 줄 묶음의 윗변
            Seg(verts, cols, tris, gaugePivot[m] + Vector2.left * GaugeR, gaugePivot[m] + Vector2.right * GaugeR, 0.0016f, GaugeDim);
            for (int i = 0; i < 18; i++)
                Seg(verts, cols, tris, GaugePoint(m, -90f + i * 10f, GaugeR), GaugePoint(m, -80f + i * 10f, GaugeR), 0.0016f, GaugeDim);
            for (int deg = -90; deg <= 90; deg += 15)   // 눈금 15° — 45° · 90°는 길게
                Seg(verts, cols, tris, GaugePoint(m, deg, GaugeR - (deg % 45 == 0 ? 0.011f : 0.006f)), GaugePoint(m, deg, GaugeR), 0.0016f, GaugeDim);
            Seg(verts, cols, tris, gaugePivot[m], GaugePoint(m, 0f, GaugeR), 0.0012f, NeutralColor);   // 0° = 중립 (10-15 절반으로)
        }

        diffHead = diffHeadText;
        BuildResultWindow(top);

        var go = new GameObject("각도기 바탕");
        go.transform.SetParent(resultArea, false);
        var mesh = new Mesh { name = "[선실측] 각도기 바탕" };
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;   // Sprites/Default — 꼭짓점 색을 그대로 쓴다
        mr.sortingOrder = 2;
    }

    /// <summary>각도기 위의 한 점(판 좌표). deg = 0° 선에서 벌어진 각, + 가 그림 오른쪽.</summary>
    private Vector2 GaugePoint(int m, float deg, float r)
    {
        float a = deg * Mathf.Deg2Rad;
        return gaugePivot[m] + new Vector2(Mathf.Sin(a), Mathf.Cos(a) * GaugeZeroY[m]) * r;
    }

    private static void Seg(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<Color> cols, System.Collections.Generic.List<int> tris,
                            Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 d = (b - a).normalized;
        Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
        int i = verts.Count;
        verts.Add(OnPanel(a - n)); verts.Add(OnPanel(a + n)); verts.Add(OnPanel(b + n)); verts.Add(OnPanel(b - n));
        for (int k = 0; k < 4; k++) cols.Add(c);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    private static Vector3 OnPanel(Vector2 p) => new Vector3(p.x, p.y, -0.002f);

    private LineRenderer MakePanelLine(string name, Color c, int count, float width)
    {
        LineRenderer lr = MakeLine(name, c, count);
        lr.transform.SetParent(resultArea, false);
        lr.useWorldSpace = false;                    // 판과 함께 움직인다(굵기도 판의 배율을 따른다)
        lr.alignment = LineAlignment.TransformZ;     // 판에 납작하게
        lr.startWidth = lr.endWidth = width;
        return lr;
    }

    // ── 결과 창(11-17 사용자 결정 · 시안 D:/CoCoChuna_XR-ROM_결과창_시안_V0.2_20261117) ──────────────────
    //   새 판이 아니라 <b>벽면 판이 «끝난 모습»으로 바뀐다</b>: 맨 위 비워 둔 띠(10-12 타이틀 자리)에 제목 · 날짜 · 사용자,
    //   평가면 그 오른쪽에 점수(큰 주황 숫자) · 80~100 띠(90 기준선) · 평균 오차 줄. 값 칸 = 평가 값(0.22) + 그 아래 «(실습 값)»(0.12),
    //   차이 칸 자리 = 오차 칩 둘(능동 / 압박 — 2° 안 초록 · 5° 안 주황 · 넘으면 빨강). 칸 · 각도기 셋 · 색은 결과표 그대로다.
    //   ★점수 띠는 시안(x 0.09~0.37)에서 왼쪽으로 당겼다(0.07~0.23) — 오른쪽 위 모서리의 배터리 글자와 겹치지 않게.
    private Transform resultTitleRoot, evalRoot, evalScoreFill;
    private TextMeshPro resultTitle, resultSub, evalScoreText, evalMeanText, diffHead;
    private readonly TextMeshPro[] evalVals = new TextMeshPro[12], evalBases = new TextMeshPro[12], errTexts = new TextMeshPro[12];
    private readonly Renderer[] errChips = new Renderer[12];
    private const float ScoreBarX0 = 0.07f, ScoreBarX1 = 0.23f, ScoreBarH = 0.010f;
    private static readonly Color ErrOk = new Color(0.45f, 0.90f, 0.55f, 1f);   // #73E68C(다 잰 탭 · 배터리 높음과 같다)
    private bool resultShown, resultEval;

    private void BuildResultWindow(float top)
    {
        resultTitleRoot = new GameObject("결과 창 제목").transform;
        resultTitleRoot.SetParent(resultArea, false);
        float left = XName - 0.07f;   // 동작 이름 글자가 시작하는 자리(이름 칸 0.14 · 왼쪽 정렬)
        resultTitle = Place(MakeText(resultTitleRoot, "", 0.22f, TextAlignmentOptions.Left, 3, fontB), new Vector2(left + 0.15f, top - 0.040f), 0.30f);
        resultSub = Place(MakeText(resultTitleRoot, "", 0.12f, TextAlignmentOptions.Left, 3, fontL), new Vector2(left + 0.20f, top - 0.072f), 0.40f);
        resultSub.color = DimColor;
        resultTitleRoot.gameObject.SetActive(false);

        evalRoot = new GameObject("결과 창 평가").transform;
        evalRoot.SetParent(resultArea, false);
        evalScoreText = Place(MakeText(evalRoot, "", 0.40f, TextAlignmentOptions.Right, 3, fontB), new Vector2(0.02f - 0.07f, top - 0.050f), 0.14f);
        evalScoreText.color = ActiveColor;
        Place(MakeText(evalRoot, "점", 0.17f, TextAlignmentOptions.Left, 3, fontM), new Vector2(0.026f + 0.02f, top - 0.058f), 0.04f);
        float yy = top - 0.040f, bw = ScoreBarX1 - ScoreBarX0;
        MakeQuad(evalRoot, "점수 띠 바탕", new Vector2((ScoreBarX0 + ScoreBarX1) * 0.5f, yy), new Vector2(bw, ScoreBarH), TabOff, 1);
        evalScoreFill = MakeQuad(evalRoot, "점수 띠", new Vector2(ScoreBarX0, yy), new Vector2(bw, ScoreBarH), ActiveColor, 2);
        MakeQuad(evalRoot, "90 기준선", new Vector2(ScoreBarX0 + bw * 0.5f, yy), new Vector2(0.002f, 0.024f), Color.white, 3);
        Place(MakeText(evalRoot, "80", 0.10f, TextAlignmentOptions.Center, 3, fontL), new Vector2(ScoreBarX0, yy - 0.020f), 0.04f).color = GaugeDim;
        Place(MakeText(evalRoot, "90 기준", 0.10f, TextAlignmentOptions.Center, 3, fontM), new Vector2(ScoreBarX0 + bw * 0.5f, yy - 0.020f), 0.06f);
        Place(MakeText(evalRoot, "100", 0.10f, TextAlignmentOptions.Center, 3, fontL), new Vector2(ScoreBarX1, yy - 0.020f), 0.04f).color = GaugeDim;
        evalMeanText = Place(MakeText(evalRoot, "", 0.10f, TextAlignmentOptions.Left, 3, fontL), new Vector2(ScoreBarX0 + 0.15f, top - 0.074f), 0.30f);
        evalMeanText.color = DimColor;

        float rowTop = top - 0.112f;
        for (int s = 0; s < 12; s++)
        {
            int m = s / 2;
            float y = rowTop - RowH * (DisplayRow[m] + 0.5f);
            float x = s % 2 == 0 ? XActive : XPressure;
            evalVals[s] = Place(MakeText(evalRoot, "", 0.22f, TextAlignmentOptions.Center, 3), new Vector2(x, y + 0.006f), 0.10f);
            evalVals[s].color = s % 2 == 0 ? ActiveColor : PressureColor;
            evalBases[s] = Place(MakeText(evalRoot, "", 0.12f, TextAlignmentOptions.Center, 3, fontL), new Vector2(x, y - 0.0135f), 0.10f);
            evalBases[s].color = DimColor;
            float cu = XDiff - 0.024f + (s % 2) * 0.048f;
            errChips[s] = MakeQuad(evalRoot, "오차 칩", new Vector2(cu, y), new Vector2(0.044f, 0.028f), ErrOk, 2).GetComponent<Renderer>();
            errTexts[s] = Place(MakeText(evalRoot, "", 0.15f, TextAlignmentOptions.Center, 3, fontB), new Vector2(cu, y), 0.046f);
        }
        evalRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// 결과 창을 켜고 끈다(상태가 바뀔 때만 부른다). eval = 평가 점수가 난 결과(값 칸 · 차이 칸을 평가 칸으로 바꾼다).
    /// 끄면 맨 위 띠는 다시 비고 값 · 차이 칸이 돌아온다.
    /// </summary>
    public void SetResultWindow(bool on, bool eval, string title, string sub)
    {
        eval &= on;
        resultTitle.text = on ? title : "";
        resultSub.text = on ? sub : "";
        if (resultTitleRoot.gameObject.activeSelf != on) resultTitleRoot.gameObject.SetActive(on);
        if (evalRoot.gameObject.activeSelf != eval) evalRoot.gameObject.SetActive(eval);
        for (int s = 0; s < 12; s++)
            if (values[s].gameObject.activeSelf == eval) values[s].gameObject.SetActive(!eval);
        for (int m = 0; m < 6; m++)
            if (diffs[m].gameObject.activeSelf == eval) diffs[m].gameObject.SetActive(!eval);
        diffHead.text = eval ? "오차 능동 / 압박" : "차이";
        diffHead.fontSize = eval ? 0.12f : 0.15f;
        resultShown = on;
        resultEval = eval;
    }

    /// <summary>평가 결과의 점수 · 평균 오차 줄(점수가 바뀔 때만).</summary>
    public void SetEvalScore(float score, string meanLine)
    {
        evalScoreText.text = score.ToString("F1");
        float k = Mathf.Clamp01((score - 80f) / 20f), bw = ScoreBarX1 - ScoreBarX0;
        evalScoreFill.localScale = new Vector3(Mathf.Max(bw * k, 0.0001f), ScoreBarH, 1f);
        evalScoreFill.localPosition = new Vector3(ScoreBarX0 + bw * k * 0.5f, evalScoreFill.localPosition.y, evalScoreFill.localPosition.z);
        evalMeanText.text = meanLine;
    }

    /// <summary>평가 결과의 한 칸(slot = 동작 × 능동 · 압박). has = 실습 · 평가 둘 다 값이 있다. err = 평가 − 실습(도).</summary>
    public void SetEvalCell(int slot, bool has, string value, string baseText, float err)
    {
        evalVals[slot].text = value;
        evalBases[slot].text = has ? baseText : "";
        bool chip = has;
        if (errChips[slot].gameObject.activeSelf != chip) errChips[slot].gameObject.SetActive(chip);
        errTexts[slot].text = has ? err.ToString("+0.0;-0.0;0.0") : "—";
        if (!has) { errTexts[slot].color = GaugeDim; return; }
        float e = Mathf.Abs(err);
        Color c = e <= 2f ? ErrOk : e <= 5f ? PressureColor : OppositeColor;
        errTexts[slot].color = c;
        errChips[slot].material.color = new Color(c.r, c.g, c.b, 0.22f);
    }

    public bool ResultShown => resultShown;
    public bool ResultEval => resultEval;

    // ── 탭 창 ── A/X로 부른다. 메인 화면 아래에 따로 뜬다. 탭 다섯 + 지금 탭의 진행 + 알림 + 버튼 안내.
    //   «리셋»만 고른 뒤 A를 눌러야 하고(확인 창이 한 번 더 뜬다), 나머지 탭은 스틱이 옮겨 가는 대로 바뀐다.
    public const int PopupTabs = 5;
    private const float PopupTabW = 0.150f, PopupTabGap = 0.006f;

    /// <summary>
    /// 보이는 탭 수(10-16 사용자 지시: 자유 측정 탭을 끄면 4). 남는 탭은 끄고, 나머지를 두 탭 줄(메인 화면 아래 · 안내 위) 가운데로 모은다.
    /// 메인 화면 탭 칸의 폭은 그대로다. ★11-17 사용자 지시: 안내 위 탭 줄은 <b>보이는 탭 수로 나눠 안내 테두리 폭을 꽉 채운다</b>
    /// (종전에는 다섯 칸 폭 그대로라 넷이면 양옆이 비었다). 한 번만 부른다(Build 뒤).
    /// </summary>
    public void SetTabCount(int n)
    {
        int shown = Mathf.Clamp(n, 1, PopupTabs);
        float cellW = (GuideW - NearTabGap * (shown - 1)) / shown;
        for (int i = 0; i < PopupTabs; i++)
        {
            bool on = i < n;
            popupTabPlates[i].gameObject.SetActive(on);
            popupTabTexts[i].gameObject.SetActive(on);
            nearTabPlates[i].gameObject.SetActive(on);
            nearTabTexts[i].gameObject.SetActive(on);
            float k = i - (n - 1) * 0.5f;
            SetX(popupTabPlates[i].transform, k * (PopupTabW + PopupTabGap));
            SetX(popupTabTexts[i].transform, k * (PopupTabW + PopupTabGap));
            nearTabPlates[i].transform.localScale = new Vector3(cellW, nearTabH, 1f);
            nearTabTexts[i].rectTransform.sizeDelta = new Vector2(cellW, nearTabH);
            SetX(nearTabPlates[i].transform, k * (cellW + NearTabGap));
            SetX(nearTabTexts[i].transform, k * (cellW + NearTabGap));
        }
    }

    // ── 홈 메뉴(11-17) ── 안내 테두리 안. 글은 상태가 바뀔 때만 넣는다(SetHomeMenu) · 막대만 매 프레임(SetHomeMenuHold, 할당 없음).
    private const int MenuRows = 3;
    private const float MenuGap = 0.008f;
    private const float MenuBarH = 0.004f, MenuBarGap = 0.004f;
    // 항목 줄 높이 · 항목 열 폭 · 설명 열 폭 — 글자 크기(Fs)에 따라 Build · 첫 SetHomeMenu에서 정한다
    private float menuRowH, menuLeftW = -1f, menuRightW;
    private Transform menuRoot, menuHi, holdBack, holdFill;
    private readonly TextMeshPro[] menuItems = new TextMeshPro[MenuRows];
    private TextMeshPro menuDesc;
    private float menuBarLeft, menuBarY;

    /// <summary>
    /// 홈 메뉴를 띄운다 / 내린다. cursor = 고른 항목(−1이면 내리고 안내 두 줄을 되살린다) · labels = 항목 이름(MenuRows개) ·
    /// desc = 고른 항목의 설명(리치 텍스트) · holdItem = 트리거 길게 항목이면 막대를 보인다. SetGuide 뒤에 부른다(테두리를 다시 잰다).
    /// </summary>
    public void SetHomeMenu(int cursor, string[] labels, string desc, bool holdItem)
    {
        bool on = cursor >= 0 && labels != null;
        if (menuRoot.gameObject.activeSelf != on) menuRoot.gameObject.SetActive(on);
        if (guideLine.gameObject.activeSelf == on) guideLine.gameObject.SetActive(!on);
        if (guideNote.gameObject.activeSelf == on) guideNote.gameObject.SetActive(!on);
        if (!on) return;   // 테두리는 방금 SetGuide가 안내 두 줄에 맞춰 두었다

        for (int i = 0; i < MenuRows; i++)
        {
            string s = i < labels.Length ? labels[i] : "";
            if (menuItems[i].text != s) menuItems[i].text = s;
            menuItems[i].color = i == cursor ? TipColor : DimColor;
            TMP_FontAsset face = i == cursor ? fontB : fontM;   // 규격: 항목 16 M · 고른 항목 16 B
            if (face != null && menuItems[i].font != face) menuItems[i].font = face;
        }
        if (menuLeftW < 0f) FitMenuColumns(labels);
        if (menuDesc.text != desc) menuDesc.text = desc;
        if (holdBack.gameObject.activeSelf != holdItem) holdBack.gameObject.SetActive(holdItem);

        float descH = menuDesc.GetPreferredValues(desc, menuRightW, 0f).y;
        float rightH = descH + (holdItem ? MenuBarGap + MenuBarH : 0f);
        float inner = Mathf.Max(MenuRows * menuRowH, rightH);
        guideH = Mathf.Max(GuideMinH, inner + GuidePad * 2f);
        float y0 = guideH * 0.5f - GuidePad;                               // 글 묶음의 윗변
        float xL = -GuideW * 0.5f + GuidePad + menuLeftW * 0.5f;           // 항목 열 가운데
        for (int i = 0; i < MenuRows; i++)
            menuItems[i].transform.localPosition = new Vector3(xL, y0 - menuRowH * (i + 0.5f), -0.004f);
        int c = Mathf.Min(cursor, MenuRows - 1);
        menuHi.localPosition = new Vector3(xL, y0 - menuRowH * (c + 0.5f), menuHi.localPosition.z);

        menuBarLeft = -GuideW * 0.5f + GuidePad + menuLeftW + MenuGap;    // 설명 열 왼쪽 끝
        float xR = menuBarLeft + menuRightW * 0.5f;
        menuDesc.rectTransform.sizeDelta = new Vector2(menuRightW, Mathf.Max(descH, 0.001f));
        menuDesc.transform.localPosition = new Vector3(xR, y0 - descH * 0.5f, -0.004f);
        menuBarY = y0 - descH - MenuBarGap - MenuBarH * 0.5f;
        holdBack.localPosition = new Vector3(xR, menuBarY, holdBack.localPosition.z);
        SetHomeMenuHold(0f);
        PlaceGuideFrame();
    }

    /// <summary>
    /// 항목 열 폭을 가장 긴 이름(굵게)에 맞추고 설명 열이 나머지를 쓴다 — 처음 한 번(항목 이름은 바뀌지 않는다).
    /// 글자 크기가 OS 배율을 따라 변하니 고정 폭 대신 잰다. 설명 열은 테두리 폭의 40% 밑으로 줄이지 않는다.
    /// </summary>
    private void FitMenuColumns(string[] labels)
    {
        float w = 0f;
        TMP_FontAsset keep = menuItems[0].font;
        if (fontB != null) menuItems[0].font = fontB;   // 고른 항목이 굵어져도 넘치지 않게 굵은 글꼴로 잰다
        for (int i = 0; i < labels.Length && i < MenuRows; i++)
            w = Mathf.Max(w, menuItems[0].GetPreferredValues(labels[i]).x);
        menuItems[0].font = keep;
        float inner = GuideW - GuidePad * 2f - MenuGap;
        menuLeftW = Mathf.Min(w + 0.006f, inner * 0.6f);
        menuRightW = inner - menuLeftW;
        for (int i = 0; i < MenuRows; i++) menuItems[i].rectTransform.sizeDelta = new Vector2(menuLeftW, menuRowH);
        menuHi.localScale = new Vector3(menuLeftW, menuRowH - 0.002f, 1f);
        holdBack.localScale = new Vector3(menuRightW, MenuBarH, 1f);
        Debug.Log($"[선실측] 홈 메뉴 — 열 폭 항목 {menuLeftW * 100f:F1}cm · 설명 {menuRightW * 100f:F1}cm · 줄 {menuRowH * 100f:F1}cm · 글자 항목 {menuItems[0].fontSize:F3} · 설명 {menuDesc.fontSize:F3}");
    }

    /// <summary>트리거 길게 막대 — t = 0~1(0이면 숨긴다). 길게 누르는 동안 매 프레임 불린다(할당 없음).</summary>
    public void SetHomeMenuHold(float t)
    {
        t = Mathf.Clamp01(t);
        bool show = t > 0f && holdBack.gameObject.activeSelf;
        if (holdFill.gameObject.activeSelf != show) holdFill.gameObject.SetActive(show);
        if (!show) return;
        float w = menuRightW * t;
        holdFill.localScale = new Vector3(w, MenuBarH, 1f);
        holdFill.localPosition = new Vector3(menuBarLeft + w * 0.5f, menuBarY, holdFill.localPosition.z);
    }

    private static void SetX(Transform t, float x) { Vector3 p = t.localPosition; t.localPosition = new Vector3(x, p.y, p.z); }

    private void BuildPopup()
    {
        MakeQuad(popup, "바탕", Vector2.zero, new Vector2(PanelW, PopupH), PopupColor, 0);
        const float w = PopupTabW, gap = PopupTabGap, h = 0.064f;
        for (int i = 0; i < PopupTabs; i++)
        {
            var c = new Vector2((i - 2f) * (w + gap), 0f);
            popupTabPlates[i] = MakeQuad(popup, "탭 " + i, c, new Vector2(w, h), TabOff, 1).GetComponent<Renderer>();
            popupTabTexts[i] = Place(MakeText(popup, "", 0.23f, TextAlignmentOptions.Center, 3), c, w);
            popupTabTexts[i].rectTransform.sizeDelta = new Vector2(w, h);   // 두 줄(이름 · 진행)
        }
        // 확인 창 — 탭 창 위에 덮어 뜬다. ★10-22: 측정값 초기화(홈 A)와 촉 끝 맞추기(홈 B)가 같이 쓴다 — 글은 ShowAsk가 넣는다
        resetCard = new GameObject("확인 창").transform;
        resetCard.SetParent(popup, false);
        resetCard.localPosition = new Vector3(0f, -0.018f, 0f);   // 탭 줄보다 키가 커서 아래로 조금 내려 덮는다
        MakeQuad(resetCard, "바탕", Vector2.zero, new Vector2(0.56f, 0.116f), ResetColor, 4);
        askTitle = MakeText(resetCard, "", 0.19f, TextAlignmentOptions.Center, 6);
        askTitle.transform.localPosition = new Vector3(0f, 0.026f, -0.008f);
        askKeys = MakeText(resetCard, "", 0.16f, TextAlignmentOptions.Center, 6);
        askKeys.transform.localPosition = new Vector3(0f, -0.028f, -0.008f);
        resetCard.gameObject.SetActive(false);
    }

    /// <summary>화면을 순서 측정 ↔ 자유 측정으로 바꾼다.</summary>
    public void SetMode(bool free)
    {
        guidedArea.gameObject.SetActive(!free);
        freeArea.gameObject.SetActive(free);
    }

    /// <summary>
    /// 탭 하나 — 메인 화면 아래의 탭 줄과 안내 위의 탭 줄을 같이 칠한다(10-14).
    /// current = 지금 탭 · dim = 지금은 못 간다(재는 중인 탭이 있다).
    /// </summary>
    public void SetTab(int i, string label, bool current, bool dim)
    {
        popupTabTexts[i].text = label;
        popupTabTexts[i].color = dim ? GaugeDim : current ? TipColor : Color.white;
        popupTabPlates[i].material.color = current ? TabOn : TabOff;

        // 안내 위 탭 — 메인 화면 탭 줄과 같은 색 규칙에 알파만 낮춘다(NearTabAlpha). 칸이 좁아 « · » 둘레의 띄어쓰기를 붙인다(글이 바뀔 때만 불린다).
        nearTabTexts[i].text = label.Replace(" · ", "·");
        nearTabTexts[i].color = popupTabTexts[i].color;
        Color plate = current ? TabOn : TabOff;
        plate.a = NearTabAlpha;
        nearTabPlates[i].material.color = plate;
    }

    /// <summary>
    /// 안내 테두리를 글 높이에 맞춘다(10-14). lineH · noteH = 두 줄의 글 높이(없으면 0). 윗줄은 위에서부터, 알림은 그 아래에 놓는다.
    /// 탭 줄은 테두리 윗변 바로 위다. 글이 바뀔 때만 불린다.
    /// </summary>
    private void LayoutGuide(float lineH, float noteH)
    {
        float inner = lineH + (noteH > 0f ? GuideNoteGap + noteH : 0f);
        guideH = Mathf.Max(GuideMinH, inner + GuidePad * 2f);   // 글 높이에 맞춘다(10-14 — 한때 고정했다가 대표님 의견으로 다시 가변)
        float top = guideH * 0.5f, y = top - (guideH - inner) * 0.5f;   // 글 묶음을 테두리 가운데에
        guideLine.rectTransform.sizeDelta = new Vector2(GuideW - 0.02f, Mathf.Max(lineH, 0.001f));
        guideLine.transform.localPosition = new Vector3(0f, y - lineH * 0.5f, -0.004f);
        guideNote.rectTransform.sizeDelta = new Vector2(GuideW - 0.02f, Mathf.Max(noteH, 0.001f));
        guideNote.transform.localPosition = new Vector3(0f, y - lineH - GuideNoteGap - noteH * 0.5f, -0.004f);
        PlaceGuideFrame();
    }

    /// <summary>테두리 · 탭 줄 · 배터리를 지금 guideH에 맞춘다(안내 두 줄 · 홈 메뉴가 같이 쓴다).</summary>
    private void PlaceGuideFrame()
    {
        float top = guideH * 0.5f;
        borderTop.localPosition = new Vector3(0f, top, borderTop.localPosition.z);
        borderBottom.localPosition = new Vector3(0f, -top, borderBottom.localPosition.z);
        borderLeft.localScale = borderRight.localScale = new Vector3(GuideBorderT, guideH, 1f);
        float tabY = top + 0.003f + nearTabH * 0.5f;
        for (int i = 0; i < PopupTabs; i++)
        {
            Vector3 p = nearTabTexts[i].transform.localPosition;
            nearTabTexts[i].transform.localPosition = new Vector3(p.x, tabY, p.z);
            nearTabPlates[i].transform.localPosition = new Vector3(p.x, tabY, nearTabPlates[i].transform.localPosition.z);
        }
        if (nearBattery != null) nearBattery.transform.localPosition = new Vector3(0f, tabY + nearTabH * 0.5f + nearBattery.fontSize * 0.06f, -0.004f);
    }

    /// <summary>확인 창을 탭 창 위에 덮어 띄운다 / 내린다 — 측정값 초기화 · 촉 끝 맞추기(10-22). 글은 바뀔 때만 넣는다.</summary>
    public void ShowAsk(bool on, string title, string keys)
    {
        if (on)
        {
            if (askTitle.text != title) askTitle.text = title;
            if (askKeys.text != keys) askKeys.text = keys;
        }
        if (resetCard.gameObject.activeSelf != on) resetCard.gameObject.SetActive(on);
    }

    /// <summary>자유 측정의 동작 갈래(0 굴곡·신전 · 1 측굴 · 2 회전) 가운데 지금 것을 밝힌다.</summary>
    public void SetFreeTab(int tab)
    {
        for (int i = 0; i < 3; i++) freeTabPlates[i].material.color = i == tab ? TabOn : TabOff;
    }

    /// <summary>
    /// 메인 화면을 세운다. eyePos = 화면을 볼 눈의 자리 · toward = 그 눈이 보는 수평 방향 · distance = 눈에서 화면까지(m) ·
    /// width = 화면 폭(m) · rise = 화면 아랫변이 눈높이보다 높은 만큼(m). 탭 창은 화면 아래에 붙어 있다.
    /// ★10-13 사용자 지시: 헤드셋이 아니라 <b>정해 둔 자리</b>에 한 번만 세운다 — 눈의 자리와 방향은 세션이 추적 원점에서 정해 준다.
    /// </summary>
    public void PlacePanel(Vector3 eyePos, Vector3 toward, float distance, float width, float rise)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(toward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
        float k = Mathf.Max(0.2f, width) / PanelW;
        panel.localScale = Vector3.one * k;
        Vector3 pos = eyePos + fwd * distance + Vector3.up * (rise + PanelH * 0.5f * k);
        panel.position = pos;
        panel.rotation = Quaternion.LookRotation((pos - eyePos).normalized, Vector3.up);   // 눈을 마주 보게 살짝 숙인다
        panel.gameObject.SetActive(true);
        PanelPlaced = true;
    }

    /// <summary>
    /// 안내를 켠다 — rise = 컨트롤러 끝에서 위로 띄우는 만큼(m). 자리는 Tick이 매 프레임 따라가며 잡는다.
    /// distance = 안내가 제 크기(GuideW)로 보이는 거리(m) — 컨트롤러가 그보다 가까우면 그만큼 줄여 같은 크기로 보이게 한다.
    /// 컨트롤러가 안 잡힐 때는 시선 앞 distance, 가운데보다 drop 아래에 선다.
    /// </summary>
    public void FollowGuide(float distance, float drop, float rise)
    {
        guideDistance = Mathf.Max(0.1f, distance);
        guideDrop = drop;
        guideRise = rise;
        guideSnapped = false;
        guide.gameObject.SetActive(true);
    }

    // ── 글자(상태가 바뀔 때만 부른다) ────────────────────────────────
    /// <summary>컨트롤러 끝의 이름표(다음에 찍을 점).</summary>
    public void SetTip(string tipText)
    {
        tipLabel.text = tipText;
    }

    /// <summary>
    /// 안내의 두 줄 — 지금 할 일 한 문장(색은 리치 텍스트로 넣어 온다) · 알림(없으면 빈 줄). 두 모드가 같이 쓴다.
    /// 메인 화면 아래의 안내 띠에도 같은 글을 넣는다 — 띠는 넓으니 줄바꿈을 띄어쓰기로 펼친다(상태가 바뀔 때만 불린다).
    /// </summary>
    public void SetGuide(string line, string noteText)
    {
        guideLine.text = line;
        guideNote.text = noteText;
        float w = GuideW - 0.02f;
        LayoutGuide(line.Length > 0 ? guideLine.GetPreferredValues(line, w, 0f).y : 0f,
                    noteText.Length > 0 ? guideNote.GetPreferredValues(noteText, w, 0f).y : 0f);
        wideLine.text = line.Replace("\n", "   ·   ");   // ★10-14 사용자 지시: 벽면UI의 안내 띠는 긴 바 그대로 — 컨트롤러UI의 여러 줄을 « · »로 이어 한 줄에 담는다
        wideNote.text = noteText;
    }

    /// <summary>결과표에서 지금 재는 동작의 줄을 밝힌다(-1이면 끔).</summary>
    public void SetRowNow(int motion)
    {
        bool show = motion >= 0;
        if (rowNow.gameObject.activeSelf != show) rowNow.gameObject.SetActive(show);
        if (show) rowNow.localPosition = new Vector3(cellPos[motion].x, cellPos[motion].y, -0.001f);
    }

    /// <summary>
    /// 그 동작의 각도기 그림. angle = 면 위 각의 크기(도), opposite = 그 동작과 반대쪽으로 움직인 값(반대편에 그린다).
    /// ★어느 쪽이 그림의 오른쪽인지는 여기(GaugeSide)가 정한다 — 세션은 크기와 «방향이 맞는가»만 준다.
    /// </summary>
    public void SetGauge(int motion, bool hasActive, float activeAngle, bool activeOpposite, bool hasPressure, float pressureAngle, bool pressureOpposite)
    {
        LineRenderer a = gaugeLines[motion * 2], p = gaugeLines[motion * 2 + 1], arcLine = gaugeArcs[motion];
        a.enabled = arcLine.enabled = hasActive;
        p.enabled = hasPressure;
        if (hasActive)
        {
            float deg = Mathf.Min(activeAngle, GaugeMaxDraw) * GaugeSide[motion] * (activeOpposite ? -1f : 1f);
            a.SetPosition(0, OnPanel(gaugePivot[motion]));
            a.SetPosition(1, OnPanel(GaugePoint(motion, deg, GaugeR)));
            for (int i = 0; i <= GaugeArcSegments; i++)
                arcLine.SetPosition(i, OnPanel(GaugePoint(motion, deg * i / GaugeArcSegments, GaugeR * 0.45f)));
        }
        if (hasPressure)
        {
            float deg = Mathf.Min(pressureAngle, GaugeMaxDraw) * GaugeSide[motion] * (pressureOpposite ? -1f : 1f);
            p.SetPosition(0, OnPanel(gaugePivot[motion]));
            p.SetPosition(1, OnPanel(GaugePoint(motion, deg, GaugeR)));
        }
    }

    /// <summary>판 맨 아래 한 줄(모니터 화면의 주소).</summary>
    public void SetFooter(string text)
    {
        footer.text = text;
    }

    /// <summary>결과표의 값 한 칸(동작 × 능동 · 압박). current = 지금 재는 칸 · opposite = 반대쪽으로 움직인 값(붉게).</summary>
    public void SetValue(int slot, string text, bool current, bool opposite)
    {
        values[slot].text = text;
        SetWeight(values[slot], current);
        values[slot].color = opposite ? OppositeColor : slot % 2 == 0 ? ActiveColor : PressureColor;
    }

    /// <summary>결과표의 «차이» 칸(동작 m) — 압박 − 능동. current = 지금 재는 동작이면 굵게.</summary>
    public void SetDiff(int m, string text, bool current)
    {
        if (diffs[m].text != text) diffs[m].text = text;
        SetWeight(diffs[m], current);
    }

    /// <summary>굵게/보통 — KoPub이면 B · M 글꼴을 바꾸고(합성 굵게 대신, 11-17), 없으면 종전처럼 FontStyles로.</summary>
    private void SetWeight(TextMeshPro t, bool bold)
    {
        if (fontB != null && fontB != fontM)
        {
            TMP_FontAsset face = bold ? fontB : fontM;
            if (t.font != face) t.font = face;
            if (t.fontStyle != FontStyles.Normal) t.fontStyle = FontStyles.Normal;
        }
        else t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
    }

    /// <summary>비교 시험 줄. row 0 = 굴곡 · 1 = 신전.</summary>
    public void SetCompare(int row, bool show, string activePlane, string activeDirect, string pressurePlane, string pressureDirect)
    {
        if (compareNames[row].gameObject.activeSelf != show) compareNames[row].gameObject.SetActive(show);
        int o = row * 4;
        for (int c = 0; c < 4; c++)
            if (compareValues[o + c].gameObject.activeSelf != show) compareValues[o + c].gameObject.SetActive(show);
        if (!show) return;
        compareValues[o].text = activePlane;
        compareValues[o + 1].text = activeDirect;
        compareValues[o + 2].text = pressurePlane;
        compareValues[o + 3].text = pressureDirect;
    }

    public void HideCompare()
    {
        cmpNeutralLine.enabled = cmpActiveLine.enabled = cmpPressureLine.enabled = false;
        cmpText.gameObject.SetActive(false);
    }

    /// <summary>비교 시험의 Gl ~ Gn 선을 찍힌 그대로(3차원) 그리고 값을 띄운다. 글자가 비면 선만 그린다.</summary>
    public void ShowCompare(bool hasN, Vector3 nA, Vector3 nB, bool hasA, Vector3 aA, Vector3 aB, bool hasP, Vector3 pA, Vector3 pB,
                            string label, Vector3 labelPos)
    {
        HideCompare();
        if (hasN) DrawRaw(cmpNeutralLine, nA, nB);
        if (hasA) DrawRaw(cmpActiveLine, aA, aB);
        if (hasP) DrawRaw(cmpPressureLine, pA, pB);
        if (label.Length == 0) return;
        cmpText.text = label;
        cmpText.transform.position = labelPos;
        cmpText.gameObject.SetActive(true);
    }

    // ── 점 ──────────────────────────────────────────────────────────
    /// <summary>비교 시험의 세 번째 점(Gn). phase 0 중립 · 1 능동 · 2 압박.</summary>
    public void SetExtraPoint(int phase, bool show, Vector3 pos)
    {
        Transform t = extraSpheres[phase];
        if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        if (show) t.position = pos;
    }

    public void SetPoint(int index, bool show, Vector3 pos)
    {
        if (spheres[index].gameObject.activeSelf != show) spheres[index].gameObject.SetActive(show);
        if (show) spheres[index].position = pos;
        if (index < neutralLabels.Length)
        {
            if (neutralLabels[index].gameObject.activeSelf != show) neutralLabels[index].gameObject.SetActive(show);
            if (show) neutralLabels[index].transform.position = pos + Vector3.up * 0.018f;
        }
    }

    // ── 선·각도기 ───────────────────────────────────────────────────
    public void HideMeasure()
    {
        neutralLine.enabled = activeLine.enabled = pressureLine.enabled = false;
        neutralText.gameObject.SetActive(false);
        activeText.gameObject.SetActive(false);
        pressureText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 순서 측정의 선 하나(10-14) — phase 0 중립 · 1 능동 · 2 압박. a · b = 선의 두 끝,
    /// length = 선 위에 띄울 길이 글자(mm). show가 false면 그 선과 글자를 끈다.
    /// </summary>
    public void SetSegment(int phase, bool show, Vector3 a, Vector3 b, string length)
    {
        LineRenderer lr = phase == 0 ? neutralLine : phase == 1 ? activeLine : pressureLine;
        TextMeshPro t = phase == 0 ? neutralText : phase == 1 ? activeText : pressureText;
        if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        if (!show) { lr.enabled = false; return; }
        DrawLine(lr, a, b);
        t.text = length;
        t.transform.position = (a + b) * 0.5f + Vector3.up * LengthRise;
    }

    private static void DrawRaw(LineRenderer lr, Vector3 a, Vector3 b) => DrawLine(lr, a, b);

    internal static void DrawLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.enabled = true;
    }

    // ── 매 프레임 ───────────────────────────────────────────────────
    /// <summary>
    /// 컨트롤러 끝 표시와 이름표, 글자들의 눈 쪽 돌리기. 할당 없음.
    /// tipValid = 끝의 자리가 있다(추정값이라도) · tipTracked = 헤드셋 카메라가 실제로 보고 있다.
    /// 못 보고 있으면 끝 구슬을 빨갛게 그리고 이름표를 숨긴다 — 그 상태에서는 점이 찍히지 않는다는 표시다.
    /// </summary>
    public void Tick(Transform eye, bool tipValid, bool tipTracked, Vector3 tipPos, bool showTipLabel)
    {
        if (tipTracked != tipDrawnTracked)
        {
            tipDrawnTracked = tipTracked;
            tipRenderer.material.color = tipTracked ? TipColor : TipLostColor;
            tip.localScale = Vector3.one * PointRadius * (tipTracked ? 1.6f : 2.6f);
        }
        showTipLabel &= tipTracked;
        if (tip.gameObject.activeSelf != tipValid) tip.gameObject.SetActive(tipValid);
        bool label = tipValid && showTipLabel;
        if (tipLabel.gameObject.activeSelf != label) tipLabel.gameObject.SetActive(label);
        if (tipValid)
        {
            tip.position = tipPos;
            if (label)
            {
                tipLabel.transform.position = tipPos + eye.right * 0.065f + eye.up * 0.012f;
                Face(tipLabel.transform, eye);
            }
        }
        // 안내 — ★10-13 오후 사용자 지시: 찍는 컨트롤러 바로 위에 붙어 다닌다(찍는 자리에서 눈을 떼지 않고 각을 본다).
        //   컨트롤러가 가까울수록 줄여 눈에는 같은 크기로 보인다. 컨트롤러가 안 잡히면 종전처럼 시선 앞에 선다.
        if (guide.gameObject.activeSelf)
        {
            float scale = 1f;
            Vector3 target;
            if (tipValid)
            {
                scale = Mathf.Clamp(Vector3.Distance(eye.position, tipPos) / guideDistance, 0.35f, 1.2f);
                target = tipPos + eye.up * (guideRise + guideH * 0.5f * scale);
            }
            else target = eye.position + eye.forward * guideDistance - eye.up * guideDrop;
            float f = guideSnapped ? 1f - Mathf.Exp(-GuideFollow * Time.unscaledDeltaTime) : 1f;
            guide.position = Vector3.Lerp(guide.position, target, f);
            guide.localScale = Vector3.one * Mathf.Lerp(guide.localScale.x, scale, f);
            guideSnapped = true;
            Face(guide, eye);
        }
        for (int i = 0; i < neutralLabels.Length; i++)
            if (neutralLabels[i].gameObject.activeSelf) Face(neutralLabels[i].transform, eye);
        if (neutralText.gameObject.activeSelf) Face(neutralText.transform, eye);
        if (activeText.gameObject.activeSelf) Face(activeText.transform, eye);
        if (pressureText.gameObject.activeSelf) Face(pressureText.transform, eye);
        if (cmpText.gameObject.activeSelf) Face(cmpText.transform, eye);
    }

    internal static void Face(Transform t, Transform eye)
    {
        Vector3 away = t.position - eye.position;
        if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, eye.up);
    }

    // ── 만들기 ──────────────────────────────────────────────────────
    internal Transform MakeSphere(string name, float radius, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * radius * 2f;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.material.color = c;
        r.sortingOrder = 4;
        return go.transform;
    }

    internal LineRenderer MakeLine(string name, Color c, int count)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = true;
        lr.positionCount = count;
        lr.startWidth = lr.endWidth = LineWidth;
        lr.startColor = lr.endColor = c;
        lr.numCapVertices = 2;
        lr.sortingOrder = 3;
        lr.enabled = false;
        return lr;
    }

    private Transform MakeQuad(Transform parent, string name, Vector2 center, Vector2 size, Color c, int order)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(center.x, center.y, -0.001f * order);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.material.color = c;
        r.sortingOrder = order;   // 같은 투명 큐라 그리는 순서를 정해 준다: 판 0 · 탭 · 밝힘 1 · 그림 2 · 글자 3 · 리셋 확인 4 · 6
        return go.transform;
    }

    internal TextMeshPro MakeText(Transform parent, string text, float size, TextAlignmentOptions align, int order) => MakeText(parent, text, size, align, order, null);

    /// <summary>글꼴을 골라 만든다(null = 기본 KoPub M). 컨트롤러UI 규격 글자가 B · L을 고를 때 쓴다(11-17).</summary>
    internal TextMeshPro MakeText(Transform parent, string text, float size, TextAlignmentOptions align, int order, TMP_FontAsset face)
    {
        var go = new GameObject("글자");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        TMP_FontAsset use = face != null ? face : font;
        if (use != null) t.font = use;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(0.2f, size * 2f);
        t.sortingOrder = order;
        t.text = text;
        return t;
    }

    /// <summary>공간에 띄우는 값 글자 — 흰 글자에 검은 외곽선(선의 색과 겹치지 않고, 밝은 패스스루 배경에서도 읽힌다).</summary>
    internal static TextMeshPro Outlined(TextMeshPro t)
    {
        t.color = Color.white;
        t.outlineColor = new Color32(0, 0, 0, 255);
        t.outlineWidth = 0.22f;
        return t;
    }

    internal static TextMeshPro Place(TextMeshPro t, Vector2 center, float width)
    {
        t.rectTransform.sizeDelta = new Vector2(width, t.fontSize * 2f);
        t.transform.localPosition = new Vector3(center.x, center.y, -0.004f);
        return t;
    }
}
