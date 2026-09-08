using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 경추ROM에서 <b>진행과 결과를 정보패널 한 판에 합친다.</b>
///
/// ★★<b>2026-09-07 회의 결정 + 사용자 지시.</b>
///   "실습모드도 진행Root를 실측처럼 진행에 붙이기로 했다. 그런데 그냥 위에다 올리는 게 아니라
///    내용 자체를 정보패널에 넣을 수 없나. 결과창 패널 자체를 진행 및 결과 같이 보여주는 걸로 통합."
///
/// ★<b>왜 겹치기를 버렸나</b>(씬 실측 근거):
///   진행Root는 UI가 아니라 <b>월드 오브젝트</b>다(TrainingScene: <c>!u!4 Transform</c>, 45도 기울어짐,
///   자체 캔버스를 자식으로 단다). 결과 페이지는 정보패널 프리팹 안의 <b>UI</b>다.
///   그래서 종전 <c>DockProgressIntoResultPage</c>는 <b>캔버스가 둘인 물건을 겹쳐 놓고 배율을 계산</b>했고,
///   그게 09-04에 남은 "진행창 배율 5.92배 · 결과 페이지가 3.54×2.38m로 잡힌다"의 원인이다.
///   → 내용을 <b>패널 안에서 그리면</b> 캔버스가 하나라 그 계산이 통째로 사라진다.
///
/// ★★<b>경추ROM 한정이다</b>(사용자 재확인). 게이트는 <see cref="CervicalRomScenarioBridge.RomScenarioActive"/>
///   하나이고, 나머지 12개 술기는 이 경로를 <b>아예 타지 않는다.</b>
///   ★<see cref="InfoPanelController"/>·<see cref="ScenarioGuideUIController"/>는 13개가 같이 쓰므로
///     저쪽 코드는 건드리지 않았다 — 읽는 창 하나만 열었다.
///
/// ★<b>실습·실측 둘 다</b>에 붙는다. 두 모드가 같은 자리에서 같은 모양으로 보여야 한다는 것이 회의 결정이다.
///
/// ★<b>넣은 쪽이 되돌린다</b>(07-27 xray 사고가 이 대칭이 깨져서 났다).
///   옮긴 토글의 원래 부모·로컬 TRS·형제 순서를 적어 두고, 술기를 벗어나면 그대로 돌려놓는다.
/// </summary>
public class RomProgressInResultPage : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomScenarioBridge bridge;
    [SerializeField] private ScenarioGuideUIController guideUI;
    [SerializeField] private InfoPanelController panel;
    [SerializeField] private CervicalRomRealityMeasure measure;

    [Header("=== 표시 ===")]
    [SerializeField] private bool enable = true;

    [Tooltip("진행 칸(단계·지시문·게이지·[다음])을 결과 페이지 <b>안에</b> 그린다 = 통합 형태.\n" +
             "★<b>기본은 끔</b>이다(2026-09-08 사용자 지시: 진행Root와 정보패널을 다시 분리).\n" +
             "  꺼도 아래 '제목 교체'는 그대로 돈다 — 그것만 살려 달라는 요구였다.\n" +
             "  켜면 09-07에 만든 통합 화면으로 그대로 돌아온다(코드는 지우지 않았다).")]
    [SerializeField] private bool dockProgressSection;

    [Tooltip("정보패널 제목을 이 술기의 표시명(경추ROM진단)으로 바꾼다. 나갈 때 되돌린다.")]
    [SerializeField] private bool applyPanelTitle = true;

    [Tooltip("'결과' 단계에 닿으면 시나리오를 그 자리에서 끝낸다.\n" +
             "★<b>기본은 끔</b>이다(2026-09-08). 이제 마지막 '종료' 가이드의 <b>[종료] 버튼</b>이\n" +
             "  정상 경로로 시나리오를 끝내고 결과를 채운다 — 여기서 미리 끝내면\n" +
             "  <b>[종료]를 누를 기회 자체가 사라진다</b>(결과 행에서 이미 끝나 버린다).\n" +
             "  통합 형태(dockProgressSection)로 돌아갈 때만 같이 켠다.")]
    [SerializeField] private bool completeAtResultStep;
    [Tooltip("진행 칸 높이(px).\n" +
             "★비율이 아니라 <b>픽셀</b>이다 — 붙는 자리가 Vertical Layout Group 밑이라\n" +
             "  앵커 stretch가 무시되고 자식이 자기 크기를 스스로 말해야 한다(09-07 실측).")]
    [SerializeField] private float sectionHeightPx = 220f;
    [Tooltip("진행 칸을 결과표 <b>위</b>에 둔다. 끄면 맨 아래에 붙는다.\n" +
             "★레이아웃 그룹이 있을 때만 쓰인다. 지금은 그룹이 없어 세로 가운데에 놓인다.")]
    [SerializeField] private bool placeAboveResults = true;
    [Tooltip("세로 가운데에서 이만큼 올린다/내린다(px). +면 위, -면 아래.")]
    [SerializeField] private float sectionYOffset;
    [Tooltip("단계 이름 글씨 크기 (준비 · 시상면 파지 · 굴곡 …)")]
    [SerializeField] private float stageFontSize = 30f;
    [Tooltip("[다음] 버튼을 오른쪽 끝에서 이만큼 안쪽으로(px)")]
    [SerializeField] private float buttonRightMargin = 24f;
    [Tooltip("[다음] 버튼을 아래에서 이만큼 띄운다(px)")]
    [SerializeField] private float buttonBottomMargin = 12f;
    [Tooltip("지시문 글씨 크기")] [SerializeField] private float stepFontSize = 34f;
    [Tooltip("진행 게이지 글씨 크기")] [SerializeField] private float gaugeFontSize = 28f;
    [SerializeField] private TMP_FontAsset font;

    [Tooltip("게이지 앞에 붙는 말.")]
    [SerializeField] private string gaugeLabel = "진행";

    [Tooltip("정보패널 제목을 강제로 이 글로 바꾼다.\n" +
             "★<b>비워 두는 것이 기본</b>이다 — 비어 있으면 DynamicResultTableUI.DisplayScenarioName을 쓴다.\n" +
             "  표기를 두 군데서 따로 정하면 결과지와 제목이 서로 다른 이름을 말한다.\n" +
             "  표기를 바꾸려면 그 표(DisplayScenarioName)를 고친다.")]
    [SerializeField] private string panelTitleOverride = "";

    [Header("=== 단계 / 지시문 구분 ===")]
    [Tooltip("단계 줄과 지시문 사이에 구분선을 긋는다.")]
    [SerializeField] private bool showDivider = true;
    [Tooltip("구분선 두께(px)")] [SerializeField] private float dividerThickness = 2f;
    [Tooltip("구분선 좌우 여백(px)")] [SerializeField] private float dividerSideMargin = 8f;
    [SerializeField] private Color dividerColor = new Color(1f, 1f, 1f, 0.28f);
    [Tooltip("단계 줄 뒤에 옅은 판을 깐다. 한 상자에 뭉쳐 보이는 것을 푼다.")]
    [SerializeField] private bool showStageBackdrop = true;
    [SerializeField] private Color stageBackdropColor = new Color(1f, 1f, 1f, 0.07f);
    [SerializeField] private Color stageTextColor = new Color(0.62f, 0.80f, 1f);

    [Header("=== 단계 번호 ===")]
    [Tooltip("단계 이름 앞에 번호를 붙인다.\n" +
             "★기본은 <b>끔</b>이다 — 켜면 '2. 시상면 파지'처럼 나오는데, 가이드 스텝(자세정렬·결과 등)은\n" +
             "  stepNo가 0이라 번호가 의미 없다. 그래서 어떤 줄엔 번호가 있고 어떤 줄엔 없어 들쭉날쭉했다\n" +
             "  (2026-09-07 지적). 다 붙이거나 다 빼는 것 중 <b>다 빼는 쪽</b>을 기본으로 했다.")]
    [SerializeField] private bool showStepNumber;

    [Header("=== 줄바꿈 ===")]
    [Tooltip("칸을 넘치면 글씨를 줄여 맞춘다.\n" +
             "★<b>기본은 끔</b>이다(2026-09-07 사용자 판단: \"오토사이즈는 텍스트 일관성이 들쭉날쭉해진다\").\n" +
             "  단계마다 지시문 길이가 달라 글씨 크기가 매번 달라진다 — 한 글자 넘어가는 것보다 눈에 거슬린다.\n" +
             "  줄바꿈이 어색하면 <b>글씨 크기를 낮추거나 좌우 여백을 줄이는</b> 쪽으로 푼다.")]
    [SerializeField] private bool autoShrinkText;
    [Tooltip("줄일 수 있는 최소 비율(0~1). autoShrinkText를 켰을 때만 쓰인다.")]
    [Range(0.4f, 1f)][SerializeField] private float minFontRatio = 0.85f;

    [Tooltip("글 칸의 <b>왼쪽</b> 여백(px). 좌정렬이라 글이 모서리에 붙는 것을 막는다.")]
    [SerializeField] private float textLeftMargin = 28f;
    [Tooltip("글 칸의 <b>오른쪽</b> 여백(px). 줄이면 한 줄에 더 들어가 한 글자만 넘어가는 것이 줄어든다.")]
    [SerializeField] private float textRightMargin = 10f;

    [Header("=== 단계 구분 이미지 (전부·중부·후부) ===")]
    [Tooltip("이 시나리오에 전부·중부·후부 구분이 <b>하나도 없으면</b> 그 이미지들을 접는다.\n" +
             "★경추ROM측정은 구분이 없다(CSV 실측 0건). 사각근·상부승모근은 있다.")]
    [SerializeField] private bool hideStageImagesWhenUnused = true;
    [Tooltip("접을 오브젝트 이름의 머리말. 씬에 '중전후', '중전후 (1)' … 로 들어 있다.\n" +
             "★이름으로 찾는다 — 코드에서 참조하는 컴포넌트가 없어 다른 손잡이가 없다(09-07 실측).\n" +
             "  이름이 바뀌면 조용히 죽지 않게 <b>못 찾으면 경고를 남긴다</b>(규칙 8).")]
    [SerializeField] private string stageImageNamePrefix = "중전후";

    [Header("=== 결과 단계 ===")]
    [Tooltip("이 단계 이름이면 진행 글을 접는다(결과표와 겹치지 않게). [다음] 버튼은 남는다.\n" +
             "★경추ROM측정 CSV의 마지막 측정 단계 이름이 '결과'다.")]
    [SerializeField] private string resultStepName = "결과";
    [Tooltip("이 phase면 진행 글을 접는다. 경추ROM측정은 '종료'가 마지막 phase다.")]
    [SerializeField] private string endPhaseName = "종료";

    [SerializeField] private bool showDebugLogs = true;

    // ── 되돌릴 것들 ──────────────────────────────────────────────────────
    private bool docked;
    private GameObject section;
    private TextMeshProUGUI stageText;
    private TextMeshProUGUI stepText;
    private TextMeshProUGUI gaugeText;
    private string shownStage;

    private Transform toggleHome;
    private Vector3 toggleHomePos;
    private Quaternion toggleHomeRot;
    private Vector3 toggleHomeScale;
    private int toggleHomeSibling;
    private GameObject movedToggle;

    private Transform progressRoot;
    private bool progressRootWasActive;

    private string titleHome;
    private bool titleChanged;

    // ★한 번 찾아 들고 있는다. 매 프레임 찾으면 씬 전체를 훑어 프레임이 떨어진다(09-07).
    private ScenarioManager scenarioManager;

    // 결과 단계에서 통째로 접을 글 조각들. ★[다음] 토글은 여기 안 담는다 — 결과에서도 눌러야 한다.
    private readonly System.Collections.Generic.List<GameObject> textParts
        = new System.Collections.Generic.List<GameObject>();
    private bool textPartsHidden;

    // ★우리가 접은 것만 우리가 편다. 원래 꺼져 있던 것은 담지 않는다.
    private readonly System.Collections.Generic.List<GameObject> hiddenStageImages
        = new System.Collections.Generic.List<GameObject>();

    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
    private string shownStep;
    private int shownGauge = int.MinValue;
    private int shownRemain = int.MinValue;

    private void Awake()
    {
        if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (panel == null) panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (measure == null) measure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);
        if (font == null) font = KoreanFontResolver.Resolve();
    }

    private void OnDisable() => Undock();
    private void OnDestroy() => Undock();

    private void LateUpdate()
    {
        if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);

        // ★★경추ROM 안일 때만. 이 한 줄이 '나머지 12개 술기는 안 건드린다'의 전부다.
        bool want = enable && bridge != null && bridge.RomScenarioActive;

        if (want && !docked) Dock();
        else if (!want && docked) Undock();

        if (docked) Refresh();
    }

    // ================= 넣기 =================

    private void Dock()
    {
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (panel == null) panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);

        GameObject page = panel != null ? panel.ResultPageObject : null;
        if (page == null || !(page.transform is RectTransform pageRect))
        {
            // ★조용히 넘어가지 않는다 — 아무 일도 안 일어나면 원인을 못 찾는다.
            ChunaLogger.LogWarning("[ROM진행통합] 정보패널 결과 페이지를 못 찾아 통합을 못 했습니다. " +
                                   "진행Root는 원래 자리에 그대로 둡니다.");
            return;
        }

        // ★진행 칸을 패널 안에 그리는 것은 <b>통합 형태</b>일 때만 한다 (2026-09-08).
        //   분리 형태에서는 진행Root가 자기 자리에 그대로 있어야 하므로 손대지 않는다.
        if (dockProgressSection)
        {
            BuildSection(pageRect);
            MoveNextToggleIn();
            HideProgressRoot();
        }

        // ★단계 구분 이미지 접기는 <b>더 이상 부르지 않는다</b>(2026-09-08).
        //   그 자리는 이제 RomPhaseSections가 굴곡·신전·측굴·회전 4칸으로 쓴다.
        //   ★그리고 찾던 이름('중전후')은 결과표의 행 템플릿이었고 TrainingScene에는 0개다 —
        //     불러 봐야 경고만 남는다. 함수는 남겨 뒀다(되돌릴 여지).

        // ★제목을 바꿔 건다(2026-09-07: "ROM측정 아니고 진단이야").
        //   ★원래 제목을 적어 두고 나갈 때 되돌린다 — 안 되돌리면 다음 술기 제목이 '진단'으로 남는다.
        // ★표기는 <b>한 표에서만</b> 나온다 — DynamicResultTableUI.DisplayScenarioName.
        //   결과지가 쓰는 그 표다. 제목만 따로 정하면 둘이 다른 이름을 말한다.
        string wantTitle = panelTitleOverride;
        if (string.IsNullOrEmpty(wantTitle))
        {
            ScenarioManager sm0 = FindFirstObjectByType<ScenarioManager>();
            ScenarioData sd = sm0 != null ? sm0.CurrentScenario : null;
            if (sd != null) wantTitle = DynamicResultTableUI.DisplayScenarioName(sd.scenarioName);
        }
        if (applyPanelTitle && !string.IsNullOrEmpty(wantTitle))
        {
            titleHome = panel.ScenarioTitle;
            titleChanged = true;
            panel.SetScenarioTitle(wantTitle);
        }

        // ★결과 페이지를 미리 띄우는 것은 <b>통합 형태일 때만</b>이다 — 그때는 이 페이지가
        //   곧 '진행 및 결과'라 처음부터 떠 있어야 한다.
        //   ★분리 형태에서 이걸 부르면 술기 시작하자마자 결과 페이지가 뜬다(2026-09-08).
        if (dockProgressSection) panel.ShowResultPageExternally();

        docked = true;
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ROM진행통합] 붙었다 — 진행 칸 {(dockProgressSection ? "통합" : "분리(안 그림)")} · " +
                            $"제목 {(titleChanged ? wantTitle : "안 바꿈")}</color>");
    }

    /// <summary>
    /// 결과 페이지 <b>위쪽</b>에 진행 칸을 만든다. 아래는 결과표가 쓰던 자리 그대로다.
    /// ★자식으로 넣으므로 <b>패널 캔버스를 그대로 따른다</b> — 배율을 계산할 일이 없다.
    ///   그게 겹치기와의 결정적 차이다.
    /// </summary>
    private void BuildSection(RectTransform pageRect)
    {
        section = new GameObject("ROM진행칸") { hideFlags = HideFlags.DontSave };
        var rect = section.AddComponent<RectTransform>();
        rect.SetParent(pageRect, false);

        // ★★<b>2026-09-07 — 여기서 글씨가 세로로 한 자씩 쏟아졌다.</b>
        //   붙는 자리(정보패널 결과 페이지)에 <b>Vertical Layout Group</b>이 걸려 있고
        //   그 그룹의 <c>Control Child Size</c>가 <b>꺼져 있다</b>(씬 실측).
        //   그러면 그룹은 자식의 <b>크기를 안 정해 주고</b> 자식이 가진 sizeDelta를 그대로 쓴다.
        //   새로 만든 RectTransform은 sizeDelta가 0이라 <b>폭 0</b>이 됐고,
        //   TMP가 글자마다 줄을 바꿔 세로 한 줄이 됐다.
        //   → 앵커 stretch에 기대지 않는다. <b>폭·높이를 숫자로 준다.</b>
        float height = Mathf.Max(60f, sectionHeightPx);
        var group = pageRect.GetComponent<LayoutGroup>();

        if (group == null)
        {
            // ★레이아웃 그룹이 없다 → <b>앵커가 먹는다.</b> 가로로 꽉 채우고 높이만 고정한다.
            // ★★2026-09-07 지적 — "진행 텍스트 너무 위에 있다. 중앙으로 내려."
            //   종전에는 페이지 <b>맨 위</b>에 붙였다. 결과 페이지가 크고 대부분 비어 있어서
            //   글이 천장에 매달린 것처럼 보였다. → <b>세로 가운데</b>에 놓는다.
            //   [다음] 버튼은 이 칸 안에 들어 있으므로 <b>칸이 내려가면 같이 내려간다</b>(지시 3).
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, sectionYOffset);
            rect.sizeDelta = new Vector2(0f, height);
        }
        else
        {
            // ★레이아웃 그룹이 있다 → 앵커 stretch가 무시된다. 폭·높이를 <b>숫자로</b> 준다.
            //   (09-07에 여기서 폭 0이 나와 글씨가 세로로 한 자씩 쏟아졌다.)
            float width = pageRect.rect.width;
            if (group is HorizontalOrVerticalLayoutGroup hv)
                width -= hv.padding.left + hv.padding.right;
            if (width < 1f) width = 600f;   // 아직 레이아웃이 안 돌았을 때의 예비값

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);

            var le = section.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;

            // 세로 그룹은 형제 순서대로 쌓으므로 그냥 붙이면 맨 아래로 간다.
            if (placeAboveResults) rect.SetSiblingIndex(0);
        }

        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        // ★★2026-09-07 지시 — 위에서부터 [단계] · [지시문] · [게이지] 이고, [다음]은 <b>그 아래 우측</b>이다.
        //   "텍스트 중앙 좌측 정렬 / 버튼은 중앙 우측 정렬 / 텍스트 안 가리게 텍스트 박스보다 무조건 아래로."
        //   ★<b>단계 표시가 사라진 것</b>도 여기서 되살린다 — 진행Root의 stepNameText가 접히면서
        //     같이 사라졌다. 준비·시상면 파지·굴곡 같은 이름이 그 줄이다.
        // ★★정렬을 나눈다(2026-09-07 지적: "텍스트만 좌정렬해야지 진행 게이지까지 좌 정렬시키면 어떡해").
        //   단계 이름·지시문 = <b>좌측</b>(읽는 글) · 진행 게이지 = <b>가운데</b>(눈금이라 가운데가 맞다).
        // ★단계 줄과 지시문이 "한 상자에 구분 없이" 보인다는 지적(2026-09-07) —
        //   옅은 판 + 구분선으로 두 영역을 가른다. 테두리를 두르는 것보다 글이 안 답답하다.
        const float stageTop = 1f, stageBottom = 0.70f;
        if (showStageBackdrop)
            MakePanel("단계판", rect, stageBackdropColor,
                      new Vector2(0f, stageBottom), new Vector2(1f, stageTop));

        stageText = MakeText("단계", rect, stageFontSize,
                             new Vector2(0f, stageBottom), new Vector2(1f, stageTop), TextAlignmentOptions.Left);
        stageText.color = stageTextColor;

        if (showDivider)
        {
            var line = MakePanel("구분선", rect, dividerColor,
                                 new Vector2(0f, stageBottom), new Vector2(1f, stageBottom));
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.rectTransform.offsetMin = new Vector2(dividerSideMargin, -dividerThickness * 0.5f);
            line.rectTransform.offsetMax = new Vector2(-dividerSideMargin, dividerThickness * 0.5f);
        }

        stepText = MakeText("지시문", rect, stepFontSize,
                            new Vector2(0f, 0.34f), new Vector2(1f, stageBottom), TextAlignmentOptions.Left);
        gaugeText = MakeText("진행게이지", rect, gaugeFontSize,
                             new Vector2(0f, 0.14f), new Vector2(1f, 0.34f), TextAlignmentOptions.Center);
    }

    private TextMeshProUGUI MakeText(string name, RectTransform parent, float size,
                                     Vector2 anchorMin, Vector2 anchorMax,
                                     TextAlignmentOptions align)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(textLeftMargin, 0f);
        rect.offsetMax = new Vector2(-textRightMargin, 0f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;

        // ★한글은 낱자 단위로 줄이 바뀌어 "…하세 / 요." 처럼 한 글자만 넘어간다(2026-09-07 지적).
        //   글씨를 조금 줄여 그 한 글자를 앞줄에 끌어올린다. 칸을 키우지 않고 푸는 유일한 손잡이다.
        if (autoShrinkText)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * Mathf.Clamp01(minFontRatio);
        }
        textParts.Add(go);
        return t;
    }

    /// <summary>구분선·배경판 같은 단색 판을 만든다.</summary>
    private Image MakePanel(string name, RectTransform parent, Color color,
                            Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        textParts.Add(go);
        return img;
    }

    /// <summary>
    /// [다음] 토글을 결과 페이지 안으로 옮긴다.
    /// ★<b>새 버튼을 만들지 않는다.</b> 만들면 보이는 조건(<c>UpdateStartToggleVisibility</c>)과
    ///   누를 때의 동작을 <b>두 벌</b> 유지해야 하고, 둘이 어긋나면 "눌러도 안 넘어간다"가 된다.
    ///   같은 오브젝트를 옮기면 배선이 통째로 따라온다.
    /// ★토글은 UI(RectTransform)라 패널 캔버스 밑으로 들어가면 그 캔버스 배율을 그대로 쓴다.
    /// </summary>
    private void MoveNextToggleIn()
    {
        GameObject tg = guideUI != null ? guideUI.NextToggleObject : null;
        if (tg == null || section == null) return;

        Transform t = tg.transform;
        toggleHome = t.parent;
        toggleHomePos = t.localPosition;
        toggleHomeRot = t.localRotation;
        toggleHomeScale = t.localScale;
        toggleHomeSibling = t.GetSiblingIndex();
        movedToggle = tg;

        t.SetParent(section.transform, false);

        if (t is RectTransform r)
        {
            // ★2026-09-07 지시 — <b>중앙 우측 정렬 · 텍스트 박스보다 무조건 아래.</b>
            //   글 칸들이 위 0.24까지 내려와 있으므로 그 아래(아래쪽 24%)를 버튼이 쓴다.
            //   오른쪽 끝에서 buttonRightMargin 만큼 안쪽으로 들인다.
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = new Vector2(-buttonRightMargin, buttonBottomMargin);
        }
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    /// <summary>
    /// 진행Root를 접는다. 내용이 패널로 들어왔으므로 두 군데 뜨면 안 된다.
    /// ★<b>우리가 끈 것만 우리가 켠다</b> — 원래 상태를 적어 둔다.
    /// </summary>
    private void HideProgressRoot()
    {
        if (progressRoot == null && bridge != null) progressRoot = bridge.ProgressRoot;
        if (progressRoot == null) return;

        progressRootWasActive = progressRoot.gameObject.activeSelf;
        if (progressRootWasActive) progressRoot.gameObject.SetActive(false);
    }

    // ================= 되돌리기 =================

    private void Undock()
    {
        if (!docked) return;
        docked = false;

        // ★토글을 먼저 돌려놓는다. 진행 칸을 지우면 그 자식인 토글까지 같이 사라진다.
        if (movedToggle != null && toggleHome != null)
        {
            // ★결과 단계에서 우리가 껐으면 <b>켜서</b> 돌려준다. 안 켜면 다음 술기에서 [다음]이 사라진다.
            if (!movedToggle.activeSelf) movedToggle.SetActive(true);

            Transform t = movedToggle.transform;
            t.SetParent(toggleHome, false);
            t.localPosition = toggleHomePos;
            t.localRotation = toggleHomeRot;
            t.localScale = toggleHomeScale;
            t.SetSiblingIndex(toggleHomeSibling);
        }
        movedToggle = null;
        toggleHome = null;

        if (section != null)
        {
            if (Application.isPlaying) Destroy(section);
            else DestroyImmediate(section);
        }
        section = null;
        textParts.Clear();
        textPartsHidden = false;
        stageText = null;
        stepText = null;
        gaugeText = null;
        shownStage = null;

        if (progressRoot != null && progressRootWasActive)
            progressRoot.gameObject.SetActive(true);

        // ★바꾼 쪽이 되돌린다. 안 되돌리면 다음 술기 제목이 '진단'으로 남는다.
        if (titleChanged && panel != null)
        {
            if (titleHome != null) panel.SetScenarioTitle(titleHome);
            titleChanged = false;
            titleHome = null;
        }

        // ★접은 쪽이 편다. 안 되돌리면 다음 술기에서 단계 구분이 통째로 사라진다.
        for (int i = 0; i < hiddenStageImages.Count; i++)
            if (hiddenStageImages[i] != null) hiddenStageImages[i].SetActive(true);
        hiddenStageImages.Clear();

        shownStep = null;
        shownGauge = int.MinValue;
        shownRemain = int.MinValue;

        if (showDebugLogs)
            ChunaLogger.Log("<color=cyan>[ROM진행통합] 원래대로 되돌렸다 — [다음]과 진행Root를 제자리로.</color>");
    }

    // ================= 단계 구분 이미지 =================

    /// <summary>
    /// 전부·중부·후부 구분이 <b>없는</b> 시나리오에서는 그 구분 이미지를 접는다(2026-09-07 사용자 지시).
    ///
    /// ★<b>판정은 시나리오 데이터로 한다.</b> 시나리오 이름을 코드에 박지 않는다.
    /// ★<b>어느 칸에 들어 있는지가 술기마다 다르다</b>(CSV 실측):
    ///   사각근·상부승모근은 <c>voiceInstruction</c>(전부회전·중부회전·후부회전),
    ///   견갑거근은 <c>handTrackingFileName</c>(후부측굴)에 있다. <c>stepName</c>에는 <b>없다</b> —
    ///   stepName만 보면 사각근도 "구분 없음"으로 잘못 판정한다.
    ///   → 그래서 substep의 <b>문자열 칸을 전부</b> 훑는다.
    /// ★한쪽으로 안전하게 틀린다 — 한 글자라도 걸리면 <b>그냥 보여 준다.</b>
    ///   잘못 접어서 있어야 할 표시가 사라지는 쪽이 더 나쁘다.
    /// </summary>
    private void HideStageImagesIfUnused(RectTransform pageRect)
    {
        if (!hideStageImagesWhenUnused || string.IsNullOrEmpty(stageImageNamePrefix)) return;
        if (ScenarioHasStageDistinction()) return;

        var rects = pageRect.GetComponentsInChildren<Transform>(true);
        int found = 0;
        for (int i = 0; i < rects.Length; i++)
        {
            Transform t = rects[i];
            if (!t.name.StartsWith(stageImageNamePrefix, System.StringComparison.Ordinal)) continue;
            found++;
            if (!t.gameObject.activeSelf) continue;
            t.gameObject.SetActive(false);
            hiddenStageImages.Add(t.gameObject);
        }

        // ★못 찾는 것이 이제는 <b>정상</b>이다 — 2026-09-07에 사용자가 결과창을 '진행 및 결과'로 바꾸면서
        //   안 쓰던 자식(중전후 30개)과 Vertical Layout Group을 씬에서 직접 지웠다(씬 실측: 30 → 0).
        //   그래서 경고로 올리지 않는다. 다만 이름이 바뀐 경우와 구분되게 한 줄은 남긴다(규칙 8).
        if (found == 0)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"<color=#909090>[ROM진행통합] '{stageImageNamePrefix}' 오브젝트가 없다 — " +
                                "접을 것이 없다(09-07에 씬에서 지워졌다).</color>");
        }
        else if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ROM진행통합] 이 시나리오는 전부·중부·후부 구분이 없어 " +
                            $"단계 구분 이미지 {hiddenStageImages.Count}개를 접었다(찾은 것 {found}개).</color>");
    }

    /// <summary>
    /// 결과를 보여 주는 단계인가. 여기서는 진행 글을 접는다(결과표와 겹친다).
    /// ★이름으로 판별한다 — 다른 손잡이가 없다. 이름이 바뀌면 조용히 죽으므로 인스펙터에 열어 뒀다(규칙 8).
    /// </summary>
    private bool IsResultStep(StepData step)
    {
        if (step == null) return false;
        if (!string.IsNullOrEmpty(resultStepName) && step.stepName == resultStepName) return true;

        if (string.IsNullOrEmpty(endPhaseName) || scenarioManager == null) return false;
        PhaseData ph = scenarioManager.CurrentPhase;
        return ph != null && ph.phaseName == endPhaseName;
    }

    /// <summary>이 시나리오에 전부·중부·후부 구분이 있는가. 한 칸이라도 걸리면 있다고 본다.</summary>
    private bool ScenarioHasStageDistinction()
    {
        ScenarioManager sm = FindFirstObjectByType<ScenarioManager>();
        ScenarioData data = sm != null ? sm.CurrentScenario : null;
        if (data == null) return true;   // 모르면 건드리지 않는다

        for (int p = 0; p < data.phases.Count; p++)
        {
            PhaseData phase = data.phases[p];
            if (phase == null) continue;
            for (int s = 0; s < phase.steps.Count; s++)
            {
                StepData step = phase.steps[s];
                if (step == null) continue;
                if (HasStageWord(step.stepName)) return true;
                for (int b = 0; b < step.subSteps.Count; b++)
                {
                    SubStepData sub = step.subSteps[b];
                    if (sub == null) continue;
                    if (HasStageWord(sub.voiceInstruction) || HasStageWord(sub.handTrackingFileName)
                        || HasStageWord(sub.textInstruction) || HasStageWord(sub.patientAnimationClip)
                        || HasStageWord(sub.movementType) || HasStageWord(sub.conditionParams))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool HasStageWord(string s)
        => !string.IsNullOrEmpty(s)
           && (s.Contains("전부") || s.Contains("중부") || s.Contains("후부"));

    // ================= 매 프레임 =================

    private void Refresh()
    {
        if (stepText == null || gaugeText == null) return;

        // ★★<b>매 프레임 FindFirstObjectByType을 부르면 안 된다</b>(2026-09-07 프레임 드랍).
        //   씬 전체를 훑는 호출이라 VR 프레임 예산에서는 그대로 드랍으로 나타난다.
        //   한 번 찾아 들고 있는다 — 시나리오매니저는 한 판 내내 바뀌지 않는다.
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();

        // ── 단계 이름 ──
        // ★진행Root의 stepNameText가 접히면서 <b>준비·시상면 파지·굴곡</b> 같은 표시가 통째로 사라졌다
        //   (09-07 지적). 같은 규칙으로 여기서 다시 만든다 — 가이드 스텝이면 이름만, 아니면 "번호. 이름".
        StepData cur = scenarioManager != null ? scenarioManager.CurrentStep : null;

        // ★★<b>결과 단계에서는 진행 글을 접는다</b>(2026-09-07 지적: "결과창이 나오면 진행 박스는
        //   닫아야 할 거 아니냐. 텍스트가 겹쳐 나오잖아").
        //   ★<b>[다음] 토글은 안 접는다</b> — CSV가 "결과를 확인하신 뒤 [다음]을 눌러 주세요"라고
        //     시키므로, 같이 접으면 진행이 막힌다. 그래서 글 조각만 따로 모아 뒀다.
        //   ★같은 손질로 "연습모드 결과에서 시나리오 대본이 그대로 뜬다"도 사라진다 — 지시문이 글 조각이다.
        bool atResult = IsResultStep(cur);
        if (atResult != textPartsHidden)
        {
            textPartsHidden = atResult;
            for (int i = 0; i < textParts.Count; i++)
                if (textParts[i] != null) textParts[i].SetActive(!atResult);

            // ★★<b>[다음]도 같이 접는다</b>(2026-09-07 지적: "다음 버튼이 왜 필요한데.
            //   결과 확인했으면 메인으로 나가면 되잖아"). 결과에서 할 일은 <b>보고 나가는 것</b>뿐이다.
            if (movedToggle != null) movedToggle.SetActive(!atResult);

            // ★★<b>대신 시나리오를 여기서 끝낸다.</b> 09-04에 정한 "넘기지 말고 끝낸다"다.
            //   ★[다음]을 그냥 숨기기만 하면 <b>실습에서는 결과표가 빈다</b> —
            //     실측은 CervicalRomMeasurementBridge가 이미 이 호출을 하지만(그쪽 :1228),
            //     실습은 <b>부르는 곳이 없어</b> [다음]으로 넘어가야만 결과가 채워졌다.
            //   호출은 멱등이다(이미 끝났으면 그냥 돌아온다).
            if (atResult && completeAtResultStep && scenarioManager != null)
            {
                scenarioManager.CompleteScenarioExternally();
                if (showDebugLogs)
                    ChunaLogger.Log("<color=cyan>[ROM진행통합] 결과 단계 — 시나리오를 끝냈다(결과표 채움).</color>");
            }

            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[ROM진행통합] 결과 단계 {(atResult ? "진입 — 진행 글과 [다음]을 접는다" : "이탈 — 다시 편다")}.</color>");
        }
        if (atResult) return;   // 접은 동안은 글을 갱신할 필요가 없다
        // ★번호를 붙일 거면 <b>전부</b> 붙이고, 뺄 거면 <b>전부</b> 뺀다(2026-09-07 지적).
        //   종전에는 가이드 스텝만 번호가 없어 '자세정렬' 다음에 '2. 시상면 파지'가 나왔다.
        //   가이드 스텝은 stepNo가 0이라 번호가 의미 없으므로 <b>기본은 다 빼는 쪽</b>이다.
        string stage = cur == null ? ""
                     : (showStepNumber && !cur.IsGuideStep())
                        ? $"{cur.stepNo}. {cur.stepName}"
                        : cur.stepName;
        if (stage != shownStage)
        {
            shownStage = stage;
            if (stageText != null) stageText.text = stage;
        }

        // ── 지시문 ──
        // ★새로 만들지 않고 진행Root가 쓰던 그 글을 그대로 읽는다. 두 벌로 만들면 어긋난다.
        string step = guideUI != null && guideUI.DescriptionLabel != null
                      ? guideUI.DescriptionLabel.text : "";
        if (step != shownStep)
        {
            shownStep = step;
            stepText.text = step;
        }

        // ── 진행 게이지 ──
        // ★ProgressCircle이 그리는 <b>그 값</b>이다. 타이머를 새로 만들면 화면과 실제가 어긋난다.
        bool has = guideUI != null && guideUI.ProgressVisible;
        int steps = has
            ? Mathf.Clamp(Mathf.RoundToInt((1f - guideUI.ProgressRatio01) * 10f), 0, 10)
            : -1;
        if (has && guideUI.ProgressCompleted) steps = 10;
        int remain = has ? Mathf.CeilToInt(guideUI.ProgressRemaining) : -1;

        if (steps == shownGauge && remain == shownRemain) return;
        shownGauge = steps; shownRemain = remain;

        if (steps < 0)
        {
            if (gaugeText.text.Length > 0) gaugeText.text = "";
            return;
        }

        sb.Clear();
        bool full = steps >= 10;
        if (full) sb.Append("<color=#7ad67a>");
        sb.Append(gaugeLabel).Append(' ');
        for (int i = 0; i < 10; i++) sb.Append(i < steps ? '■' : '□');
        if (remain > 0) sb.Append(' ').Append(remain).Append('초');
        if (full) sb.Append("</color>");
        gaugeText.text = sb.ToString();
    }
}
