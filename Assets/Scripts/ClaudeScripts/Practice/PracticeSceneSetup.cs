using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 연습 씬 자동 설정 도우미
/// 에디터에서 이 컴포넌트를 사용하여 연습 씬을 쉽게 구성
/// </summary>
public class PracticeSceneSetup : MonoBehaviour
{
    [Header("=== 자동 생성할 오브젝트 ===")]
    [SerializeField] private bool createOnStart = false;

    [Header("=== 필수 참조 (기존 씬에서) ===")]
    [Tooltip("OVR Camera Rig")]
    [SerializeField] private Transform cameraRig;

    [Tooltip("환자 모델")]
    [SerializeField] private Transform patientModel;

    [Tooltip("가이드 핸드 표시 컴포넌트")]
    [SerializeField] private MonoBehaviour guideHandDisplay;

    [Header("=== 생성된 오브젝트 (자동 할당) ===")]
    [SerializeField] private PracticeManager practiceManager;
    [SerializeField] private PracticeUI practiceUI;
    [SerializeField] private UIGrabDetector uiGrabDetector;
    [SerializeField] private PatientPositionDetector patientPositionDetector;
    [SerializeField] private LateralFlexionDetector lateralFlexionDetector;

    void Start()
    {
        if (createOnStart)
        {
            SetupPracticeScene();
        }
    }

    /// <summary>
    /// 연습 씬 구성 (에디터에서 버튼으로 호출)
    /// </summary>
    [ContextMenu("Setup Practice Scene")]
    public void SetupPracticeScene()
    {
        ChunaLogger.Log("[PracticeSetup] Starting scene setup...");

        // 1. PracticeManager 생성
        CreatePracticeManager();

        // 2. PracticeUI 생성
        CreatePracticeUI();

        // 3. 감지 스크립트 연결
        SetupDetectors();

        ChunaLogger.Log("[PracticeSetup] Scene setup complete!");
    }

    private void CreatePracticeManager()
    {
        if (practiceManager != null) return;

        GameObject managerObj = new GameObject("PracticeManager");
        practiceManager = managerObj.AddComponent<PracticeManager>();

        ChunaLogger.Log("[PracticeSetup] PracticeManager created");
    }

    private void CreatePracticeUI()
    {
        if (practiceUI != null) return;

        // Canvas 생성
        GameObject canvasObj = new GameObject("PracticeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Canvas 위치 설정 (카메라 앞)
        if (cameraRig != null)
        {
            canvasObj.transform.position = cameraRig.position + cameraRig.forward * 2f;
            canvasObj.transform.rotation = Quaternion.LookRotation(canvasObj.transform.position - cameraRig.position);
        }

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800, 600);
        canvasObj.transform.localScale = Vector3.one * 0.001f;

        // PracticeUI 추가
        practiceUI = canvasObj.AddComponent<PracticeUI>();

        // UI 요소들 생성
        CreateUIElements(canvasObj.transform);

        ChunaLogger.Log("[PracticeSetup] PracticeUI created");
    }

    private void CreateUIElements(Transform parent)
    {
        // 메인 패널
        GameObject mainPanel = CreatePanel(parent, "PracticePanel", new Vector2(0, 100), new Vector2(700, 300));

        // 제목 텍스트
        CreateText(mainPanel.transform, "StepTitleText", "Step 1: 시작", new Vector2(0, 80), 36);

        // 안내 텍스트
        CreateText(mainPanel.transform, "InstructionText", "안내 메시지가 여기에 표시됩니다", new Vector2(0, 20), 24);

        // 진행률 텍스트
        CreateText(mainPanel.transform, "ProgressText", "0 / 3", new Vector2(0, -40), 20);

        // 진행률 슬라이더
        CreateSlider(mainPanel.transform, "ProgressSlider", new Vector2(0, -80));

        // 단계 완료 패널
        GameObject stepCompletePanel = CreatePanel(parent, "StepCompletePanel", new Vector2(0, -100), new Vector2(400, 100));
        CreateText(stepCompletePanel.transform, "StepCompleteText", "단계 완료!", Vector2.zero, 30);
        stepCompletePanel.SetActive(false);

        // 전체 완료 패널
        GameObject completePanel = CreatePanel(parent, "CompletePanel", Vector2.zero, new Vector2(600, 400));
        CreateText(completePanel.transform, "CompleteTitleText", "연습 완료!", new Vector2(0, 80), 40);
        CreateText(completePanel.transform, "CompleteMessageText", "모든 연습을 완료했습니다!", new Vector2(0, 0), 24);
        completePanel.SetActive(false);

        ChunaLogger.Log("[PracticeSetup] UI elements created");
    }

