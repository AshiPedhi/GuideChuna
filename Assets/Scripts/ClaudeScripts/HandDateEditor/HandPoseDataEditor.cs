using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using System.Globalization;

/// <summary>
/// 핸드 포즈 데이터 편집기 - 심플 버전
/// 녹화된 CSV 파일을 불러와서 구간을 선택하고 저장하는 기능
/// </summary>
public class HandPoseDataEditor : MonoBehaviour
{
    [Header("=== UI 연결 (선택사항) ===")]
    public Slider timelineSlider;      // 재생 위치 슬라이더
    public Slider startPointSlider;    // 시작점 슬라이더
    public Slider endPointSlider;      // 끝점 슬라이더

    [Header("=== 플레이어 연결 ===")]
    public HandPosePlayer handPosePlayer;  // 재생용 플레이어

    [Header("=== 현재 상태 ===")]
    public float currentTime = 0f;     // 현재 재생 시간
    public float startTime = 0f;       // 선택 구간 시작
    public float endTime = 0f;         // 선택 구간 끝
    public float totalDuration = 0f;   // 전체 길이
    public bool isPlaying = false;     // 재생 중인지
    public string currentFileName = "";// 현재 로드된 파일명

    [Header("=== 편집기 설정 ===")]
    [SerializeField]
    private bool autoLoadToPlayer = true;  // 파일 로드 시 자동으로 플레이어에도 로드
    [SerializeField]
    private bool livePreview = true;       // 슬라이더 조작 시 실시간 미리보기

    // 내부 데이터
    private List<HandPoseData> loadedData = new List<HandPoseData>();
    private float playbackSpeed = 1.0f;
    private bool isInitialized = false;

    [System.Serializable]
    private class HandPoseData
    {
        public int frameIndex;
        public float timestamp;
        public string handType;
        public int jointId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public bool hasWorldData;
    }

    void Start()
    {
        // 슬라이더가 연결되어 있으면 이벤트 연결
        if (timelineSlider != null)
            timelineSlider.onValueChanged.AddListener(OnTimelineChanged);
        if (startPointSlider != null)
            startPointSlider.onValueChanged.AddListener(OnStartPointChanged);
        if (endPointSlider != null)
            endPointSlider.onValueChanged.AddListener(OnEndPointChanged);

        // HandPosePlayer가 있으면 재생 전용 모드로 설정
        if (handPosePlayer != null)
        {
            // PlaybackOnly 모드 활성화 (비교 없이 재생만)
            handPosePlayer.EnablePlaybackOnlyMode();
            Debug.Log("HandPosePlayer 재생 전용 모드 설정됨");
        }

        isInitialized = true;
    }

    void Update()
    {
        // 재생 중일 때 타임라인 업데이트
        if (isPlaying && handPosePlayer != null)
        {
            // HandPosePlayer의 현재 시간 가져오기
            float playerTime = handPosePlayer.GetCurrentTime();

            // 플레이어의 재생이 완료되었는지 확인
            if (!handPosePlayer.IsLeftHandPlaying() && !handPosePlayer.IsRightHandPlaying())
            {
                isPlaying = false;
                currentTime = totalDuration;
                Debug.Log("재생 완료");
            }
            else
            {
                currentTime = playerTime;
            }

            // 슬라이더 업데이트
            if (timelineSlider != null)
                timelineSlider.SetValueWithoutNotify(currentTime);
        }
    }

    // ==================== 핵심 기능 메서드 ====================

