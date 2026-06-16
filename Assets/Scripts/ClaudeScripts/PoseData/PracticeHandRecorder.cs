using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using System.Text;
using System.Globalization;

/// <summary>
/// 실습 진행 중 손 동작을 자동으로 녹화하는 컴포넌트
/// - 시나리오에 핸드 데이터가 있는 단계에서만 녹화
/// - 시작 홀드 완료 후 녹화 시작
/// - 핸드 데이터의 마지막 프레임 도달 시 녹화 종료
/// </summary>
public class PracticeHandRecorder : MonoBehaviour
{
    [Header("=== 녹화 대상 ===")]
    [SerializeField] private HandVisual leftHandVisual;
    [SerializeField] private HandVisual rightHandVisual;

    [Header("=== OpenXR Root (자동 탐색) ===")]
    [SerializeField] private Transform leftOpenXRRoot;
    [SerializeField] private Transform rightOpenXRRoot;

    [Header("=== 참조 설정 ===")]
    [Tooltip("ChunaPathEvaluator - 자동 탐색됨")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("ScenarioManager - 자동 탐색됨")]
    [SerializeField] private ScenarioManager scenarioManager;

    [Tooltip("기준점 - 환자 모델 (Patient 태그로 자동 탐색)")]
    [SerializeField] private Transform referencePoint;

    [Tooltip("자동으로 Patient 태그 오브젝트 찾기")]
    [SerializeField] private bool autoFindPatient = true;

    [Header("=== 녹화 설정 ===")]
    [SerializeField] private bool autoRecordEnabled = true;
    [SerializeField] private float recordInterval = 0.033f;  // ~30fps
    [SerializeField] private bool recordLeftHand = true;
    [SerializeField] private bool recordRightHand = true;

    [Header("=== 파일 설정 ===")]
    [SerializeField] private string saveFolder = "RecordedHandPose";
    [SerializeField] private bool includeTimestamp = true;

    [Header("=== 시나리오 선택 토글 (선택) ===")]
    [Tooltip("시나리오 대단원 토글 목록. 켜진 토글의 이름이 파일명에 사용됨. 미할당 시 자동 추출")]
    [SerializeField] private ScenarioToggleEntry[] scenarioToggles;
    [Tooltip("선택된 시나리오 이름 (읽기 전용)")]
    [SerializeField] private string selectedScenarioName = "";
    [Tooltip("드롭다운 헤더 텍스트. 선택된 시나리오 이름으로 자동 갱신")]
    [SerializeField] private TMP_Text scenarioDropdownLabel;
    [Tooltip("미선택 시 헤더에 표시할 텍스트")]
    [SerializeField] private string scenarioPlaceholderText = "시나리오 선택";

    [Serializable]
    public class ScenarioToggleEntry
    {
        [Tooltip("이 토글을 켰을 때 파일명에 들어갈 시나리오 대단원 이름")]
        public string scenarioName = "";

        [Tooltip("토글 본체")]
        public Toggle toggle;

        [Tooltip("이 항목의 자식 라벨 (할당 시 scenarioName으로 자동 동기화)")]
        public TMP_Text itemLabel;
    }

    [Header("=== 녹화 시작 시 비활성 ===")]
    [Tooltip("녹화 시작 시 OFF로 전환할 위치 설정 토글 (예: 환자 위치 조정 토글)")]
    [SerializeField] private Toggle positionAdjustToggle;
    [Tooltip("녹화 시작 시 비활성화할 위치 조정 오브젝트 (예: 환자 위치 조정 컨트롤러)")]
    [SerializeField] private GameObject positionAdjustObject;

    [Header("=== 침대(테이블) 가시성 ===")]
    [Tooltip("침대 모델 루트. 토글로 표시/숨김. 녹화 시 실제 환자/침대 위치 정렬용 가이드")]
    [SerializeField] private GameObject tableObject;
    [Tooltip("침대 표시 토글. ON = 표시, OFF = 숨김(SetActive false)")]
    [SerializeField] private Toggle tableToggle;
    [Tooltip("침대 반투명 알파. 기본 0.5 (반투명)")]
    [SerializeField][Range(0f, 1f)] private float tableDefaultAlpha = 0.5f;
    [Tooltip("씬 시작 시 침대 표시 여부. true = 반투명으로 켜진 상태로 시작")]
    [SerializeField] private bool startWithTableVisible = true;
    [Tooltip("강제 교체용 Transparent 셰이더 (옵션). 비어 있으면 원본 셰이더 모드 전환만 시도. " +
             "URP 환경에서 변환이 안 먹으면 'Universal Render Pipeline/Unlit' 또는 'Sprites/Default' 할당")]
    [SerializeField] private Shader forceTransparentShader;
    [Tooltip("최초 적용 시 머티리얼 셰이더 이름을 로그로 출력 (진단용)")]
    [SerializeField] private bool logTableShaders = true;

