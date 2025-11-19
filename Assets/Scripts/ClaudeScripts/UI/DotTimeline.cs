using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class DotTimeline : MonoBehaviour
{
    [Header("Dot Settings")]
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private Transform dotsContainer;
    [SerializeField] private int totalDots = 5;
    
    [Header("Dot Colors")]
    [SerializeField] private Color completedColor = new Color(0.2f, 0.8f, 0.4f, 1f); // 완료된 단계 - 초록색
    [SerializeField] private Color currentColor = new Color(1f, 0.8f, 0.2f, 1f); // 현재 단계 - 노란색
    [SerializeField] private Color upcomingColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // 미완료 단계 - 회색
    
    [Header("Dot Size")]
    [SerializeField] private float completedDotSize = 12f;
    [SerializeField] private float currentDotSize = 16f;
    [SerializeField] private float upcomingDotSize = 10f;
    
    private List<Image> dotImages = new List<Image>();
    private int currentStep = 0;
    
    void Start()
    {
        InitializeDots();
        UpdateDots();
    }
    
    public void InitializeDots()
    {
        // 기존 도트들 제거
        foreach (Transform child in dotsContainer)
        {
            Destroy(child.gameObject);
        }
        dotImages.Clear();
        
        // 새로운 도트들 생성
        for (int i = 0; i < totalDots; i++)
        {
            GameObject dot = new GameObject($"Dot_{i}");
            dot.transform.SetParent(dotsContainer, false);
            
            Image dotImage = dot.AddComponent<Image>();
            dotImage.sprite = CreateCircleSprite();
            dotImage.raycastTarget = false;
            
            RectTransform rectTransform = dot.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(upcomingDotSize, upcomingDotSize);
            
            dotImages.Add(dotImage);
        }
        
        UpdateDots();
    }
    
    public void SetTotalSteps(int steps)
    {
        if (steps != totalDots)
        {
            totalDots = steps;
            InitializeDots();
        }
    }
    
    public void SetCurrentStep(int step)
    {
        currentStep = Mathf.Clamp(step, 0, totalDots - 1);
        UpdateDots();
    }
    
    public void NextStep()
    {
        if (currentStep < totalDots - 1)
        {
            currentStep++;
            UpdateDots();
        }
    }
    
    public void PreviousStep()
    {
        if (currentStep > 0)
        {
            currentStep--;
            UpdateDots();
        }
    }
    
    public int GetCurrentStep()
    {
        return currentStep;
    }
    
    public int GetTotalSteps()
    {
        return totalDots;
    }
    
    public bool IsLastStep()
    {
        return currentStep >= totalDots - 1;
    }
    
    private void UpdateDots()
    {
        for (int i = 0; i < dotImages.Count; i++)
        {
            if (i < currentStep)
            {
                // 완료된 단계
                dotImages[i].color = completedColor;
                dotImages[i].rectTransform.sizeDelta = new Vector2(completedDotSize, completedDotSize);
            }
            else if (i == currentStep)
            {
                // 현재 단계
                dotImages[i].color = currentColor;
                dotImages[i].rectTransform.sizeDelta = new Vector2(currentDotSize, currentDotSize);
            }
            else
            {
                // 미완료 단계
                dotImages[i].color = upcomingColor;
                dotImages[i].rectTransform.sizeDelta = new Vector2(upcomingDotSize, upcomingDotSize);
            }
        }
    }
    
    private Sprite CreateCircleSprite()
    {
        // 간단한 원형 스프라이트 생성 (Unity 기본 UI Sprite 사용)
        return Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
    }
}
