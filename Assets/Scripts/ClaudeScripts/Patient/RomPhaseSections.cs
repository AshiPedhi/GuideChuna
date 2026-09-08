using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 경추ROM에서 진행 패널의 <b>Phase 칸을 4개로 바꾼다</b> — 굴곡 · 신전 · 측굴 · 회전.
///
/// ★★<b>2026-09-08 사용자 지시.</b> "중전후 이미지는 굴곡 신전 측굴 회전 이렇게 4개로
///   나눠서 나오게 해줘. 디자인은 기존 이미지 유지하고."
///
/// ★<b>왜 여기 있나</b>: 칸 셋(<c>Section</c>·<c>Section (1)</c>·<c>Section (2)</c>)은
///   <see cref="ScenarioGuideUIController"/>가 들고 있고 그 컴포넌트는 <b>13개 술기가 같이 쓴다.</b>
///   그래서 저쪽에는 <b>창</b>(읽기 접근자 + <c>ExternalPhaseControl</c>)만 내고,
///   칸을 늘리고 라벨을 바꾸는 일은 경추ROM인 <b>이쪽</b>에서 한다.
///   ★<b>넣은 쪽이 되돌린다</b> — 술기를 벗어나면 복제를 지우고 라벨을 원래대로 돌린다.
///     안 되돌리면 다음 술기에 '굴곡·신전·측굴·회전'이 그대로 남는다(07-27 xray 사고가 이 형태였다).
///
/// ★<b>디자인은 손대지 않는다.</b> 4번째 칸은 기존 칸을 <c>Instantiate</c>한 것이라
///   스프라이트·색·폰트·크기가 원본과 같다. 부모 <c>Phase</c>가
///   <c>HorizontalLayoutGroup(ChildForceExpandWidth=1)</c>이라 폭은 340/4로 알아서 나뉜다(씬 실측).
///
/// ★<b>실습·평가 둘 다</b> 띄운다(2026-09-08 사용자 확정). 그래서 평가모드에서 칸 하나만
///   남기는 공용 규칙(<c>ShowOnlyActivePhaseImage</c>)도 <c>ExternalPhaseControl</c>로 같이 막는다.
/// </summary>
public class RomPhaseSections : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomScenarioBridge bridge;
    [SerializeField] private ScenarioGuideUIController guideUI;

    [Header("=== 표시 ===")]
    [Tooltip("끄면 종전 그대로 전부·중부·후부 3칸이 나온다.")]
    [SerializeField] private bool enableSections = true;

    [Tooltip("왼쪽부터 채울 칸 이름. 개수를 늘리면 그만큼 칸을 복제한다.\n" +
             "★칸 이름이 곧 <b>stepName 매칭 조각</b>이다 — '측굴'은 좌측굴·우측굴에,\n" +
             "  '회전'은 좌회전·우회전에 걸린다(2026-09-08 CSV 실측).")]
    [SerializeField] private string[] sectionLabels = { "굴곡", "신전", "측굴", "회전" };

    [Tooltip("앞 형제(가이드 제목 칸)를 이 폭 아래로는 줄이지 않는다(px).\n" +
             "★칸이 늘어난 만큼 앞칸을 줄여 줄을 왼쪽으로 넓히는데, 끝까지 줄이면 제목이 깨진다.")]
    [SerializeField] private float minSiblingWidth = 200f;

    [SerializeField] private bool showDebugLogs = true;

    // ── 되돌릴 것들 ──────────────────────────────────────────────────────
    private bool docked;
    private readonly List<Image> slots = new List<Image>();        // 화면 왼쪽부터의 칸
    private readonly List<Image> baseSlots = new List<Image>();    // 원래 있던 칸(파괴하면 안 된다)
    private readonly List<string> homeLabels = new List<string>(); // 원래 글자
    private readonly List<bool> homeActive = new List<bool>();     // 원래 켜짐 여부
    private readonly List<GameObject> clones = new List<GameObject>();

    // ── 줄 넓히기 되돌릴 것 (2026-09-08) ────────────────────────────────
    private bool rowFitted;
    private RectTransform rowRect, sibRect;
    private Vector2 rowHome, sibHome;

    private ScenarioManager scenarioManager;
    private int lastActiveIndex = -2;   // -2 = 아직 한 번도 안 칠했다
    private float nextSilentLog;
    private string lastSilentReason;

    private void Awake()
    {
        if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);

        // ⓐ 붙었다 — '안 붙은 것'과 '붙었는데 안 도는 것'은 화면에서 똑같아 보인다.
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ROM단계칸] 붙었다 — 브리지 {(bridge != null ? "있음" : "★없음")} · " +
                            $"가이드UI {(guideUI != null ? "있음" : "★없음")}</color>");
    }

    private void OnDisable() => Undock();
    private void OnDestroy() => Undock();

    private void LateUpdate()
    {
        if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);

        bool want = enableSections && bridge != null && bridge.RomScenarioActive;

        if (want && !docked) Dock();
        else if (!want && docked) Undock();

        if (docked) Refresh();
        else if (enableSections && bridge == null) ReportSilent("브리지를 못 찾았다");
        else if (enableSections && bridge != null && !bridge.RomScenarioActive) ReportSilent("경추ROM 시나리오가 아니다");
    }

    // ================= 넣기 =================

    private void Dock()
    {
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (guideUI == null) { ReportSilent("가이드UI가 씬에 없다"); return; }

        // ★칸을 <b>화면 왼쪽부터</b> 모은다. 필드 순서(front·middle·back)가 아니라
        //   계층의 형제 순서가 곧 보이는 순서다 — 씬은 Section (1)·Section·Section (2) 순이다.
        baseSlots.Clear();
        CollectBase(guideUI.FrontPhaseImage);
        CollectBase(guideUI.MiddlePhaseImage);
        CollectBase(guideUI.BackPhaseImage);
        if (baseSlots.Count == 0) { ReportSilent("Phase 칸이 하나도 안 물려 있다"); return; }
        baseSlots.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        // 원래 모습을 적어 둔다 — 되돌릴 때 쓴다.
        homeLabels.Clear();
        homeActive.Clear();
        for (int i = 0; i < baseSlots.Count; i++)
        {
            TextMeshProUGUI label = LabelOf(baseSlots[i]);
            homeLabels.Add(label != null ? label.text : null);
            homeActive.Add(baseSlots[i].gameObject.activeSelf);
        }

        // 필요한 만큼 칸을 채운다. 모자라면 마지막 칸을 복제한다(디자인이 그대로 따라온다).
        slots.Clear();
        slots.AddRange(baseSlots);
        Transform parent = baseSlots[0].transform.parent;
        while (slots.Count < sectionLabels.Length)
        {
            GameObject copy = Instantiate(baseSlots[baseSlots.Count - 1].gameObject, parent);
            copy.name = $"Section (ROM {slots.Count})";
            copy.transform.SetSiblingIndex(slots[slots.Count - 1].transform.GetSiblingIndex() + 1);
            clones.Add(copy);
            slots.Add(copy.GetComponent<Image>());
        }

        // 라벨을 갈아 끼우고 전부 켠다.
        for (int i = 0; i < slots.Count; i++)
        {
            bool used = i < sectionLabels.Length;
            slots[i].gameObject.SetActive(used);
            if (!used) continue;

            TextMeshProUGUI label = LabelOf(slots[i]);
            if (label != null) label.text = sectionLabels[i];
            else ChunaLogger.LogWarning($"[ROM단계칸] {slots[i].name}에 글자(TMP)가 없어 '{sectionLabels[i]}'를 못 넣었다.");
        }

        // ★★칸을 늘렸으면 <b>줄을 왼쪽으로 넓혀야 한다</b>(2026-09-08 Play에서 넷째 칸이 넘쳤다).
        rowFitted = false;
        FitSectionRow();

        // ★공용 규칙이 덮어쓰지 못하게 막는다. 이걸 안 하면 phase가 바뀔 때마다 색이 지워진다.
        guideUI.ExternalPhaseControl = true;

        docked = true;
        lastActiveIndex = -2;
        lastSilentReason = null;
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ROM단계칸] 칸을 {slots.Count}개로 맞췄다 " +
                            $"(원래 {baseSlots.Count} + 복제 {clones.Count}) — {string.Join(" · ", sectionLabels)}</color>");
    }

    private void CollectBase(Image img)
    {
        if (img != null && !baseSlots.Contains(img)) baseSlots.Add(img);
    }

    private TextMeshProUGUI LabelOf(Image img)
    {
        return img != null ? img.GetComponentInChildren<TextMeshProUGUI>(true) : null;
    }

    /// <summary>
    /// 칸이 늘어난 만큼 <b>줄을 왼쪽으로 넓힌다.</b>
    ///
    /// ★★<b>2026-09-08 Play에서 넷째 칸이 오른쪽으로 넘쳤다.</b> 내가 씬을 잘못 읽었다 —
    ///   부모 <c>Phase</c>는 <c>ChildForceExpandWidth=1</c>이지만 <b><c>ChildControlWidth=0</c></b>이다.
    ///   그러면 그룹은 <b>자식 폭을 정해 주지 않고</b> 각자의 <c>sizeDelta</c>(100)를 그대로 쓴다.
    ///   ForceExpand는 <b>남는 공간만</b> 나눠 준다. 그래서 100×4=400 > 340이 되어 넘쳤다.
    ///
    /// ★<b>칸 폭은 건드리지 않는다</b>(2026-09-08 사용자 지시: "나누는 간격은 지금 그대로가 좋은데
    ///   오른쪽으로 쌓이게 말고 왼쪽으로 알아서 밀리기"). 대신 두 가지를 한다:
    ///   ① <c>Phase</c>의 폭을 필요한 만큼 늘린다 — <b>pivot이 (1,1)</b>이라 <b>왼쪽으로</b> 자란다(씬 실측).
    ///   ② 그만큼 <b>앞 형제(가이드 제목 칸)를 줄인다</b> — 안 줄이면 줄 전체가 넓어져
    ///      결국 오른쪽으로 그대로 넘친다. 줄의 총폭은 그대로 두는 것이 핵심이다.
    /// ★<b>원래 폭은 적어 두고 되돌린다</b> — 다른 술기는 3칸 그대로여야 한다.
    /// ★레이아웃이 아직 안 잡혀 폭이 0으로 읽히는 프레임이 있다. 그때는 다음 프레임에 다시 잰다.
    /// </summary>
    private void FitSectionRow()
    {
        if (rowFitted || slots.Count == 0) return;
        if (!(slots[0].transform.parent is RectTransform row)) return;

        float have = row.rect.width;
        if (have <= 1f) return;   // 아직 레이아웃 전 — 다음 프레임에 다시 온다

        float spacing = 0f;
        var hg = row.GetComponent<HorizontalLayoutGroup>();
        if (hg != null) spacing = hg.spacing;

        int n = Mathf.Min(slots.Count, sectionLabels.Length);
        if (n <= 0) return;

        float need = spacing * (n - 1);
        for (int i = 0; i < n; i++)
            if (slots[i] != null && slots[i].transform is RectTransform rt) need += rt.rect.width;

        float delta = need - have;
        if (delta <= 0.5f) { rowFitted = true; return; }   // 이미 들어간다

        rowRect = row;
        rowHome = row.sizeDelta;
        row.sizeDelta = new Vector2(have + delta, row.sizeDelta.y);

        // ★앞 형제를 그만큼 줄여 줄의 총폭을 지킨다. 못 찾으면 넓히기만 하고 경고를 남긴다.
        RectTransform sib = PreviousSibling(row);
        if (sib != null)
        {
            sibRect = sib;
            sibHome = sib.sizeDelta;
            float shrunk = Mathf.Max(minSiblingWidth, sib.sizeDelta.x - delta);
            sib.sizeDelta = new Vector2(shrunk, sib.sizeDelta.y);
            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[ROM단계칸] 줄을 왼쪽으로 {delta:F0}px 넓혔다 — " +
                                $"칸줄 {have:F0}→{have + delta:F0} · 앞칸 '{sib.name}' {sibHome.x:F0}→{shrunk:F0}</color>");
        }
        else
        {
            ChunaLogger.LogWarning($"[ROM단계칸] 앞 형제를 못 찾아 줄만 {delta:F0}px 넓혔다 — " +
                                   "오른쪽으로 밀려 보일 수 있다.");
        }

        rowFitted = true;
    }

    /// <summary>같은 부모 안에서 <paramref name="t"/> 바로 앞의 형제 RectTransform.</summary>
    private RectTransform PreviousSibling(RectTransform t)
    {
        Transform parent = t != null ? t.parent : null;
        if (parent == null) return null;
        int idx = t.GetSiblingIndex();
        for (int i = idx - 1; i >= 0; i--)
        {
            if (parent.GetChild(i) is RectTransform r && r.gameObject.activeSelf) return r;
        }
        return null;
    }

    // ================= 칠하기 =================

    private void Refresh()
    {
        // ★첫 프레임에는 레이아웃이 아직 안 잡혀 폭이 0으로 읽힌다. 잡힐 때까지 다시 잰다.
        if (!rowFitted) FitSectionRow();

        int idx = ActiveIndexFromStep();

        // ★바뀔 때만 칠한다. 매 프레임 색을 대입하면 VR 프레임 예산에서 의미 없는 비용이다.
        if (idx == lastActiveIndex) return;
        lastActiveIndex = idx;

        for (int i = 0; i < slots.Count && i < sectionLabels.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i].color = (i == idx) ? guideUI.ActivePhaseColor : guideUI.InactivePhaseColor;
        }

        if (showDebugLogs)
        {
            string where = (idx >= 0 && idx < sectionLabels.Length) ? sectionLabels[idx] : "없음(전부 회색)";
            ChunaLogger.Log($"<color=cyan>[ROM단계칸] 단계 '{CurrentStepName()}' → 활성 칸 {where}</color>");
        }
    }

    /// <summary>
    /// 지금 stepName이 어느 칸인가. 못 찾으면 -1(전부 회색).
    /// ★<b>부분 문자열</b>로 본다 — 굴곡압박·좌측굴·우회전이 전부 걸려야 한다(CSV 실측).
    ///   파지·평가·준비·자세정렬은 <b>일부러</b> 어느 칸도 아니다. 아직 방향을 고르지 않은 자리다.
    /// </summary>
    private int ActiveIndexFromStep()
    {
        string step = CurrentStepName();
        if (string.IsNullOrEmpty(step)) return -1;

        for (int i = 0; i < sectionLabels.Length; i++)
        {
            if (!string.IsNullOrEmpty(sectionLabels[i]) && step.Contains(sectionLabels[i])) return i;
        }
        return -1;
    }

    private string CurrentStepName()
    {
        // ★매 프레임 FindFirstObjectByType을 부르지 않는다 — 씬 전체를 훑는 호출이라
        //   VR 프레임 예산에서는 그대로 드랍으로 나타난다(09-07에 실제로 겪었다).
        //   시나리오매니저는 한 판 내내 바뀌지 않으므로 한 번 찾아 들고 있는다.
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        StepData cur = scenarioManager != null ? scenarioManager.CurrentStep : null;
        return cur != null ? cur.stepName : null;
    }

    // ================= 되돌리기 =================

    private void Undock()
    {
        if (!docked) return;
        docked = false;

        for (int i = 0; i < clones.Count; i++)
            if (clones[i] != null) Destroy(clones[i]);
        clones.Clear();

        for (int i = 0; i < baseSlots.Count; i++)
        {
            if (baseSlots[i] == null) continue;

            if (i < homeLabels.Count && homeLabels[i] != null)
            {
                TextMeshProUGUI label = LabelOf(baseSlots[i]);
                if (label != null) label.text = homeLabels[i];
            }
            if (i < homeActive.Count) baseSlots[i].gameObject.SetActive(homeActive[i]);
        }

        // ★넓힌 줄을 원래대로 되돌린다. 안 되돌리면 다음 술기의 제목 칸이 좁은 채로 남는다.
        if (rowRect != null) rowRect.sizeDelta = rowHome;
        if (sibRect != null) sibRect.sizeDelta = sibHome;
        rowRect = sibRect = null;
        rowFitted = false;

        // ★풀어 준다. 안 풀면 다음 술기의 전부·중부·후부 표시가 통째로 죽는다.
        if (guideUI != null) guideUI.ExternalPhaseControl = false;

        slots.Clear();
        lastActiveIndex = -2;

        if (showDebugLogs)
            ChunaLogger.Log("[ROM단계칸] 칸을 원래대로 되돌렸다(복제 파괴 · 라벨 복원 · 공용 제어 해제).");
    }

    // ================= 진단 =================

    /// <summary>★막힐 때만 말한다. 통과하면 저절로 조용해진다.</summary>
    private void ReportSilent(string reason)
    {
        if (!showDebugLogs) return;
        if (reason == lastSilentReason && Time.unscaledTime < nextSilentLog) return;
        lastSilentReason = reason;
        nextSilentLog = Time.unscaledTime + 2f;
        ChunaLogger.LogWarning($"<color=orange>[ROM단계칸] 안 그린다 — {reason}</color>");
    }
}
