using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using System.Text;
using System.Globalization;

/// <summary>
/// 실습 진행 중 손 동작을 자동으로 녹화하는 컴포넌트
/// - 시나리오에 핸드 데이터가 있는 단계에서만 녹화
/// - 시작 홀드 완료 후 녹화 시작
/// - 종료 홀드(MidHold) 완료 시 녹화 종료
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

    [Header("=== 녹화 상태 (읽기 전용) ===")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private int recordedFrames = 0;
    [SerializeField] private string currentProcedureName = "";
    [SerializeField] private string lastSavedFilePath = "";

    // 내부 데이터
    private List<FrameData> recordedData = new List<FrameData>();
    private float lastRecordTime = 0f;
    private float recordingStartTime = 0f;
    private int currentFrameIndex = 0;
    private StringBuilder csvBuilder = new StringBuilder(1024 * 100);
    private bool hasHandDataInCurrentStep = false;

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
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
        }
    }

    private void OnEnable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete += OnStartHoldCompleteHandler;
            pathEvaluator.OnMidHoldComplete += OnMidHoldCompleteHandler;
            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompletedHandler;
            pathEvaluator.OnSubStepStarted += OnSubStepStartedHandler;
            Debug.Log("<color=cyan>[PracticeHandRecorder] ChunaPathEvaluator 이벤트 연결됨</color>");
        }
        else
        {
            Debug.LogWarning("[PracticeHandRecorder] ChunaPathEvaluator를 찾을 수 없습니다.");
        }
    }

    private void OnDisable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete -= OnStartHoldCompleteHandler;
            pathEvaluator.OnMidHoldComplete -= OnMidHoldCompleteHandler;
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompletedHandler;
            pathEvaluator.OnSubStepStarted -= OnSubStepStartedHandler;
        }

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
        hasHandDataInCurrentStep = pathEvaluator.GetLoadedFrameCount() > 0;
        currentProcedureName = pathEvaluator.GetCurrentProcedureName();

        if (hasHandDataInCurrentStep)
        {
            Debug.Log($"<color=cyan>[PracticeHandRecorder] SubStep {subStepIndex}: 핸드 데이터 있음 ({pathEvaluator.GetLoadedFrameCount()} 프레임) - 녹화 대기</color>");
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
    /// 중간 홀드(종료 홀드) 완료 - 녹화 종료
    /// </summary>
    private void OnMidHoldCompleteHandler()
    {
        if (isRecording)
        {
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
            Debug.LogWarning("[PracticeHandRecorder] 이미 녹화 중입니다.");
            return;
        }

        // OpenXR Root 재탐색
        FindOpenXRRoots();

        recordedData.Clear();
        recordedFrames = 0;
        currentFrameIndex = 0;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;
        isRecording = true;

        string fileName = GenerateFileName();
        Debug.Log($"<color=green>[PracticeHandRecorder] 녹화 시작: {fileName}</color>");

        OnRecordingStarted?.Invoke(fileName);
    }

    /// <summary>
    /// 녹화 중지 및 저장
    /// </summary>
    public void StopRecordingAndSave()
    {
        if (!isRecording)
        {
            Debug.LogWarning("[PracticeHandRecorder] 녹화 중이 아닙니다.");
            return;
        }

        isRecording = false;

        float duration = Time.time - recordingStartTime;
        Debug.Log($"<color=yellow>[PracticeHandRecorder] 녹화 종료: {recordedFrames} 프레임, {duration:F1}초</color>");

        if (recordedFrames > 0)
        {
            SaveToCSV();
            OnRecordingStopped?.Invoke(lastSavedFilePath, recordedFrames);
        }
        else
        {
            Debug.LogWarning("[PracticeHandRecorder] 녹화된 프레임이 없어 저장하지 않습니다.");
        }
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
        Debug.Log("<color=orange>[PracticeHandRecorder] 녹화 취소됨</color>");
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
            Debug.LogError("[PracticeHandRecorder] 저장할 데이터가 없습니다.");
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
            Debug.Log($"<color=green>[PracticeHandRecorder] CSV 저장 완료!</color>\n" +
                     $"  경로: {fullPath}\n" +
                     $"  프레임: {recordedFrames}\n" +
                     $"  크기: {fileSize:F1} KB");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PracticeHandRecorder] CSV 저장 실패: {e.Message}");
        }
    }

    private string GenerateFileName()
    {
        string baseName = string.IsNullOrEmpty(currentProcedureName) ? "Practice" : currentProcedureName;

        if (includeTimestamp)
        {
            return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
        return baseName;
    }

    private void EnsureSaveFolder()
    {
        string folderPath = Path.Combine(Application.persistentDataPath, saveFolder);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log($"[PracticeHandRecorder] 저장 폴더 생성: {folderPath}");
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
            Debug.Log($"<color=green>[PracticeHandRecorder] 환자 기준점 자동 설정: {patient.name}</color>");
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
                Debug.Log($"<color=green>[PracticeHandRecorder] 환자 기준점 자동 설정 (이름): {obj.name}</color>");
                return;
            }
        }

        Debug.LogWarning("[PracticeHandRecorder] 환자 기준점을 찾을 수 없습니다. 월드 좌표로 녹화됩니다.");
    }

    // === Public API ===

    public bool IsRecording => isRecording;
    public int RecordedFrames => recordedFrames;
    public string LastSavedFilePath => lastSavedFilePath;
    public float RecordingDuration => isRecording ? Time.time - recordingStartTime : 0f;

    public void SetAutoRecordEnabled(bool enabled)
    {
        autoRecordEnabled = enabled;
        Debug.Log($"[PracticeHandRecorder] 자동 녹화: {(enabled ? "활성화" : "비활성화")}");
    }

    public void SetReferencePoint(Transform reference)
    {
        referencePoint = reference;
    }

    public void SetRecordInterval(float interval)
    {
        recordInterval = Mathf.Max(0.01f, interval);
    }
}
