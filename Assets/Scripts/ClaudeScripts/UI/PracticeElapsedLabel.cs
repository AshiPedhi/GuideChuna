using UnityEngine;
using TMPro;

/// <summary>
/// 골격 페이지 <b>좌상단</b>에 "실습 진행 중 - 실습 소요 시간 3:24"를 띄운다.
///
/// ★<b>2026-09-08 사용자 지시</b>: "골격 보여주는 화면에 '실습 진행 중 - 실습 소요 시간' 이렇게
///   추가로 넣어 줄 수 있나? 좌상단에 공간 여백 좀 띄워서."
///
/// ★<b>시간을 새로 세지 않는다.</b> <see cref="TrainingResultTracker.SessionElapsed"/> 하나만 읽는다 —
///   표시 쪽이 자기 타이머를 만들면 조건이 조금만 달라도 화면과 기록이 다른 말을 한다(08-24 전례).
/// ★<b>13개 술기가 같이 쓰는 정보패널</b>에 붙는다. 그래서 <see cref="InfoPanelController"/>에는
///   <b>읽기 접근자 하나</b>만 내고(골격 페이지), 그리는 일은 전부 이쪽에서 한다.
/// ★붙는 자리(골격 페이지)는 자식이 없고 레이아웃 그룹도 없다(씬 실측) —
///   09-07에 글씨가 세로로 쏟아졌던 그 함정이 여기엔 없다. 그래도 폭은 <b>숫자로</b> 준다.
/// </summary>
public class PracticeElapsedLabel : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private InfoPanelController panel;
    [SerializeField] private CervicalRomScenarioBridge bridge;

    [Header("=== 표시 ===")]
    [SerializeField] private bool enableLabel = true;

    [Tooltip("경추ROM 시나리오에서만 띄운다.\n" +
             "★<b>기본은 켬</b>(2026-09-08 지시: \"경추ROM만 나오게 게이트 걸어 줘\").\n" +
             "  이 컴포넌트는 13개 술기가 같이 쓰는 정보패널에 붙으므로, 안 막으면 전부에 뜬다.\n" +
             "★끄면 골격 페이지를 여는 모든 술기에서 보인다.")]
    [SerializeField] private bool romOnly = true;

    [Header("=== 바탕 (밝은 배경 대비) ===")]
    [Tooltip("글자 뒤에 어두운 판을 깐다.\n" +
             "★골격 페이지는 <b>RawImage(렌더텍스처)</b>라 배경이 밝다 —\n" +
             "  글씨만 얹으면 묻힌다(2026-09-08 지적).")]
    [SerializeField] private bool showBackdrop = true;
    [SerializeField] private Color backdropColor = new Color(0.05f, 0.06f, 0.09f, 0.72f);
    [Tooltip("판이 글자보다 좌우/위아래로 넉넉한 정도(px)")]
    [SerializeField] private float backdropPadX = 18f;
    [SerializeField] private float backdropPadY = 8f;

    [Tooltip("앞에 붙는 말. 시간은 이 뒤에 이어 붙는다.")]
    [SerializeField] private string prefix = "실습 진행 중 - 실습 소요 시간 ";

    [Tooltip("실습이 아직 시작 전이거나 끝난 뒤에 띄울 글. 비우면 그때는 아무것도 안 띄운다.")]
    [SerializeField] private string idleText = "";

    [SerializeField] private float fontSize = 26f;
    [SerializeField] private Color textColor = new Color(0.86f, 0.90f, 1f, 0.95f);

    [Header("=== 좌상단 여백 (px) ===")]
    [SerializeField] private float marginLeft = 36f;
    [SerializeField] private float marginTop = 28f;
    [Tooltip("글 칸의 가로·세로 크기(px). 레이아웃 그룹이 없어도 확실히 잡히게 숫자로 준다.")]
    [SerializeField] private float boxWidth = 620f;
    [SerializeField] private float boxHeight = 44f;

    [SerializeField] private bool showDebugLogs = true;

    private TextMeshProUGUI label;
    private RectTransform backdrop;
    private TrainingResultTracker tracker;
    private int shownSeconds = -1;
    private bool shownTracking;
    private float nextSilentLog;
    private string lastSilentReason;

    private void Awake()
    {
        if (panel == null) panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실습시간] 붙었다 — 정보패널 {(panel != null ? "있음" : "★없음")}</color>");
    }

    private void OnDisable() => Teardown();
    private void OnDestroy() => Teardown();

    private void LateUpdate()
    {
        if (!enableLabel) { Teardown(); return; }

        // ★경추ROM 안에서만. 이 한 줄이 '나머지 12개 술기는 안 건드린다'의 전부다.
        if (romOnly)
        {
            if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
            if (bridge == null || !bridge.RomScenarioActive)
            {
                Teardown();
                ReportSilent(bridge == null ? "브리지를 못 찾았다" : "경추ROM 시나리오가 아니다");
                return;
            }
        }

        if (panel == null) panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (panel == null) { ReportSilent("정보패널이 씬에 없다"); return; }

        GameObject page = panel.SkeletonPageObject;
        if (page == null) { ReportSilent("골격 페이지가 안 물려 있다"); return; }

        // ★페이지가 꺼져 있으면 글도 같이 접힌다 — 자식이라 저절로 그렇게 된다.
        if (label == null) Build(page);
        if (label == null) return;

        Refresh();
    }

    private void Build(GameObject page)
    {
        if (!(page.transform is RectTransform pageRect))
        {
            ReportSilent("골격 페이지가 RectTransform이 아니다");
            return;
        }

        // ★바탕판을 <b>먼저</b> 만든다 — 형제 순서가 곧 그리는 순서라, 나중에 만들면 글자를 덮는다.
        if (showBackdrop)
        {
            var bg = new GameObject("실습소요시간_바탕") { hideFlags = HideFlags.DontSave };
            backdrop = bg.AddComponent<RectTransform>();
            backdrop.SetParent(pageRect, false);
            backdrop.anchorMin = backdrop.anchorMax = new Vector2(0f, 1f);
            backdrop.pivot = new Vector2(0f, 1f);
            backdrop.anchoredPosition = new Vector2(marginLeft - backdropPadX,
                                                    -(marginTop - backdropPadY));
            var img = bg.AddComponent<UnityEngine.UI.Image>();
            img.color = backdropColor;
            img.raycastTarget = false;
        }

        var go = new GameObject("실습소요시간") { hideFlags = HideFlags.DontSave };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(pageRect, false);

        // ★좌상단 고정. 앵커·피벗을 모두 (0,1)로 두고 여백만큼 안쪽으로 민다.
        //   ★크기는 <b>숫자로</b> 준다 — stretch에 기대면 부모가 바뀔 때 폭 0으로 무너진다(09-07 전례).
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(boxWidth, boxHeight);
        rect.anchoredPosition = new Vector2(marginLeft, -marginTop);

        label = go.AddComponent<TextMeshProUGUI>();
        label.font = KoreanFontResolver.Resolve();
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        shownSeconds = -1;
        shownTracking = false;
        lastSilentReason = null;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실습시간] 골격 페이지 좌상단에 만들었다 " +
                            $"(여백 {marginLeft:F0}/{marginTop:F0}px)</color>");
    }

    private void Refresh()
    {
        // ★매 프레임 FindFirstObjectByType을 부르지 않는다 — 씬 전체를 훑는 호출이라
        //   VR 프레임 예산에서는 그대로 드랍으로 나타난다(09-07에 실제로 겪었다).
        if (tracker == null) tracker = FindFirstObjectByType<TrainingResultTracker>();

        bool tracking = tracker != null && tracker.IsTracking;
        int secs = tracking ? Mathf.FloorToInt(tracker.SessionElapsed) : 0;

        // ★초가 바뀔 때만 글을 다시 만든다. 매 프레임 문자열을 만들면 VR에서 쓰레기가 쌓인다.
        if (tracking == shownTracking && secs == shownSeconds) return;
        shownTracking = tracking;
        shownSeconds = secs;

        label.text = tracking
            ? prefix + TrainingResultData.FormatTime(secs)
            : idleText;

        FitBackdrop();
    }

    /// <summary>바탕판을 글자 길이에 맞춘다. 글이 비면 판도 접는다.</summary>
    private void FitBackdrop()
    {
        if (backdrop == null || label == null) return;

        if (string.IsNullOrEmpty(label.text))
        {
            // ★빈 판이 남아 있으면 "무언가 떠 있다"로 보인다. 글이 없으면 판도 없앤다.
            if (backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(false);
            return;
        }
        if (!backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(true);

        label.ForceMeshUpdate();
        float w = Mathf.Min(label.preferredWidth, boxWidth);
        backdrop.sizeDelta = new Vector2(w + backdropPadX * 2f, boxHeight + backdropPadY * 2f);
    }

    private void Teardown()
    {
        // ★넣은 쪽이 되돌린다. 남겨 두면 다음 술기에서 멈춘 시간이 그대로 떠 있다.
        //   ★바탕판도 같이 지운다 — 하나만 지우면 검은 판만 남는다.
        if (label != null)
        {
            if (Application.isPlaying) Destroy(label.gameObject);
            else DestroyImmediate(label.gameObject);
            label = null;
        }
        if (backdrop != null)
        {
            if (Application.isPlaying) Destroy(backdrop.gameObject);
            else DestroyImmediate(backdrop.gameObject);
            backdrop = null;
        }
        shownSeconds = -1;
        shownTracking = false;
    }

    /// <summary>★막힐 때만 말한다. 그리기 시작하면 저절로 조용해진다.</summary>
    private void ReportSilent(string reason)
    {
        if (!showDebugLogs) return;
        if (reason == lastSilentReason && Time.unscaledTime < nextSilentLog) return;
        lastSilentReason = reason;
        nextSilentLog = Time.unscaledTime + 2f;
        ChunaLogger.LogWarning($"<color=orange>[실습시간] 안 그린다 — {reason}</color>");
    }
}
