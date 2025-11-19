using System.Collections;
using UnityEngine;

/// <summary>
/// HandPosePlayer에 루프 재생 기능을 추가하는 확장 컴포넌트
/// HandPosePlayer와 함께 GameObject에 부착하여 사용
/// 
/// [수정 내역]
/// - HandPosePlayer의 실제 메서드에 맞게 호출 수정
/// - OnPlaybackCompleted 이벤트 사용
/// - LoadFromCSV(), StopAllPlayback() 등 새로 추가된 메서드 활용
/// </summary>
[RequireComponent(typeof(HandPosePlayer))]
public class HandPoseLoopController : MonoBehaviour
{
    [Header("=== HandPosePlayer 참조 ===")]
    [SerializeField] private HandPosePlayer handPosePlayer;

    [Header("=== 루프 설정 ===")]
    [SerializeField] private bool enableLoopOnStart = true;

    [SerializeField]
    [Tooltip("-1 = 무한 루프, 0 = 루프 없음, 1+ = 지정 횟수")]
    private int loopCount = -1;

    [SerializeField]
    [Tooltip("루프 사이 대기 시간 (초)")]
    private float loopDelay = 0.5f;

    [SerializeField]
    [Tooltip("루프 활성화")]
    private bool loopEnabled = true;

    [Header("=== 재생 설정 ===")]
    [SerializeField] private string motionDataFileName;
    [SerializeField] private bool startOnEnable = false;

    // 상태 변수
    private int currentLoopIteration = 0;
    private bool isLooping = false;
    private Coroutine loopCoroutine = null;

    // 이벤트
    public System.Action OnLoopStarted;
    public System.Action<int> OnLoopIteration;
    public System.Action OnAllLoopsCompleted;

    // 공개 프로퍼티
    public bool IsLooping => isLooping;
    public int CurrentIteration => currentLoopIteration;
    public int TotalLoops => loopCount;