    /// <summary>
    /// CSV 파일 불러오기
    /// </summary>
    public void LoadFile(string fileName)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!path.EndsWith(".csv"))
            path += ".csv";

        if (!File.Exists(path))
        {
            Debug.LogError($"파일 없음: {path}");
            return;
        }

        try
        {
            loadedData.Clear();

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2)
            {
                Debug.LogError("CSV 데이터 부족");
                return;
            }

            CultureInfo invariantCulture = CultureInfo.InvariantCulture;

            // 헤더 확인
            string[] headers = lines[0].Split(',');

            // 데이터 파싱 (헤더 스킵)
            for (int i = 1; i < lines.Length; i++)
            {
                string[] values = lines[i].Split(',');
                if (values.Length < 11) continue;

                HandPoseData data = new HandPoseData
                {
                    frameIndex = int.Parse(values[0]),
                    timestamp = float.Parse(values[10], invariantCulture),
                    handType = values[1].Trim(),
                    jointId = int.Parse(values[2]),
                    localPosition = new Vector3(
                        float.Parse(values[3], invariantCulture),
                        float.Parse(values[4], invariantCulture),
                        float.Parse(values[5], invariantCulture)
                    ),
                    localRotation = new Quaternion(
                        float.Parse(values[6], invariantCulture),
                        float.Parse(values[7], invariantCulture),
                        float.Parse(values[8], invariantCulture),
                        float.Parse(values[9], invariantCulture)
                    ),
                    hasWorldData = false
                };

                // 월드 좌표 (있으면)
                if (values.Length > 17 && !string.IsNullOrEmpty(values[11]))
                {
                    try
                    {
                        data.worldPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        data.worldRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );
                        data.hasWorldData = true;
                    }
                    catch
                    {
                        // 월드 데이터 파싱 실패 시 무시
                        data.hasWorldData = false;
                    }
                }

                loadedData.Add(data);
            }

            // 시간 설정
            if (loadedData.Count > 0)
            {
                totalDuration = loadedData.Max(d => d.timestamp);
                startTime = 0f;
                endTime = totalDuration;
                currentTime = 0f;
                currentFileName = Path.GetFileNameWithoutExtension(fileName);

                // 슬라이더 업데이트
                UpdateSliders();

                int totalFrames = loadedData.Select(d => d.frameIndex).Distinct().Count();

                Debug.Log($"<color=green>파일 로드 완료:</color> {currentFileName}");
                Debug.Log($"총 {loadedData.Count}개 데이터, {totalFrames}개 프레임, {totalDuration:F2}초");

                // HandPosePlayer에도 자동 로드
                if (autoLoadToPlayer && handPosePlayer != null)
                {
                    // 파일명만 전달 (확장자 제외)
                    handPosePlayer.LoadFromCSV(currentFileName);

                    // 첫 프레임 표시
                    handPosePlayer.GoToFrame(0);

                    Debug.Log($"HandPosePlayer에 {currentFileName} 로드됨");
                }
            }
            else
            {
                Debug.LogError("유효한 데이터가 없습니다.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"파일 로드 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 선택한 구간만 새 파일로 저장
    /// </summary>
    public void SaveSelection(string newFileName = null)
    {
        if (loadedData.Count == 0)
        {
            Debug.LogError("로드된 데이터 없음");
            return;
        }

        // 파일명 결정
        string fileName = string.IsNullOrEmpty(newFileName)
            ? $"{currentFileName}_edit_{startTime:F1}to{endTime:F1}"
            : newFileName;

        // 확장자 확인
        if (!fileName.EndsWith(".csv"))
            fileName += ".csv";

        string path = Path.Combine(Application.persistentDataPath, fileName);

        try
        {
            // 선택 구간 데이터만 추출
            var selectedData = loadedData.Where(d => d.timestamp >= startTime && d.timestamp <= endTime).ToList();

            if (selectedData.Count == 0)
            {
                Debug.LogError("선택한 구간에 데이터가 없습니다.");
                return;
            }

            // 시간 조정 (시작을 0으로)
            float timeOffset = startTime;

            // 프레임 인덱스 재정렬
            int newFrameIndex = 0;
            int lastOriginalFrame = -1;
            Dictionary<int, int> frameRemap = new Dictionary<int, int>();

            foreach (var data in selectedData.OrderBy(d => d.timestamp))
            {
                if (data.frameIndex != lastOriginalFrame)
                {
                    frameRemap[data.frameIndex] = newFrameIndex++;
                    lastOriginalFrame = data.frameIndex;
                }
            }

            // CSV 작성
            using (StreamWriter writer = new StreamWriter(path))
            {
                // 헤더
                writer.WriteLine("FrameIndex,HandType,JointID,LocalPosX,LocalPosY,LocalPosZ," +
                               "LocalRotX,LocalRotY,LocalRotZ,LocalRotW,Timestamp," +
                               "WorldPosX,WorldPosY,WorldPosZ,WorldRotX,WorldRotY,WorldRotZ,WorldRotW");

                CultureInfo invariantCulture = CultureInfo.InvariantCulture;

                // 데이터 작성
                foreach (var data in selectedData.OrderBy(d => d.timestamp).ThenBy(d => d.handType).ThenBy(d => d.jointId))
                {
                    // 새 프레임 인덱스와 조정된 타임스탬프 사용
                    int newFrame = frameRemap[data.frameIndex];
                    float newTimestamp = data.timestamp - timeOffset;

                    string line;
                    if (data.hasWorldData)
                    {
                        line = string.Format(invariantCulture,
                            "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F3}," +
                            "{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4}",
                            newFrame, data.handType, data.jointId,
                            data.localPosition.x, data.localPosition.y, data.localPosition.z,
                            data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                            newTimestamp,
                            data.worldPosition.x, data.worldPosition.y, data.worldPosition.z,
                            data.worldRotation.x, data.worldRotation.y, data.worldRotation.z, data.worldRotation.w
                        );
                    }
                    else
                    {
                        line = string.Format(invariantCulture,
                            "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F3},,,,,,",
                            newFrame, data.handType, data.jointId,
                            data.localPosition.x, data.localPosition.y, data.localPosition.z,
                            data.localRotation.x, data.localRotation.y, data.localRotation.z, data.localRotation.w,
                            newTimestamp
                        );
                    }

                    writer.WriteLine(line);
                }
            }

            float newDuration = endTime - startTime;
            int newTotalFrames = frameRemap.Count;

            Debug.Log($"<color=green>저장 완료!</color> {fileName}");
            Debug.Log($"구간: {startTime:F2}초 ~ {endTime:F2}초 ({newDuration:F2}초)");
            Debug.Log($"프레임: {newTotalFrames}개, 데이터: {selectedData.Count}개");
        }
        catch (Exception e)
        {
            Debug.LogError($"저장 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 재생 시작/일시정지 토글
    /// </summary>
    public void TogglePlayback()
    {
        if (loadedData.Count == 0)
        {
            Debug.LogWarning("로드된 데이터 없음");
            return;
        }

        isPlaying = !isPlaying;

        if (isPlaying)
        {
            // 끝에 있으면 처음으로
            if (currentTime >= totalDuration)
            {
                currentTime = 0f;
                SeekToTime(0f);
            }

            if (handPosePlayer != null)
            {
                // 현재 위치부터 재생 시작
                handPosePlayer.StartPlayback();
            }

            Debug.Log("재생 시작");
        }
        else
        {
            if (handPosePlayer != null)
            {
                handPosePlayer.PausePlayback();
            }

            Debug.Log("일시정지");
        }
    }

    /// <summary>
    /// 재생 정지 (처음으로 되돌리기)
    /// </summary>
    public void StopPlayback()
    {
        isPlaying = false;
        currentTime = 0f;

        if (timelineSlider != null)
            timelineSlider.SetValueWithoutNotify(0f);

        if (handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
            handPosePlayer.GoToFrame(0);
        }

        Debug.Log("정지 - 처음으로");
    }

    /// <summary>
    /// 특정 시간으로 이동
    /// </summary>
    public void SeekToTime(float time)
    {
        currentTime = Mathf.Clamp(time, 0f, totalDuration);

        if (timelineSlider != null)
            timelineSlider.SetValueWithoutNotify(currentTime);

        // HandPosePlayer에 시간 위치 전달
        if (handPosePlayer != null && loadedData.Count > 0)
        {
            // SeekToTime 메서드 사용 (HandPosePlayer에 구현되어 있음)
            handPosePlayer.SeekToTime(currentTime);

            Debug.Log($"이동: {currentTime:F2}초 (HandPosePlayer 연동)");
        }
        else
        {
            Debug.Log($"이동: {currentTime:F2}초");
        }
    }

    /// <summary>
    /// 시작점을 현재 위치로 설정
    /// </summary>
    public void SetStartToCurrent()
    {
        startTime = currentTime;

        // 끝점보다 뒤면 조정
        if (startTime > endTime)
            endTime = startTime;

        if (startPointSlider != null)
            startPointSlider.SetValueWithoutNotify(startTime);

        Debug.Log($"시작점 설정: {startTime:F2}초");
    }

    /// <summary>
    /// 끝점을 현재 위치로 설정
    /// </summary>
    public void SetEndToCurrent()
    {
        endTime = currentTime;

        // 시작점보다 앞이면 조정
        if (endTime < startTime)
            startTime = endTime;

        if (endPointSlider != null)
            endPointSlider.SetValueWithoutNotify(endTime);

        Debug.Log($"끝점 설정: {endTime:F2}초");
    }

    /// <summary>
    /// 재생 속도 설정
    /// </summary>
    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Clamp(speed, 0.1f, 5f);

        // HandPosePlayer의 재생 속도도 동기화
        if (handPosePlayer != null)
        {
            handPosePlayer.SetPlaybackSpeed(playbackSpeed);
        }

        Debug.Log($"재생 속도: {playbackSpeed:F1}x");
    }

    /// <summary>
    /// persistentDataPath의 CSV 파일 목록 가져오기
    /// </summary>
    public List<string> GetFileList()
    {
        List<string> files = new List<string>();

        try
        {
            string[] csvFiles = Directory.GetFiles(Application.persistentDataPath, "*.csv");

            foreach (string file in csvFiles)
            {
                files.Add(Path.GetFileNameWithoutExtension(file));
            }

            files.Sort();
        }
        catch (Exception e)
        {
            Debug.LogError($"파일 목록 가져오기 실패: {e.Message}");
        }

        return files;
    }

    /// <summary>
    /// 파일 삭제
    /// </summary>
    public void DeleteFile(string fileName)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!path.EndsWith(".csv"))
            path += ".csv";

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"파일 삭제됨: {fileName}");
            }
            else
            {
                Debug.LogWarning($"파일이 존재하지 않음: {fileName}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"파일 삭제 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 현재 편집기 상태 정보 가져오기
    /// </summary>
    public string GetStatusInfo()
    {
        if (loadedData.Count == 0)
            return "파일이 로드되지 않음";

        return $"파일: {currentFileName}\n" +
               $"현재: {currentTime:F2}초 / 전체: {totalDuration:F2}초\n" +
               $"선택 구간: {startTime:F2}초 ~ {endTime:F2}초 ({endTime - startTime:F2}초)\n" +
               $"재생: {(isPlaying ? "재생 중" : "정지")} | 속도: {playbackSpeed:F1}x\n" +
               $"미리보기: {(livePreview ? "ON" : "OFF")}";
    }

    /// <summary>
    /// 실시간 미리보기 설정
    /// </summary>
    public void SetLivePreview(bool enable)
    {
        livePreview = enable;
        Debug.Log($"실시간 미리보기: {(enable ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// 자동 플레이어 로드 설정
    /// </summary>
    public void SetAutoLoadToPlayer(bool enable)
    {
        autoLoadToPlayer = enable;
        Debug.Log($"자동 플레이어 로드: {(enable ? "활성화" : "비활성화")}");
    }

    // ==================== 내부 메서드 ====================

    private void OnTimelineChanged(float value)
    {
        currentTime = value;

        // 실시간 미리보기가 켜져있고 재생 중이 아닐 때
        if (livePreview && !isPlaying && handPosePlayer != null && loadedData.Count > 0)
        {
            // HandPosePlayer의 SeekToTime 호출
            handPosePlayer.SeekToTime(currentTime);

            // 디버그 로그 (매 프레임 출력 방지)
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"타임라인 미리보기: {currentTime:F2}초");
            }
        }
    }

    private void OnStartPointChanged(float value)
    {
        startTime = Mathf.Min(value, endTime);

        if (startPointSlider != null && startTime != value)
            startPointSlider.SetValueWithoutNotify(startTime);

        Debug.Log($"시작점: {startTime:F2}초");
    }

    private void OnEndPointChanged(float value)
    {
        endTime = Mathf.Max(value, startTime);

        if (endPointSlider != null && endTime != value)
            endPointSlider.SetValueWithoutNotify(endTime);

        Debug.Log($"끝점: {endTime:F2}초");
    }

    private void UpdateSliders()
    {
        if (timelineSlider != null)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = totalDuration;
            timelineSlider.SetValueWithoutNotify(0f);
        }

        if (startPointSlider != null)
        {
            startPointSlider.minValue = 0f;
            startPointSlider.maxValue = totalDuration;
            startPointSlider.SetValueWithoutNotify(0f);
        }

        if (endPointSlider != null)
        {
            endPointSlider.minValue = 0f;
            endPointSlider.maxValue = totalDuration;
            endPointSlider.SetValueWithoutNotify(totalDuration);
        }
    }

    /// <summary>
    /// 시간에 해당하는 프레임 인덱스 찾기
    /// </summary>
    private int GetFrameIndexAtTime(float time)
    {
        if (loadedData.Count == 0) return 0;

        // 시간 이하의 마지막 프레임 찾기
        var frameAtTime = loadedData
            .Where(d => d.timestamp <= time)
            .Select(d => d.frameIndex)
            .Distinct()
            .OrderBy(f => f)
            .LastOrDefault();

        return frameAtTime;
    }

    /// <summary>
    /// 프레임 개수 가져오기
    /// </summary>
    public int GetTotalFrames()
    {
        if (loadedData.Count == 0) return 0;
        return loadedData.Select(d => d.frameIndex).Distinct().Count();
    }

    /// <summary>
    /// 특정 프레임의 시간 가져오기
    /// </summary>
    public float GetTimeAtFrame(int frameIndex)
    {
        if (loadedData.Count == 0) return 0f;

        var frameData = loadedData.Where(d => d.frameIndex == frameIndex).FirstOrDefault();
        return frameData != null ? frameData.timestamp : 0f;
    }

    void OnDestroy()
    {
        // 종료 시 재생 중지
        if (isPlaying && handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
        }
    }

#if UNITY_EDITOR
    // 에디터에서 값 변경 시
    void OnValidate()
    {
        // 에디터에서 설정 변경 시 즉시 적용
        if (isInitialized && handPosePlayer != null)
        {
            if (!isPlaying && livePreview && loadedData.Count > 0)
            {
                handPosePlayer.SeekToTime(currentTime);
            }
        }
    }
#endif
}