using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 전문가 영상 창 영상 칸 아래쪽에 자막을 얹는다 (2026-11-04 사용자 «전문가 영상은 기본 음소거 상태에서 자막으로 내용 대체» · 자막 A안).
///
/// ★자막 내용 = <see cref="GuideVideoController.CurrentSubtitle"/>(Resources/Videos/{이름}_자막.txt · SRT). 여기서는 그리기만 한다.
/// ★영상 칸은 <b>전문가 영상 창 안에서만</b> 찾는다 — 11-04 오전에 출력 텍스처로 씬 전체를 뒤져 45×45 엉뚱한 칸에 붙었다(로그 실측).
/// ★글이 바뀔 때만 손댄다(VR 프레임 예산). 자막이 비면 띠도 숨긴다.
/// ★런타임에 스스로 붙는다(TrainingScene). 씬은 건드리지 않는다(규칙 1).
/// </summary>
public class ExpertVideoSubtitle : MonoBehaviour
{
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
        if (FindFirstObjectByType<ExpertVideoSubtitle>() != null) return;
        new GameObject("전문가 영상 자막 (런타임)").AddComponent<ExpertVideoSubtitle>();
    }

    private GuideVideoController gvc;
    private GameObject band;
    private TextMeshProUGUI text;
    private string last = null;
    private bool built;
    private float nextFind;

    private void Update()
    {
        if (!built)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            TryBuild();
            return;
        }
        string s = gvc.CurrentSubtitle;
        if (ReferenceEquals(s, last)) return;   // 같은 줄이면 같은 문자열 참조다(할당 없음)
        last = s;
        bool on = !string.IsNullOrEmpty(s);
        if (band.activeSelf != on) band.SetActive(on);
        if (on) text.text = s;
    }

    private void TryBuild()
    {
        if (gvc == null) gvc = FindFirstObjectByType<GuideVideoController>(FindObjectsInactive.Include);
        var panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var page = panel != null ? panel.ExpertVideoPageObject : null;
        if (gvc == null || page == null) return;

        // 창 안의 RawImage 중 영상 출력 텍스처를 그리는 것 · 없으면 가장 큰 것
        var rt = gvc.Player != null ? gvc.Player.targetTexture : null;
        RawImage view = null; float best = -1f;
        foreach (var ri in page.GetComponentsInChildren<RawImage>(true))
        {
            float area = ri.rectTransform.rect.width * ri.rectTransform.rect.height;
            if (rt != null && ri.texture == rt) { view = ri; break; }
            if (area > best) { best = area; view = ri; }
        }
        if (view == null) { Debug.LogWarning("[영상자막] ★전문가 영상 창 안에 영상 칸(RawImage)이 없다"); built = true; enabled = false; return; }

        band = new GameObject("자막 띠 (런타임)", typeof(RectTransform));
        band.layer = view.gameObject.layer;
        var br = (RectTransform)band.transform;
        br.SetParent(view.rectTransform, false);
        br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(1f, 0.22f); br.offsetMin = br.offsetMax = Vector2.zero;
        band.AddComponent<LayoutElement>().ignoreLayout = true;
        var bg = band.AddComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.6f); bg.raycastTarget = false;

        var tgo = new GameObject("글", typeof(RectTransform));
        tgo.layer = band.layer;
        var tr = (RectTransform)tgo.transform;
        tr.SetParent(br, false);
        tr.anchorMin = new Vector2(0.03f, 0.08f); tr.anchorMax = new Vector2(0.97f, 0.92f); tr.offsetMin = tr.offsetMax = Vector2.zero;
        text = tgo.AddComponent<TextMeshProUGUI>();
        var font = KoreanFontResolver.Resolve(); if (font != null) text.font = font;
        text.enableAutoSizing = true; text.fontSizeMin = 6; text.fontSizeMax = 48;
        text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;

        band.SetActive(false);
        built = true;
        Debug.Log($"[영상자막] 붙음 · 영상 칸 '{view.name}' ({view.rectTransform.rect.width:F0}×{view.rectTransform.rect.height:F0}) · 자막 {(gvc.HasSubtitles ? "있음" : "아직 없음(영상 로드 때 읽음)")}");
    }
}
