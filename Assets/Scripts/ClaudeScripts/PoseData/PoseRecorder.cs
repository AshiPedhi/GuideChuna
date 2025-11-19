using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Body.PoseDetection;  // Interaction SDK 네임스페이스
using Oculus.Interaction.Body.Input;  // BodyJointId 네임스페이스
using Oculus.Interaction;  // IActiveState 네임스페이스
using System.IO;  // 파일 저장용
using System.Linq;  // LINQ for sorting files
using System.Text;  // StringBuilder for CSV
using System.Reflection;  // 리플렉션으로 internal 메서드 접근

public class PoseRecorder : MonoBehaviour, IBodyPose
{
    [SerializeField]
    private PoseFromBody realTimeBodyPose;  // 실시간 현재 동작 소스 (Inspector 할당)

    [SerializeField]
    private BodyPoseComparerActiveState comparer;  // 비교 컴포넌트 (Inspector 할당, Body Pose에 realTimeBodyPose 할당)

    private List<PoseFrame> poseSequence = new List<PoseFrame>();  // 녹화용 리스트
    private List<PoseFrame> loadedSequence = new List<PoseFrame>();  // 불러온 시퀀스
    private bool isRecording = false;
    private bool isComparing = false;  // 비교 모드 상태
    private int currentCompareIndex = 0;  // 현재 비교 프레임 인덱스
    private bool wasActive = false;  // 이전 Active 상태

    private Dictionary<BodyJointId, Pose> _jointPosesLocal = new Dictionary<BodyJointId, Pose>();
    private Dictionary<BodyJointId, Pose> _jointPosesFromRoot = new Dictionary<BodyJointId, Pose>();

    public event Action WhenBodyPoseUpdated = delegate { };  // 인터페이스 멤버 구현

    public ISkeletonMapping SkeletonMapping => realTimeBodyPose.SkeletonMapping;

    public bool GetJointPoseLocal(BodyJointId bodyJointId, out Pose pose) => _jointPosesLocal.TryGetValue(bodyJointId, out pose);
    public bool GetJointPoseFromRoot(BodyJointId bodyJointId, out Pose pose) => _jointPosesFromRoot.TryGetValue(bodyJointId, out pose);

    // 각 프레임의 포즈 데이터 구조 (Serializable for JSON)
    [System.Serializable]
    private class PoseFrame
    {
        public Dictionary<int, PoseData> localPoses = new Dictionary<int, PoseData>();
        public Dictionary<int, PoseData> fromRootPoses = new Dictionary<int, PoseData>();
        public float timestamp;  // 프레임 타임스탬프 (초 단위)
    }

    [System.Serializable]
    private class PoseData
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [System.Serializable]
    private class PoseSequenceWrapper
    {
        public List<PoseFrame> sequences;
    }

    void Update()
    {
        if (isRecording && realTimeBodyPose != null)
        {
            realTimeBodyPose.UpdatePose();  // 포즈 업데이트

            PoseFrame frame = new PoseFrame();
            frame.timestamp = Time.time;

            foreach (var joint in realTimeBodyPose.SkeletonMapping.Joints)
            {
                if (realTimeBodyPose.GetJointPoseLocal(joint, out Pose localPose))
                {
                    frame.localPoses[(int)joint] = new PoseData { position = localPose.position, rotation = localPose.rotation };
                }
                if (realTimeBodyPose.GetJointPoseFromRoot(joint, out Pose fromRootPose))
                {
                    frame.fromRootPoses[(int)joint] = new PoseData { position = fromRootPose.position, rotation = fromRootPose.rotation };
                }
            }

            poseSequence.Add(frame);
        }

        // 비교 모드: comparer.Active 확인, 유사 시 다음 프레임으로
        if (isComparing && loadedSequence.Count > 0 && currentCompareIndex < loadedSequence.Count)
        {
            realTimeBodyPose.UpdatePose();
            bool isActiveNow = comparer.Active;
            if (isActiveNow && !wasActive)
            {
                Debug.Log($"프레임 {currentCompareIndex} 유사 통과! 다음 프레임으로 전환.");
                currentCompareIndex++;
                if (currentCompareIndex < loadedSequence.Count)
                {
                    LoadNextFrameToStoredPose();
                    UpdateComparerWithStoredPose();  // 다시 할당
                }
                else
                {
                    isComparing = false;
                    Debug.Log("전체 시퀀스 비교 완료!");
                }
            }
            wasActive = isActiveNow;
        }
    }

    // 녹화 시작 (버튼 호출)
    public void StartRecording()
    {
        if (realTimeBodyPose == null)
        {
            Debug.LogError("realTimeBodyPose가 할당되지 않았습니다.");
            return;
        }
        isRecording = true;
        poseSequence.Clear();
        Debug.Log("녹화 시작");
    }

    // 녹화 종료 및 저장 (버튼 호출)
    public void StopRecording()
    {
        isRecording = false;
        if (poseSequence.Count > 0)
        {
            SaveSequenceToFile();
            SaveSequenceToCSV();
        }
        Debug.Log("녹화 종료");
    }

