using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 상단 [시작 · 타이머] 바로 아래의 [다음] 판 (2026-11-04 사용자 «다음으로 넘기는 표시 시작 타이머 밑으로 나오게 해»).
///
/// ★종전(11-03 ~ 11-04 오후)엔 옆 안내판(<see cref="PatientSideGuideReadout"/>)에 파란 정사각 · 흰 알약 [다음]을 띄웠다 — 뺐다.
/// ★뜨는 때 = 정면 진행 창의 시작/계속하기 토글(<see cref="ScenarioGuideUIController.NextToggle"/>)이 <b>보이고 눌릴 수 있을 때</b>. 따로 세지 않는다 —
///   «20초 뒤 [계속하기]»는 ScenarioConditionManager.progressTimeout(에디터 Play는 editorTestProgressTimeout)이 정한다.
/// ★누르면 그 토글을 그대로 켠다(<c>isOn = true</c> — 정면 버튼을 누른 것과 같은 경로 · 규칙 9).
/// ★글자는 정면 토글의 글자(시작 · 계속하기 · 종료)를 따른다. 없으면 «다음».
/// ★판(UISet · 자체 손 · 레이 누름 영역)은 TopBar1104Tool이 씬에 만든다(«상단 바 A (시안)/다음 판»). 버튼은 여기서 런타임에 채운다.
/// </summary>
public class TopBarNextPlate : MonoBehaviour
{
    public const string PlateName = "다음 판";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != "TrainingScene") return;
        if (FindFirstObjectByType<TopBarNextPlate>() != null) return;
        new GameObject("상단 다음 판 (런타임)").AddComponent<TopBarNextPlate>();
    }

    private GameObject plate;
    private ScenarioGuideUIController guideUI;
    private InfoPanelController panel;
    private TextMeshProUGUI label;
    private TMP_Text frontLabel;
    private bool built, shown;
    private float nextFind;
    private string lastText;

    private void Update()
    {
        if (!built)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            TryBuild();
            return;
        }
        if (guideUI == null) { guideUI = FindFirstObjectByType<ScenarioGuideUIController>(); if (guideUI == null) return; }

        var obj = guideUI.NextToggleObject;
        var tg = guideUI.NextToggle;
        bool waiting = panel != null && panel.IsAwaitingStart;   // 개요 중엔 상단 [시작]이 그 일을 한다
        bool on = !waiting && obj != null && obj.activeInHierarchy && tg != null && tg.interactable;
        if (on != shown)
        {
            shown = on;
            plate.SetActive(on);
            Debug.Log($"[상단다음] {(on ? "보임" : "숨김")}");
        }
        if (!on) return;
        // 정면 토글 글자를 따른다(바뀔 때만)
        if (frontLabel == null && tg != null) frontLabel = tg.GetComponentInChildren<TMP_Text>(true);
        string t = frontLabel != null && !string.IsNullOrEmpty(frontLabel.text) ? frontLabel.text : "다음";
        if (t != lastText) { lastText = t; label.text = t; }
    }

    private void TryBuild()
    {
        if (plate == null)
            foreach (var placer in FindObjectsByType<TopBarPlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var tr = placer.transform.Find(PlateName);
                if (tr != null) { plate = tr.gameObject; break; }
            }
        if (plate == null) { Debug.LogWarning($"[상단다음] ★상단 바 아래 '{PlateName}'이 씬에 없다 — 메뉴 «11-04 상단 메뉴 …»를 돌리고 저장"); built = true; enabled = false; return; }
        RectTransform back = null;
        foreach (var r in plate.GetComponentsInChildren<RectTransform>(true)) if (r.name == "UIBackplate") { back = r; break; }
        if (back == null) { Debug.LogWarning($"[상단다음] ★'{PlateName}'에 UIBackplate가 없다"); built = true; enabled = false; return; }
        panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);

        // 판 전체가 버튼 — 흰 알약(팝업 주 행동과 같은 색) · 어두운 굵은 글자
        var go = new GameObject("다음 버튼 (런타임)", typeof(RectTransform));
        go.layer = back.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(back, false);
        rt.anchorMin = new Vector2(0.04f, 0.12f); rt.anchorMax = new Vector2(0.96f, 0.88f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;   // 판 전체를 누름 칸으로
        var hit = go.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0.001f);
        var b = go.AddComponent<Button>();
        // 판(이미 둥근 알약 · UISet 둥근 재질)을 흰 알약으로 칠하고, 가리킴 · 누름 색 반응도 판에 건다
        var plateImg = back.GetComponent<Image>();
        if (plateImg != null) { plateImg.color = Color.white; b.targetGraphic = plateImg; } else b.targetGraphic = hit;
        var cb = b.colors;
        cb.normalColor = cb.selectedColor = new Color(0.965f, 0.965f, 0.965f, 1f);
        cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
        b.colors = cb;
        b.onClick.AddListener(Press);

        var tgo = new GameObject("글자", typeof(RectTransform));
        tgo.layer = go.layer;
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tgo.AddComponent<TextMeshProUGUI>();
        var font = KoreanFontResolver.Resolve(); if (font != null) label.font = font;
        label.text = "다음"; label.fontSize = 30f; label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center; label.color = new Color(0.153f, 0.153f, 0.153f, 1f); label.raycastTarget = false;

        plate.SetActive(false);
        built = true;
        Debug.Log($"[상단다음] 붙음 · '{PlateName}' ({back.rect.width:F0}×{back.rect.height:F0})");
    }

    private void Press()
    {
        var tg = guideUI != null ? guideUI.NextToggle : null;
        if (tg == null || !tg.interactable) return;
        Debug.Log($"[상단다음] 누름 → 정면 토글 '{lastText}' 켬");
        tg.isOn = true;   // 정면 버튼을 누른 것과 같은 경로(OnStartToggleChanged)
    }
}
