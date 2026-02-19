using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Oculus.Interaction.Body.Input;  // BoneId ���ӽ����̽�
using System.Reflection;  // ���÷���
using static OVRSkeleton;

public class PosePlayer : MonoBehaviour
{
    [SerializeField]
    private OVRCustomSkeleton customSkeleton;  // Inspector���� OVRCustomSkeleton �Ҵ�

    [SerializeField]
    private float playbackInterval = 0.1f;  // ��� ���� (��)

    private List<PoseFrame> loadedSequence = new List<PoseFrame>();  // �ҷ��� ������
    private bool isPlaying = false;
    private int currentPlaybackIndex = 0;
    private float lastPlaybackTime = 0f;

    [System.Serializable]
    private class PoseFrame
    {
        public Dictionary<int, PoseData> localPoses = new Dictionary<int, PoseData>();
        public Dictionary<int, PoseData> fromRootPoses = new Dictionary<int, PoseData>();
        public float timestamp;
    }

    [System.Serializable]
    private class PoseData
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    void Update()
    {
        if (isPlaying && currentPlaybackIndex < loadedSequence.Count)
        {
            if (Time.time - lastPlaybackTime >= playbackInterval)
            {
                ApplyFrameToBones();
                currentPlaybackIndex++;
                lastPlaybackTime = Time.time;
                if (currentPlaybackIndex >= loadedSequence.Count)
                {
                    isPlaying = false;
                    ChunaLogger.Log("��� �Ϸ�.");
                }
            }
        }
    }

    // csv �ҷ��� ��� ���� (csvFileName �Է�, ��: "MySequence")
    public void StartPlaybackFromCSV(string csvFileName)
    {
        string path = Path.Combine(Application.persistentDataPath, csvFileName + ".csv");
        if (!File.Exists(path))
        {
            ChunaLogger.LogError("CSV ���� ����.");
            return;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            ChunaLogger.LogError("CSV ������ ����.");
            return;
        }

        loadedSequence.Clear();
        PoseFrame currentFrame = null;
        int lastFrameIndex = -1;

        for (int i = 1; i < lines.Length; i++)  // ��� ��ŵ
        {
            string[] values = lines[i].Split(',');
            int frameIndex = int.Parse(values[0]);
            float timestamp = float.Parse(values[1]);
            int jointId = int.Parse(values[2]);
            Vector3 localPos = new Vector3(float.Parse(values[3]), float.Parse(values[4]), float.Parse(values[5]));
            Quaternion localRot = new Quaternion(float.Parse(values[6]), float.Parse(values[7]), float.Parse(values[8]), float.Parse(values[9]));
            Vector3 fromRootPos = new Vector3(float.Parse(values[10]), float.Parse(values[11]), float.Parse(values[12]));
            Quaternion fromRootRot = new Quaternion(float.Parse(values[13]), float.Parse(values[14]), float.Parse(values[15]), float.Parse(values[16]));

            if (frameIndex != lastFrameIndex)
            {
                if (currentFrame != null) loadedSequence.Add(currentFrame);
                currentFrame = new PoseFrame { timestamp = timestamp };
                lastFrameIndex = frameIndex;
            }

            currentFrame.localPoses[jointId] = new PoseData { position = localPos, rotation = localRot };
            currentFrame.fromRootPoses[jointId] = new PoseData { position = fromRootPos, rotation = fromRootRot };
        }

        if (currentFrame != null) loadedSequence.Add(currentFrame);

        if (loadedSequence.Count == 0)
        {
            ChunaLogger.LogError("CSV �Ľ� ����.");
            return;
        }

        isPlaying = true;
        currentPlaybackIndex = 0;
        lastPlaybackTime = Time.time;
        ApplyFrameToBones();  // ù ������ ����
        ChunaLogger.Log("��� ����.");
    }

    // ������ ������ CustomBones�� ����
    private void ApplyFrameToBones()
    {
        PoseFrame frame = loadedSequence[currentPlaybackIndex];
        foreach (var jointId in frame.localPoses.Keys)
        {
            BoneId boneId = (BoneId)jointId;
            MethodInfo getBoneMethod = typeof(OVRCustomSkeleton).GetMethod("GetBoneTransform", BindingFlags.Instance | BindingFlags.NonPublic);
            if (getBoneMethod != null)
            {
                Transform boneTransform = (Transform)getBoneMethod.Invoke(customSkeleton, new object[] { boneId });
                if (boneTransform != null)
                {
                    PoseData local = frame.localPoses[jointId];
                    boneTransform.localPosition = local.position;
                    boneTransform.localRotation = local.rotation;
                }
            }
            else
            {
                ChunaLogger.LogError("GetBoneTransform �޼��带 ã�� �� ����.");
            }
        }
        ChunaLogger.Log($"������ {currentPlaybackIndex} ����.");
    }
}