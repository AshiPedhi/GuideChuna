#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Linq;

/// <summary>
/// 연습 씬 에디터 도구
/// 메뉴에서 Practice Mode 설정 도구 제공
/// </summary>
public class PracticeSceneEditor : EditorWindow
{
    [MenuItem("GuideChuna/Practice Mode/Setup Practice Scene")]
    public static void SetupPracticeScene()
    {
        if (!EditorUtility.DisplayDialog("연습 모드 설정",
            "현재 씬을 연습 모드로 설정하시겠습니까?\n\n" +
            "처리 항목:\n" +
            "- 시나리오 관련 컴포넌트 비활성화\n" +
            "- PracticeManager 추가\n" +
            "- 기존 UI/버튼 연결",
            "설정", "취소"))
        {
            return;
        }

        // 1. 충돌하는 컴포넌트 비활성화
        DisableConflictingComponents();

        // 2. PracticeManager 생성
        GameObject practiceManager = CreatePracticeManager();

        // 3. 감지 스크립트 연결
        SetupDetectors(practiceManager);

        // 4. 기존 씬 오브젝트와 연결 (기존 UI 사용)
        ConnectExistingObjects(practiceManager);

        // 씬 저장 표시
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("완료", "연습 모드 설정이 완료되었습니다!\n\n" +
            "기존 UI를 그대로 사용합니다.\n" +
            "인스펙터에서 PracticeManager의 버튼 연결을 확인해주세요.", "확인");

        // PracticeManager 선택
        Selection.activeGameObject = practiceManager;
    }

    private static GameObject CreatePracticeManager()
    {
        // 기존 PracticeManager 확인
        PracticeManager existing = Object.FindFirstObjectByType<PracticeManager>();
        if (existing != null)
        {
            Debug.Log("[PracticeEditor] PracticeManager already exists");
            return existing.gameObject;
        }

        GameObject managerObj = new GameObject("PracticeManager");
        managerObj.AddComponent<PracticeManager>();

        Debug.Log("[PracticeEditor] PracticeManager created");
        return managerObj;
    }

    /// <summary>
    /// 시나리오 모드와 충돌하는 컴포넌트 비활성화 (에디터 시점)
    /// </summary>
    private static void DisableConflictingComponents()
    {
        int disabledCount = 0;

        // ScenarioManager 비활성화
        var scenarioManager = Object.FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.enabled = false;
            disabledCount++;
            Debug.Log("[PracticeEditor] ScenarioManager disabled");
        }

        // ScenarioConditionManager 비활성화
        var conditionManager = Object.FindFirstObjectByType<ScenarioConditionManager>();
        if (conditionManager != null)
        {
            conditionManager.enabled = false;
            disabledCount++;
            Debug.Log("[PracticeEditor] ScenarioConditionManager disabled");
        }

        // HandPoseTrainingController 비활성화
        var trainingController = Object.FindFirstObjectByType<HandPoseTrainingController>();
        if (trainingController != null)
        {
            trainingController.enabled = false;
            disabledCount++;
            Debug.Log("[PracticeEditor] HandPoseTrainingController disabled");
        }

        // TrainingResultTracker 비활성화
        var resultTracker = Object.FindFirstObjectByType<TrainingResultTracker>();
        if (resultTracker != null)
        {
            resultTracker.enabled = false;
            disabledCount++;
            Debug.Log("[PracticeEditor] TrainingResultTracker disabled");
        }

        // QuizPanel 게임오브젝트 비활성화
        var quizPanel = Object.FindFirstObjectByType<QuizPanel>();
        if (quizPanel != null)
        {
            quizPanel.gameObject.SetActive(false);
            disabledCount++;
            Debug.Log("[PracticeEditor] QuizPanel disabled");
        }

        // 결과 패널 비활성화
        var resultPanels = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
            .Where(g => g.name.ToLower().Contains("result") && g.name.ToLower().Contains("panel"));
        foreach (var panel in resultPanels)
        {
            panel.SetActive(false);
            disabledCount++;
            Debug.Log($"[PracticeEditor] {panel.name} disabled");
        }