    // JSON 저장
    private void SaveSequenceToFile()
    {
        string json = JsonUtility.ToJson(new PoseSequenceWrapper { sequences = poseSequence });
        string fileName = $"PoseSequence_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.json";
        string path = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(path, json);
        Debug.Log($"포즈 시퀀스 JSON 저장: {path}");
    }

    // CSV 저장
    private void SaveSequenceToCSV()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Frame,Timestamp,JointId,LocalPosX,LocalPosY,LocalPosZ,LocalRotX,LocalRotY,LocalRotZ,LocalRotW,FromRootPosX,FromRootPosY,FromRootPosZ,FromRootRotX,FromRootRotY,FromRootRotZ,FromRootRotW");

        for (int frameIndex = 0; frameIndex < poseSequence.Count; frameIndex++)
        {
            PoseFrame frame = poseSequence[frameIndex];
            foreach (var jointId in frame.localPoses.Keys)
            {
                if (frame.localPoses.TryGetValue(jointId, out PoseData local) &&
                    frame.fromRootPoses.TryGetValue(jointId, out PoseData fromRoot))
                {
                    sb.AppendLine($"{frameIndex},{frame.timestamp},{jointId}," +
                                  $"{local.position.x},{local.position.y},{local.position.z}," +
                                  $"{local.rotation.x},{local.rotation.y},{local.rotation.z},{local.rotation.w}," +
                                  $"{fromRoot.position.x},{fromRoot.position.y},{fromRoot.position.z}," +
                                  $"{fromRoot.rotation.x},{fromRoot.rotation.y},{fromRoot.rotation.z},{fromRoot.rotation.w}");
                }
            }
        }

        string fileName = $"PoseSequence_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.csv";
        string path = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"포즈 시퀀스 CSV 저장: {path}");
    }

    // 최근 JSON 불러와 비교 시작 (버튼 호출)
    public void StartSequenceComparison()
    {
        string dirPath = Application.persistentDataPath;
        var files = new DirectoryInfo(dirPath)
            .GetFiles("PoseSequence_*.json")
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (files.Count == 0)
        {
            Debug.LogError("저장된 JSON 파일이 없습니다.");
            return;
        }

        FileInfo latestFile = files.First();
        string json = File.ReadAllText(latestFile.FullName);
        PoseSequenceWrapper wrapper = JsonUtility.FromJson<PoseSequenceWrapper>(json);

        if (wrapper == null || wrapper.sequences == null || wrapper.sequences.Count == 0)
        {
            Debug.LogError("JSON 파싱 실패 또는 빈 시퀀스.");
            return;
        }

        loadedSequence = wrapper.sequences;
        isComparing = true;
        currentCompareIndex = 0;
        wasActive = false;
        LoadNextFrameToStoredPose();  // 첫 프레임 입력
        UpdateComparerWithStoredPose();  // comparer에 StoredPose 할당
        Debug.Log("시퀀스 비교 시작: 첫 프레임부터 검사.");
    }

    // 다음 프레임 데이터를 StoredPose (this)에 입력
    private void LoadNextFrameToStoredPose()
    {
        _jointPosesLocal.Clear();
        _jointPosesFromRoot.Clear();
        PoseFrame frame = loadedSequence[currentCompareIndex];
        foreach (var jointId in frame.localPoses.Keys)
        {
            PoseData local = frame.localPoses[jointId];
            _jointPosesLocal[(BodyJointId)jointId] = new Pose(local.position, local.rotation);
            PoseData fromRoot = frame.fromRootPoses[jointId];
            _jointPosesFromRoot[(BodyJointId)jointId] = new Pose(fromRoot.position, fromRoot.rotation);
        }
        WhenBodyPoseUpdated.Invoke();  // 이벤트 호출 (포즈 업데이트 알림)
        Debug.Log($"프레임 {currentCompareIndex} 데이터 입력 완료.");
    }

    // comparer에 StoredPose (this)를 참조 포즈로 할당 (리플렉션 사용, 이름 변경 테스트)
    private void UpdateComparerWithStoredPose()
    {
        // 가능한 필드 이름 테스트 (SDK 소스 확인 후 변경)
        string[] possibleFieldNames = { "_bodyPose", "_referenceBodyPose", "_pose", "_iBodyPose" };
        FieldInfo bodyPoseField = null;

        foreach (var fieldName in possibleFieldNames)
        {
            bodyPoseField = typeof(BodyPoseComparerActiveState).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (bodyPoseField != null)
            {
                Debug.Log($"필드 찾음: {fieldName}");
                break;
            }
        }

        if (bodyPoseField != null)
        {
            bodyPoseField.SetValue(comparer, this);  // this는 IBodyPose 구현체
        }
        else
        {
            Debug.LogError("bodyPose 필드를 찾을 수 없음. SDK 소스 코드 확인하세요.");
        }
    }
}