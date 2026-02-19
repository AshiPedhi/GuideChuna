using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Body.PoseDetection;  // Interaction SDK ���ӽ����̽�
using Oculus.Interaction.Body.Input;  // BodyJointId ���ӽ����̽�
using Oculus.Interaction;  // IActiveState ���ӽ����̽�
using System.IO;  // ���� �����
using System.Linq;  // LINQ for sorting files
using System.Text;  // StringBuilder for CSV
using System.Reflection;  // ���÷������� internal �޼��� ����

public class PoseRecorder : MonoBehaviour, IBodyPose
{
    [SerializeField]
    private PoseFromBody realTimeBodyPose;  // �ǽð� ���� ���� �ҽ� (Inspector �Ҵ�)

    [SerializeField]
    private BodyPoseComparerActiveState comparer;  // �� ������Ʈ (Inspector �Ҵ�, Body Pose�� realTimeBodyPose �Ҵ�)

    private List<PoseFrame> poseSequence = new List<PoseFrame>();  // ��ȭ�� ����Ʈ
    private List<PoseFrame> loadedSequence = new List<PoseFrame>();  // �ҷ��� ������
    private bool isRecording = false;
    private bool isComparing = false;  // �� ��� ����
    private int currentCompareIndex = 0;  // ���� �� ������ �ε���
    private bool wasActive = false;  // ���� Active ����

    private Dictionary<BodyJointId, Pose> _jointPosesLocal = new Dictionary<BodyJointId, Pose>();
    private Dictionary<BodyJointId, Pose> _jointPosesFromRoot = new Dictionary<BodyJointId, Pose>();

    public event Action WhenBodyPoseUpdated = delegate { };  // �������̽� ��� ����

    public ISkeletonMapping SkeletonMapping => realTimeBodyPose.SkeletonMapping;

    public bool GetJointPoseLocal(BodyJointId bodyJointId, out Pose pose) => _jointPosesLocal.TryGetValue(bodyJointId, out pose);
    public bool GetJointPoseFromRoot(BodyJointId bodyJointId, out Pose pose) => _jointPosesFromRoot.TryGetValue(bodyJointId, out pose);

    // �� �������� ���� ������ ���� (Serializable for JSON)
    [System.Serializable]
    private class PoseFrame
    {
        public Dictionary<int, PoseData> localPoses = new Dictionary<int, PoseData>();
        public Dictionary<int, PoseData> fromRootPoses = new Dictionary<int, PoseData>();
        public float timestamp;  // ������ Ÿ�ӽ����� (�� ����)
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
            realTimeBodyPose.UpdatePose();  // ���� ������Ʈ

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

        // �� ���: comparer.Active Ȯ��, ���� �� ���� ����������
        if (isComparing && loadedSequence.Count > 0 && currentCompareIndex < loadedSequence.Count)
        {
            realTimeBodyPose.UpdatePose();
            bool isActiveNow = comparer.Active;
            if (isActiveNow && !wasActive)
            {
                ChunaLogger.Log($"������ {currentCompareIndex} ���� ���! ���� ���������� ��ȯ.");
                currentCompareIndex++;
                if (currentCompareIndex < loadedSequence.Count)
                {
                    LoadNextFrameToStoredPose();
                    UpdateComparerWithStoredPose();  // �ٽ� �Ҵ�
                }
                else
                {
                    isComparing = false;
                    ChunaLogger.Log("��ü ������ �� �Ϸ�!");
                }
            }
            wasActive = isActiveNow;
        }
    }

    // ��ȭ ���� (��ư ȣ��)
    public void StartRecording()
    {
        if (realTimeBodyPose == null)
        {
            ChunaLogger.LogError("realTimeBodyPose�� �Ҵ���� �ʾҽ��ϴ�.");
            return;
        }
        isRecording = true;
        poseSequence.Clear();
        ChunaLogger.Log("��ȭ ����");
    }

    // ��ȭ ���� �� ���� (��ư ȣ��)
    public void StopRecording()
    {
        isRecording = false;
        if (poseSequence.Count > 0)
        {
            SaveSequenceToFile();
            SaveSequenceToCSV();
        }
        ChunaLogger.Log("��ȭ ����");
    }

    // JSON ����
    private void SaveSequenceToFile()
    {
        string json = JsonUtility.ToJson(new PoseSequenceWrapper { sequences = poseSequence });
        string fileName = $"PoseSequence_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.json";
        string path = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(path, json);
        ChunaLogger.Log($"���� ������ JSON ����: {path}");
    }

    // CSV ����
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
        ChunaLogger.Log($"���� ������ CSV ����: {path}");
    }

    // �ֱ� JSON �ҷ��� �� ���� (��ư ȣ��)
    public void StartSequenceComparison()
    {
        string dirPath = Application.persistentDataPath;
        var files = new DirectoryInfo(dirPath)
            .GetFiles("PoseSequence_*.json")
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (files.Count == 0)
        {
            ChunaLogger.LogError("����� JSON ������ �����ϴ�.");
            return;
        }

        FileInfo latestFile = files.First();
        string json = File.ReadAllText(latestFile.FullName);
        PoseSequenceWrapper wrapper = JsonUtility.FromJson<PoseSequenceWrapper>(json);

        if (wrapper == null || wrapper.sequences == null || wrapper.sequences.Count == 0)
        {
            ChunaLogger.LogError("JSON �Ľ� ���� �Ǵ� �� ������.");
            return;
        }

        loadedSequence = wrapper.sequences;
        isComparing = true;
        currentCompareIndex = 0;
        wasActive = false;
        LoadNextFrameToStoredPose();  // ù ������ �Է�
        UpdateComparerWithStoredPose();  // comparer�� StoredPose �Ҵ�
        ChunaLogger.Log("������ �� ����: ù �����Ӻ��� �˻�.");
    }

    // ���� ������ �����͸� StoredPose (this)�� �Է�
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
        WhenBodyPoseUpdated.Invoke();  // �̺�Ʈ ȣ�� (���� ������Ʈ �˸�)
        ChunaLogger.Log($"������ {currentCompareIndex} ������ �Է� �Ϸ�.");
    }

    // comparer�� StoredPose (this)�� ���� ����� �Ҵ� (���÷��� ���, �̸� ���� �׽�Ʈ)
    private void UpdateComparerWithStoredPose()
    {
        // ������ �ʵ� �̸� �׽�Ʈ (SDK �ҽ� Ȯ�� �� ����)
        string[] possibleFieldNames = { "_bodyPose", "_referenceBodyPose", "_pose", "_iBodyPose" };
        FieldInfo bodyPoseField = null;

        foreach (var fieldName in possibleFieldNames)
        {
            bodyPoseField = typeof(BodyPoseComparerActiveState).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (bodyPoseField != null)
            {
                ChunaLogger.Log($"�ʵ� ã��: {fieldName}");
                break;
            }
        }

        if (bodyPoseField != null)
        {
            bodyPoseField.SetValue(comparer, this);  // this�� IBodyPose ����ü
        }
        else
        {
            ChunaLogger.LogError("bodyPose �ʵ带 ã�� �� ����. SDK �ҽ� �ڵ� Ȯ���ϼ���.");
        }
    }
}