        Debug.Log($"[PracticeEditor] Disabled {disabledCount} conflicting components");
    }

    private static GameObject CreatePracticeUI()
    {
        // 기존 PracticeUI 확인
        PracticeUI existing = Object.FindFirstObjectByType<PracticeUI>();
        if (existing != null)
        {
            Debug.Log("[PracticeEditor] PracticeUI already exists");
            return existing.gameObject;
        }

        // OVRCameraRig 찾기
        var cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
        Transform cameraTransform = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main?.transform;

        // Canvas 생성
        GameObject canvasObj = new GameObject("PracticeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Canvas 위치 설정
        if (cameraTransform != null)
        {
            canvasObj.transform.position = cameraTransform.position + cameraTransform.forward * 1.5f + Vector3.up * 0.5f;
            canvasObj.transform.rotation = Quaternion.LookRotation(canvasObj.transform.position - cameraTransform.position);
        }
        else
        {
            canvasObj.transform.position = new Vector3(0, 1.5f, 1.5f);
        }

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800, 600);
        canvasObj.transform.localScale = Vector3.one * 0.001f;

        // PracticeUI 컴포넌트 추가
        PracticeUI practiceUI = canvasObj.AddComponent<PracticeUI>();

        // UI 요소들 생성
        CreateUIElements(canvasObj.transform, practiceUI);

        Debug.Log("[PracticeEditor] PracticeUI created");
        return canvasObj;
    }

    private static void CreateUIElements(Transform parent, PracticeUI practiceUI)
    {
        // === 메인 패널 ===
        GameObject mainPanel = CreatePanel(parent, "PracticePanel", new Vector2(0, 100), new Vector2(700, 300));

        // 제목 텍스트
        TMP_Text stepTitle = CreateText(mainPanel.transform, "StepTitleText", "Step 1: UI 옮기기 연습", new Vector2(0, 80), 36);

        // 안내 텍스트
        TMP_Text instruction = CreateText(mainPanel.transform, "InstructionText", "UI 패널을 잡고 원하는 위치로 옮겨보세요", new Vector2(0, 20), 24);

        // 진행률 텍스트
        TMP_Text progress = CreateText(mainPanel.transform, "ProgressText", "0 / 3", new Vector2(0, -40), 20);

        // 진행률 슬라이더
        Slider slider = CreateSlider(mainPanel.transform, "ProgressSlider", new Vector2(0, -80));

        // === 단계 완료 패널 ===
        GameObject stepCompletePanel = CreatePanel(parent, "StepCompletePanel", new Vector2(0, -150), new Vector2(400, 80));
        TMP_Text stepCompleteText = CreateText(stepCompletePanel.transform, "StepCompleteText", "단계 완료!", Vector2.zero, 28);
        stepCompletePanel.SetActive(false);

        // === 전체 완료 패널 ===
        GameObject completePanel = CreatePanel(parent, "CompletePanel", Vector2.zero, new Vector2(600, 400));
        CreateText(completePanel.transform, "CompleteTitleText", "🎉 연습 완료!", new Vector2(0, 100), 48);
        TMP_Text completeMessage = CreateText(completePanel.transform, "CompleteMessageText",
            "모든 연습을 완료했습니다!\n\n이제 실제 시나리오를 진행해보세요.", new Vector2(0, 0), 24);

        // 메인 메뉴 버튼
        GameObject menuBtnObj = CreateButton(completePanel.transform, "MainMenuButton", "메인 메뉴로", new Vector2(0, -120), new Vector2(200, 50));
        Button menuBtn = menuBtnObj.GetComponent<Button>();

        completePanel.SetActive(false);

        // PracticeUI에 참조 연결 (SerializedObject 사용)
        SerializedObject so = new SerializedObject(practiceUI);
        so.FindProperty("stepTitleText").objectReferenceValue = stepTitle;
        so.FindProperty("instructionText").objectReferenceValue = instruction;
        so.FindProperty("progressText").objectReferenceValue = progress;
        so.FindProperty("progressSlider").objectReferenceValue = slider;
        so.FindProperty("stepCompletePanel").objectReferenceValue = stepCompletePanel;
        so.FindProperty("stepCompleteText").objectReferenceValue = stepCompleteText;
        so.FindProperty("practiceCompletePanel").objectReferenceValue = completePanel;
        so.FindProperty("completeMessageText").objectReferenceValue = completeMessage;
        so.FindProperty("mainMenuButton").objectReferenceValue = menuBtn;
        so.ApplyModifiedProperties();
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        return panel;
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, Vector2 position, int fontSize)
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
        rect.sizeDelta = new Vector2(650, 60);

        return tmpText;
    }

    private static Slider CreateSlider(Transform parent, string name, Vector2 position)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);

        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchoredPosition = position;
        sliderRect.sizeDelta = new Vector2(500, 20);

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0;

        // Background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
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
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        // Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.7f, 1f, 1f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.sizeDelta = Vector2.zero;

        slider.fillRect = fillRect;
        slider.targetGraphic = fillImage;

        return slider;
    }

    private static GameObject CreateButton(Transform parent, string name, string text, Vector2 position, Vector2 size)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.2f, 0.5f, 0.8f, 1f);

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // 텍스트
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        TMP_Text tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = 20;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = Color.white;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        return btnObj;
    }

    private static void SetupDetectors(GameObject practiceManagerObj)
    {
        PracticeManager pm = practiceManagerObj.GetComponent<PracticeManager>();

        // LateralFlexionDetector 생성
        LateralFlexionDetector existingLFD = Object.FindFirstObjectByType<LateralFlexionDetector>();
        if (existingLFD == null)
        {
            GameObject detectorObj = new GameObject("LateralFlexionDetector");
            LateralFlexionDetector lfd = detectorObj.AddComponent<LateralFlexionDetector>();
            lfd.SetPracticeManager(pm);

            // OVRHand 찾아서 연결 (이름으로 구분)
            var hands = Object.FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            SerializedObject lfdSO = new SerializedObject(lfd);
            foreach (var hand in hands)
            {
                string handName = hand.gameObject.name.ToLower();
                if (handName.Contains("left"))
                    lfdSO.FindProperty("leftHand").objectReferenceValue = hand;
                else if (handName.Contains("right"))
                    lfdSO.FindProperty("rightHand").objectReferenceValue = hand;
            }
            lfdSO.ApplyModifiedProperties();

            Debug.Log("[PracticeEditor] LateralFlexionDetector created");
        }
    }

    private static void ConnectExistingObjects(GameObject practiceManagerObj)
    {
        PracticeManager pm = practiceManagerObj.GetComponent<PracticeManager>();
        SerializedObject so = new SerializedObject(pm);

        // PracticeUI 연결
        PracticeUI practiceUI = Object.FindFirstObjectByType<PracticeUI>();
        if (practiceUI != null)
        {
            so.FindProperty("practiceUI").objectReferenceValue = practiceUI;
        }

        // ChunaPathEvaluator 찾기
        var chunaEvaluator = Object.FindFirstObjectByType<ChunaPathEvaluator>();
        if (chunaEvaluator != null)
        {
            so.FindProperty("guideHandDisplay").objectReferenceValue = chunaEvaluator;
            Debug.Log("[PracticeEditor] ChunaPathEvaluator connected");
        }

        // 환자 모델 찾기 (Patient 태그 또는 이름으로)
        GameObject patient = GameObject.FindGameObjectWithTag("Patient");
        if (patient == null)
        {
            patient = GameObject.Find("Patient");
            if (patient == null)
            {
                // 이름에 Patient 포함된 오브젝트 찾기
                var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var obj in allObjects)
                {
                    if (obj.name.ToLower().Contains("patient") || obj.name.ToLower().Contains("환자"))
                    {
                        patient = obj.gameObject;
                        break;
                    }
                }
            }
        }
        if (patient != null)
        {
            so.FindProperty("patientTransform").objectReferenceValue = patient.transform;

            // PatientPositionDetector 추가
            PatientPositionDetector ppd = patient.GetComponent<PatientPositionDetector>();
            if (ppd == null)
            {
                ppd = patient.AddComponent<PatientPositionDetector>();
                ppd.SetPracticeManager(pm);
                Debug.Log("[PracticeEditor] PatientPositionDetector added to Patient");
            }
        }

        // UI 버튼들 찾기 (Canvas 내부에서)
        var allButtons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);

        // 난이도 버튼 찾기
        var difficultyButtons = allButtons.Where(b =>
            b.name.ToLower().Contains("difficulty") ||
            b.name.ToLower().Contains("난이도") ||
            b.name.ToLower().Contains("easy") ||
            b.name.ToLower().Contains("normal") ||
            b.name.ToLower().Contains("hard")).ToList();

        if (difficultyButtons.Count > 0)
        {
            SerializedProperty diffBtnProp = so.FindProperty("difficultyButtons");
            diffBtnProp.ClearArray();
            for (int i = 0; i < difficultyButtons.Count; i++)
            {
                diffBtnProp.InsertArrayElementAtIndex(i);
                diffBtnProp.GetArrayElementAtIndex(i).objectReferenceValue = difficultyButtons[i];
            }
            Debug.Log($"[PracticeEditor] Found {difficultyButtons.Count} difficulty buttons");
        }

        // 설정 버튼 찾기
        var settingsBtn = allButtons.FirstOrDefault(b =>
            b.name.ToLower().Contains("setting") ||
            b.name.ToLower().Contains("설정") ||
            b.name.ToLower().Contains("config"));
        if (settingsBtn != null)
        {
            so.FindProperty("settingsButton").objectReferenceValue = settingsBtn;
            Debug.Log("[PracticeEditor] Settings button found");
        }

        // 가이드 핸드 토글 버튼 찾기
        var guideHandBtn = allButtons.FirstOrDefault(b =>
            b.name.ToLower().Contains("guide") ||
            b.name.ToLower().Contains("가이드") ||
            b.name.ToLower().Contains("hand"));
        if (guideHandBtn != null)
        {
            so.FindProperty("guideHandToggleButton").objectReferenceValue = guideHandBtn;
            Debug.Log("[PracticeEditor] Guide hand toggle button found");
        }

        // Grabbable UI 찾기
        var grabbables = Object.FindObjectsByType<OVRGrabbable>(FindObjectsSortMode.None);
        if (grabbables.Length > 0)
        {
            // Canvas를 가진 Grabbable 우선
            var uiGrabbable = grabbables.FirstOrDefault(g => g.GetComponentInChildren<Canvas>() != null);
            if (uiGrabbable == null)
                uiGrabbable = grabbables[0];

            so.FindProperty("grabbableUI").objectReferenceValue = uiGrabbable.transform;

            // UIGrabDetector 추가
            UIGrabDetector ugd = uiGrabbable.GetComponent<UIGrabDetector>();
            if (ugd == null)
            {
                ugd = uiGrabbable.gameObject.AddComponent<UIGrabDetector>();
                ugd.SetPracticeManager(pm);
                Debug.Log("[PracticeEditor] UIGrabDetector added to Grabbable UI");
            }
        }

        so.ApplyModifiedProperties();
    }

    [MenuItem("GuideChuna/Practice Mode/Open Practice Scene")]
    public static void OpenPracticeScene()
    {
        string scenePath = "Assets/Scenes/Practice_Scene.unity";

        if (!System.IO.File.Exists(Application.dataPath.Replace("Assets", "") + scenePath))
        {
            EditorUtility.DisplayDialog("오류", "Practice_Scene.unity 파일이 없습니다.\n\n" +
                "먼저 Scenario_1 씬을 Practice_Scene으로 복사해주세요.", "확인");
            return;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(scenePath);
        }
    }

    [MenuItem("GuideChuna/Practice Mode/Create Practice Scene from Scenario_1")]
    public static void CreatePracticeSceneFromScenario1()
    {
        string sourcePath = "Assets/Scenes/Scenario_1.unity";
        string destPath = "Assets/Scenes/Practice_Scene.unity";

        if (!AssetDatabase.CopyAsset(sourcePath, destPath))
        {
            // 파일 시스템으로 직접 복사
            string fullSourcePath = Application.dataPath.Replace("Assets", "") + sourcePath;
            string fullDestPath = Application.dataPath.Replace("Assets", "") + destPath;

            if (System.IO.File.Exists(fullSourcePath))
            {
                System.IO.File.Copy(fullSourcePath, fullDestPath, true);
                AssetDatabase.Refresh();
                Debug.Log("[PracticeEditor] Practice_Scene.unity created");
            }
        }

        EditorUtility.DisplayDialog("완료", "Practice_Scene.unity가 생성되었습니다.\n\n" +
            "메뉴에서 'Open Practice Scene'을 선택하여 씬을 열고,\n" +
            "'Setup Practice Scene'으로 연습 모드를 설정하세요.", "확인");
    }
}
#endif
