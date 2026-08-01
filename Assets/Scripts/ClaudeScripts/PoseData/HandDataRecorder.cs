using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;
using System.Globalization;
using Oculus.Interaction;
using Oculus.Interaction.Input;

/// <summary>
/// 독립형 핸드 데이터 녹화 도구
/// 시나리오 시스템 없이 아무 씬에서나 핸드 데이터를 녹화할 수 있음.
/// 녹화된 CSV는 HandPoseDataLoader와 호환되는 포맷으로 저장됨.
/// </summary>
public class HandDataRecorder : MonoBehaviour
{
    #region Inspector Fields

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual leftHandVisual;
    [SerializeField] private HandVisual rightHandVisual;

    [Header("=== 손목 루트 (직접 할당) ===")]
    [Tooltip("왼손 손목 루트 Transform (예: OpenXRLeftHand, LeftHandAnchor 등)")]
    [SerializeField] private Transform leftWristRoot;
    [Tooltip("오른손 손목 루트 Transform (예: OpenXRRightHand, RightHandAnchor 등)")]
    [SerializeField] private Transform rightWristRoot;

    [Header("=== 기준점 ===")]
    [Tooltip("녹화 좌표의 기준점. 설정 시 이 트랜스폼 기준 상대좌표로 저장된다.\n" +
             "★반드시 재생 쪽 기준점과 같아야 한다 — ChunaPathEvaluator.referenceTransform(씬에서는 " +
             "'HandGuideAxis')과 다른 걸 넣으면 가이드 손이 어긋난 위치에 뜬다. " +
             "비워 두면 아래 autoResolveReferences가 평가기에서 같은 값을 자동으로 가져온다.")]
    [SerializeField] private Transform patientReference;

    [Header("=== 자동 배선 ===")]
    [Tooltip("★기본 ON. 비어 있는 참조를 씬에서 자동으로 찾는다(인스펙터에 이미 넣은 값은 건드리지 않음).\n" +
             "  · 손        = 활성 HandVisual을 Handedness로 좌우 판별(ChunaLimitChecker와 같은 관용구)\n" +
             "  · 손목 루트 = HandVisual의 조상 중 OpenXRLeftHand/LeftHandAnchor (PracticeHandRecorder와 같은 규약)\n" +
             "  · 기준점    = ChunaPathEvaluator.referenceTransform을 그대로 가져옴(재생과 좌표계 일치 보장)")]
    [SerializeField] private bool autoResolveReferences = true;

    [Header("=== 녹화 설정 ===")]
    [SerializeField] private string fileName = "NewHandData";
    [SerializeField] private float recordInterval = 0.033f; // ~30fps
    [SerializeField] private bool recordLeftHand = true;
    [SerializeField] private bool recordRightHand = true;

    [Header("=== 상태 (읽기 전용) ===")]
    [SerializeField] private bool isRecording;
    [SerializeField] private int recordedFrames;
    [SerializeField] private float recordingDuration;

    #endregion

    #region Private Fields

    private List<FrameData> recordedData = new List<FrameData>();
    private float lastRecordTime;
    private float recordingStartTime;
    private int currentFrameIndex;

    private StringBuilder csvBuilder = new StringBuilder(1024 * 100);

    #endregion

    #region Data Structures

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

    #endregion

    #region Public Properties

    public bool IsRecording => isRecording;
    public int RecordedFrames => recordedFrames;
    public float RecordingDuration => recordingDuration;
    public string FileName => fileName;
    public Transform PatientReference => patientReference;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (autoResolveReferences) ResolveReferences();

        if (leftWristRoot == null && recordLeftHand)
            ChunaLogger.LogError("[HandDataRecorder] leftWristRoot가 할당되지 않았습니다! Inspector에서 왼손 손목 루트를 할당하세요.");

        if (rightWristRoot == null && recordRightHand)
            ChunaLogger.LogError("[HandDataRecorder] rightWristRoot가 할당되지 않았습니다! Inspector에서 오른손 손목 루트를 할당하세요.");

