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
        font = f;
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
        guideLine = Place(MakeText(guide, "", 0.16f, TextAlignmentOptions.Center, 3), Vector2.zero, GuideW - 0.02f);
        guideNote = Place(MakeText(guide, "", 0.085f, TextAlignmentOptions.Center, 3), Vector2.zero, GuideW - 0.02f);
        guideNote.textWrappingMode = TextWrappingModes.Normal;
        guideNote.color = NoteColor;
        // 바탕이 없어 밝은 배경(패스스루)에서 글자가 묻힌다 — 검은 외곽선을 두른다
        guideLine.outlineColor = guideNote.outlineColor = new Color32(0, 0, 0, 255);
        guideLine.outlineWidth = guideNote.outlineWidth = 0.22f;

        // 탭 줄의 복제(10-14 사용자 지시) — 안내 테두리 바로 위, <b>테두리 폭에 맞춘다</b>.
        //   ★같은 날 사용자 지시: 메인 화면의 탭(칸 바탕)을 그대로 옮긴 첫 판은 너무 튀었다 → 바탕 없이 글자만, 지금 탭만 밝은 글자 + 밑줄.
        float cellW = (GuideW - NearTabGap * (PopupTabs - 1)) / PopupTabs;
        for (int i = 0; i < PopupTabs; i++)
        {
            var c = new Vector2((i - 2f) * (cellW + NearTabGap), 0f);
            nearTabTexts[i] = Place(MakeText(guide, "", 0.076f, TextAlignmentOptions.Center, 3), c, cellW);
            nearTabTexts[i].rectTransform.sizeDelta = new Vector2(cellW, NearTabH);
            nearTabTexts[i].outlineColor = new Color32(0, 0, 0, 255);
            nearTabTexts[i].outlineWidth = 0.2f;
            nearTabBgs[i] = MakeQuad(guide, "탭 바탕 " + i, c, new Vector2(cellW, NearTabH), NearTabBg, 1);   // 지금 탭만 켠다 — 옅은 바탕으로 «여기»를 보인다
            nearTabBgs[i].gameObject.SetActive(false);
            nearTabLines[i] = MakeQuad(guide, "탭 밑줄 " + i, c, new Vector2(cellW * 0.8f, 0.0025f), NearTabLine, 2);
            nearTabLines[i].gameObject.SetActive(false);
        }
        LayoutGuide(0f, 0f);
        guide.gameObject.SetActive(false);

        // 맨 아래 한 줄 — 모니터 화면의 주소
        footer = Place(MakeText(panel, "", 0.075f, TextAlignmentOptions.Center, 3), new Vector2(0f, -PanelH * 0.5f + 0.010f), PanelW - 0.02f);
        footer.color = new Color(0.60f, 0.66f, 0.72f, 1f);
    }

    // ── 결과표 ── ★10-13 사용자 지시: 3 × 2 칸을 풀어 <b>왼쪽에 여섯 줄</b>(측정 명칭 | 능동 | 압박), <b>오른쪽에 각도기 셋</b>으로 나눈다.
    //   같은 단면의 두 동작(굴곡 · 신전 / 우측굴 · 좌측굴 / 우회전 · 좌회전)은 <b>한 각도기</b>에 좌우로 그린다 — 각도기는 그 두 줄 옆에 크게 선다.
    //   각도기 그림은 «보는 방향»을 정해 그린 약도다(찍은 점의 좌표를 그대로 옮긴 그림이 아니다). 값은 세션이 준 «면 위 각» 그대로다.
    //   ★10-13 저녁 사용자 지시: 그림의 좌우를 뒤집었다 — 양옆 표시가 «뒤 · 앞» → «앞 · 뒤», «우 · 좌» → «좌 · 우»다. 글자만 바꾼 것이 아니라 선이 기우는 쪽(GaugeSide)도 같이 뒤집었다.
    //     굴곡 · 신전 = 옆에서 본 그림(왼쪽이 환자 앞) · 측굴 = 환자 왼쪽이 그림 왼쪽 · 회전 = 위에서 본 그림(위가 환자 앞 · 환자 왼쪽이 그림 왼쪽)
    //     ★모니터 화면의 세 그림은 종전 방향 그대로다(헤드셋 결과표와 좌우가 반대다).
    //   줄의 순서도 «좌측굴 · 우측굴», «좌회전 · 우회전»으로 바꿨다(DisplayRow) — 보여 주는 자리만이다. 재는 순서와 동작 번호(우 → 좌)는 그대로다.
    private const float RowH = 0.041f, GaugeR = 0.068f;
    private const float XName = -0.31f, XActive = -0.17f, XPressure = -0.05f, GaugeX = 0.20f;   // 판 가운데에서 잰 자리
    private const float LeftX = -0.18f, LeftW = 0.42f;   // 왼쪽 표(지금 줄을 밝히는 띠)의 가운데와 폭
    private const float PopupH = 0.088f, PopupGap = 0.015f;   // 탭 한 줄뿐이다(아래의 설명 세 줄은 뺐다, 10-12)
    private const float BandH = 0.075f;                       // 탭 줄 아래 안내 띠의 높이(배율 전, 10-14)
    private const float NearTabH = 0.022f, NearTabGap = 0.002f; // 안내 위 탭 줄(실제 크기 m, 10-14) — 다섯 칸을 안내 테두리 폭(GuideW)에 맞춘다
    private const float GuidePad = 0.008f, GuideNoteGap = 0.004f, GuideMinH = 0.040f, GuideBorderT = 0.0015f;   // 안내 테두리 안쪽 여백 · 두 줄 사이 · 최소 높이 · 테두리 굵기(10-14)
    private static readonly Color NearTabIdle = new Color(0.88f, 0.91f, 0.95f, 0.92f);   // 안내 위 탭 — 지금 탭이 아닌 것(바탕 없음). ★10-14: 0.75 알파는 눈에 안 들어온다는 지적으로 밝혔다
    private static readonly Color NearTabLine = new Color(1f, 0.95f, 0.2f, 1f);           // 지금 탭의 밑줄 — 메인 화면 탭 줄의 «지금 탭» 글자색(TipColor)과 같은 노랑
    private static readonly Color NearTabBg = new Color(1f, 1f, 1f, 0.16f);              // 지금 탭의 옅은 바탕
    private const float GuideW = 0.24f;                       // 안내 테두리의 폭(m) — ★10-13 오후 사용자 지시: 길쭉한 띠(0.40 × 0.085) 말고 조금 넓은 직사각형. 높이는 10-14부터 글에 맞춘다(LayoutGuide)
    private const float GuideFollow = 10f;                   // 안내가 컨트롤러를 따라오는 빠르기(1/초) — 손 떨림은 걸러 내되 손을 옮기면 바로 따라붙는다
    private static readonly Color GuideBorder = new Color(1f, 1f, 1f, 0.35f);
    private const float GaugeMaxDraw = 100f;     // 지움: 이보다 큰 각은 그림에서 여기까지만 그린다(반원 밖으로 넘어간다) — 숫자는 그대로 나온다
    private const int GaugeArcSegments = 12;
    private static readonly float[] GaugeZeroY = { 1f, 1f, 1f, 1f, 1f, 1f };         // 0° 선이 향하는 쪽(위 +1 · 아래 −1) — 회전도 위로 세웠다(10-13 저녁 사용자 지시 · 종전에는 아래가 환자 앞이라 뒤집혀 보였다)
    private static readonly float[] GaugeSide = { -1f, 1f, 1f, -1f, 1f, -1f };       // 그 동작이 제 방향으로 움직였을 때 선이 기우는 쪽(그림 오른쪽 +1) — 10-13 저녁에 전부 뒤집었다
    private static readonly int[] DisplayRow = { 0, 1, 3, 2, 5, 4 };                  // 동작 번호 → 결과표의 줄(좌측굴 · 좌회전을 먼저 보여 준다)
    private static readonly string[] GaugeView = { "옆에서 본 그림", "옆에서 본 그림", "앞에서 본 그림", "앞에서 본 그림", "위에서 본 그림", "위에서 본 그림" };
    private static readonly string[] GaugeLeft = { "앞", "앞", "좌", "좌", "좌", "좌" };
    private static readonly string[] GaugeRight = { "뒤", "뒤", "우", "우", "우", "우" };
    private static readonly string[] FreeTabNames = { "3점", "4점", "연속" };   // 자유 측정의 방식(10-13) — RomLineSession.FreeTabName과 같다
    private static readonly Color NoteColor = new Color(0.80f, 0.86f, 0.93f, 1f);     // 알림 · 안내 — 주황(측정값)과 섞이지 않게 푸른 회색
    private static readonly Color DimColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    private static readonly Color GaugeDim = new Color(0.42f, 0.48f, 0.56f, 1f);
    private static readonly Color OppositeColor = new Color(1f, 0.45f, 0.45f, 1f);    // 반대쪽으로 움직인 값
    private static readonly Color RowNowColor = new Color(0.15f, 0.21f, 0.29f, 1f);
    private static readonly Color PopupColor = new Color(0.09f, 0.12f, 0.17f, 0.96f);
    private static readonly Color TabCurrent = new Color(0.20f, 0.29f, 0.40f, 1f);     // 지금 재는 탭(스틱이 다른 탭 위에 있을 때)
    private static readonly Color ResetColor = new Color(0.42f, 0.13f, 0.13f, 1f);

    private Transform resultArea, popup, rowNow, resetCard;
    private Transform guide;                 // 안내 — 헤드셋이 보는 쪽을 따라온다
    private float guideDistance, guideDrop, guideRise;
    private bool guideSnapped;
    private TextMeshPro guideLine, guideNote, wideLine, wideNote;
    private readonly Transform[] nearTabLines = new Transform[PopupTabs], nearTabBgs = new Transform[PopupTabs];
    private readonly TextMeshPro[] nearTabTexts = new TextMeshPro[PopupTabs];
    private Transform borderTop, borderBottom, borderLeft, borderRight;
    private float guideH = GuideMinH;        // 지금 안내 테두리의 높이(글에 맞춘다 — LayoutGuide)
    private readonly Vector2[] cellPos = new Vector2[6], gaugePivot = new Vector2[6];
    private readonly LineRenderer[] gaugeLines = new LineRenderer[12];   // 동작 × (능동 · 압박)
    private readonly LineRenderer[] gaugeArcs = new LineRenderer[6];     // 0° 선에서 능동 선까지의 호
    private readonly Renderer[] freeTabPlates = new Renderer[3], popupTabPlates = new Renderer[PopupTabs];
    private readonly TextMeshPro[] popupTabTexts = new TextMeshPro[PopupTabs];

    private void BuildResults(float top)
    {
        float headY = top - 0.098f, rowTop = top - 0.112f;
        Place(MakeText(resultArea, "측정", 0.12f, TextAlignmentOptions.Left, 3), new Vector2(XName, headY), 0.14f).color = DimColor;
        Place(MakeText(resultArea, "능동", 0.15f, TextAlignmentOptions.Center, 3), new Vector2(XActive, headY), 0.10f).color = ActiveColor;
        Place(MakeText(resultArea, "압박", 0.15f, TextAlignmentOptions.Center, 3), new Vector2(XPressure, headY), 0.10f).color = PressureColor;
        Place(MakeText(resultArea, "각도기", 0.12f, TextAlignmentOptions.Center, 3), new Vector2(GaugeX, headY), 0.10f).color = DimColor;

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

            gaugeArcs[m] = MakePanelLine("각도기 호", ActiveColor, GaugeArcSegments + 1, 0.0020f);
            gaugeLines[m * 2 + 1] = MakePanelLine("각도기 압박선", PressureColor, 2, 0.0030f);
            gaugeLines[m * 2] = MakePanelLine("각도기 능동선", ActiveColor, 2, 0.0036f);
            if (m % 2 == 1) continue;   // 아래는 단면마다 한 번만 그린다

            Place(MakeText(resultArea, GaugeLeft[m], 0.12f, TextAlignmentOptions.Center, 3), gaugePivot[m] + Vector2.left * (GaugeR + 0.014f), 0.02f).color = GaugeDim;
            Place(MakeText(resultArea, GaugeRight[m], 0.12f, TextAlignmentOptions.Center, 3), gaugePivot[m] + Vector2.right * (GaugeR + 0.014f), 0.02f).color = GaugeDim;
            Place(MakeText(resultArea, GaugeView[m], 0.10f, TextAlignmentOptions.Center, 3), new Vector2(0.345f, pairY), 0.10f).color = GaugeDim;

            Seg(verts, cols, tris, new Vector2(-PanelW * 0.5f + 0.01f, pairY + RowH), new Vector2(PanelW * 0.5f - 0.01f, pairY + RowH), 0.0012f, GaugeDim);   // 두 줄 묶음의 윗변
            Seg(verts, cols, tris, gaugePivot[m] + Vector2.left * GaugeR, gaugePivot[m] + Vector2.right * GaugeR, 0.0016f, GaugeDim);
            for (int i = 0; i < 18; i++)
                Seg(verts, cols, tris, GaugePoint(m, -90f + i * 10f, GaugeR), GaugePoint(m, -80f + i * 10f, GaugeR), 0.0016f, GaugeDim);
            for (int deg = -90; deg <= 90; deg += 15)   // 눈금 15° — 45° · 90°는 길게
                Seg(verts, cols, tris, GaugePoint(m, deg, GaugeR - (deg % 45 == 0 ? 0.011f : 0.006f)), GaugePoint(m, deg, GaugeR), 0.0016f, GaugeDim);
            Seg(verts, cols, tris, gaugePivot[m], GaugePoint(m, 0f, GaugeR), 0.0024f, NeutralColor);   // 0° = 중립
        }

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

    // ── 탭 창 ── A/X로 부른다. 메인 화면 아래에 따로 뜬다. 탭 다섯 + 지금 탭의 진행 + 알림 + 버튼 안내.
    //   «리셋»만 고른 뒤 A를 눌러야 하고(확인 창이 한 번 더 뜬다), 나머지 탭은 스틱이 옮겨 가는 대로 바뀐다.
    public const int PopupTabs = 5;

    private void BuildPopup()
    {
        MakeQuad(popup, "바탕", Vector2.zero, new Vector2(PanelW, PopupH), PopupColor, 0);
        const float w = 0.150f, gap = 0.006f, h = 0.064f;
        for (int i = 0; i < PopupTabs; i++)
        {
            var c = new Vector2((i - 2f) * (w + gap), 0f);
            popupTabPlates[i] = MakeQuad(popup, "탭 " + i, c, new Vector2(w, h), TabOff, 1).GetComponent<Renderer>();
            popupTabTexts[i] = Place(MakeText(popup, "", 0.23f, TextAlignmentOptions.Center, 3), c, w);
            popupTabTexts[i].rectTransform.sizeDelta = new Vector2(w, h);   // 두 줄(이름 · 진행)
        }
        // 리셋 확인 — 탭 창 위에 덮어 뜬다
        resetCard = new GameObject("리셋 확인").transform;
        resetCard.SetParent(popup, false);
        resetCard.localPosition = new Vector3(0f, -0.018f, 0f);   // 탭 줄보다 키가 커서 아래로 조금 내려 덮는다
        MakeQuad(resetCard, "바탕", Vector2.zero, new Vector2(0.56f, 0.116f), ResetColor, 4);
        MakeText(resetCard, "전체 측정 결과를 지울까요?", 0.19f, TextAlignmentOptions.Center, 6).transform.localPosition = new Vector3(0f, 0.026f, -0.008f);
        MakeText(resetCard, "A = 지우기          B = 취소", 0.16f, TextAlignmentOptions.Center, 6).transform.localPosition = new Vector3(0f, -0.028f, -0.008f);
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

        // 안내 위 탭 — 바탕 없이 글자만. 지금 탭 = 흰 굵은 글자 + 밑줄 · 나머지 = 흐린 글자. 칸이 좁아 « · » 둘레의 띄어쓰기를 붙인다(글이 바뀔 때만 불린다).
        nearTabTexts[i].text = label.Replace(" · ", "·");
        nearTabTexts[i].color = dim ? GaugeDim : current ? Color.white : NearTabIdle;
        nearTabTexts[i].fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
        if (nearTabLines[i].gameObject.activeSelf != current) nearTabLines[i].gameObject.SetActive(current);
        if (nearTabBgs[i].gameObject.activeSelf != current) nearTabBgs[i].gameObject.SetActive(current);
    }

    /// <summary>
    /// 안내 테두리를 글 높이에 맞춘다(10-14). lineH · noteH = 두 줄의 글 높이(없으면 0). 윗줄은 위에서부터, 알림은 그 아래에 놓는다.
    /// 탭 줄은 테두리 윗변 바로 위다. 글이 바뀔 때만 불린다.
    /// </summary>
    private void LayoutGuide(float lineH, float noteH)
    {
        float inner = lineH + (noteH > 0f ? GuideNoteGap + noteH : 0f);
        guideH = Mathf.Max(GuideMinH, inner + GuidePad * 2f);
        float top = guideH * 0.5f, y = top - (guideH - inner) * 0.5f;   // 글 묶음을 테두리 가운데에
        guideLine.rectTransform.sizeDelta = new Vector2(GuideW - 0.02f, Mathf.Max(lineH, 0.001f));
        guideLine.transform.localPosition = new Vector3(0f, y - lineH * 0.5f, -0.004f);
        guideNote.rectTransform.sizeDelta = new Vector2(GuideW - 0.02f, Mathf.Max(noteH, 0.001f));
        guideNote.transform.localPosition = new Vector3(0f, y - lineH - GuideNoteGap - noteH * 0.5f, -0.004f);

        borderTop.localPosition = new Vector3(0f, top, borderTop.localPosition.z);
        borderBottom.localPosition = new Vector3(0f, -top, borderBottom.localPosition.z);
        borderLeft.localScale = borderRight.localScale = new Vector3(GuideBorderT, guideH, 1f);
        float tabY = top + 0.003f + NearTabH * 0.5f;
        for (int i = 0; i < PopupTabs; i++)
        {
            Vector3 p = nearTabTexts[i].transform.localPosition;
            nearTabTexts[i].transform.localPosition = new Vector3(p.x, tabY, p.z);
            nearTabLines[i].localPosition = new Vector3(p.x, tabY - NearTabH * 0.5f, nearTabLines[i].localPosition.z);
            nearTabBgs[i].localPosition = new Vector3(p.x, tabY, nearTabBgs[i].localPosition.z);
        }
    }

    /// <summary>리셋 확인 창(«전체 측정 결과를 지울까요?»)을 탭 창 위에 덮어 띄운다 / 내린다.</summary>
    public void ShowResetAsk(bool on)
    {
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
        wideLine.text = line.Replace("\n", "     ");
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
        values[slot].fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
        values[slot].color = opposite ? OppositeColor : slot % 2 == 0 ? ActiveColor : PressureColor;
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

    internal TextMeshPro MakeText(Transform parent, string text, float size, TextAlignmentOptions align, int order)
    {
        var go = new GameObject("글자");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
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