    // 침대 렌더러 캐싱 (자식 전체 - 다중 MeshRenderer + 다중 머티리얼 슬롯 지원)
    private Renderer[] tableRenderers;
    private bool tableShadersLogged = false;

    [Header("=== UI 토글 (선택) ===")]
    [Tooltip("녹화 시작/중지 토글. 인스펙터 미할당 시 UI 없이 자동/스크립트로만 동작")]
    [SerializeField] private Toggle recordToggle;
    [Tooltip("상태에 따라 sprite가 바뀌는 아이콘 (선택)")]
    [SerializeField] private Image toggleIcon;
    [Tooltip("상태에 따라 text가 바뀌는 라벨 (선택)")]
    [SerializeField] private TMP_Text toggleLabel;
    [Tooltip("녹화 중이 아닐 때 표시할 아이콘")]
    [SerializeField] private Sprite iconStopped;
    [Tooltip("녹화 중일 때 표시할 아이콘")]
    [SerializeField] private Sprite iconRecording;
    [Tooltip("녹화 중이 아닐 때 표시할 텍스트")]
    [SerializeField] private string labelStopped = "녹화 시작";
    [Tooltip("녹화 중일 때 표시할 텍스트")]
    [SerializeField] private string labelRecording = "녹화 종료";

    [Header("=== 녹화 상태 (읽기 전용) ===")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private int recordedFrames = 0;
    [SerializeField] private string currentPhaseName = "";
    [SerializeField] private string currentStepName = "";
    [SerializeField] private string lastSavedFilePath = "";

    // 내부 데이터
    private List<FrameData> recordedData = new List<FrameData>();
    private float lastRecordTime = 0f;
    private float recordingStartTime = 0f;
    private int currentFrameIndex = 0;
    private StringBuilder csvBuilder = new StringBuilder(1024 * 100);
    private bool hasHandDataInCurrentStep = false;
    private int totalHandDataFrames = 0;  // 핸드 데이터 총 프레임 수

    // 이벤트
    public event Action<string> OnRecordingStarted;  // 파일명 전달
    public event Action<string, int> OnRecordingStopped;  // 파일 경로, 프레임 수 전달

    [Serializable]
    private class FrameData
    {
        public int frameIndex;
        public string handType;
        public int jointId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public float timestamp;
    }

    private void Awake()
    {
        // ChunaPathEvaluator 자동 탐색
        if (pathEvaluator == null)
        {
            pathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        }

        // ScenarioManager 자동 탐색
        if (scenarioManager == null)
        {
            scenarioManager = FindFirstObjectByType<ScenarioManager>();
        }
    }

    private void OnEnable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete += OnStartHoldCompleteHandler;
            pathEvaluator.OnUserFrameChanged += OnUserFrameChangedHandler;
            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompletedHandler;
            pathEvaluator.OnSubStepStarted += OnSubStepStartedHandler;
            ChunaLogger.Log("<color=cyan>[PracticeHandRecorder] ChunaPathEvaluator 이벤트 연결됨</color>");
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeHandRecorder] ChunaPathEvaluator를 찾을 수 없습니다.");
        }

        if (recordToggle != null)
        {
            recordToggle.onValueChanged.AddListener(OnRecordToggleChanged);
        }

        if (tableToggle != null)
        {
            tableToggle.onValueChanged.AddListener(OnTableToggleChanged);
        }

        SubscribeScenarioToggles();
    }

    private void OnDisable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete -= OnStartHoldCompleteHandler;
            pathEvaluator.OnUserFrameChanged -= OnUserFrameChangedHandler;
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompletedHandler;
            pathEvaluator.OnSubStepStarted -= OnSubStepStartedHandler;
        }

        if (recordToggle != null)
        {
            recordToggle.onValueChanged.RemoveListener(OnRecordToggleChanged);
        }

        if (tableToggle != null)
        {
            tableToggle.onValueChanged.RemoveListener(OnTableToggleChanged);
        }

        UnsubscribeScenarioToggles();

        // 녹화 중이면 저장
        if (isRecording)
        {
            StopRecordingAndSave();
        }
    }

    private void Start()
    {
        FindOpenXRRoots();
        FindPatientReference();
        EnsureSaveFolder();
        UpdateToggleUI();
        SyncScenarioItemLabels();
        SyncSelectedScenarioFromToggles();
        UpdateScenarioDropdownLabel();
        InitializeTable();
    }

    private void Update()
    {
        if (isRecording && Time.time - lastRecordTime >= recordInterval)
        {
            RecordFrame();
            lastRecordTime = Time.time;
        }
    }

    /// <summary>
    /// SubStep 시작 시 핸드 데이터 유무 확인
    /// </summary>
    private void OnSubStepStartedHandler(int subStepIndex)
    {
        // 핸드 데이터가 있는지 확인
        totalHandDataFrames = pathEvaluator.GetLoadedFrameCount();
        hasHandDataInCurrentStep = totalHandDataFrames > 0;

        // 현재 페이즈/단계 이름 저장
        if (scenarioManager != null)
        {
            currentPhaseName = scenarioManager.CurrentPhase?.phaseName ?? "";
            currentStepName = scenarioManager.CurrentStep?.stepName ?? "";
        }

        if (hasHandDataInCurrentStep)
        {
            ChunaLogger.Log($"<color=cyan>[PracticeHandRecorder] {currentPhaseName}/{currentStepName}: 핸드 데이터 있음 ({totalHandDataFrames} 프레임) - 녹화 대기</color>");
        }
    }

    /// <summary>
    /// 시작 홀드 완료 - 녹화 시작
    /// </summary>
    private void OnStartHoldCompleteHandler()
    {
        if (!autoRecordEnabled) return;
        if (!hasHandDataInCurrentStep) return;

        StartRecording();
    }

    /// <summary>
    /// 프레임 변경 시 마지막 프레임 도달 확인 - 녹화 종료
    /// </summary>
    private void OnUserFrameChangedHandler(int currentFrame, int totalFrames, float ratio)
    {
        if (!isRecording) return;

        // 오토플레이 진행 이벤트는 무시 (totalFrames가 1인 경우)
        // 실제 핸드 데이터 프레임이 있을 때만 체크
        if (totalFrames <= 1) return;

        // 마지막 프레임에 도달하면 녹화 종료
        if (currentFrame >= totalFrames - 1)
        {
            ChunaLogger.Log($"<color=yellow>[PracticeHandRecorder] 마지막 프레임 도달 ({currentFrame + 1}/{totalFrames}) - 녹화 종료</color>");
            StopRecordingAndSave();
        }
    }

    /// <summary>
    /// 평가 완료 - 녹화 중이면 저장
    /// </summary>
    private void OnEvaluationCompletedHandler(ChunaPathEvaluator.EvaluationSession session)
    {
        if (isRecording)
        {
            StopRecordingAndSave();
        }
    }

    /// <summary>
    /// 녹화 시작
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            ChunaLogger.LogWarning("[PracticeHandRecorder] 이미 녹화 중입니다.");
            return;
        }

        // 위치 설정 토글/오브젝트 종료 (녹화 중 위치 조정 방지)
        DisablePositionAdjust();

        // OpenXR Root 재탐색
        FindOpenXRRoots();

        recordedData.Clear();
        recordedFrames = 0;
        currentFrameIndex = 0;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;
        isRecording = true;

        string fileName = GenerateFileName();
        ChunaLogger.Log($"<color=green>[PracticeHandRecorder] 녹화 시작: {fileName}</color>");

        OnRecordingStarted?.Invoke(fileName);
        UpdateToggleUI();
    }

    /// <summary>
    /// 녹화 중지 및 저장
    /// </summary>
    public void StopRecordingAndSave()
    {
        if (!isRecording)
        {
            ChunaLogger.LogWarning("[PracticeHandRecorder] 녹화 중이 아닙니다.");
            return;
        }

        isRecording = false;

        float duration = Time.time - recordingStartTime;
        ChunaLogger.Log($"<color=yellow>[PracticeHandRecorder] 녹화 종료: {recordedFrames} 프레임, {duration:F1}초</color>");

        if (recordedFrames > 0)
        {
            SaveToCSV();
            OnRecordingStopped?.Invoke(lastSavedFilePath, recordedFrames);
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeHandRecorder] 녹화된 프레임이 없어 저장하지 않습니다.");
        }

        UpdateToggleUI();
    }

    /// <summary>
    /// 녹화 취소 (저장 없이 중지)
    /// </summary>
    public void CancelRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        recordedData.Clear();
        recordedFrames = 0;
        ChunaLogger.Log("<color=orange>[PracticeHandRecorder] 녹화 취소됨</color>");
        UpdateToggleUI();
    }

    private void RecordFrame()
    {
        bool frameRecorded = false;

        if (recordLeftHand && leftHandVisual != null)
        {
            if (RecordHandData(leftHandVisual, "Left", leftOpenXRRoot))
            {
                frameRecorded = true;
            }
        }

        if (recordRightHand && rightHandVisual != null)
        {
            if (RecordHandData(rightHandVisual, "Right", rightOpenXRRoot))
            {
                frameRecorded = true;
            }
        }

        if (frameRecorded)
        {
            currentFrameIndex++;
            recordedFrames++;
        }
    }

    private bool RecordHandData(HandVisual handVisual, string handType, Transform openXRRoot)
    {
        if (handVisual == null || handVisual.Hand == null)
            return false;

        if (!handVisual.Hand.IsTrackedDataValid)
            return false;

        float timestamp = Time.time - recordingStartTime;

        // World Transform 계산
        Vector3 worldPos = Vector3.zero;
        Quaternion worldRot = Quaternion.identity;

        if (openXRRoot != null)
        {
            if (referencePoint != null)
            {
                // 기준점 상대 좌표
                worldPos = openXRRoot.position - referencePoint.position;
                worldRot = Quaternion.Inverse(referencePoint.rotation) * openXRRoot.rotation;
            }
            else
            {
                worldPos = openXRRoot.position;
                worldRot = openXRRoot.rotation;
            }
        }

        // 각 조인트 데이터 기록
        for (int i = 0; i < handVisual.Joints.Count; i++)
        {
            Transform joint = handVisual.Joints[i];
            if (joint == null) continue;

            FrameData frameData = new FrameData
            {
                frameIndex = currentFrameIndex,
                handType = handType,
                jointId = i,
                localPosition = joint.localPosition,
                localRotation = joint.localRotation,
                timestamp = timestamp
            };

            // Wrist 조인트에 World Transform 저장
            if (i == (int)HandJointId.HandWristRoot)
            {
                frameData.worldPosition = worldPos;
                frameData.worldRotation = worldRot;
            }

            recordedData.Add(frameData);
        }

        return true;
    }

    private void SaveToCSV()
    {
        if (recordedData.Count == 0)
        {
            ChunaLogger.LogError("[PracticeHandRecorder] 저장할 데이터가 없습니다.");
            return;
        }

        string fileName = GenerateFileName();
        string fullPath = Path.Combine(Application.persistentDataPath, saveFolder, fileName + ".csv");

        try
        {
            csvBuilder.Clear();
            CultureInfo inv = CultureInfo.InvariantCulture;

            // CSV 헤더
            csvBuilder.AppendLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                                 "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                                 "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW");

            foreach (FrameData data in recordedData)
            {
                bool hasWorld = data.jointId == (int)HandJointId.HandWristRoot;

                csvBuilder.AppendFormat(inv,
                    "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F3},{11},{12},{13},{14},{15},{16},{17}\n",
                    data.frameIndex,
                    data.handType,
                    data.jointId,
                    data.localPosition.x, data.localPosition.y, data.localPosition.z,
                    data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                    data.timestamp,
                    hasWorld ? data.worldPosition.x.ToString("F4", inv) : "",
                    hasWorld ? data.worldPosition.y.ToString("F4", inv) : "",
                    hasWorld ? data.worldPosition.z.ToString("F4", inv) : "",
                    hasWorld ? data.worldRotation.x.ToString("F4", inv) : "",
                    hasWorld ? data.worldRotation.y.ToString("F4", inv) : "",
                    hasWorld ? data.worldRotation.z.ToString("F4", inv) : "",
                    hasWorld ? data.worldRotation.w.ToString("F4", inv) : ""
                );
            }

            File.WriteAllText(fullPath, csvBuilder.ToString());
            lastSavedFilePath = fullPath;

            float fileSize = new FileInfo(fullPath).Length / 1024f;
            ChunaLogger.Log($"<color=green>[PracticeHandRecorder] CSV 저장 완료!</color>\n" +
                     $"  경로: {fullPath}\n" +
                     $"  프레임: {recordedFrames}\n" +
                     $"  크기: {fileSize:F1} KB");
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[PracticeHandRecorder] CSV 저장 실패: {e.Message}");
        }
    }

    private string GenerateFileName()
    {
        // 파일명 형식: {페이즈}_{단계}_핸드데이터_{원본파일명} - {타임스탬프}
        string phaseName = string.IsNullOrEmpty(currentPhaseName) ? "Unknown" : SanitizeFileName(currentPhaseName);
        string stepName = string.IsNullOrEmpty(currentStepName) ? "Unknown" : SanitizeFileName(currentStepName);

        // 시나리오 이름 — 토글 선택이 있으면 우선 사용, 없으면 evaluator에서 자동 추출
        string scenarioName = "";
        if (!string.IsNullOrEmpty(selectedScenarioName))
        {
            scenarioName = selectedScenarioName;
        }
        else if (pathEvaluator != null)
        {
            scenarioName = pathEvaluator.GetCurrentProcedureName();
        }
        if (string.IsNullOrEmpty(scenarioName))
        {
            scenarioName = "Unknown";
        }
        scenarioName = SanitizeFileName(scenarioName);

        string baseName = $"{phaseName}_{stepName}_핸드데이터_{scenarioName}";

        if (includeTimestamp)
        {
            return $"{baseName} - {DateTime.Now:yyyyMMdd_HHmmss}";
        }
        return baseName;
    }

    /// <summary>
    /// 파일명에 사용할 수 없는 문자 제거
    /// </summary>
    private string SanitizeFileName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            name = name.Replace(c, '_');
        }
        // 공백도 언더스코어로 변경
        name = name.Replace(' ', '_');
        return name;
    }

    private void EnsureSaveFolder()
    {
        string folderPath = Path.Combine(Application.persistentDataPath, saveFolder);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            ChunaLogger.Log($"[PracticeHandRecorder] 저장 폴더 생성: {folderPath}");
        }
    }

    private void FindOpenXRRoots()
    {
        // 왼손 OpenXR Root 찾기
        if (leftHandVisual != null && leftOpenXRRoot == null)
        {
            Transform parent = leftHandVisual.transform.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXRLeftHand") || parent.name.Contains("LeftHandAnchor"))
                {
                    leftOpenXRRoot = parent;
                    break;
                }
                parent = parent.parent;
            }
        }

        // 오른손 OpenXR Root 찾기
        if (rightHandVisual != null && rightOpenXRRoot == null)
        {
            Transform parent = rightHandVisual.transform.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXRRightHand") || parent.name.Contains("RightHandAnchor"))
                {
                    rightOpenXRRoot = parent;
                    break;
                }
                parent = parent.parent;
            }
        }
    }

    private void FindPatientReference()
    {
        if (!autoFindPatient || referencePoint != null) return;

        // Patient 태그로 찾기
        GameObject patient = GameObject.FindGameObjectWithTag("Patient");
        if (patient != null)
        {
            referencePoint = patient.transform;
            ChunaLogger.Log($"<color=green>[PracticeHandRecorder] 환자 기준점 자동 설정: {patient.name}</color>");
            return;
        }

        // 태그로 못 찾으면 이름으로 찾기
        string[] patientNames = { "Patient", "환자", "Chuna_Patient", "PatientModel" };
        foreach (var name in patientNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                referencePoint = obj.transform;
                ChunaLogger.Log($"<color=green>[PracticeHandRecorder] 환자 기준점 자동 설정 (이름): {obj.name}</color>");
                return;
            }
        }

        ChunaLogger.LogWarning("[PracticeHandRecorder] 환자 기준점을 찾을 수 없습니다. 월드 좌표로 녹화됩니다.");
    }

    /// <summary>
    /// 토글 클릭 시 호출. ON이면 녹화 시작, OFF면 녹화 종료
    /// </summary>
    private void OnRecordToggleChanged(bool isOn)
    {
        if (isOn && !isRecording)
        {
            StartRecording();
        }
        else if (!isOn && isRecording)
        {
            StopRecordingAndSave();
        }
    }

    /// <summary>
    /// 토글/아이콘/라벨을 현재 녹화 상태에 맞춰 갱신.
    /// 자동 녹화로 상태가 바뀐 경우에도 UI가 따라오도록 SetIsOnWithoutNotify 사용
    /// </summary>
    private void UpdateToggleUI()
    {
        if (recordToggle != null && recordToggle.isOn != isRecording)
        {
            recordToggle.SetIsOnWithoutNotify(isRecording);
        }

        if (toggleIcon != null)
        {
            Sprite next = isRecording ? iconRecording : iconStopped;
            if (next != null) toggleIcon.sprite = next;
        }

        if (toggleLabel != null)
        {
            toggleLabel.text = isRecording ? labelRecording : labelStopped;
        }
    }

    /// <summary>
    /// 시나리오 토글 이벤트 구독
    /// </summary>
    private void SubscribeScenarioToggles()
    {
        if (scenarioToggles == null) return;
        for (int i = 0; i < scenarioToggles.Length; i++)
        {
            var entry = scenarioToggles[i];
            if (entry == null || entry.toggle == null) continue;
            int captured = i;
            entry.toggle.onValueChanged.AddListener((isOn) => OnScenarioToggleChanged(captured, isOn));
        }
    }

    /// <summary>
    /// 시나리오 토글 이벤트 해제
    /// </summary>
    private void UnsubscribeScenarioToggles()
    {
        if (scenarioToggles == null) return;
        foreach (var entry in scenarioToggles)
        {
            if (entry == null || entry.toggle == null) continue;
            entry.toggle.onValueChanged.RemoveAllListeners();
        }
    }

    /// <summary>
    /// 시나리오 토글이 켜질 때 selectedScenarioName 갱신.
    /// ToggleGroup이 없어도 단일 선택을 유지하기 위해 다른 토글은 명시적으로 끔
    /// </summary>
    private void OnScenarioToggleChanged(int index, bool isOn)
    {
        if (scenarioToggles == null || index < 0 || index >= scenarioToggles.Length) return;

        var entry = scenarioToggles[index];
        if (entry == null) return;

        if (isOn)
        {
            selectedScenarioName = entry.scenarioName;
            ChunaLogger.Log($"<color=cyan>[PracticeHandRecorder] 시나리오 선택: {selectedScenarioName}</color>");

            // 다른 토글들은 강제로 끔 (ToggleGroup 없이도 단일 선택 보장)
            for (int i = 0; i < scenarioToggles.Length; i++)
            {
                if (i == index) continue;
                var other = scenarioToggles[i];
                if (other?.toggle != null && other.toggle.isOn)
                {
                    other.toggle.SetIsOnWithoutNotify(false);
                }
            }
        }
        else
        {
            // 이 토글이 꺼졌고 다른 켜진 토글도 없으면 선택 해제
            if (!HasAnyToggleOn())
            {
                selectedScenarioName = "";
                ChunaLogger.Log("<color=gray>[PracticeHandRecorder] 시나리오 선택 해제</color>");
            }
        }

        UpdateScenarioDropdownLabel();
    }

    private bool HasAnyToggleOn()
    {
        if (scenarioToggles == null) return false;
        foreach (var entry in scenarioToggles)
        {
            if (entry?.toggle != null && entry.toggle.isOn) return true;
        }
        return false;
    }

    /// <summary>
    /// Start 시 인스펙터에서 이미 켜져있는 토글이 있으면 selectedScenarioName 초기화
    /// </summary>
    private void SyncSelectedScenarioFromToggles()
    {
        if (scenarioToggles == null) return;
        foreach (var entry in scenarioToggles)
        {
            if (entry?.toggle != null && entry.toggle.isOn)
            {
                selectedScenarioName = entry.scenarioName;
                ChunaLogger.Log($"<color=cyan>[PracticeHandRecorder] 초기 시나리오: {selectedScenarioName}</color>");
                return;
            }
        }
    }

    /// <summary>
    /// 각 시나리오 토글 항목 라벨을 scenarioName과 동기화 (인스펙터에서 한 번만 적게)
    /// </summary>
    private void SyncScenarioItemLabels()
    {
        if (scenarioToggles == null) return;
        foreach (var entry in scenarioToggles)
        {
            if (entry?.itemLabel != null && !string.IsNullOrEmpty(entry.scenarioName))
            {
                entry.itemLabel.text = entry.scenarioName;
            }
        }
    }

    /// <summary>
    /// 드롭다운 헤더 라벨을 현재 선택된 시나리오 이름으로 갱신
    /// </summary>
    private void UpdateScenarioDropdownLabel()
    {
        if (scenarioDropdownLabel == null) return;
        scenarioDropdownLabel.text = string.IsNullOrEmpty(selectedScenarioName)
            ? scenarioPlaceholderText
            : selectedScenarioName;
    }

    // === Public API ===

    public bool IsRecording => isRecording;
    public int RecordedFrames => recordedFrames;
    public string LastSavedFilePath => lastSavedFilePath;
    public float RecordingDuration => isRecording ? Time.time - recordingStartTime : 0f;

    public void SetAutoRecordEnabled(bool enabled)
    {
        autoRecordEnabled = enabled;
        ChunaLogger.Log($"[PracticeHandRecorder] 자동 녹화: {(enabled ? "활성화" : "비활성화")}");
    }

    public void SetReferencePoint(Transform reference)
    {
        referencePoint = reference;
    }

    // === 위치 설정 비활성 ===

    /// <summary>
    /// 녹화 시작 시 위치 설정 토글/오브젝트를 강제로 OFF.
    /// 토글 isOn=false는 PracticeSettingsController의 onValueChanged 콜백을 트리거해
    /// 오브젝트도 비활성화되지만, 안전을 위해 오브젝트도 명시적으로 SetActive(false)
    /// </summary>
    private void DisablePositionAdjust()
    {
        if (positionAdjustToggle != null && positionAdjustToggle.isOn)
        {
            positionAdjustToggle.isOn = false;
            ChunaLogger.Log("<color=cyan>[PracticeHandRecorder] 위치 설정 토글 OFF</color>");
        }

        if (positionAdjustObject != null && positionAdjustObject.activeSelf)
        {
            positionAdjustObject.SetActive(false);
            ChunaLogger.Log("<color=cyan>[PracticeHandRecorder] 위치 조정 오브젝트 비활성화</color>");
        }
    }

    // === 침대 가시성 ===

    /// <summary>
    /// 침대 초기 상태 적용. 자식 전체 Renderer 캐싱 + 반투명/표시 여부
    /// </summary>
    private void InitializeTable()
    {
        if (tableObject == null) return;

        // 자식 전체 Renderer 캐싱 (MeshRenderer + SkinnedMeshRenderer 모두 포함)
        tableRenderers = tableObject.GetComponentsInChildren<Renderer>(true);
        ChunaLogger.Log($"<color=cyan>[PracticeHandRecorder] 침대 Renderer {tableRenderers.Length}개 캐싱</color>");

        // 토글 UI 동기화
        if (tableToggle != null)
        {
            tableToggle.SetIsOnWithoutNotify(startWithTableVisible);
        }

        ApplyTableVisibility(startWithTableVisible);
    }

    /// <summary>
    /// 토글 변경 핸들러
    /// </summary>
    private void OnTableToggleChanged(bool isOn)
    {
        ApplyTableVisibility(isOn);
    }

    /// <summary>
    /// 침대 표시/숨김. ON일 때 자식 모든 Renderer 머티리얼에 반투명 알파 적용
    /// </summary>
    private void ApplyTableVisibility(bool isOn)
    {
        if (tableObject == null) return;

        tableObject.SetActive(isOn);

        if (isOn)
        {
            ApplyAlphaToTableRenderers(tableDefaultAlpha);
            ChunaLogger.Log($"<color=cyan>[PracticeHandRecorder] 침대 표시 (Alpha: {tableDefaultAlpha})</color>");
        }
        else
        {
            ChunaLogger.Log("<color=gray>[PracticeHandRecorder] 침대 숨김</color>");
        }
    }

    /// <summary>
    /// 침대 자식 전체 Renderer의 모든 머티리얼 슬롯에 알파 적용.
    /// Standard(_Mode) + URP/HDRP(_Surface) 양쪽 셰이더 패밀리 호환.
    /// 한 번만 실행 후 캐시(tableAlphaApplied) — 반복 호출은 색상만 갱신
    /// </summary>
    private void ApplyAlphaToTableRenderers(float alpha)
    {
        if (tableRenderers == null || tableRenderers.Length == 0) return;

        foreach (Renderer r in tableRenderers)
        {
            if (r == null) continue;

            // .materials는 인스턴스 머티리얼 반환 (sharedMaterials 공유 회피)
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                if (m.shader != null && m.shader.name == "Hidden/InternalErrorShader") continue;

                if (logTableShaders && !tableShadersLogged)
                {
                    ChunaLogger.Log($"<color=yellow>[PracticeHandRecorder] 침대 머티리얼 셰이더: '{m.shader.name}' (renderer: {r.gameObject.name}, slot {i})</color>");
                }

                SetMaterialAlpha(m, alpha);
            }
            r.materials = mats;
        }

        tableShadersLogged = true;
    }

    /// <summary>
    /// 단일 머티리얼 알파 처리. 셰이더 모드 전환 + 블렌딩 + 컬러 프로퍼티.
    /// forceTransparentShader가 할당되어 있으면 셰이더 자체를 강제 교체 (가장 견고)
    /// </summary>
    private void SetMaterialAlpha(Material m, float alpha)
    {
        bool wantTransparent = alpha < 1f;

        // 0. 강제 셰이더 교체 — 가장 견고. 텍스처/컬러 보존 후 교체
        if (wantTransparent && forceTransparentShader != null && m.shader != forceTransparentShader)
        {
            ReplaceWithTransparentShader(m);
        }

        // 1. RenderType 태그 (SRP 렌더 패스 선택에 영향)
        m.SetOverrideTag("RenderType", wantTransparent ? "Transparent" : "Opaque");

        // 2. Standard Shader 모드 (_Mode: 0=Opaque, 3=Transparent)
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", wantTransparent ? 3f : 0f);
        }

        // 3. URP/HDRP Lit 모드 (_Surface: 0=Opaque, 1=Transparent)
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", wantTransparent ? 1f : 0f);
            if (wantTransparent)
            {
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
        }

        // 4. URP Lit _Blend: 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
        if (m.HasProperty("_Blend"))
        {
            m.SetFloat("_Blend", 0f);
        }

        // 5. AlphaClip 끄기 (Transparent와 동시 사용 시 alpha 합성 깨질 수 있음)
        if (m.HasProperty("_AlphaClip"))
        {
            m.SetFloat("_AlphaClip", 0f);
        }

        // 6. 블렌딩 + ZWrite + renderQueue
        if (wantTransparent)
        {
            if (m.HasProperty("_SrcBlend"))
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite"))
                m.SetInt("_ZWrite", 0);

            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");

            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 3000
        }
        else
        {
            if (m.HasProperty("_SrcBlend"))
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlend"))
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            if (m.HasProperty("_ZWrite"))
                m.SetInt("_ZWrite", 1);

            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");

            m.renderQueue = -1;
        }

        // 7. 컬러 알파 적용 (셰이더가 가진 모든 컬러 프로퍼티에 알파 반영)
        ApplyAlphaToColorProperties(m, alpha);
    }

    /// <summary>
    /// 머티리얼 셰이더를 forceTransparentShader로 교체. 텍스처/컬러 보존
    /// </summary>
    private void ReplaceWithTransparentShader(Material m)
    {
        Color baseColor = Color.white;
        if (m.HasProperty("_BaseColor")) baseColor = m.GetColor("_BaseColor");
        else if (m.HasProperty("_Color")) baseColor = m.GetColor("_Color");

        Texture baseTex = null;
        if (m.HasProperty("_BaseMap")) baseTex = m.GetTexture("_BaseMap");
        else if (m.HasProperty("_MainTex")) baseTex = m.GetTexture("_MainTex");

        m.shader = forceTransparentShader;

        // 셰이더 교체 후 새 셰이더의 컬러/텍스처 프로퍼티에 복원
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
        if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
        if (baseTex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", baseTex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", baseTex);
        }
    }

    private void ApplyAlphaToColorProperties(Material m, float alpha)
    {
        if (m.HasProperty("_Color"))
        {
            Color c = m.GetColor("_Color");
            c.a = alpha;
            m.SetColor("_Color", c);
        }
        if (m.HasProperty("_BaseColor"))
        {
            Color c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
        }
        if (m.HasProperty("_MainColor"))
        {
            Color c = m.GetColor("_MainColor");
            c.a = alpha;
            m.SetColor("_MainColor", c);
        }
        if (m.HasProperty("_TintColor"))
        {
            Color c = m.GetColor("_TintColor");
            c.a = alpha;
            m.SetColor("_TintColor", c);
        }
        if (m.HasProperty("_DiffuseColor"))
        {
            Color c = m.GetColor("_DiffuseColor");
            c.a = alpha;
            m.SetColor("_DiffuseColor", c);
        }
        if (m.HasProperty("_Opacity"))
        {
            m.SetFloat("_Opacity", alpha);
        }
        if (m.HasProperty("_Alpha"))
        {
            m.SetFloat("_Alpha", alpha);
        }
    }

    public void SetRecordInterval(float interval)
    {
        recordInterval = Mathf.Max(0.01f, interval);
    }
}