        if (patientReference == null)
            ChunaLogger.LogWarning("[HandDataRecorder] patientReference가 설정되지 않아 절대 좌표로 저장됩니다. " +
                                   "가이드 손 재생과 좌표계가 어긋나므로, 씬에 ChunaPathEvaluator가 있는지 확인하거나 " +
                                   "HandGuideAxis를 직접 할당하세요.");
    }

    /// <summary>비어 있는 참조를 씬에서 채운다(멱등). 손 조인트는 트래킹이 붙기 전엔 준비가 안 되므로
    /// 녹화 시작 시점에 한 번 더 호출한다.</summary>
    private void ResolveReferences()
    {
        // ① 손 — 활성 HandVisual을 Handedness로 좌우 판별(ChunaLimitChecker·ChunaPathEvaluator와 동일 관용구)
        if (leftHandVisual == null || rightHandVisual == null)
        {
            var hands = FindObjectsByType<HandVisual>(FindObjectsSortMode.None);
            foreach (var hand in hands)
            {
                if (hand == null || !hand.isActiveAndEnabled || hand.Hand == null) continue;
                if (hand.Hand.Handedness == Handedness.Left && leftHandVisual == null) leftHandVisual = hand;
                else if (hand.Hand.Handedness == Handedness.Right && rightHandVisual == null) rightHandVisual = hand;
            }
        }

        // ② 손목 루트 — PracticeHandRecorder와 같은 규약(조상 이름 탐색). 좌표계를 기존 데이터와 맞추기 위해 규약을 그대로 따른다.
        if (leftWristRoot == null)
            leftWristRoot = FindAncestorNamed(leftHandVisual, "OpenXRLeftHand", "LeftHandAnchor");
        if (rightWristRoot == null)
            rightWristRoot = FindAncestorNamed(rightHandVisual, "OpenXRRightHand", "RightHandAnchor");

        // ③ 기준점 — ★재생 쪽과 반드시 동일해야 하므로 이름으로 찾지 않고 평가기의 값을 그대로 가져온다.
        if (patientReference == null)
        {
            var evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
            if (evaluator != null && evaluator.ReferenceTransform != null)
            {
                patientReference = evaluator.ReferenceTransform;
                ChunaLogger.Log($"<color=green>[HandDataRecorder] 기준점 자동 연결: {patientReference.name}</color> " +
                                 "(ChunaPathEvaluator.referenceTransform과 동일 = 재생 좌표계 일치)");
            }
        }
    }

    /// <summary>HandVisual의 조상 중 지정한 이름 조각을 가진 트랜스폼을 찾는다.</summary>
    private static Transform FindAncestorNamed(HandVisual visual, params string[] nameContains)
    {
        if (visual == null) return null;
        Transform t = visual.transform.parent;
        while (t != null)
        {
            for (int i = 0; i < nameContains.Length; i++)
                if (t.name.Contains(nameContains[i])) return t;
            t = t.parent;
        }
        return null;
    }

    private void Update()
    {
        if (!isRecording) return;

        recordingDuration = Time.time - recordingStartTime;

        if (Time.time - lastRecordTime >= recordInterval)
        {
            RecordFrame();
            lastRecordTime = Time.time;
        }
    }

    #endregion

    #region Public API

    public void StartRecording()
    {
        if (isRecording)
        {
            ChunaLogger.LogWarning("[HandDataRecorder] 이미 녹화 중입니다.");
            return;
        }

        // Start 시점엔 손 트래킹이 아직 안 붙어 HandVisual.Hand가 null일 수 있다 → 여기서 한 번 더 채운다.
        if (autoResolveReferences) ResolveReferences();

        // 손목 루트 할당 검증
        if (recordLeftHand && leftWristRoot == null)
        {
            ChunaLogger.LogError("[HandDataRecorder] leftWristRoot가 없어 왼손 월드 위치를 기록할 수 없습니다!");
        }
        if (recordRightHand && rightWristRoot == null)
        {
            ChunaLogger.LogError("[HandDataRecorder] rightWristRoot가 없어 오른손 월드 위치를 기록할 수 없습니다!");
        }

        recordedData.Clear();
        recordedFrames = 0;
        currentFrameIndex = 0;
        recordingDuration = 0f;

        isRecording = true;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;

        ChunaLogger.Log($"<color=green>[HandDataRecorder] 녹화 시작!</color> 파일명: {fileName}.csv, " +
                         $"왼손: {(recordLeftHand ? "ON" : "OFF")}, 오른손: {(recordRightHand ? "ON" : "OFF")}, " +
                         $"leftWrist: {(leftWristRoot != null ? leftWristRoot.name : "NULL")}, " +
                         $"rightWrist: {(rightWristRoot != null ? rightWristRoot.name : "NULL")}");
    }

    public void StopRecording()
    {
        if (!isRecording)
        {
            ChunaLogger.LogWarning("[HandDataRecorder] 녹화 중이 아닙니다.");
            return;
        }

        isRecording = false;
        recordingDuration = Time.time - recordingStartTime;

        ChunaLogger.Log($"<color=yellow>[HandDataRecorder] 녹화 중지</color> " +
                         $"시간: {recordingDuration:F1}초, 프레임: {recordedFrames}");

        SaveToCSV();
    }

    /// <summary>
    /// 저장 경로 반환 (Editor: Assets/Resources/HandPoseData/, Build: persistentDataPath/HandPoseData/)
    /// </summary>
    public string GetSavePath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.dataPath, "Resources", "HandPoseData", fileName + ".csv");
#else
        return Path.Combine(Application.persistentDataPath, "HandPoseData", fileName + ".csv");
