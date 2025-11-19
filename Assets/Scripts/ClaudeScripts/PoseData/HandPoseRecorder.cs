using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using System.Text;
using System.Globalization;

public class HandPoseRecorder : MonoBehaviour
{
    [Header("녹화할 손 모델")]
    [SerializeField]
    private HandVisual leftHandVisual;

    [SerializeField]
    private HandVisual rightHandVisual;

    [Header("OpenXR Root (자동 탐색)")]
    [SerializeField]
    private Transform leftOpenXRRoot;

    [SerializeField]
    private Transform rightOpenXRRoot;

    [Header("녹화 설정")]
    [SerializeField]
    private string recordingFileName = "HandPose";

    [SerializeField]
    private float recordInterval = 0.1f;

    [SerializeField]
    private bool recordLeftHand = true;

    [SerializeField]
    private bool recordRightHand = true;

    [SerializeField]
    private Transform referencePoint;

    [Header("타이머 설정")]
    [SerializeField]
    private bool useTimer = false;

    [SerializeField]
    private float timerDuration = 60f;

    [SerializeField]
    private bool showCountdown = true;

    [Header("녹화 상태")]
    [SerializeField]
    private bool isRecording = false;

    [SerializeField]
    private bool isWaitingToRecord = false;

    [SerializeField]
    private int recordedFrames = 0;

    [SerializeField]
    private float remainingTime = 0f;

    private List<FrameData> recordedData = new List<FrameData>();
    private float lastRecordTime = 0f;
    private float recordingStartTime = 0f;
    private int currentFrameIndex = 0;

    private StringBuilder csvBuilder = new StringBuilder(1024 * 100);

    // 타이머 이벤트
    public event Action<float> OnTimerTick;
    public event Action OnTimerComplete;
    public event Action OnRecordingStarted;
    public event Action OnRecordingStopped;

    [System.Serializable]
    private class FrameData
    {
        public int frameIndex;
        public string handType;
        public int jointId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 rootPosition;     // OpenXRRoot의 위치
        public Quaternion rootRotation;  // OpenXRRoot의 회전
        public float timestamp;
    }

    void Start()
    {
        FindOpenXRRoots();

        if (referencePoint == null)
        {
            Debug.LogWarning("기준점이 설정되지 않아 월드 좌표를 사용합니다.");
        }
    }

    /// <summary>
    /// OpenXRRoot GameObject 자동 탐색
    /// </summary>
    private void FindOpenXRRoots()
    {
        // 왼손 OpenXRRoot 찾기
        if (leftHandVisual != null && leftOpenXRRoot == null)
        {
            Transform parent = leftHandVisual.transform.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXRLeftHand") || parent.name.Contains("LeftHandAnchor"))
                {
                    leftOpenXRRoot = parent;
                    Debug.Log($"왼손 OpenXRRoot 찾음: {leftOpenXRRoot.name}");
                    break;
                }
                parent = parent.parent;
            }

            if (leftOpenXRRoot == null)
            {
                Debug.LogWarning("왼손 OpenXRRoot를 찾을 수 없습니다. 수동 설정 필요!");
            }
        }

        // 오른손 OpenXRRoot 찾기
        if (rightHandVisual != null && rightOpenXRRoot == null)
        {
            Transform parent = rightHandVisual.transform.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXRRightHand") || parent.name.Contains("RightHandAnchor"))
                {
                    rightOpenXRRoot = parent;
                    Debug.Log($"오른손 OpenXRRoot 찾음: {rightOpenXRRoot.name}");
                    break;
                }
                parent = parent.parent;
            }