    private GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0.8f);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        return panel;
    }

    private TMP_Text CreateText(Transform parent, string name, string text, Vector2 position, int fontSize)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TMP_Text tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = Color.white;

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(600, 50);

        return tmpText;
    }

    private Slider CreateSlider(Transform parent, string name, Vector2 position)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);

        Slider slider = sliderObj.AddComponent<Slider>();

        RectTransform rect = sliderObj.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(400, 20);

        // Background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.sizeDelta = Vector2.zero;

        // Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.6f, 1f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;

        slider.fillRect = fillRect;

        return slider;
    }

    private void SetupDetectors()
    {
        // 환자 위치 감지
        if (patientModel != null && patientPositionDetector == null)
        {
            patientPositionDetector = patientModel.gameObject.AddComponent<PatientPositionDetector>();
            patientPositionDetector.SetPracticeManager(practiceManager);
            ChunaLogger.Log("[PracticeSetup] PatientPositionDetector added");
        }

        // 측굴 감지
        if (lateralFlexionDetector == null)
        {
            GameObject detectorObj = new GameObject("LateralFlexionDetector");
            lateralFlexionDetector = detectorObj.AddComponent<LateralFlexionDetector>();
            lateralFlexionDetector.SetPracticeManager(practiceManager);

            if (guideHandDisplay != null)
            {
                lateralFlexionDetector.SetChunaPathEvaluator(guideHandDisplay);
            }

            ChunaLogger.Log("[PracticeSetup] LateralFlexionDetector added");
        }
    }

    /// <summary>
    /// 씬 구성 가이드 출력
    /// </summary>
    [ContextMenu("Print Setup Guide")]
    public void PrintSetupGuide()
    {
        string guide = @"
=== 연습 씬 구성 가이드 ===

1. 필수 오브젝트:
   - OVRCameraRig (Meta XR SDK)
   - 환자 모델
   - Grabbable UI (OVRGrabbable 컴포넌트 포함)

2. PracticeManager 설정:
   - practiceUI: PracticeUI 컴포넌트
   - grabbableUI: 그랩 가능한 UI Transform
   - difficultyButtons: 난이도 버튼들 (List<Button>)
   - panelButtons: 안내 패널 버튼들 (List<Button>)
   - settingsButton: 설정 버튼
   - patientTransform: 환자 Transform
   - guideHandToggleButton: 가이드 핸드 토글 버튼
   - guideHandDisplay: 가이드 핸드 표시 컴포넌트

3. 감지 스크립트:
   - UIGrabDetector: Grabbable UI에 추가
   - PatientPositionDetector: 환자 모델에 추가
   - LateralFlexionDetector: 별도 오브젝트에 추가

4. UI 구성:
   - World Space Canvas
   - PracticeUI 컴포넌트
   - 각 패널과 텍스트 연결

5. 버튼 이벤트:
   - 난이도 버튼, 패널 버튼 등은 PracticeManager가 자동 연결
   - UIGrabDetector의 OnGrabBegin/OnGrabEnd는 OVRGrabbable 이벤트에 연결

6. 가이드 핸드 데이터:
   - Resources/PoseData/practice_hand 경로에 저장
   - 기존 시나리오 데이터 재사용 가능

";
        ChunaLogger.Log(guide);
    }
}
