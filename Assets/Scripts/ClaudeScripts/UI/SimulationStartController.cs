using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationStartController : MonoBehaviour
{
    [Header("═══ 시작 버튼 ═══")]
    [SerializeField] private Button startButton;
    [SerializeField] private GameObject startButtonObject;
    [SerializeField] private TextMeshProUGUI startButtonText;
    
    [Header("═══ 애니메이션 ═══")]
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float pulseScale = 1.1f;
    
    private ModeSelectionManagerV2 modeManager;
    private bool isReadyToStart = false;
    private Coroutine pulseCoroutine;
    
    void Awake()
    {
        modeManager = FindObjectOfType<ModeSelectionManagerV2>();
    }
    
    void Start()
    {
        SetupStartButton();
        UpdateStartButtonState();
    }
    
    void SetupStartButton()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClick);
            
            // 시작 버튼 텍스트 설정
            if (startButtonText != null)
            {
                startButtonText.text = "시작하기";
            }
        }
        
        // 초기에는 비활성화
        if (startButtonObject != null)
        {
            startButtonObject.SetActive(false);
        }
    }
    
    public void UpdateStartButtonState()
    {
        if (modeManager == null) return;
        
        // 모드가 선택되었는지 확인
        bool isModeSelected = (modeManager.GetSelectedMode() != ModeSelectionManagerV2.ModeType.None);
        
        // 시작 버튼 표시/숨김
        if (startButtonObject != null)
        {
            startButtonObject.SetActive(isModeSelected);
            
            if (isModeSelected && !isReadyToStart)
            {
                isReadyToStart = true;
                StartPulseAnimation();
            }
            else if (!isModeSelected && isReadyToStart)
            {
                isReadyToStart = false;
                StopPulseAnimation();
            }
        }
        
        // 버튼 상호작용 가능 여부
        if (startButton != null)
        {
            startButton.interactable = isModeSelected;
        }
        
        // 버튼 텍스트 업데이트
        UpdateButtonText();
    }
    
    void UpdateButtonText()
    {
        if (startButtonText == null || modeManager == null) return;
        
        string mode = "";
        string difficulty = "";
        
        switch (modeManager.GetSelectedMode())
        {
            case ModeSelectionManagerV2.ModeType.Practice:
                mode = "실습";
                break;
            case ModeSelectionManagerV2.ModeType.Evaluation:
                mode = "평가";
                break;
            default:
                mode = "";
                break;
        }
        
        switch (modeManager.GetSelectedDifficulty())
        {
            case ModeSelectionManagerV2.DifficultyType.Beginner:
                difficulty = "초급";
                break;
            case ModeSelectionManagerV2.DifficultyType.Intermediate:
                difficulty = "중급";
                break;
            case ModeSelectionManagerV2.DifficultyType.Advanced:
                difficulty = "상급";
                break;
        }
        
        if (!string.IsNullOrEmpty(mode))
        {
            startButtonText.text = $"{difficulty} {mode} 시작";
        }
        else
        {
            startButtonText.text = "시작하기";
        }
    }
    
    void StartPulseAnimation()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
        }
        pulseCoroutine = StartCoroutine(PulseAnimation());
    }
    
    void StopPulseAnimation()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }
        
        // 스케일 원래대로
        if (startButtonObject != null)
        {
            startButtonObject.transform.localScale = Vector3.one;
        }
    }
    
    IEnumerator PulseAnimation()
    {
        Transform buttonTransform = startButtonObject.transform;
        
        while (true)
        {
            // 확대
            float elapsed = 0f;
            float duration = 1f / pulseSpeed;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float scale = Mathf.Lerp(1f, pulseScale, Mathf.Sin(t * Mathf.PI));
                buttonTransform.localScale = Vector3.one * scale;
                yield return null;
            }
        }
    }
    
    void OnStartButtonClick()
    {
        if (modeManager == null) return;
        
        Debug.Log("시뮬레이션 시작!");
        
        // 선택 정보 로그
        Debug.Log($"- 모드: {modeManager.GetSelectedMode()}");
        Debug.Log($"- 난이도: {modeManager.GetSelectedDifficulty()}");
        
        // 페이드 아웃 효과
        StartCoroutine(StartSimulationWithFade());
    }
    
    IEnumerator StartSimulationWithFade()
    {
        // 화면 페이드 아웃 (필요시 CanvasGroup 사용)
        CanvasGroup canvasGroup = GetComponentInParent<CanvasGroup>();
        if (canvasGroup == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }
        }
        
        if (canvasGroup != null)
        {
            float fadeTime = 0.5f;
            float elapsed = 0f;
            
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeTime);
                yield return null;
            }
        }
        
        // 실제 시뮬레이션 시작
        modeManager.StartSimulation();
    }
    
    // ModeSelectionManagerV2에서 호출할 수 있는 public 메서드
    public void OnModeSelectionChanged()
    {
        UpdateStartButtonState();
    }
    
    void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
        }
        
        StopPulseAnimation();
    }
}