            if (rightOpenXRRoot == null)
            {
                Debug.LogWarning("오른손 OpenXRRoot를 찾을 수 없습니다. 수동 설정 필요!");
            }
        }
    }

    void Update()
    {
        if (isRecording)
        {
            if (Time.time - lastRecordTime >= recordInterval)
            {
                RecordFrame();
                lastRecordTime = Time.time;
            }

            // 타이머 사용 중이면 시간 체크
            if (useTimer)
            {
                remainingTime = timerDuration - (Time.time - recordingStartTime);

                if (remainingTime <= 0)
                {
                    StopRecording();
                    OnTimerComplete?.Invoke();
                }
            }
        }

        // 타이머 대기 중
        if (isWaitingToRecord)
        {
            remainingTime -= Time.deltaTime;

            if (showCountdown)
            {
                OnTimerTick?.Invoke(remainingTime);
            }

            if (remainingTime <= 0)
            {
                isWaitingToRecord = false;
                ActualStartRecording();
                OnTimerComplete?.Invoke();
            }
        }
    }

    // 녹화 시작 (타이머 고려)
    public void StartRecording()
    {
        if (isRecording || isWaitingToRecord)
        {
            Debug.LogWarning("이미 녹화 중이거나 대기 중입니다.");
            return;
        }

        // OpenXRRoot 재탐색 (런타임 중 변경될 수 있음)
        FindOpenXRRoots();

        recordedData.Clear();
        recordedFrames = 0;
        currentFrameIndex = 0;

        if (useTimer && showCountdown)
        {
            // 카운트다운 시작
            isWaitingToRecord = true;
            remainingTime = 3f; // 3초 카운트다운
            Debug.Log("<color=orange>3초 후 녹화를 시작합니다...</color>");
        }
        else
        {
            ActualStartRecording();
        }
    }

    // 실제 녹화 시작
    private void ActualStartRecording()
    {
        isRecording = true;
        recordingStartTime = Time.time;
        lastRecordTime = Time.time;

        if (useTimer)
        {
            remainingTime = timerDuration;
        }

        Debug.Log($"<color=green>녹화 시작!</color>\n" +
                 $"파일명: {recordingFileName}.csv\n" +
                 $"녹화 간격: {recordInterval}초\n" +
                 $"왼손: {(recordLeftHand ? "ON" : "OFF")}\n" +
                 $"오른손: {(recordRightHand ? "ON" : "OFF")}\n" +
                 $"타이머: {(useTimer ? timerDuration + "초" : "OFF")}");

        OnRecordingStarted?.Invoke();
    }

    // 타이머 취소
    public void CancelTimer()
    {
        if (isWaitingToRecord)
        {
            isWaitingToRecord = false;
            remainingTime = 0f;
            Debug.Log("<color=red>타이머 취소됨</color>");
        }
    }

    // 녹화 중지
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("녹화 중이 아닙니다.");
            return;
        }

        isRecording = false;

        float recordingDuration = Time.time - recordingStartTime;
        Debug.Log($"<color=yellow>녹화 중지</color>\n" +
                 $"녹화 시간: {recordingDuration:F1}초\n" +
                 $"프레임 수: {recordedFrames}\n" +
                 $"저장 중...");

        SaveToCSV();

        OnRecordingStopped?.Invoke();
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

            if (recordedFrames % 10 == 0)
            {
                Debug.Log($"녹화 중... 프레임: {recordedFrames}");
            }
        }
    }

    private bool RecordHandData(HandVisual handVisual, string handType, Transform openXRRoot)
    {
        if (handVisual == null || handVisual.Hand == null)
            return false;

        if (!handVisual.Hand.IsTrackedDataValid)
        {
            Debug.LogWarning($"{handType} 핸드 트래킹 데이터가 유효하지 않습니다.");
            return false;
        }

        float timestamp = Time.time - recordingStartTime;

        // OpenXRRoot Transform 저장
        Vector3 rootPos = Vector3.zero;
        Quaternion rootRot = Quaternion.identity;

        if (openXRRoot != null)
        {
            if (referencePoint != null)
            {
                // 기준점 상대 좌표로 저장
                rootPos = openXRRoot.position - referencePoint.position;
                rootRot = Quaternion.Inverse(referencePoint.rotation) * openXRRoot.rotation;
            }
            else
            {
                // 월드 좌표로 저장
                rootPos = openXRRoot.position;
                rootRot = openXRRoot.rotation;
            }
        }
        else
        {
            Debug.LogWarning($"{handType} OpenXRRoot를 찾을 수 없습니다!");
        }

        // 각 조인트 데이터 저장
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

            // Wrist 조인트에만 Root Transform 저장
            if (i == (int)HandJointId.HandWristRoot)
            {
                frameData.rootPosition = rootPos;
                frameData.rootRotation = rootRot;

                if (recordedFrames % 10 == 0)
                {
                    Debug.Log($"[Frame {currentFrameIndex}] {handType} Root Pos: {rootPos}, Rot: {rootRot.eulerAngles}");
                }
            }
            else
            {
                frameData.rootPosition = Vector3.zero;
                frameData.rootRotation = Quaternion.identity;
            }

            recordedData.Add(frameData);
        }

        return true;
    }

    private void SaveToCSV()
    {
        if (recordedData.Count == 0)
        {
            Debug.LogError("저장할 데이터가 없습니다.");
            return;
        }

        string path = Path.Combine(Application.persistentDataPath, recordingFileName + ".csv");

        try
        {
            csvBuilder.Clear();

            // CSV 헤더 - RootPos/Rot으로 변경
            csvBuilder.AppendLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                                 "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                                 "RootPosX,RootPosY,RootPosZ,RootRotX,RootRotY,RootRotZ,RootRotW");

            CultureInfo invariantCulture = CultureInfo.InvariantCulture;

            foreach (FrameData data in recordedData)
            {
                if (data.jointId == (int)HandJointId.HandWristRoot)
                {
                    // Root Transform 데이터 포함
                    csvBuilder.AppendFormat(invariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17}\n",
                        data.frameIndex,
                        data.handType,
                        data.jointId,
                        data.localPosition.x, data.localPosition.y, data.localPosition.z,
                        data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                        data.timestamp,
                        data.rootPosition.x, data.rootPosition.y, data.rootPosition.z,
                        data.rootRotation.x, data.rootRotation.y, data.rootRotation.z, data.rootRotation.w
                    );
                }
                else
                {
                    // 다른 조인트는 Root 데이터 없이
                    csvBuilder.AppendFormat(invariantCulture,
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

            File.WriteAllText(path, csvBuilder.ToString());

            Debug.Log($"<color=green>CSV 저장 완료!</color>\n경로: {path}\n크기: {new FileInfo(path).Length / 1024f:F1} KB");
        }
        catch (Exception e)
        {
            Debug.LogError($"CSV 저장 실패: {e.Message}");
        }
    }

    // 녹화 토글
    public void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    // 파일명 변경
    public void SetFileName(string fileName)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            recordingFileName = fileName;
        }
    }

    // 녹화 간격 변경
    public void SetRecordInterval(float interval)
    {
        recordInterval = Mathf.Max(0.01f, interval);
    }

    // 타이머 설정
    public void SetTimerDuration(float duration)
    {
        timerDuration = Mathf.Max(1f, duration);
    }

    // 타이머 토글
    public void SetUseTimer(bool use)
    {
        useTimer = use;
    }

    // 상태 확인
    public bool IsRecording() => isRecording;
    public bool IsWaitingToRecord() => isWaitingToRecord;
    public int GetRecordedFrames() => recordedFrames;
    public float GetRemainingTime() => remainingTime;
    public float GetRecordingDuration() => isRecording ? Time.time - recordingStartTime : 0f;

    // 손 활성화 상태 변경
    public void SetRecordLeftHand(bool record)
    {
        recordLeftHand = record;
    }

    public void SetRecordRightHand(bool record)
    {
        recordRightHand = record;
    }

    public bool IsRecordingLeftHand() => recordLeftHand;
    public bool IsRecordingRightHand() => recordRightHand;

    // CSV 파일 경로 가져오기
    public string GetSavedFilePath()
    {
        return Path.Combine(Application.persistentDataPath, recordingFileName + ".csv");
    }

    // CSV 파일 존재 확인
    public bool DoesSavedFileExist()
    {
        return File.Exists(GetSavedFilePath());
    }

    // OpenXRRoot 수동 설정
    public void SetLeftOpenXRRoot(Transform root)
    {
        leftOpenXRRoot = root;
        Debug.Log($"왼손 OpenXRRoot 수동 설정: {root?.name}");
    }

    public void SetRightOpenXRRoot(Transform root)
    {
        rightOpenXRRoot = root;
        Debug.Log($"오른손 OpenXRRoot 수동 설정: {root?.name}");
    }
}