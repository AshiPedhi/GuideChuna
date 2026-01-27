using UnityEngine;
using UnityEngine.UI;
using TLab.WebView;

/// <summary>
/// VR 핸드트래킹으로 WebView 상호작용을 처리하는 스크립트
/// - 검지 손가락으로 WebView 포인팅
/// - 핀치 제스처로 클릭/드래그
/// </summary>
public class VRWebViewInput : MonoBehaviour
{
    [Header("=== WebView ===")]
    [Tooltip("WebView 또는 GeckoView 컴포넌트")]
    [SerializeField] private Browser browser;

    [Tooltip("WebView가 표시되는 RectTransform")]
    [SerializeField] private RectTransform webViewRect;

    [Header("=== Hand Tracking (Right) ===")]
    [SerializeField] private OVRHand handRight;
    [SerializeField] private OVRSkeleton skeletonRight;

    [Header("=== Hand Tracking (Left) ===")]
    [SerializeField] private OVRHand handLeft;
    [SerializeField] private OVRSkeleton skeletonLeft;

    [Header("=== Pointer 시각화 (선택) ===")]
    [Tooltip("손가락 위치를 표시할 포인터 오브젝트")]
    [SerializeField] private Transform pointer;

    [Tooltip("포인터가 WebView 앞에 표시될 거리")]
    [SerializeField] private float pointerDistance = 0.02f;

    [Header("=== 설정 ===")]
    [Tooltip("WebView와 상호작용 가능한 최대 거리 (미터)")]
    [SerializeField] private float interactionDistance = 0.15f;

