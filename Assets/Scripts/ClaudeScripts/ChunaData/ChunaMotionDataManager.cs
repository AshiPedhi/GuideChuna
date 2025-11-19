using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 추나 동작 데이터 관리 유틸리티
/// CSV 파일 관리, 데이터 검증, 프리셋 관리 등을 담당
/// </summary>
public class ChunaMotionDataManager : MonoBehaviour
{
    [Header("=== 데이터 경로 설정 ===")]
    [SerializeField] private string dataFolderPath = "ChunaMotionData";
    [SerializeField] private bool useStreamingAssets = true;
    [SerializeField] private bool createSampleData = false;

    [Header("=== 프리셋 관리 ===")]
    [SerializeField] private List<MotionPreset> motionPresets = new List<MotionPreset>();
    [SerializeField] private int activePresetIndex = 0;

    [Header("=== 데이터 검증 설정 ===")]
    [SerializeField] private bool validateOnLoad = true;
    [SerializeField] private bool autoFixErrors = false;
    [SerializeField] private float maxPositionValue = 2.0f; // 최대 위치값 (미터)
    [SerializeField] private int minFrameCount = 10; // 최소 프레임 수

    // 싱글톤
    private static ChunaMotionDataManager instance;
    public static ChunaMotionDataManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<ChunaMotionDataManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("ChunaMotionDataManager");
                    instance = go.AddComponent<ChunaMotionDataManager>();
                }
            }
            return instance;
        }
    }

    // 데이터 캐시
    private Dictionary<string, MotionData> loadedMotionData = new Dictionary<string, MotionData>();

    [System.Serializable]
    public class MotionPreset
    {
        public string presetName = "기본 프리셋";
        public string description = "추나 시술 기본 동작 세트";
        public List<StepMotionInfo> steps = new List<StepMotionInfo>();
        public DifficultyLevel difficulty = DifficultyLevel.Beginner;
        public float estimatedDuration = 60f; // 예상 소요 시간 (초)

        [System.Serializable]
        public class StepMotionInfo
        {
            public string stepName;
            public string csvFileName;
            public float holdTime = 1.0f;
            public float positionThreshold = 0.05f;
            public float rotationThreshold = 15f;
            public float similarityThreshold = 0.7f;
            public string instruction = "";
            public string voiceGuidePath = "";
            public bool isOptional = false;
        }

        public enum DifficultyLevel
        {
            Beginner,
            Intermediate,
            Advanced,
            Expert
        }
    }

    [System.Serializable]
    public class MotionData
    {
        public string fileName;
        public List<FrameData> frames = new List<FrameData>();
        public float totalDuration;
        public int totalFrames;
        public bool hasLeftHand;
        public bool hasRightHand;
        public ValidationResult validation;

        [System.Serializable]
        public class FrameData
        {
            public int frameIndex;
            public float timestamp;
            public Dictionary<string, List<JointData>> handData = new Dictionary<string, List<JointData>>();
        }

        [System.Serializable]
        public class JointData
        {
            public int jointId;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 worldPosition;
            public Quaternion worldRotation;
        }

        [System.Serializable]
        public class ValidationResult
        {
            public bool isValid = true;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
            public DateTime validatedTime;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeDataManager();
    }

    private void InitializeDataManager()
    {
        // 데이터 폴더 확인/생성
        string fullPath = GetDataPath();
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            Debug.Log($"<color=cyan>데이터 폴더 생성: {fullPath}</color>");
        }

        // 샘플 데이터 생성
        if (createSampleData)
        {
            CreateSampleDataFiles();
        }

        // 기존 데이터 검색
        ScanForMotionDataFiles();
    }

    /// <summary>
    /// 데이터 경로 가져오기
    /// </summary>
    private string GetDataPath()
    {
        if (useStreamingAssets)
        {
            return Path.Combine(Application.streamingAssetsPath, dataFolderPath);
        }
        else
        {
            return Path.Combine(Application.persistentDataPath, dataFolderPath);
        }
    }

    /// <summary>
    /// 모션 데이터 파일 검색
    /// </summary>
    private void ScanForMotionDataFiles()
    {
        string path = GetDataPath();
        if (!Directory.Exists(path)) return;

        string[] csvFiles = Directory.GetFiles(path, "*.csv");
        Debug.Log($"<color=yellow>{csvFiles.Length}개의 모션 데이터 파일 발견</color>");

        foreach (string file in csvFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            Debug.Log($"  - {fileName}");
        }
    }

    /// <summary>
    /// CSV 파일 로드 (비동기)
    /// </summary>
    public async UniTask<MotionData> LoadMotionDataAsync(string fileName)
    {
        // 캐시 확인
        if (loadedMotionData.ContainsKey(fileName))
        {
            Debug.Log($"<color=green>캐시에서 로드: {fileName}</color>");
            return loadedMotionData[fileName];
        }

        // 파일 경로
        string fullPath = Path.Combine(GetDataPath(), fileName + ".csv");

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"파일을 찾을 수 없습니다: {fullPath}");
            return null;
        }

        try
        {
            // 파일 읽기
            string csvContent = await File.ReadAllTextAsync(fullPath);

            // 파싱
            MotionData motionData = ParseCSVData(csvContent, fileName);

            // 검증
            if (validateOnLoad)
            {
                ValidateMotionData(motionData);

                if (!motionData.validation.isValid && !autoFixErrors)
                {
                    Debug.LogError($"데이터 검증 실패: {fileName}");
                    foreach (var error in motionData.validation.errors)
                    {
                        Debug.LogError($"  - {error}");
                    }
                    return null;
                }

                if (autoFixErrors && motionData.validation.errors.Count > 0)
                {
                    motionData = FixMotionDataErrors(motionData);
                }
            }

            // 캐시에 저장
            loadedMotionData[fileName] = motionData;

            Debug.Log($"<color=green>로드 완료: {fileName} (프레임: {motionData.totalFrames}, 시간: {motionData.totalDuration:F2}초)</color>");

            return motionData;
        }
        catch (Exception e)
        {
            Debug.LogError($"파일 로드 실패: {fileName}\n{e.Message}");
            return null;
        }
    }

    /// <summary>
    /// CSV 데이터 파싱
    /// </summary>
    private MotionData ParseCSVData(string csvContent, string fileName)
    {
        MotionData motionData = new MotionData
        {
            fileName = fileName,
            validation = new MotionData.ValidationResult()
        };

        string[] lines = csvContent.Split('\n');
        if (lines.Length < 2)
        {
            motionData.validation.errors.Add("데이터가 부족합니다");
            return motionData;
        }

        // 헤더 파싱
        string[] headers = lines[0].Split(',');

        // 프레임별 데이터 그룹화
        Dictionary<int, MotionData.FrameData> frameDict = new Dictionary<int, MotionData.FrameData>();

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] values = lines[i].Split(',');
            if (values.Length < 11) continue;

            try
            {
                int frameIndex = int.Parse(values[0]);
                string handType = values[1];
                int jointId = int.Parse(values[2]);

                // 프레임 데이터 생성/가져오기
                if (!frameDict.ContainsKey(frameIndex))
                {
                    frameDict[frameIndex] = new MotionData.FrameData
                    {
                        frameIndex = frameIndex,
                        timestamp = float.Parse(values[10]),
                        handData = new Dictionary<string, List<MotionData.JointData>>()
                    };
                }

                var frame = frameDict[frameIndex];

                // 손 타입별 데이터 생성
                if (!frame.handData.ContainsKey(handType))
                {
                    frame.handData[handType] = new List<MotionData.JointData>();
                }

                // 조인트 데이터 추가
                var jointData = new MotionData.JointData
                {
                    jointId = jointId,
                    localPosition = new Vector3(
                        float.Parse(values[3]),
                        float.Parse(values[4]),
                        float.Parse(values[5])
                    ),
                    localRotation = new Quaternion(
                        float.Parse(values[6]),
                        float.Parse(values[7]),
                        float.Parse(values[8]),
                        float.Parse(values[9])
                    )
                };

                // 월드 좌표 (있는 경우)
                if (values.Length > 11 && !string.IsNullOrWhiteSpace(values[11]))
                {
                    jointData.worldPosition = new Vector3(
                        float.Parse(values[11]),
                        float.Parse(values[12]),
                        float.Parse(values[13])
                    );
                    jointData.worldRotation = new Quaternion(
                        float.Parse(values[14]),
                        float.Parse(values[15]),
                        float.Parse(values[16]),
                        float.Parse(values[17])
                    );
                }

                frame.handData[handType].Add(jointData);

                // 손 타입 플래그 설정
                if (handType == "Left") motionData.hasLeftHand = true;
                if (handType == "Right") motionData.hasRightHand = true;
            }
            catch (Exception e)
            {
                motionData.validation.warnings.Add($"라인 {i} 파싱 오류: {e.Message}");
            }
        }

        // 프레임 리스트로 변환 (정렬)
        motionData.frames = frameDict.Values.OrderBy(f => f.frameIndex).ToList();
        motionData.totalFrames = motionData.frames.Count;

        if (motionData.frames.Count > 0)
        {
            motionData.totalDuration = motionData.frames.Last().timestamp;
        }

        return motionData;
    }

    /// <summary>
    /// 모션 데이터 검증
    /// </summary>
    private void ValidateMotionData(MotionData data)
    {
        data.validation.validatedTime = DateTime.Now;
        data.validation.errors.Clear();
        data.validation.warnings.Clear();

        // 프레임 수 확인
        if (data.totalFrames < minFrameCount)
        {
            data.validation.errors.Add($"프레임 수가 너무 적습니다 ({data.totalFrames} < {minFrameCount})");
        }

        // 프레임 간격 확인
        for (int i = 1; i < data.frames.Count; i++)
        {
            float timeDiff = data.frames[i].timestamp - data.frames[i - 1].timestamp;
            if (timeDiff < 0)
            {
                data.validation.errors.Add($"타임스탬프 오류: 프레임 {i}");
            }
            else if (timeDiff > 1.0f)
            {
                data.validation.warnings.Add($"프레임 간격이 큽니다: 프레임 {i - 1} -> {i} ({timeDiff:F2}초)");
            }
        }

        // 위치값 범위 확인
        foreach (var frame in data.frames)
        {
            foreach (var handData in frame.handData.Values)
            {
                foreach (var joint in handData)
                {
                    if (joint.localPosition.magnitude > maxPositionValue)
                    {
                        data.validation.warnings.Add($"비정상적인 위치값: 프레임 {frame.frameIndex}, 조인트 {joint.jointId}");
                    }

                    // 회전값 검증
                    if (Mathf.Abs(joint.localRotation.w) > 1.1f ||
                        Mathf.Abs(joint.localRotation.x) > 1.1f ||
                        Mathf.Abs(joint.localRotation.y) > 1.1f ||
                        Mathf.Abs(joint.localRotation.z) > 1.1f)
                    {
                        data.validation.errors.Add($"비정상적인 회전값: 프레임 {frame.frameIndex}, 조인트 {joint.jointId}");
                    }
                }
            }
        }

        // 손 데이터 일관성 확인
        bool hasLeftInAllFrames = true;
        bool hasRightInAllFrames = true;

        foreach (var frame in data.frames)
        {
            if (data.hasLeftHand && !frame.handData.ContainsKey("Left"))
            {
                hasLeftInAllFrames = false;
            }
            if (data.hasRightHand && !frame.handData.ContainsKey("Right"))
            {
                hasRightInAllFrames = false;
            }
        }

        if (data.hasLeftHand && !hasLeftInAllFrames)
        {
            data.validation.warnings.Add("일부 프레임에 왼손 데이터가 없습니다");
        }
        if (data.hasRightHand && !hasRightInAllFrames)
        {
            data.validation.warnings.Add("일부 프레임에 오른손 데이터가 없습니다");
        }

        // 최종 검증 결과
        data.validation.isValid = data.validation.errors.Count == 0;
    }

    /// <summary>
    /// 모션 데이터 오류 수정
    /// </summary>
    private MotionData FixMotionDataErrors(MotionData data)
    {
        Debug.Log($"<color=yellow>데이터 자동 수정 시작: {data.fileName}</color>");

        // 타임스탬프 정렬
        data.frames = data.frames.OrderBy(f => f.frameIndex).ToList();

        // 타임스탬프 수정
        float lastValidTime = 0;
        foreach (var frame in data.frames)
        {
            if (frame.timestamp < lastValidTime)
            {
                frame.timestamp = lastValidTime + 0.1f;
                Debug.Log($"  타임스탬프 수정: 프레임 {frame.frameIndex}");
            }
            lastValidTime = frame.timestamp;
        }

        // 비정상 위치값 클램핑
        foreach (var frame in data.frames)
        {
            foreach (var handData in frame.handData.Values)
            {
                foreach (var joint in handData)
                {
                    if (joint.localPosition.magnitude > maxPositionValue)
                    {
                        joint.localPosition = joint.localPosition.normalized * maxPositionValue;
                        Debug.Log($"  위치값 클램핑: 프레임 {frame.frameIndex}, 조인트 {joint.jointId}");
                    }

                    // 회전값 정규화
                    joint.localRotation = joint.localRotation.normalized;
                }
            }
        }

        // 재검증
        ValidateMotionData(data);

        Debug.Log($"<color=yellow>데이터 자동 수정 완료</color>");

        return data;
    }

    /// <summary>
    /// 프리셋 로드
    /// </summary>
    public async UniTask<List<MotionData>> LoadPresetAsync(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= motionPresets.Count)
        {
            Debug.LogError($"잘못된 프리셋 인덱스: {presetIndex}");
            return null;
        }

        var preset = motionPresets[presetIndex];
        List<MotionData> presetData = new List<MotionData>();

        Debug.Log($"<color=cyan>프리셋 로드 시작: {preset.presetName}</color>");

        foreach (var step in preset.steps)
        {
            var data = await LoadMotionDataAsync(step.csvFileName);
            if (data != null)
            {
                presetData.Add(data);
            }
            else if (!step.isOptional)
            {
                Debug.LogError($"필수 스텝 로드 실패: {step.stepName}");
                return null;
            }
        }

        Debug.Log($"<color=green>프리셋 로드 완료: {presetData.Count}개 스텝</color>");

        return presetData;
    }

    /// <summary>
    /// 샘플 데이터 생성
    /// </summary>
    private void CreateSampleDataFiles()
    {
        string path = GetDataPath();

        // 샘플 1: 기본 손 위치
        CreateSampleFile(Path.Combine(path, "Sample_HandPosition.csv"),
            GenerateSampleHandPosition());

        // 샘플 2: 회전 동작
        CreateSampleFile(Path.Combine(path, "Sample_HandRotation.csv"),
            GenerateSampleHandRotation());

        // 샘플 3: 복합 동작
        CreateSampleFile(Path.Combine(path, "Sample_ComplexMotion.csv"),
            GenerateSampleComplexMotion());

        Debug.Log("<color=green>샘플 데이터 파일 생성 완료</color>");
    }

    private void CreateSampleFile(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
            Debug.Log($"  생성: {Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            Debug.LogError($"파일 생성 실패: {path}\n{e.Message}");
        }
    }

    private string GenerateSampleHandPosition()
    {
        // CSV 헤더
        string csv = "FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                    "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                    "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW\n";

        // 간단한 손 위치 데이터 생성
        for (int frame = 0; frame < 30; frame++)
        {
            float time = frame * 0.1f;

            // 왼손 손목 위치 (위아래 움직임)
            float y = Mathf.Sin(time * 2) * 0.1f;
            csv += $"{frame},Left,0,0,{y:F4},0.3,0,0,0,1,{time:F2},0,{y:F4},0.3,0,0,0,1\n";

            // 오른손 손목 위치 (좌우 움직임)
            float x = Mathf.Cos(time * 2) * 0.1f;
            csv += $"{frame},Right,0,{x:F4},0,-0.3,0,0,0,1,{time:F2},{x:F4},0,-0.3,0,0,0,1\n";
        }

        return csv;
    }

    private string GenerateSampleHandRotation()
    {
        string csv = "FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                    "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                    "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW\n";

        for (int frame = 0; frame < 40; frame++)
        {
            float time = frame * 0.1f;
            float angle = time * 30; // 30도/초 회전

            // 쿼터니언 계산
            Quaternion rot = Quaternion.Euler(0, angle, 0);

            csv += $"{frame},Left,0,0,0,0.3,{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4},{time:F2}," +
                  $"0,0,0.3,{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4}\n";

            csv += $"{frame},Right,0,0,0,-0.3,{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4},{time:F2}," +
                  $"0,0,-0.3,{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4}\n";
        }

        return csv;
    }

    private string GenerateSampleComplexMotion()
    {
        string csv = "FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                    "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                    "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW\n";

        for (int frame = 0; frame < 50; frame++)
        {
            float time = frame * 0.1f;

            // 복합 움직임 (원 운동)
            float radius = 0.2f;
            float x = Mathf.Cos(time) * radius;
            float z = Mathf.Sin(time) * radius;

            // 손목 회전
            Quaternion rot = Quaternion.Euler(0, time * 45, 0);

            // 왼손
            csv += $"{frame},Left,0,{x:F4},0,{z:F4}," +
                  $"{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4},{time:F2}," +
                  $"{x:F4},0,{z:F4},{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4}\n";

            // 오른손 (반대 방향)
            csv += $"{frame},Right,0,{-x:F4},0,{-z:F4}," +
                  $"{rot.x:F4},{-rot.y:F4},{rot.z:F4},{rot.w:F4},{time:F2}," +
                  $"{-x:F4},0,{-z:F4},{rot.x:F4},{-rot.y:F4},{rot.z:F4},{rot.w:F4}\n";
        }

        return csv;
    }

    /// <summary>
    /// 캐시 클리어
    /// </summary>
    public void ClearCache()
    {
        loadedMotionData.Clear();
        Debug.Log("모션 데이터 캐시 클리어됨");
    }

    /// <summary>
    /// 특정 데이터 캐시에서 제거
    /// </summary>
    public void RemoveFromCache(string fileName)
    {
        if (loadedMotionData.ContainsKey(fileName))
        {
            loadedMotionData.Remove(fileName);
            Debug.Log($"캐시에서 제거: {fileName}");
        }
    }

    /// <summary>
    /// 로드된 데이터 목록
    /// </summary>
    public List<string> GetLoadedDataList()
    {
        return loadedMotionData.Keys.ToList();
    }

    /// <summary>
    /// 프리셋 목록
    /// </summary>
    public List<string> GetPresetNames()
    {
        return motionPresets.Select(p => p.presetName).ToList();
    }
}