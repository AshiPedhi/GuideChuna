using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Oculus.Interaction.Body.Input;  // BoneId 네임스페이스
using System.Reflection;  // 리플렉션
using static OVRSkeleton;

public class PosePlayer : MonoBehaviour
{
    [SerializeField]
    private OVRCustomSkeleton customSkeleton;  // Inspector에서 OVRCustomSkeleton 할당

    [SerializeField]
    private float playbackInterval = 0.1f;  // 재생 간격 (초)

    private List<PoseFrame> loadedSequence = new List<PoseFrame>();  // 불러온 시퀀스
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
                    Debug.Log("재생 완료.");
                }
            }
        }
    }

    // csv 불러와 재생 시작 (csvFileName 입력, 예: "MySequence")
    public void StartPlaybackFromCSV(string csvFileName)
    {
        string path = Path.Combine(Application.persistentDataPath, csvFileName + ".csv");
        if (!File.Exists(path))
        {
            Debug.LogError("CSV 파일 없음.");
            return;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            Debug.LogError("CSV 데이터 부족.");
            return;
        }

        loadedSequence.Clear();
        PoseFrame currentFrame = null;
        int lastFrameIndex = -1;

        for (int i = 1; i < lines.Length; i++)  // 헤더 스킵
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
            Debug.LogError("CSV 파싱 실패.");
            return;
        }

        isPlaying = true;
        currentPlaybackIndex = 0;
        lastPlaybackTime = Time.time;
        ApplyFrameToBones();  // 첫 프레임 적용
        Debug.Log("재생 시작.");
    }

    // 프레임 데이터 CustomBones에 적용
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
                Debug.LogError("GetBoneTransform 메서드를 찾을 수 없음.");
            }
        }
        Debug.Log($"프레임 {currentPlaybackIndex} 적용.");
    }
}