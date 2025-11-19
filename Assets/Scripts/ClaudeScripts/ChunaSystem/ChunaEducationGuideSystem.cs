using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 추나 교육 가이드 시스템
/// 고스트 핸드와 시각적 피드백을 제공
/// </summary>
public class ChunaEducationGuideSystem : MonoBehaviour
{
    [Header("=== 가이드 손 설정 ===")]
    [SerializeField] private GameObject guideLeftHandPrefab;
    [SerializeField] private GameObject guideRightHandPrefab;
    [SerializeField] private Material ghostHandMaterial;
    [SerializeField] private float ghostAlpha = 0.4f;
    [SerializeField] private Color perfectMatchColor = new Color(0, 1, 0, 0.5f);
    [SerializeField] private Color closeMatchColor = new Color(1, 1, 0, 0.5f);
    [SerializeField] private Color farMatchColor = new Color(1, 0, 0, 0.5f);
    
    [Header("=== 시각적 피드백 ===")]
    [SerializeField] private GameObject directionArrowPrefab;
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private GameObject distanceIndicatorPrefab;
    [SerializeField] private float arrowScale = 0.5f;
    [SerializeField] private float arrowDistance = 0.1f;
    
    [Header("=== 진행 표시 ===")]
    [SerializeField] private Image circularProgressBar;
    [SerializeField] private TextMeshProUGUI progressPercentText;
    [SerializeField] private GameObject checkpointMarkerPrefab;
    [SerializeField] private Transform checkpointContainer;
    
    [Header("=== 피드백 텍스트 ===")]
    [SerializeField] private TextMeshProUGUI realTimeFeedbackText;
    [SerializeField] private GameObject encouragementPanel;
    [SerializeField] private TextMeshProUGUI encouragementText;
    
    [Header("=== 설정 ===")]
    [SerializeField] private bool showDirectionArrows = true;
    [SerializeField] private bool showTrajectory = true;
    [SerializeField] private bool showDistanceIndicator = true;
    [SerializeField] private bool autoAdjustDifficulty = true;
    [SerializeField] private float feedbackUpdateInterval = 0.1f;
    
    // 가이드 손 인스턴스
    private GameObject activeLeftGuide;
    private GameObject activeRightGuide;
    private List<GameObject> directionArrows = new List<GameObject>();
    private List<GameObject> checkpointMarkers = new List<GameObject>();
    
    // 상태 변수
    private bool isGuiding = false;
    private float currentProgress = 0f;
    private Vector3 lastLeftHandPos;
    private Vector3 lastRightHandPos;
    
    // 피드백 메시지
    private string[] encouragementMessages = new string[]
    {
        "잘하고 있어요!",
        "완벽해요!",
        "조금만 더 가까이!",
        "거의 다 왔어요!",
        "훌륭합니다!",
        "정확해요!"
    };
    
    private string[] correctionMessages = new string[]
    {
        "손을 조금 더 높이 들어보세요",
        "팔꿈치를 더 굽혀보세요",
        "천천히 따라해보세요",
        "손목 각도를 조정해보세요",
        "더 넓게 벌려보세요"
    };
    
    /// <summary>
    /// 가이드 시작
    /// </summary>
    public void StartGuiding(string motionDataFile)
    {
        if (isGuiding) return;
        
        isGuiding = true;
        currentProgress = 0f;
        
        // 가이드 손 생성
        CreateGuideHands();
        
        // 피드백 시스템 시작
        StartCoroutine(UpdateFeedbackLoop());
        
        Debug.Log($"<color=cyan>교육 가이드 시작: {motionDataFile}</color>");
    }
    
    /// <summary>
    /// 가이드 중지
    /// </summary>
    public void StopGuiding()
    {
        isGuiding = false;
        
        // 가이드 손 제거
        DestroyGuideHands();
        
        // 화살표 제거
        ClearDirectionArrows();
        
        // 체크포인트 제거
        ClearCheckpoints();
        
        StopAllCoroutines();
    }
    