    private void Awake()
    {
        if (handPosePlayer == null)
        {
            handPosePlayer = GetComponent<HandPosePlayer>();
        }

        if (handPosePlayer == null)
        {
            Debug.LogError("[HandPoseLoopController] HandPosePlayer를 찾을 수 없습니다!");
            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        if (handPosePlayer != null)
        {
            // ★ 수정: HandPosePlayer의 실제 이벤트 구독
            handPosePlayer.OnPlaybackCompleted += OnPlaybackCompleted;
        }

        if (startOnEnable && loopEnabled && !string.IsNullOrEmpty(motionDataFileName))
        {
            StartLoopPlayback(motionDataFileName);
        }
    }

    private void OnDisable()
    {
        if (handPosePlayer != null)
        {
            // ★ 수정: 이벤트 구독 해제
            handPosePlayer.OnPlaybackCompleted -= OnPlaybackCompleted;
        }

        StopLoopPlayback();
    }

    /// <summary>
    /// 재생 완료 시 호출되는 콜백
    /// </summary>
    private void OnPlaybackCompleted()
    {
        if (!loopEnabled || !isLooping) return;

        currentLoopIteration++;

        Debug.Log($"[HandPoseLoopController] 루프 {currentLoopIteration}회 완료");

        OnLoopIteration?.Invoke(currentLoopIteration);

        // 루프 횟수 체크
        if (loopCount >= 0 && currentLoopIteration >= loopCount)
        {
            // 지정된 횟수 완료
            Debug.Log($"[HandPoseLoopController] 모든 루프 완료 (총 {currentLoopIteration}회)");
            OnAllLoopsCompleted?.Invoke();
            isLooping = false;
            return;
        }

        // 다음 루프 시작
        if (loopDelay > 0f)
        {
            loopCoroutine = StartCoroutine(DelayedLoopRestart());
        }
        else
        {
            RestartPlayback();
        }
    }

    /// <summary>
    /// 지연 후 재생 재시작
    /// </summary>
    private IEnumerator DelayedLoopRestart()
    {
        yield return new WaitForSeconds(loopDelay);
        RestartPlayback();
    }

    /// <summary>
    /// 재생 재시작
    /// </summary>
    private void RestartPlayback()
    {
        if (handPosePlayer != null && !string.IsNullOrEmpty(motionDataFileName))
        {
            // ★ 수정: 실제 메서드 사용
            handPosePlayer.StopAllPlayback();
            handPosePlayer.LoadFromCSV(motionDataFileName);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 공개 메서드
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 루프 재생 시작
    /// </summary>
    public void StartLoopPlayback(string csvFileName)
    {
        if (string.IsNullOrEmpty(csvFileName))
        {
            Debug.LogError("[HandPoseLoopController] CSV 파일명이 비어있습니다!");
            return;
        }

        motionDataFileName = csvFileName;
        currentLoopIteration = 0;
        isLooping = true;

        // ★ 수정: PlaybackOnly 모드 활성화
        handPosePlayer.EnablePlaybackOnlyMode();

        // ★ 수정: 재생 시작 (LoadFromCSV 사용)
        handPosePlayer.LoadFromCSV(csvFileName);

        Debug.Log($"[HandPoseLoopController] 루프 재생 시작: {csvFileName}");

        OnLoopStarted?.Invoke();
    }

    /// <summary>
    /// 루프 재생 중지
    /// </summary>
    public void StopLoopPlayback()
    {
        isLooping = false;

        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            loopCoroutine = null;
        }

        if (handPosePlayer != null)
        {
            // ★ 수정: StopAllPlayback 사용
            handPosePlayer.StopAllPlayback();
        }

        Debug.Log("[HandPoseLoopController] 루프 재생 중지");
    }

    /// <summary>
    /// 루프 일시 중지
    /// </summary>
    public void PauseLoopPlayback()
    {
        if (handPosePlayer != null)
        {
            // ★ 수정: 실제 메서드 사용
            handPosePlayer.PausePlayback();
        }
    }

    /// <summary>
    /// 루프 재개
    /// </summary>
    public void ResumeLoopPlayback()
    {
        if (handPosePlayer != null)
        {
            // ★ 수정: 실제 메서드 사용
            handPosePlayer.ResumePlayback();
        }
    }

    /// <summary>
    /// 루프 횟수 설정
    /// </summary>
    public void SetLoopCount(int count)
    {
        loopCount = count;
    }

    /// <summary>
    /// 루프 지연 시간 설정
    /// </summary>
    public void SetLoopDelay(float delay)
    {
        loopDelay = Mathf.Max(0f, delay);
    }

    /// <summary>
    /// 루프 활성화/비활성화
    /// </summary>
    public void EnableLoop(bool enable)
    {
        loopEnabled = enable;

        if (!enable && isLooping)
        {
            StopLoopPlayback();
        }
    }

    /// <summary>
    /// 현재 루프 진행 상황 (0.0 ~ 1.0)
    /// </summary>
    public float GetLoopProgress()
    {
        if (loopCount <= 0) return 0f;
        return (float)currentLoopIteration / loopCount;
    }

    // ═══════════════════════════════════════════════════════════════
    // 디버그 메서드
    // ═══════════════════════════════════════════════════════════════

    [ContextMenu("테스트 - 루프 시작")]
    private void TestStartLoop()
    {
        if (!string.IsNullOrEmpty(motionDataFileName))
        {
            StartLoopPlayback(motionDataFileName);
        }
        else
        {
            Debug.LogWarning("[HandPoseLoopController] motionDataFileName을 설정하세요!");
        }
    }

    [ContextMenu("테스트 - 루프 중지")]
    private void TestStopLoop()
    {
        StopLoopPlayback();
    }

    [ContextMenu("테스트 - 일시 중지")]
    private void TestPause()
    {
        PauseLoopPlayback();
    }

    [ContextMenu("테스트 - 재개")]
    private void TestResume()
    {
        ResumeLoopPlayback();
    }
}