#endif
    }

    /// <summary>
    /// 저장 폴더 경로 반환
    /// </summary>
    public static string GetSaveFolder()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.dataPath, "Resources", "HandPoseData");
#else
        return Path.Combine(Application.persistentDataPath, "HandPoseData");
#endif
    }

    #endregion

    #region Recording Logic

    private void RecordFrame()
    {
        bool frameRecorded = false;

        if (recordLeftHand && leftHandVisual != null)
        {
            if (RecordHandData(leftHandVisual, "Left", leftWristRoot))
                frameRecorded = true;
        }

        if (recordRightHand && rightHandVisual != null)
        {
            if (RecordHandData(rightHandVisual, "Right", rightWristRoot))
                frameRecorded = true;
        }

        if (frameRecorded)
        {
            currentFrameIndex++;
            recordedFrames++;
        }
    }

    private bool RecordHandData(HandVisual handVisual, string handType, Transform wristRoot)
    {
        if (handVisual == null || handVisual.Hand == null)
            return false;

        if (!handVisual.Hand.IsTrackedDataValid)
            return false;

        float timestamp = Time.time - recordingStartTime;

        // 손목 월드 위치 계산 (기준점 상대좌표 또는 절대좌표)
        Vector3 worldPos = Vector3.zero;
        Quaternion worldRot = Quaternion.identity;

        if (wristRoot != null)
        {
            if (patientReference != null)
            {
                // 기준점 로컬 좌표로 저장 (기준점 회전에 독립적)
                worldPos = Quaternion.Inverse(patientReference.rotation) * (wristRoot.position - patientReference.position);
                worldRot = Quaternion.Inverse(patientReference.rotation) * wristRoot.rotation;
            }
            else
            {
                // 절대 좌표로 저장
                worldPos = wristRoot.position;
                worldRot = wristRoot.rotation;
            }
        }

        // 각 조인트 데이터 기록
        for (int i = 0; i < handVisual.Joints.Count; i++)
        {
            Transform joint = handVisual.Joints[i];
            if (joint == null)
                continue;

            FrameData frameData = new FrameData
            {
                frameIndex = currentFrameIndex,
                handType = handType,
                jointId = i,
                localPosition = joint.localPosition,
                localRotation = joint.localRotation,
                timestamp = timestamp
            };

            // Wrist 조인트에만 World Transform 저장
            if (i == (int)HandJointId.HandWristRoot)
            {
                frameData.worldPosition = worldPos;
                frameData.worldRotation = worldRot;
            }
            else
            {
                frameData.worldPosition = Vector3.zero;
                frameData.worldRotation = Quaternion.identity;
            }

            recordedData.Add(frameData);
        }

        return true;
    }

    #endregion

    #region CSV Save

    private void SaveToCSV()
    {
        if (recordedData.Count == 0)
        {
            ChunaLogger.LogError("[HandDataRecorder] 저장할 데이터가 없습니다.");
            return;
        }

        string path = GetSavePath();
        string directory = Path.GetDirectoryName(path);

        try
        {
            // 디렉토리 생성
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            csvBuilder.Clear();

            // CSV 헤더 (HandPoseDataLoader 호환)
            csvBuilder.AppendLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                                  "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                                  "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW");

            CultureInfo inv = CultureInfo.InvariantCulture;

            foreach (FrameData data in recordedData)
            {
                if (data.jointId == (int)HandJointId.HandWristRoot)
                {
                    csvBuilder.AppendFormat(inv,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17}\n",
                        data.frameIndex,
                        data.handType,
                        data.jointId,
                        data.localPosition.x, data.localPosition.y, data.localPosition.z,
                        data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                        data.timestamp,
                        data.worldPosition.x, data.worldPosition.y, data.worldPosition.z,
                        data.worldRotation.x, data.worldRotation.y, data.worldRotation.z, data.worldRotation.w
                    );
                }
                else
                {
                    csvBuilder.AppendFormat(inv,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},,,,,,,\n",
                        data.frameIndex,
                        data.handType,
                        data.jointId,
                        data.localPosition.x, data.localPosition.y, data.localPosition.z,
                        data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                        data.timestamp
                    );
                }
            }

            File.WriteAllText(path, csvBuilder.ToString(), Encoding.UTF8);

            long fileSize = new FileInfo(path).Length;
            ChunaLogger.Log($"<color=green>[HandDataRecorder] CSV 저장 완료!</color>\n" +
                             $"경로: {path}\n크기: {fileSize / 1024f:F1} KB");

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            ChunaLogger.Log("[HandDataRecorder] AssetDatabase 갱신 완료. Resources에서 바로 사용 가능.");
#endif
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[HandDataRecorder] CSV 저장 실패: {e.Message}");
        }
    }

    #endregion
}
