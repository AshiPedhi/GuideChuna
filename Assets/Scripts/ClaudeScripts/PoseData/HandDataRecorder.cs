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
    [Tooltip("환자 모델 기준 위치 (예: 머리, 어깨 등). 설정 시 상대좌표로 저장됨.")]
    [SerializeField] private Transform patientReference;

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
        if (leftWristRoot == null && recordLeftHand)
            ChunaLogger.LogError("[HandDataRecorder] leftWristRoot가 할당되지 않았습니다! Inspector에서 왼손 손목 루트를 할당하세요.");

        if (rightWristRoot == null && recordRightHand)
            ChunaLogger.LogError("[HandDataRecorder] rightWristRoot가 할당되지 않았습니다! Inspector에서 오른손 손목 루트를 할당하세요.");

        if (patientReference == null)
            ChunaLogger.LogWarning("[HandDataRecorder] patientReference가 설정되지 않아 절대 좌표로 저장됩니다.");
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
                // 기준점 상대 좌표로 저장
                worldPos = wristRoot.position - patientReference.position;
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
