using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;

/// <summary>
/// HandPose CSV 데이터 로더
/// CSV 파일 로드 및 파싱 전담 클래스
///
/// 기능:
/// - Resources 폴더에서 CSV 로드
/// - PersistentDataPath에서 CSV 로드
/// - UTF-8, EUC-KR 자동 인코딩 감지
/// - 프레임 데이터 파싱
/// - OpenXRRoot Transform 데이터 포함
///
/// 사용법:
/// var loader = new HandPoseDataLoader();
/// List<PoseFrame> frames = loader.LoadFromResources("HandPoseData/등척성운동");
/// </summary>
public class HandPoseDataLoader
{
    /// <summary>
    /// 포즈 데이터 (조인트별 로컬 좌표)
    /// </summary>
    [System.Serializable]
    public class PoseData
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>
    /// 프레임 데이터 (특정 시점의 양손 포즈)
    /// </summary>
    [System.Serializable]
    public class PoseFrame
    {
        public Dictionary<int, PoseData> leftLocalPoses = new Dictionary<int, PoseData>();
        public Dictionary<int, PoseData> rightLocalPoses = new Dictionary<int, PoseData>();
        public Vector3 leftRootPosition;      // OpenXRRoot 위치
        public Quaternion leftRootRotation;   // OpenXRRoot 회전
        public Vector3 rightRootPosition;     // OpenXRRoot 위치
        public Quaternion rightRootRotation;  // OpenXRRoot 회전
        public float timestamp;
    }

    /// <summary>
    /// 로드 결과
    /// </summary>
    public class LoadResult
    {
        public List<PoseFrame> frames = new List<PoseFrame>();
        public bool success = false;
        public string errorMessage = "";
        public float totalDuration = 0f;
    }

    /// <summary>
    /// Resources 폴더에서 CSV 로드
    /// </summary>
    /// <param name="resourcePath">Resources 폴더 기준 경로 (확장자 제외). 예: "HandPoseData/등척성운동"</param>
    public LoadResult LoadFromResources(string resourcePath)
    {
        LoadResult result = new LoadResult();

        // .csv 확장자 제거
        string fileNameWithoutExt = resourcePath;
        if (fileNameWithoutExt.EndsWith(".csv"))
            fileNameWithoutExt = fileNameWithoutExt.Substring(0, fileNameWithoutExt.Length - 4);

        // Resources에서 로드
        TextAsset csvFile = Resources.Load<TextAsset>(fileNameWithoutExt);

        if (csvFile == null)
        {
            result.errorMessage = $"Resources/{fileNameWithoutExt} 파일을 찾을 수 없습니다!";
            Debug.LogError($"<color=red>[HandPoseDataLoader] {result.errorMessage}</color>");
            return result;
        }

        Debug.Log($"<color=green>[HandPoseDataLoader] ✓ Resources에서 CSV 로드 성공: {fileNameWithoutExt}</color>");

        // CSV 텍스트 디코딩 (한글 지원)
        string csvText = DecodeCSVText(csvFile.bytes);

        // 파싱
        return ParseCSV(csvText);
    }

    /// <summary>
    /// 파일 시스템에서 CSV 로드 (PersistentDataPath)
    /// </summary>
    public LoadResult LoadFromFile(string csvFileName)
    {
        LoadResult result = new LoadResult();

        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        if (!path.EndsWith(".csv"))
            path += ".csv";

        if (!File.Exists(path))
        {
            result.errorMessage = $"CSV 파일 없음: {path}";
            Debug.LogError($"[HandPoseDataLoader] {result.errorMessage}");
            return result;
        }

        string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        if (lines.Length < 2)
        {
            result.errorMessage = "CSV 데이터 부족.";
            Debug.LogError($"[HandPoseDataLoader] {result.errorMessage}");
            return result;
        }

        string csvText = string.Join("\n", lines);
        return ParseCSV(csvText);
    }

    /// <summary>
    /// CSV 텍스트 파싱
    /// </summary>
    private LoadResult ParseCSV(string csvText)
    {
        LoadResult result = new LoadResult();

        string[] lines = csvText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            result.errorMessage = "CSV 데이터 부족.";
            Debug.LogError($"[HandPoseDataLoader] {result.errorMessage}");
            return result;
        }

        PoseFrame currentFrame = null;
        int lastFrameIndex = -1;
        CultureInfo invariantCulture = CultureInfo.InvariantCulture;

