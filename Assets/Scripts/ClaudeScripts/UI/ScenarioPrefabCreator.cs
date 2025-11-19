using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ScenarioPrefabCreator : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("Tools/Create Scenario UI Prefabs")]
    public static void CreatePrefabs()
    {
        // Assets/Prefabs 폴더가 없으면 생성
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        
        // 1. DotTimeline 프리팹 생성
        CreateDotTimelinePrefab();
        
        // 2. ScenarioUI 전체 프리팹 생성
        CreateScenarioUIPrefab();
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log("시나리오 UI 프리팹들이 성공적으로 생성되었습니다!");
    }
    
    private static void CreateDotTimelinePrefab()
    {
        // DotTimeline GameObject 생성
        GameObject dotTimelineObj = new GameObject("DotTimeline");
        
        // HorizontalLayoutGroup 추가
        HorizontalLayoutGroup layoutGroup = dotTimelineObj.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.spacing = 15f;
        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;
        
        // RectTransform 설정
        RectTransform rectTransform = dotTimelineObj.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(200f, 30f);
        
        // DotsContainer 생성
        GameObject dotsContainer = new GameObject("DotsContainer");
        dotsContainer.transform.SetParent(dotTimelineObj.transform, false);
        
        HorizontalLayoutGroup dotsLayout = dotsContainer.AddComponent<HorizontalLayoutGroup>();
        dotsLayout.spacing = 15f;
        dotsLayout.childAlignment = TextAnchor.MiddleCenter;
        dotsLayout.childControlWidth = false;
        dotsLayout.childControlHeight = false;
        
        RectTransform dotsRect = dotsContainer.GetComponent<RectTransform>();
        dotsRect.anchorMin = new Vector2(0, 0);
        dotsRect.anchorMax = new Vector2(1, 1);
        dotsRect.offsetMin = Vector2.zero;
        dotsRect.offsetMax = Vector2.zero;
        
        // DotTimeline 컴포넌트 추가 및 설정
        DotTimeline dotTimeline = dotTimelineObj.AddComponent<DotTimeline>();
        
        // Reflection을 사용하여 private 필드 설정
        var dotsContainerField = typeof(DotTimeline).GetField("dotsContainer", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (dotsContainerField != null)
        {
            dotsContainerField.SetValue(dotTimeline, dotsContainer.transform);
        }
        
        // 프리팹 저장
        string prefabPath = "Assets/Prefabs/DotTimeline.prefab";
        PrefabUtility.SaveAsPrefabAsset(dotTimelineObj, prefabPath);
        
        // 씬에서 제거
        DestroyImmediate(dotTimelineObj);
        
        Debug.Log($"DotTimeline 프리팹이 생성되었습니다: {prefabPath}");
    }
    
    private static void CreateScenarioUIPrefab()
    {
        // Canvas 생성 (World Space for VR)
        GameObject canvasObj = new GameObject("ScenarioUI_Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        CanvasScaler canvasScaler = canvasObj.AddComponent<CanvasScaler>();
        canvasScaler.dynamicPixelsPerUnit = 10f;
        
        GraphicRaycaster raycaster = canvasObj.AddComponent<GraphicRaycaster>();
        
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800f, 400f);
        canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f); // VR 스케일
        
        // Background Panel
        GameObject backgroundPanel = new GameObject("BackgroundPanel");
        backgroundPanel.transform.SetParent(canvasObj.transform, false);
        
        Image bgImage = backgroundPanel.AddComponent<Image>();
        bgImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        
        RectTransform bgRect = backgroundPanel.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        // Title Text (좌측 상단)
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(canvasObj.transform, false);
        
        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "상부승모근";
        titleText.fontSize = 36;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Left;
        titleText.fontStyle = FontStyles.Bold;
        
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(0, 1);
        titleRect.pivot = new Vector2(0, 1);
        titleRect.anchoredPosition = new Vector2(30f, -30f);
        titleRect.sizeDelta = new Vector2(400f, 50f);
        
        // Description Text (중앙)
        GameObject descObj = new GameObject("DescriptionText");
        descObj.transform.SetParent(canvasObj.transform, false);
        
        TextMeshProUGUI descText = descObj.AddComponent<TextMeshProUGUI>();
        descText.text = "시나리오 설명이 여기에 표시됩니다.";
        descText.fontSize = 24;
        descText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        descText.alignment = TextAlignmentOptions.TopLeft;
        descText.enableWordWrapping = true;
        
        RectTransform descRect = descObj.GetComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0.5f, 0.5f);
        descRect.anchorMax = new Vector2(0.5f, 0.5f);
        descRect.pivot = new Vector2(0.5f, 0.5f);
        descRect.anchoredPosition = new Vector2(0f, 20f);
        descRect.sizeDelta = new Vector2(700f, 200f);
        
        // Next Button (우측 하단)
        GameObject buttonObj = new GameObject("NextButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        
        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        
        Button button = buttonObj.AddComponent<Button>();
        
        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1, 0);
        buttonRect.anchorMax = new Vector2(1, 0);
        buttonRect.pivot = new Vector2(1, 0);
        buttonRect.anchoredPosition = new Vector2(-30f, 30f);
        buttonRect.sizeDelta = new Vector2(120f, 60f);
        
        // Button Icon (Play Icon)
        GameObject iconObj = new GameObject("PlayIcon");
        iconObj.transform.SetParent(buttonObj.transform, false);
        
        TextMeshProUGUI iconText = iconObj.AddComponent<TextMeshProUGUI>();
        iconText.text = "▶";
        iconText.fontSize = 32;
        iconText.color = Color.white;
        iconText.alignment = TextAlignmentOptions.Center;
        
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        
        // Button Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(buttonObj.transform, false);
        
        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = "다음";
        labelText.fontSize = 18;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.Bottom;
        
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 0);
        labelRect.pivot = new Vector2(0.5f, 0);
        labelRect.anchoredPosition = new Vector2(0f, 5f);
        labelRect.sizeDelta = new Vector2(0f, 20f);
        
        // DotTimeline 추가 (중앙 하단)
        string dotTimelinePrefabPath = "Assets/Prefabs/DotTimeline.prefab";
        GameObject dotTimelinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dotTimelinePrefabPath);
        
        GameObject dotTimelineInstance;
        if (dotTimelinePrefab != null)
        {
            dotTimelineInstance = PrefabUtility.InstantiatePrefab(dotTimelinePrefab) as GameObject;
        }
        else
        {
            // DotTimeline 프리팹이 없으면 기본 GameObject 생성
            dotTimelineInstance = new GameObject("DotTimeline");
            dotTimelineInstance.AddComponent<DotTimeline>();
        }
        
        dotTimelineInstance.transform.SetParent(canvasObj.transform, false);
        
        RectTransform dotRect = dotTimelineInstance.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0.5f, 0);
        dotRect.anchorMax = new Vector2(0.5f, 0);
        dotRect.pivot = new Vector2(0.5f, 0);
        dotRect.anchoredPosition = new Vector2(0f, 80f);
        dotRect.sizeDelta = new Vector2(250f, 30f);
        
        // ScenarioManager 컴포넌트 추가
        Scenario manager = canvasObj.AddComponent<Scenario>();
        
        // Reflection을 사용하여 private 필드 설정
        SetPrivateField(manager, "titleText", titleText);
        SetPrivateField(manager, "descriptionText", descText);
        SetPrivateField(manager, "nextButton", button);
        SetPrivateField(manager, "nextButtonText", labelText);
        SetPrivateField(manager, "dotTimeline", dotTimelineInstance.GetComponent<DotTimeline>());
        SetPrivateField(manager, "scenarioName", "상부승모근");
        SetPrivateField(manager, "nextStepText", "다음");
        SetPrivateField(manager, "completeText", "완료");
        
        // 프리팹 저장
        string prefabPath = "Assets/Prefabs/ScenarioUI.prefab";
        PrefabUtility.SaveAsPrefabAsset(canvasObj, prefabPath);
        
        // 씬에서 제거
        DestroyImmediate(canvasObj);
        
        Debug.Log($"ScenarioUI 프리팹이 생성되었습니다: {prefabPath}");
    }
    
    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);
        
        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            Debug.LogWarning($"필드를 찾을 수 없습니다: {fieldName}");
        }
    }
#endif
}
