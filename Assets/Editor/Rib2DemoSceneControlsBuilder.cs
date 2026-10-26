using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Creates UI only in the explicitly supplied, isolated demonstration scene.</summary>
public static class Rib2DemoSceneControlsBuilder
{
    private const string DemoScenePrefix = "Assets/Demos/Rib2VideoTextScene/";
    private const string FontPath = "Assets/_NJS/Noto_Sans_KR/NotoSansKR-Regular SDF.asset";
    private static readonly Color PanelColor = new Color(.045f, .065f, .095f, .95f);
    private static readonly Color TextColor = new Color(.94f, .96f, .99f);
    private static readonly Color MutedColor = new Color(.72f, .78f, .86f);
    private static readonly Color AccentColor = new Color(.16f, .78f, .94f);

    public static GameObject Create(Scene scene, GameObject controllerRoot,
        Rib2VideoTextScenePlayer player, Rib2DemoPatientArm arm)
    {
        if (!scene.IsValid() || !scene.isLoaded ||
            (!string.IsNullOrEmpty(scene.path) && !scene.path.StartsWith(DemoScenePrefix, StringComparison.Ordinal)))
            throw new InvalidOperationException("UI creation is restricted to a fresh unsaved scene or an isolated Rib2VideoTextScene demonstration scene.");
        if (controllerRoot == null || player == null || arm == null || controllerRoot.scene != scene ||
            player.gameObject.scene != scene || arm.gameObject.scene != scene)
            throw new InvalidOperationException("All demonstration UI references must belong to the supplied demonstration scene.");
        if (controllerRoot.GetComponent<Rib2DemoSceneControls>() != null)
            throw new InvalidOperationException("The new controller already has Rib2DemoSceneControls; refusing to create duplicate UI.");
        Camera camera = controllerRoot.GetComponentInChildren<Camera>(true);
        if (camera == null)
            throw new InvalidOperationException("The demonstration controller must contain its own camera for screen-space UI rendering.");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/NotoSansKR-Bold.asset");
        if (font == null) throw new InvalidOperationException("Existing Korean TMP font is missing.");
        EnsureEventSystem(scene);

        var canvasObject = new GameObject("Rib2DemoCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = .25f;
        canvas.sortingOrder = 40;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        RectTransform header = Panel("Header", canvasObject.transform, true, 132f);
        RectTransform title = NewRect("Title", header);
        Place(title, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -10f), new Vector2(1040f, 60f));
        MakeText(title, font, "제2늑골 · 영상 기반 시연", 30f, TextAlignmentOptions.MidlineLeft, TextColor);
        RectTransform subtitle = NewRect("Subtitle", header);
        Place(subtitle, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -73f), new Vector2(960f, 40f));
        MakeText(subtitle, font, "영상·텍스트로 재구성한 초안", 20f, TextAlignmentOptions.MidlineLeft, MutedColor);
        RectTransform legend = NewRect("Legend", header);
        Place(legend, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -23f), new Vector2(650f, 34f));
        MakeText(legend, font, "<color=#28C7EF>청록 · 팔 이동</color>     <color=#F5A04B>주황 · 흉곽 지지</color>",
            22f, TextAlignmentOptions.MidlineRight, TextColor);
        RectTransform notice = NewRect("Limitations", header);
        Place(notice, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -70f), new Vector2(730f, 30f));
        MakeText(notice, font, "실제 깊이·임상 정확도 미검증", 20f, TextAlignmentOptions.MidlineRight, MutedColor);

        RectTransform footer = Panel("Controls", canvasObject.transform, false, 174f);
        RectTransform row = NewRect("Buttons", footer);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = Vector2.one;
        row.pivot = new Vector2(.5f, 1f); row.offsetMin = new Vector2(28f, -78f); row.offsetMax = new Vector2(-28f, -20f);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        UnityEngine.UI.Button start = Button("StartPose", row, font, "시작 파지", 196f, out _);
        UnityEngine.UI.Button cycle = Button("PlayCycle", row, font, "팔 한 주기", 214f, out _);
        UnityEngine.UI.Button play = Button("PlayPause", row, font, "재생/정지", 214f, out _);
        UnityEngine.UI.Button half = Button("HalfSpeed", row, font, "0.5배", 214f, out TMP_Text halfLabel);
        RectTransform spacer = NewRect("Spacer", row);
        var flexible = spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        flexible.flexibleWidth = 1f;
        RectTransform readoutRect = NewRect("TimeReadout", row);
        var readoutLayout = readoutRect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        readoutLayout.minWidth = 260f; readoutLayout.preferredWidth = 260f;
        TMP_Text readout = MakeText(readoutRect, font, "0.0 / 0.0초", 25f, TextAlignmentOptions.MidlineRight, TextColor);
        UnityEngine.UI.Slider slider = MakeSlider(footer);
        slider.minValue = 0f; slider.maxValue = player.Duration; slider.SetValueWithoutNotify(player.CurrentTime);

        var controls = controllerRoot.AddComponent<Rib2DemoSceneControls>();
        var properties = new SerializedObject(controls);
        properties.FindProperty("player").objectReferenceValue = player;
        properties.FindProperty("arm").objectReferenceValue = arm;
        properties.FindProperty("timelineSlider").objectReferenceValue = slider;
        properties.FindProperty("readout").objectReferenceValue = readout;
        properties.FindProperty("halfSpeedLabel").objectReferenceValue = halfLabel;
        properties.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(start.onClick, controls.StartPose);
        UnityEventTools.AddPersistentListener(cycle.onClick, controls.PlayCycle);
        UnityEventTools.AddPersistentListener(play.onClick, controls.TogglePlay);
        UnityEventTools.AddPersistentListener(half.onClick, controls.ToggleHalf);
        UnityEventTools.AddPersistentListener(slider.onValueChanged, controls.Seek);
        foreach (Transform element in canvasObject.GetComponentsInChildren<Transform>(true))
            element.gameObject.layer = controllerRoot.layer;
        controls.RefreshNow();
        Canvas.ForceUpdateCanvases();
        Debug.Log("[Rib2SceneDemo] Isolated scene UI created: Screen-space camera Canvas, Korean TMP font, persistent callbacks and one EventSystem.");
        return canvasObject;
    }

    private static void EnsureEventSystem(Scene scene)
    {
        EventSystem found = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (EventSystem system in root.GetComponentsInChildren<EventSystem>(true))
        {
            if (found != null) throw new InvalidOperationException("The demonstration scene has more than one EventSystem.");
            found = system;
        }
        if (found != null)
        {
            if (found.GetComponent<StandaloneInputModule>() == null)
                throw new InvalidOperationException("The existing demonstration EventSystem is missing StandaloneInputModule.");
            return;
        }
        var eventObject = new GameObject("Rib2DemoEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(eventObject, scene);
    }

    private static RectTransform Panel(string name, Transform parent, bool top, float height)
    {
        RectTransform rect = NewRect(name, parent);
        float edge = top ? 1f : 0f;
        rect.anchorMin = new Vector2(0f, edge); rect.anchorMax = new Vector2(1f, edge);
        rect.pivot = new Vector2(.5f, edge); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(0f, height);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = PanelColor; image.raycastTarget = false;
        return rect;
    }

    private static UnityEngine.UI.Button Button(string name, Transform parent, TMP_FontAsset font, string label,
        float width, out TMP_Text text)
    {
        RectTransform rect = NewRect(name, parent);
        var size = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        size.minWidth = width; size.preferredWidth = width; size.minHeight = 58f; size.preferredHeight = 58f;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.12f, .19f, .27f); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(.68f, .88f, 1f);
        colors.pressedColor = new Color(.48f, .75f, .92f); colors.selectedColor = new Color(.75f, .9f, 1f);
        button.colors = colors;
        RectTransform labelRect = NewRect("Label", rect); Stretch(labelRect, 8f, 4f);
        text = MakeText(labelRect, font, label, 25f, TextAlignmentOptions.Center, TextColor);
        return button;
    }

    private static UnityEngine.UI.Slider MakeSlider(Transform parent)
    {
        RectTransform rect = NewRect("Timeline", parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(.5f, 0f); rect.offsetMin = new Vector2(28f, 28f); rect.offsetMax = new Vector2(-28f, 64f);
        var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.wholeNumbers = false;
        RectTransform background = NewRect("Background", rect);
        background.anchorMin = new Vector2(0f, .4f); background.anchorMax = new Vector2(1f, .6f);
        background.offsetMin = Vector2.zero; background.offsetMax = Vector2.zero;
        var backgroundImage = background.gameObject.AddComponent<UnityEngine.UI.Image>();
        backgroundImage.color = new Color(.22f, .29f, .38f); backgroundImage.raycastTarget = true;
        RectTransform fillArea = NewRect("FillArea", rect); Stretch(fillArea, 12f, 14f);
        RectTransform fill = NewRect("Fill", fillArea); Stretch(fill, 0f, 0f);
        var fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>(); fillImage.color = AccentColor; fillImage.raycastTarget = false;
        RectTransform handleArea = NewRect("HandleArea", rect); Stretch(handleArea, 12f, 0f);
        RectTransform handle = NewRect("Handle", handleArea);
        handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f);
        handle.anchoredPosition = Vector2.zero; handle.sizeDelta = new Vector2(22f, 0f);
        var handleImage = handle.gameObject.AddComponent<UnityEngine.UI.Image>(); handleImage.color = TextColor; handleImage.raycastTarget = true;
        slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        return slider;
    }

    private static TMP_Text MakeText(RectTransform rect, TMP_FontAsset font, string content, float size,
        TextAlignmentOptions alignment, Color color)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size;
        text.color = color; text.alignment = alignment; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; text.text = content;
        return text;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect, float horizontalInset, float verticalInset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(horizontalInset, verticalInset); rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
    }
}