    [Tooltip("핀치 인식 임계값 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float pinchThreshold = 0.7f;

    [Tooltip("디버그 로그 표시")]
    [SerializeField] private bool showDebugLogs = false;

    // Android 터치 액션 상수
    private const int ACTION_DOWN = 0;
    private const int ACTION_UP = 1;
    private const int ACTION_MOVE = 2;

    // 상태 변수
    private bool isPinching = false;
    private long touchDownTime = 0;
    private OVRHand activeHand;
    private OVRSkeleton activeSkeleton;
    private Vector2 lastTouchPoint;
    private bool isInitialized = false;

    void Start()
    {
        ValidateReferences();
        StartCoroutine(InitializeWebView());
    }

    /// <summary>
    /// WebView 초기화 (약간의 딜레이 후 실행)
    /// </summary>
    private System.Collections.IEnumerator InitializeWebView()
    {
        Debug.Log("[VRWebView] === INIT START ===");

        // 1프레임 대기
        yield return null;

        Debug.Log("[VRWebView] Frame waited, checking browser...");

        if (browser == null)
        {
            Debug.LogError("[VRWebView] ERROR: Browser is NULL!");
            yield break;
        }

        Debug.Log("[VRWebView] Browser is valid, getting info...");

        // WebView 상태 확인
        try
        {
            Debug.Log($"[VRWebView] Browser Type: {browser.GetType().Name}");
            Debug.Log($"[VRWebView] URL: {browser.url}");
            Debug.Log($"[VRWebView] View Size: {browser.viewSize}");
            Debug.Log($"[VRWebView] Tex Size: {browser.texSize}");
            Debug.Log($"[VRWebView] Capture Mode: {browser.captureMode}");
            Debug.Log($"[VRWebView] Current State: {browser.state}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRWebView] ERROR getting info: {e.Message}");
        }

        // Init 호출
        Debug.Log("[VRWebView] Calling browser.Init()...");
        try
        {
            browser.Init();
            Debug.Log("[VRWebView] browser.Init() called successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRWebView] ERROR in Init(): {e.Message}\n{e.StackTrace}");
        }

        // 잠시 대기 후 상태 확인
        Debug.Log("[VRWebView] Waiting 2 seconds...");
        yield return new WaitForSeconds(2f);

        try
        {
            Debug.Log($"[VRWebView] State after init: {browser.state}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRWebView] ERROR checking state: {e.Message}");
        }

        // URL 로드 시도
        if (!string.IsNullOrEmpty(browser.url))
        {
            Debug.Log($"[VRWebView] Loading URL: {browser.url}");
            try
            {
                browser.LoadUrl(browser.url);
                Debug.Log("[VRWebView] LoadUrl() called");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VRWebView] ERROR in LoadUrl(): {e.Message}");
            }
        }

        isInitialized = true;
        Debug.Log("[VRWebView] === INIT COMPLETE ===");
    }

    void Update()
    {
        if (browser == null || webViewRect == null) return;

        // 활성화된 손 찾기
        FindActiveHand();

        if (activeHand == null || !activeHand.IsTracked)
        {
            HidePointer();

            // 핀치 중이었다면 터치 종료 처리
            if (isPinching)
            {
                isPinching = false;
            }
            return;
        }

        // 검지 손가락 끝 위치 가져오기
        Vector3 fingerTip = GetIndexFingerTip();

        // WebView와의 교차 확인
        if (TryGetWebViewPoint(fingerTip, out Vector2 normalizedPoint, out Vector3 hitPoint))
        {
            // 포인터 표시
            ShowPointer(hitPoint);

            // 웹뷰 픽셀 좌표 계산
            int x = (int)(normalizedPoint.x * browser.viewSize.x);
            int y = (int)(normalizedPoint.y * browser.viewSize.y);

            // 핀치 감지
            bool pinching = activeHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            float pinchStrength = activeHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            bool isPinchValid = pinching && pinchStrength > pinchThreshold;

            // 터치 이벤트 처리
            ProcessTouch(x, y, isPinchValid);

            lastTouchPoint = normalizedPoint;
        }
        else
        {
            HidePointer();

            // 범위 벗어났는데 핀치 중이었다면 터치 종료
            if (isPinching)
            {
                int x = (int)(lastTouchPoint.x * browser.viewSize.x);
                int y = (int)(lastTouchPoint.y * browser.viewSize.y);
                browser.TouchEvent(x, y, ACTION_UP, touchDownTime);
                isPinching = false;

                if (showDebugLogs)
                    Debug.Log($"[VRWebView] Touch UP (out of bounds)");
            }
        }
    }

    /// <summary>
    /// 참조 유효성 검사
    /// </summary>
    private void ValidateReferences()
    {
        if (browser == null)
            Debug.LogError("[VRWebViewInput] Browser가 연결되지 않았습니다!");

        if (webViewRect == null)
            Debug.LogError("[VRWebViewInput] WebView RectTransform이 연결되지 않았습니다!");

        if (handRight == null && handLeft == null)
            Debug.LogWarning("[VRWebViewInput] 손이 연결되지 않았습니다. OVRHand를 연결해주세요.");
    }

    /// <summary>
    /// 활성화된 손 찾기 (트래킹 중인 손 우선)
    /// </summary>
    private void FindActiveHand()
    {
        // 오른손 우선
        if (handRight != null && handRight.IsTracked && handRight.IsDataHighConfidence)
        {
            activeHand = handRight;
            activeSkeleton = skeletonRight;
        }
        else if (handLeft != null && handLeft.IsTracked && handLeft.IsDataHighConfidence)
        {
            activeHand = handLeft;
            activeSkeleton = skeletonLeft;
        }
        // High Confidence 아니어도 트래킹 중이면 사용
        else if (handRight != null && handRight.IsTracked)
        {
            activeHand = handRight;
            activeSkeleton = skeletonRight;
        }
        else if (handLeft != null && handLeft.IsTracked)
        {
            activeHand = handLeft;
            activeSkeleton = skeletonLeft;
        }
        else
        {
            activeHand = null;
            activeSkeleton = null;
        }
    }

    /// <summary>
    /// 검지 손가락 끝 위치 가져오기
    /// </summary>
    private Vector3 GetIndexFingerTip()
    {
        if (activeSkeleton != null && activeSkeleton.Bones != null)
        {
            foreach (var bone in activeSkeleton.Bones)
            {
                if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
                {
                    return bone.Transform.position;
                }
            }
        }

        // Fallback: PointerPose 사용
        if (activeHand != null)
        {
            return activeHand.PointerPose.position;
        }

        return Vector3.zero;
    }

    /// <summary>
    /// WebView 평면과의 교차점 계산
    /// </summary>
    private bool TryGetWebViewPoint(Vector3 fingerPos, out Vector2 normalizedPoint, out Vector3 hitPoint)
    {
        normalizedPoint = Vector2.zero;
        hitPoint = Vector3.zero;

        // 손가락 위치를 WebView 로컬 좌표로 변환
        Vector3 localPos = webViewRect.InverseTransformPoint(fingerPos);
        Rect rect = webViewRect.rect;

        // Z 거리 확인 (WebView 평면과의 거리)
        float distanceToPlane = Mathf.Abs(localPos.z);
        if (distanceToPlane > interactionDistance)
        {
            return false;
        }

        // 정규화 좌표 계산 (0~1)
        normalizedPoint.x = (localPos.x - rect.x) / rect.width;
        normalizedPoint.y = 1f - (localPos.y - rect.y) / rect.height; // Y축 반전 (WebView 좌표계)

        // 범위 체크 (WebView 영역 내인지)
        if (normalizedPoint.x < 0 || normalizedPoint.x > 1 ||
            normalizedPoint.y < 0 || normalizedPoint.y > 1)
        {
            return false;
        }

        // 히트 포인트 계산 (포인터 표시용)
        Vector3 clampedLocal = new Vector3(
            Mathf.Clamp(localPos.x, rect.x, rect.x + rect.width),
            Mathf.Clamp(localPos.y, rect.y, rect.y + rect.height),
            0
        );
        hitPoint = webViewRect.TransformPoint(clampedLocal);

        return true;
    }

    /// <summary>
    /// 터치 이벤트 처리
    /// </summary>
    private void ProcessTouch(int x, int y, bool isPinchValid)
    {
        if (isPinchValid && !isPinching)
        {
            // 터치 시작 (DOWN)
            touchDownTime = GetTimeMillis();
            browser.TouchEvent(x, y, ACTION_DOWN, touchDownTime);

            if (showDebugLogs)
                Debug.Log($"[VRWebView] Touch DOWN at ({x}, {y})");
        }
        else if (!isPinchValid && isPinching)
        {
            // 터치 종료 (UP)
            browser.TouchEvent(x, y, ACTION_UP, touchDownTime);

            if (showDebugLogs)
                Debug.Log($"[VRWebView] Touch UP at ({x}, {y})");
        }
        else if (isPinchValid && isPinching)
        {
            // 드래그 (MOVE)
            browser.TouchEvent(x, y, ACTION_MOVE, touchDownTime);

            if (showDebugLogs && Time.frameCount % 30 == 0)
                Debug.Log($"[VRWebView] Touch MOVE at ({x}, {y})");
        }

        isPinching = isPinchValid;
    }

    /// <summary>
    /// 포인터 표시
    /// </summary>
    private void ShowPointer(Vector3 position)
    {
        if (pointer != null)
        {
            pointer.gameObject.SetActive(true);
            // WebView 앞쪽에 포인터 표시
            pointer.position = position - webViewRect.forward * pointerDistance;
        }
    }

    /// <summary>
    /// 포인터 숨기기
    /// </summary>
    private void HidePointer()
    {
        if (pointer != null)
        {
            pointer.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 현재 시간 (밀리초)
    /// </summary>
    private long GetTimeMillis()
    {
        return System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

    #region Public Methods (외부 호출용)

    /// <summary>
    /// URL 로드
    /// </summary>
    public void LoadUrl(string url)
    {
        if (browser != null)
        {
            browser.LoadUrl(url);
            if (showDebugLogs)
                Debug.Log($"[VRWebView] Loading URL: {url}");
        }
    }

    /// <summary>
    /// 뒤로 가기
    /// </summary>
    public void GoBack()
    {
        if (browser != null)
        {
            browser.GoBack();
            if (showDebugLogs)
                Debug.Log("[VRWebView] Go Back");
        }
    }

    /// <summary>
    /// 앞으로 가기
    /// </summary>
    public void GoForward()
    {
        if (browser != null)
        {
            browser.GoForward();
            if (showDebugLogs)
                Debug.Log("[VRWebView] Go Forward");
        }
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    public void Reload()
    {
        if (browser != null)
        {
            string currentUrl = browser.GetUrl();
            browser.LoadUrl(currentUrl);
            if (showDebugLogs)
                Debug.Log("[VRWebView] Reload");
        }
    }

    /// <summary>
    /// 현재 URL 가져오기
    /// </summary>
    public string GetCurrentUrl()
    {
        return browser != null ? browser.GetUrl() : string.Empty;
    }

    /// <summary>
    /// 스크롤
    /// </summary>
    public void ScrollBy(int x, int y)
    {
        if (browser != null)
        {
            browser.ScrollBy(x, y);
        }
    }

    #endregion
}