    /// <summary>
    /// 가이드 손 생성
    /// </summary>
    private void CreateGuideHands()
    {
        // 왼손 가이드
        if (guideLeftHandPrefab != null && activeLeftGuide == null)
        {
            activeLeftGuide = Instantiate(guideLeftHandPrefab);
            ApplyGhostMaterial(activeLeftGuide);
        }
        
        // 오른손 가이드
        if (guideRightHandPrefab != null && activeRightGuide == null)
        {
            activeRightGuide = Instantiate(guideRightHandPrefab);
            ApplyGhostMaterial(activeRightGuide);
        }
    }
    
    /// <summary>
    /// 가이드 손 제거
    /// </summary>
    private void DestroyGuideHands()
    {
        if (activeLeftGuide != null)
        {
            Destroy(activeLeftGuide);
            activeLeftGuide = null;
        }
        
        if (activeRightGuide != null)
        {
            Destroy(activeRightGuide);
            activeRightGuide = null;
        }
    }
    
    /// <summary>
    /// 고스트 재질 적용
    /// </summary>
    private void ApplyGhostMaterial(GameObject hand)
    {
        if (ghostHandMaterial == null)
        {
            // 기본 반투명 재질 생성
            ghostHandMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ghostHandMaterial.SetFloat("_Surface", 1); // Transparent
            ghostHandMaterial.SetFloat("_Blend", 0);   // Alpha
        }
        
        Renderer[] renderers = hand.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.material = ghostHandMaterial;
            
            // 알파 설정
            Color color = renderer.material.color;
            color.a = ghostAlpha;
            renderer.material.color = color;
        }
    }
    
    /// <summary>
    /// 실시간 피드백 업데이트 루프
    /// </summary>
    private IEnumerator UpdateFeedbackLoop()
    {
        while (isGuiding)
        {
            // 플레이어 손 위치 가져오기
            Vector3 playerLeftPos = GetPlayerLeftHandPosition();
            Vector3 playerRightPos = GetPlayerRightHandPosition();
            
            // 가이드 손 위치 가져오기
            Vector3 guideLeftPos = GetGuideLeftHandPosition();
            Vector3 guideRightPos = GetGuideRightHandPosition();
            
            // 거리 계산
            float leftDistance = Vector3.Distance(playerLeftPos, guideLeftPos);
            float rightDistance = Vector3.Distance(playerRightPos, guideRightPos);
            
            // 색상 피드백 업데이트
            UpdateHandColorFeedback(leftDistance, rightDistance);
            
            // 방향 화살표 업데이트
            if (showDirectionArrows)
            {
                UpdateDirectionArrows(playerLeftPos, guideLeftPos, playerRightPos, guideRightPos);
            }
            
            // 궤적 표시
            if (showTrajectory)
            {
                UpdateTrajectory(playerLeftPos, playerRightPos);
            }
            
            // 거리 표시기
            if (showDistanceIndicator)
            {
                UpdateDistanceIndicators(leftDistance, rightDistance);
            }
            
            // 텍스트 피드백
            UpdateTextFeedback(leftDistance, rightDistance);
            
            // 진행도 업데이트
            UpdateProgress(leftDistance, rightDistance);
            
            // 격려 메시지
            if (Random.Range(0f, 1f) < 0.05f) // 5% 확률로 격려 메시지
            {
                ShowEncouragement(leftDistance, rightDistance);
            }
            
            yield return new WaitForSeconds(feedbackUpdateInterval);
        }
    }
    
    /// <summary>
    /// 손 색상 피드백 업데이트
    /// </summary>
    private void UpdateHandColorFeedback(float leftDist, float rightDist)
    {
        if (activeLeftGuide != null)
        {
            SetHandColor(activeLeftGuide, GetFeedbackColor(leftDist));
        }
        
        if (activeRightGuide != null)
        {
            SetHandColor(activeRightGuide, GetFeedbackColor(rightDist));
        }
    }
    
    /// <summary>
    /// 거리에 따른 피드백 색상
    /// </summary>
    private Color GetFeedbackColor(float distance)
    {
        if (distance < 0.05f) return perfectMatchColor;
        if (distance < 0.1f) return closeMatchColor;
        return farMatchColor;
    }
    
    /// <summary>
    /// 방향 화살표 업데이트
    /// </summary>
    private void UpdateDirectionArrows(Vector3 playerLeft, Vector3 guideLeft, Vector3 playerRight, Vector3 guideRight)
    {
        ClearDirectionArrows();
        
        // 왼손 화살표
        if (Vector3.Distance(playerLeft, guideLeft) > 0.05f)
        {
            CreateDirectionArrow(playerLeft, guideLeft - playerLeft);
        }
        
        // 오른손 화살표
        if (Vector3.Distance(playerRight, guideRight) > 0.05f)
        {
            CreateDirectionArrow(playerRight, guideRight - playerRight);
        }
    }
    
    /// <summary>
    /// 방향 화살표 생성
    /// </summary>
    private void CreateDirectionArrow(Vector3 position, Vector3 direction)
    {
        if (directionArrowPrefab == null) return;
        
        GameObject arrow = Instantiate(directionArrowPrefab);
        arrow.transform.position = position + direction.normalized * arrowDistance;
        arrow.transform.rotation = Quaternion.LookRotation(direction);
        arrow.transform.localScale = Vector3.one * arrowScale;
        
        directionArrows.Add(arrow);
    }
    
    /// <summary>
    /// 방향 화살표 제거
    /// </summary>
    private void ClearDirectionArrows()
    {
        foreach (var arrow in directionArrows)
        {
            if (arrow != null) Destroy(arrow);
        }
        directionArrows.Clear();
    }
    
    /// <summary>
    /// 궤적 업데이트
    /// </summary>
    private void UpdateTrajectory(Vector3 leftPos, Vector3 rightPos)
    {
        if (trajectoryLine == null) return;
        
        // 궤적 점 추가 (최대 50개 점)
        if (trajectoryLine.positionCount < 50)
        {
            trajectoryLine.positionCount++;
            trajectoryLine.SetPosition(trajectoryLine.positionCount - 1, leftPos);
        }
        else
        {
            // 오래된 점 제거하고 새 점 추가
            for (int i = 0; i < trajectoryLine.positionCount - 1; i++)
            {
                trajectoryLine.SetPosition(i, trajectoryLine.GetPosition(i + 1));
            }
            trajectoryLine.SetPosition(trajectoryLine.positionCount - 1, leftPos);
        }
    }
    
    /// <summary>
    /// 거리 표시기 업데이트
    /// </summary>
    private void UpdateDistanceIndicators(float leftDist, float rightDist)
    {
        if (realTimeFeedbackText != null)
        {
            realTimeFeedbackText.text = $"왼손: {leftDist*100:F1}cm | 오른손: {rightDist*100:F1}cm";
        }
    }
    
    /// <summary>
    /// 텍스트 피드백 업데이트
    /// </summary>
    private void UpdateTextFeedback(float leftDist, float rightDist)
    {
        string feedback = "";
        
        if (leftDist > 0.15f)
        {
            feedback += "왼손을 가이드에 더 가까이 움직이세요\n";
        }
        
        if (rightDist > 0.15f)
        {
            feedback += "오른손을 가이드에 더 가까이 움직이세요\n";
        }
        
        if (leftDist < 0.05f && rightDist < 0.05f)
        {
            feedback = "완벽합니다! 이 자세를 유지하세요!";
        }
        
        if (realTimeFeedbackText != null)
        {
            realTimeFeedbackText.text = feedback;
        }
    }
    
    /// <summary>
    /// 진행도 업데이트
    /// </summary>
    private void UpdateProgress(float leftDist, float rightDist)
    {
        // 거리 기반 진행도 계산
        float avgDistance = (leftDist + rightDist) / 2f;
        float accuracy = Mathf.Clamp01(1f - avgDistance / 0.2f);
        
        currentProgress = Mathf.Lerp(currentProgress, accuracy, Time.deltaTime * 2f);
        
        // UI 업데이트
        if (circularProgressBar != null)
        {
            circularProgressBar.fillAmount = currentProgress;
        }
        
        if (progressPercentText != null)
        {
            progressPercentText.text = $"{currentProgress * 100:F0}%";
        }
    }
    
    /// <summary>
    /// 격려 메시지 표시
    /// </summary>
    private void ShowEncouragement(float leftDist, float rightDist)
    {
        if (encouragementPanel == null || encouragementText == null) return;
        
        float avgDist = (leftDist + rightDist) / 2f;
        string message = "";
        
        if (avgDist < 0.05f)
        {
            message = encouragementMessages[Random.Range(0, encouragementMessages.Length)];
        }
        else if (avgDist > 0.15f)
        {
            message = correctionMessages[Random.Range(0, correctionMessages.Length)];
        }
        
        if (!string.IsNullOrEmpty(message))
        {
            StartCoroutine(ShowEncouragementMessage(message));
        }
    }
    
    /// <summary>
    /// 격려 메시지 애니메이션
    /// </summary>
    private IEnumerator ShowEncouragementMessage(string message)
    {
        encouragementText.text = message;
        encouragementPanel.SetActive(true);
        
        // 페이드인
        CanvasGroup canvasGroup = encouragementPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = encouragementPanel.AddComponent<CanvasGroup>();
        }
        
        float fadeTime = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeTime);
            yield return null;
        }
        
        yield return new WaitForSeconds(2f);
        
        // 페이드아웃
        elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            yield return null;
        }
        
        encouragementPanel.SetActive(false);
    }
    
    /// <summary>
    /// 체크포인트 추가
    /// </summary>
    public void AddCheckpoint(Vector3 position, string label)
    {
        if (checkpointMarkerPrefab == null) return;
        
        GameObject marker = Instantiate(checkpointMarkerPrefab, checkpointContainer);
        marker.transform.position = position;
        
        TextMeshPro text = marker.GetComponentInChildren<TextMeshPro>();
        if (text != null)
        {
            text.text = label;
        }
        
        checkpointMarkers.Add(marker);
    }
    
    /// <summary>
    /// 체크포인트 제거
    /// </summary>
    private void ClearCheckpoints()
    {
        foreach (var marker in checkpointMarkers)
        {
            if (marker != null) Destroy(marker);
        }
        checkpointMarkers.Clear();
    }
    
    // === 헬퍼 메서드들 ===
    
    private Vector3 GetPlayerLeftHandPosition()
    {
        // 실제 플레이어 왼손 위치 가져오기
        // TODO: OVRHand 또는 HandVisual에서 위치 가져오기
        return Vector3.zero;
    }
    
    private Vector3 GetPlayerRightHandPosition()
    {
        // 실제 플레이어 오른손 위치 가져오기
        // TODO: OVRHand 또는 HandVisual에서 위치 가져오기
        return Vector3.zero;
    }
    
    private Vector3 GetGuideLeftHandPosition()
    {
        if (activeLeftGuide != null)
        {
            return activeLeftGuide.transform.position;
        }
        return Vector3.zero;
    }
    
    private Vector3 GetGuideRightHandPosition()
    {
        if (activeRightGuide != null)
        {
            return activeRightGuide.transform.position;
        }
        return Vector3.zero;
    }
    
    private void SetHandColor(GameObject hand, Color color)
    {
        Renderer[] renderers = hand.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (renderer.material != null)
            {
                renderer.material.color = color;
            }
        }
    }
    
    /// <summary>
    /// 난이도 자동 조정
    /// </summary>
    public void AdjustDifficulty(float userPerformance)
    {
        if (!autoAdjustDifficulty) return;
        
        if (userPerformance > 0.8f)
        {
            // 난이도 증가: 더 빠른 속도, 더 정확한 동작 요구
            Debug.Log("난이도 증가");
        }
        else if (userPerformance < 0.4f)
        {
            // 난이도 감소: 느린 속도, 더 넓은 허용 범위
            Debug.Log("난이도 감소");
        }
    }
}