        // CSV 파싱
        for (int i = 1; i < lines.Length; i++)
        {
            try
            {
                string[] values = lines[i].Split(',');

                if (values.Length < 11)
                {
                    Debug.LogWarning($"[HandPoseDataLoader] 라인 {i}: 필드가 부족합니다. ({values.Length}개)");
                    continue;
                }

                int frameIndex = int.Parse(values[0], invariantCulture);
                string handType = values[1].Trim();
                int jointId = int.Parse(values[2], invariantCulture);

                Vector3 pos = new Vector3(
                    float.Parse(values[3], invariantCulture),
                    float.Parse(values[4], invariantCulture),
                    float.Parse(values[5], invariantCulture)
                );
                Quaternion rot = new Quaternion(
                    float.Parse(values[6], invariantCulture),
                    float.Parse(values[7], invariantCulture),
                    float.Parse(values[8], invariantCulture),
                    float.Parse(values[9], invariantCulture)
                );
                float timestamp = float.Parse(values[10], invariantCulture);

                // 새 프레임 시작
                if (frameIndex != lastFrameIndex)
                {
                    if (currentFrame != null)
                        result.frames.Add(currentFrame);

                    currentFrame = new PoseFrame { timestamp = timestamp };
                    lastFrameIndex = frameIndex;
                }

                PoseData poseData = new PoseData { position = pos, rotation = rot };

                // 왼손 데이터
                if (handType == "Left")
                {
                    currentFrame.leftLocalPoses[jointId] = poseData;

                    // Root Transform 읽기 (WorldData)
                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.leftRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.leftRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );
                    }
                }
                // 오른손 데이터
                else if (handType == "Right")
                {
                    currentFrame.rightLocalPoses[jointId] = poseData;

                    // Root Transform 읽기 (WorldData)
                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.rightRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.rightRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HandPoseDataLoader] 라인 {i} 파싱 실패: {e.Message}\n라인 내용: {lines[i]}");
                continue;
            }
        }

        // 마지막 프레임 추가
        if (currentFrame != null)
            result.frames.Add(currentFrame);

        if (result.frames.Count == 0)
        {
            result.errorMessage = "CSV 파싱 실패.";
            Debug.LogError($"[HandPoseDataLoader] {result.errorMessage}");
            return result;
        }

        // 총 재생 시간 계산
        result.totalDuration = result.frames[result.frames.Count - 1].timestamp;
        result.success = true;

        Debug.Log($"<color=cyan>[HandPoseDataLoader] ✓ 파싱 완료 - {result.frames.Count} 프레임, 총 {result.totalDuration:F2}초</color>");

        return result;
    }

    /// <summary>
    /// CSV 바이트 배열을 올바른 인코딩으로 디코딩
    /// UTF-8, EUC-KR 자동 감지
    /// </summary>
    private string DecodeCSVText(byte[] bytes)
    {
        // 1. UTF-8 BOM 체크
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            Debug.Log("[HandPoseDataLoader] ✓ UTF-8 BOM 감지 - UTF-8로 디코딩");
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // 2. UTF-8 시도 (BOM 없음)
        try
        {
            string utf8Text = System.Text.Encoding.UTF8.GetString(bytes);

            // UTF-8 디코딩 오류 체크 (� 문자가 있으면 잘못된 인코딩)
            if (!utf8Text.Contains("�"))
            {
                // 한글이 제대로 디코딩되었는지 확인
                bool hasKorean = ContainsKoreanCharacters(utf8Text);

                if (hasKorean || !ContainsKoreanBytes(bytes))
                {
                    Debug.Log("[HandPoseDataLoader] ✓ UTF-8 인코딩 사용");
                    return utf8Text;
                }
            }
        }
        catch
        {
            Debug.LogWarning("[HandPoseDataLoader] UTF-8 디코딩 실패");
        }

        // 3. EUC-KR 시도
        try
        {
            System.Text.Encoding euckr = System.Text.Encoding.GetEncoding("euc-kr");
            string euckrText = euckr.GetString(bytes);

            Debug.Log("[HandPoseDataLoader] ✓ EUC-KR 인코딩 사용");
            return euckrText;
        }
        catch
        {
            Debug.LogWarning("[HandPoseDataLoader] EUC-KR 디코딩 실패");
        }

        // 4. 최후의 수단: 시스템 기본 인코딩
        Debug.LogWarning("[HandPoseDataLoader] ⚠ 기본 인코딩 사용 (한글이 깨질 수 있음)");
        return System.Text.Encoding.Default.GetString(bytes);
    }

    /// <summary>
    /// 문자열에 한글 문자가 있는지 확인
    /// 한글 유니코드 범위: AC00-D7A3 (가-힣)
    /// </summary>
    private bool ContainsKoreanCharacters(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 바이트 배열에 한글 바이트가 있는지 확인
    /// EUC-KR 한글 범위: 첫 바이트 0xB0-0xC8, 두 번째 바이트 0xA1-0xFE
    /// </summary>
    private bool ContainsKoreanBytes(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            // EUC-KR 한글 범위 체크
            if (bytes[i] >= 0xB0 && bytes[i] <= 0xC8 &&
                bytes[i + 1] >= 0xA1 && bytes[i + 1] <= 0xFE)
            {
                return true;
            }
        }
        return false;
    }
}
