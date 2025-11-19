using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 도트 타임라인 제어 스크립트
/// 
/// [특징]
/// - 단계별 진행 상태를 시각화 (완료/현재/미완료)
/// - 현재 단계 도트에 펄싱 애니메이션
/// - 비상호작용 UI (정보 표시 전용)
/// 
/// [사용 방법]
/// 1. DotTimeline 프리팹에 자동으로 포함됨
/// 2. SetTotalSteps()로 전체 단계 수 설정
/// 3. SetCurrentStep()으로 현재 단계 업데이트
/// </summary>
public class DotTimelineController : MonoBehaviour
{
    [Header("Dot Settings")]
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private Transform dotsContainer;
    [SerializeField] private float dotSpacing = 80f;
    
    [Header("Colors")]
    [SerializeField] private Color completedColor = new Color(0.3f, 0.8f, 0.3f, 1f); // 완료: 초록색
    [SerializeField] private Color currentColor = new Color(1f, 1f, 1f, 1f);        // 현재: 흰색
    [SerializeField] private Color incompleteColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // 미완료: 회색 반투명
    [SerializeField] private Color lineColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);   // 라인: 회색
    
    [Header("Animation")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseMinScale = 0.9f;
    [SerializeField] private float pulseMaxScale = 1.1f;
    
    private List<Image> dots = new List<Image>();
    private List<Image> lines = new List<Image>();
    private int currentStep = 0;
    private int totalSteps = 0;
    private Coroutine pulseCoroutine;
    
    /// <summary>
    /// 전체 단계 수를 설정하고 타임라인을 초기화합니다
    /// </summary>
    public void SetTotalSteps(int steps)
    {
        if (steps <= 0)
        {
            Debug.LogError("[DotTimeline] 단계 수는 1 이상이어야 합니다!");
            return;
        }
        
        totalSteps = steps;
        ClearTimeline();
        CreateTimeline();
        SetCurrentStep(0);
    }
    
    /// <summary>
    /// 현재 단계를 설정합니다 (0부터 시작)
    /// </summary>
    public void SetCurrentStep(int step)
    {
        if (step < 0 || step >= totalSteps)
        {
            Debug.LogWarning($"[DotTimeline] 유효하지 않은 단계: {step} (전체: {totalSteps})");
            return;
        }
        
        currentStep = step;
        UpdateDotStates();
    }
    
    /// <summary>
    /// 다음 단계로 진행합니다
    /// </summary>
    public bool NextStep()
    {
        if (currentStep < totalSteps - 1)
        {
            SetCurrentStep(currentStep + 1);
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// 이전 단계로 돌아갑니다
    /// </summary>
    public bool PreviousStep()
    {
        if (currentStep > 0)
        {
            SetCurrentStep(currentStep - 1);
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// 현재 단계 번호를 반환합니다
    /// </summary>
    public int GetCurrentStep() => currentStep;
    
    /// <summary>
    /// 전체 단계 수를 반환합니다
    /// </summary>
    public int GetTotalSteps() => totalSteps;
    
    /// <summary>
    /// 마지막 단계인지 확인합니다
    /// </summary>
    public bool IsLastStep() => currentStep >= totalSteps - 1;
    
    private void CreateTimeline()
    {
        if (dotPrefab == null || dotsContainer == null)
        {
            Debug.LogError("[DotTimeline] dotPrefab 또는 dotsContainer가 설정되지 않았습니다!");
            return;
        }
        
        // 도트와 라인 생성
        for (int i = 0; i < totalSteps; i++)
        {
            // 도트 생성
            GameObject dotObj = Instantiate(dotPrefab, dotsContainer);
            dotObj.name = $"Dot_{i}";
            Image dotImage = dotObj.GetComponent<Image>();
            dots.Add(dotImage);
            
            // 위치 설정
            RectTransform dotRect = dotObj.GetComponent<RectTransform>();
            dotRect.anchoredPosition = new Vector2(i * dotSpacing, 0);
            
            // 마지막 도트가 아니면 라인 생성
            if (i < totalSteps - 1)
            {
                GameObject lineObj = new GameObject($"Line_{i}");
                lineObj.transform.SetParent(dotsContainer);
                lineObj.transform.SetSiblingIndex(dotObj.transform.GetSiblingIndex()); // 도트 뒤에 배치
                
                Image lineImage = lineObj.AddComponent<Image>();
                lineImage.color = lineColor;
                
                RectTransform lineRect = lineObj.GetComponent<RectTransform>();
                lineRect.anchoredPosition = new Vector2((i + 0.5f) * dotSpacing, 0);
                lineRect.sizeDelta = new Vector2(dotSpacing - 40f, 4f); // 라인 두께: 4px
                
                lines.Add(lineImage);
            }
        }
    }
    
    private void UpdateDotStates()
    {
        // 펄싱 애니메이션 중지
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }
        
        for (int i = 0; i < dots.Count; i++)
        {
            Image dot = dots[i];
            
            // 스케일 초기화
            dot.transform.localScale = Vector3.one;
            
            if (i < currentStep)
            {
                // 완료된 단계: 이중 원 효과 (외곽선)
                dot.color = completedColor;
                dot.sprite = null; // 기본 원형
            }
            else if (i == currentStep)
            {
                // 현재 단계: 채워진 원 + 펄싱
                dot.color = currentColor;
                dot.sprite = null;
                pulseCoroutine = StartCoroutine(PulseDot(dot));
            }
            else
            {
                // 미완료 단계: 빈 원
                dot.color = incompleteColor;
                dot.sprite = null;
            }
        }
    }
    
    private IEnumerator PulseDot(Image dot)
    {
        while (true)
        {
            // 확대
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * pulseSpeed;
                float scale = Mathf.Lerp(pulseMinScale, pulseMaxScale, t);
                dot.transform.localScale = Vector3.one * scale;
                yield return null;
            }
            
            // 축소
            t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * pulseSpeed;
                float scale = Mathf.Lerp(pulseMaxScale, pulseMinScale, t);
                dot.transform.localScale = Vector3.one * scale;
                yield return null;
            }
        }
    }
    
    private void ClearTimeline()
    {
        // 기존 도트와 라인 제거
        foreach (var dot in dots)
        {
            if (dot != null)
                Destroy(dot.gameObject);
        }
        foreach (var line in lines)
        {
            if (line != null)
                Destroy(line.gameObject);
        }
        
        dots.Clear();
        lines.Clear();
        
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }
    }
    
    private void OnDestroy()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
        }
    }
}
