using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정보 디스플레이 펼침 — 골격 · 전문가 영상 · 수행결과를 <b>동시에</b> 띄운다 (2026-10-27 사용자 요청 5 · 시안 V0.7 그림 5).
///
/// «하나 켜면 하나 꺼지는» 배타 방식을 버리고 여러 판을 같이 띄운다.
/// ★10-29 사용자 정정: «펼쳐질 때 좌측에 있던 걸 우측으로 보내지 말고, 좌측이 차 있으면 밀어내는 게 아니라 본인이 우측으로 가라».
///   → 자리는 정면 → 왼쪽 → 오른쪽 → (더 바깥) 순의 <b>고정 칸</b>이다. 새로 연 판이 <b>비어 있는 첫 칸</b>에 들어가고,
///     이미 떠 있는 판은 움직이지 않는다. 닫으면 그 칸만 빈다.
///   (종전 10-27~28 = 최근 판이 정면 · 먼저 켠 판은 왼쪽 → 오른쪽으로 밀림 — 사용자 의도와 달랐다)
///
/// [판단 기준] 페이지 오브젝트의 activeSelf 하나다. 페이지를 켜고 끄는 규칙은 여전히 <see cref="InfoPanelController"/>가 쥔다 —
///   이 컴포넌트는 그 결과를 보고 판(frame)을 켜고 끄고 자리만 잡는다. 그래서 InfoPanelController가 페이지를 직접 끄는
///   어느 경로(초기화 · 리셋 · ROM 실측 접근구)를 타도 판이 같이 따라 꺼진다.
/// [자리] 첫 판이 열릴 때 헤드셋 위치 · 방향을 한 번 잡고 고정한다(따라오지 않음). <see cref="Recenter"/>로 다시 잡는다.
/// [옛 패널] 모드 선택 페이지나 종료 확인 팝업이 떠 있을 때만 옛 정보 패널을 보인다(그 둘은 아직 옛 패널 안에 있다).
/// </summary>
public class InfoWindowArranger : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public GameObject page;     // InfoPanelController가 켜고 끄는 페이지(골격 · 영상 · 진행 및 결과)
        public Transform frame;     // 그 페이지를 담은 판(UISet 창)
    }

    [SerializeField] private Slot[] slots = new Slot[0];
    [Tooltip("비우면 OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Transform headset;

    [Header("자리 (시안 제안값)")]
    [SerializeField] private float radius = 1.2f;
    [Tooltip("정면에서 좌우 판까지 각도")]
    [SerializeField] private float sideAngle = 38f;
    [Tooltip("눈높이 기준 판 중심 높이")]
    [SerializeField] private float heightOffset = -0.05f;
    [SerializeField] private float moveSpeed = 8f;

    [Header("옛 정보 패널 (모드 선택 · 종료 확인이 아직 그 안에 있다)")]
    [SerializeField] private GameObject legacyPanel;
    [SerializeField] private GameObject modeSelectionPage;
    [SerializeField] private GameObject exitConfirmPopup;

    [SerializeField] private bool showDebugLog = true;

    private readonly List<int> order = new List<int>(4);   // 열린 판 목록(움직일 판)
    private int[] seat;                                     // 판마다 앉은 칸(0 정면 · 1 왼쪽 · 2 오른쪽 · 3 더 왼쪽 …) · -1 = 닫힘
    private bool[] wasOpen;
    private bool[] snapNext;   // 새로 연 판은 제자리에서 바로 나타난다(날아오지 않게) — 밀려나는 판만 움직인다
    private Vector3[] targetPos;
    private Quaternion[] targetRot;
    private bool anchored;
    // ★10-29 실측(Quest 3 로그): 씬 로드 0.1초 뒤 첫 판이 열리며 기준을 잡았는데 머리가 (-0.01, 1.15, -0.02) — 추적 전 초기값이었다.
    //   그래서 상단 메뉴 · 정보 창 높이가 실제 눈높이와 어긋났다(사용자 «헤드셋 기준 높이가 이상하다»).
    //   → 씬 로드 직후에 잡은 기준은 «임시»로 두고, 머리가 처음 값에서 움직이거나 settleSeconds가 지나면 한 번 다시 잡는다.
    [Tooltip("씬 로드 뒤 이 시간 안에 잡은 기준은 임시 — 추적이 안정되면 한 번 다시 잡는다(초)")]
    [SerializeField] private float settleSeconds = 1.5f;
    // 10-30: 로비 경로 자동 맞춤(InfoPanelController · 1.5초)이 환자를 옮긴 뒤에 환자 쪽 정면을 잡는다
    public const float AfterAutoFit = 0.1f;
    private Vector3 anchorPos;
    private Vector3 anchorFwd;

    public bool IsReady => slots != null && slots.Length > 0;

    /// <summary>펼침 기준을 잡을 때마다(머리 위치 · 수평 정면) 알린다 — 상단 메뉴(TopBarPlacer)가 같은 정면으로 다시 놓는다(10-29).</summary>
    public static event System.Action<Vector3, Vector3> Anchored;

    private void Awake()
    {
        wasOpen = new bool[slots.Length];
        seat = new int[slots.Length];
        for (int i = 0; i < seat.Length; i++) seat[i] = -1;
        snapNext = new bool[slots.Length];
        targetPos = new Vector3[slots.Length];
        targetRot = new Quaternion[slots.Length];
        if (headset == null)
        {
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) headset = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        }
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].frame != null) slots[i].frame.gameObject.SetActive(false);
    }

    /// <summary>InfoPanelController가 페이지를 띄울 때 부른다. ★10-29부터 이미 떠 있는 판은 자리를 옮기지 않는다(고정 칸) —
    /// 아직 칸이 없으면 비어 있는 첫 칸을 준다. 이름은 호출부 호환으로 남겼다.</summary>
    public void BringToFront(GameObject page)
    {
        int i = IndexOf(page);
        if (i < 0) return;
        if (!order.Contains(i)) order.Add(i);
        if (seat[i] < 0) { seat[i] = FreeSeat(); Layout(); }
    }

    private int FreeSeat()
    {
        for (int s = 0; ; s++)
        {
            bool used = false;
            for (int j = 0; j < seat.Length; j++) if (seat[j] == s) { used = true; break; }
            if (!used) return s;
        }
    }

    /// <summary>지금 머리 위치 · 방향으로 펼침 자리를 다시 잡는다.</summary>
    public void Recenter()
    {
        anchored = false;
        Anchor();
        Layout();
        SnapAll();
    }

    private void LateUpdate()
    {
        // ★10-30 옛 «임시 기준 → settleSeconds 뒤 다시 잡기»는 없앴다(사용자 «거슬린다») — 이제 추적이 들어온 뒤(TrackingReady) 한 번만 잡는다
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s.page == null || s.frame == null) continue;
            bool open = s.page.activeSelf;
            if (open == wasOpen[i]) continue;
            // ★10-30 사용자 «1.5초 뒤 다시 재설정하는 거 거슬린다» — 먼저 띄우고 옮기지 않고, 추적이 안정될 때까지(settleSeconds) 띄우기를 미룬다.
            //   한 번만 놓이니 눈앞에서 자리가 바뀌지 않는다(그래서 아래 «임시 기준 다시 잡기»는 더 걸리지 않는다)
            if (open && !anchored && !TrackingReady.Ready(AfterAutoFit) && Time.timeSinceLevelLoad < settleSeconds + AfterAutoFit) continue;   // settleSeconds = 추적 신호가 안 올 때 상한
            wasOpen[i] = open;
            changed = true;
            if (open)
            {
                if (!anchored) Anchor();
                if (!order.Contains(i)) order.Add(i);
                if (seat[i] < 0) seat[i] = FreeSeat();
                s.frame.gameObject.SetActive(true);
                snapNext[i] = true;
            }
            else
            {
                order.Remove(i);
                seat[i] = -1;   // 그 칸만 빈다 — 다른 판은 그대로
                s.frame.gameObject.SetActive(false);
            }
        }
        if (changed)
        {
            Layout();
            for (int i = 0; i < slots.Length; i++)
                if (snapNext[i]) { Snap(i); snapNext[i] = false; }
        }

        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            Transform f = slots[i].frame;
            float t = 1f - Mathf.Exp(-moveSpeed * Time.unscaledDeltaTime);
            f.position = Vector3.Lerp(f.position, targetPos[i], t);
            f.rotation = Quaternion.Slerp(f.rotation, targetRot[i], t);
        }

        // ★10-30 나가기 팝업이 옛 정보패널 밖의 독립 판(TrainingExitPopupTool · 로비 규격)이면 옛 패널을 켜지도 옮기지도 않는다
        bool exitInLegacy = exitConfirmPopup != null && legacyPanel != null && exitConfirmPopup.transform.IsChildOf(legacyPanel.transform);
        if (legacyPanel != null)
        {
            bool legacy = (modeSelectionPage != null && modeSelectionPage.activeSelf) || (exitInLegacy && exitConfirmPopup.activeSelf);
            if (legacyPanel.activeSelf != legacy) legacyPanel.SetActive(legacy);
        }

        // 10-30 사용자 «나가기 팝업은 상단 메뉴 아래에 · 크기는 상단 메뉴 가로에 맞춰 비율 조정»
        bool exitOpen = exitConfirmPopup != null && exitConfirmPopup.activeInHierarchy;
        if (exitOpen && !exitPlaced) { exitPlaceFrames++; if (exitPlaceFrames >= 2) PlaceExitUnderBar(exitInLegacy); }   // 2프레임 뒤 — ContentSizeFitter가 크기를 잡은 다음
        else if (!exitOpen && exitPlaced) RestoreLegacyPose();
        if (!exitOpen) exitPlaceFrames = 0;
    }

    // ── 나가기 팝업 자리 (10-30) ──
    [Tooltip("나가기 팝업과 상단 메뉴 사이 틈(m)")]
    [SerializeField] private float exitGapBelowBar = 0.02f;
    private bool exitPlaced;
    private int exitPlaceFrames;
    private Vector3 legacyPos, legacyScale;
    private Quaternion legacyRot;
    private static readonly Vector3[] Corners = new Vector3[4];

    private Transform exitMoveRoot;   // 옮긴 것 — 옛 정보패널(옛 팝업) 또는 독립 판의 뿌리(새 팝업). 닫을 때 이걸 되돌린다

    private void PlaceExitUnderBar(bool inLegacy)
    {
        exitPlaced = true;
        var bar = ActiveBarPlate();
        // 옛 팝업 = 팝업 RectTransform 자체 · 독립 판 = 그 안의 UIBackplate(HzUIKit.Plate)
        var exit = inLegacy ? exitConfirmPopup.transform as RectTransform : FindDeep(exitConfirmPopup.transform, "UIBackplate") as RectTransform;
        exitMoveRoot = inLegacy ? legacyPanel.transform : exitConfirmPopup.transform;
        if (bar == null || exit == null) { Debug.Log($"[InfoWindows] 나가기 팝업 — 상단 메뉴 {(bar != null)} · 팝업 판 {(exit != null)} 없음, 자리 그대로"); exitMoveRoot = null; return; }
        Transform root = exitMoveRoot;
        legacyPos = root.position; legacyRot = root.rotation; legacyScale = root.localScale;

        // 1) 방향 = 수직으로 서서 사용자를 본다  2) 가로 = 상단 메뉴 가로  3) 위치 = 상단 메뉴 아래 가장자리 바로 밑
        // ★10-30 사용자 «실습 씬 나가는 팝업 기울기 빼고 사용자 정면으로 바라보게» — 종전엔 상단 메뉴 판의 방향(위로 30° 젖힘)을 그대로 따랐다
        float barW = WorldSize(bar, out float barH);
        Vector3 barBottom = WorldCenter(bar) - bar.up * (barH * 0.5f);
        Vector3 eye = Camera.main != null ? Camera.main.transform.position : anchorPos;
        Vector3 look = Vector3.ProjectOnPlane(barBottom - eye, Vector3.up);
        if (look.sqrMagnitude < 1e-6f) look = Vector3.ProjectOnPlane(bar.forward, Vector3.up);
        Quaternion face = Quaternion.LookRotation(look.normalized, Vector3.up);   // UI 캔버스는 +Z가 사용자 반대쪽
        root.rotation = face * Quaternion.Inverse(exit.rotation) * root.rotation;
        float exitW = WorldSize(exit, out _);
        if (exitW > 1e-5f) root.localScale *= barW / exitW;
        WorldSize(exit, out float exitH);
        Vector3 target = barBottom - Vector3.up * (exitGapBelowBar + exitH * 0.5f);
        root.position += target - WorldCenter(exit);
        if (inLegacy) HideLegacyBackground(exit);
        if (showDebugLog) Debug.Log($"[InfoWindows] 나가기 팝업 → 상단 메뉴 아래 ({(inLegacy ? "옛 정보패널 판" : "독립 판")}) — 가로 {barW:F2}m(종전 {exitW:F2}m) · 세로 {exitH:F2}m · 자리 {target:F2}");
    }

    // ★10-30 사용자 «나가기 팝업 뒷배경은 없어져야지 왜 같이 나와» — 팝업이 옛 정보패널 판 안에 있어 판 바탕 · 다른 페이지가 같이 켜졌다.
    //   팝업이 떠 있는 동안: 팝업까지 가는 길의 형제(다른 페이지)는 끄고 · 길 위 판들의 바탕은 숨긴다(Mask 판은 마스크 그림만 숨김 — 끄면 팝업도 잘려 안 보인다).
    //   ★우리가 바꾼 것만 기록해 닫을 때 그대로 되돌린다
    private readonly List<GameObject> hiddenSiblings = new List<GameObject>();
    private readonly List<Graphic> hiddenGraphics = new List<Graphic>();
    private readonly List<Mask> hiddenMaskGraphics = new List<Mask>();

    private void HideLegacyBackground(Transform exit)
    {
        Transform root = legacyPanel.transform;
        for (Transform t = exit; t != null && t != root; t = t.parent)
        {
            Transform parent = t.parent; if (parent == null) break;
            // ★10-30 사용자 «나가기 팝업 버튼이 안 눌린다» — 손 · 레이 표면(ISDK_PokeInteraction · ISDK_RayInteraction)이 팝업의 형제라 같이 꺼졌다. 그건 남긴다
            foreach (Transform sib in parent)
                if (sib != t && sib.gameObject.activeSelf && !sib.name.StartsWith("ISDK_")) { sib.gameObject.SetActive(false); hiddenSiblings.Add(sib.gameObject); }
            HideOwnBackground(parent);
        }
        if (showDebugLog) Debug.Log($"[InfoWindows] 나가기 팝업 뒷배경 숨김 — 다른 페이지 {hiddenSiblings.Count}개 · 판 바탕 {hiddenGraphics.Count + hiddenMaskGraphics.Count}개");
    }

    private void HideOwnBackground(Transform t)
    {
        var mask = t.GetComponent<Mask>();
        if (mask != null) { if (mask.showMaskGraphic) { mask.showMaskGraphic = false; hiddenMaskGraphics.Add(mask); } return; }
        var g = t.GetComponent<Graphic>();
        if (g != null && g.enabled) { g.enabled = false; hiddenGraphics.Add(g); }
    }

    private void RestoreLegacyBackground()
    {
        foreach (var go in hiddenSiblings) if (go != null) go.SetActive(true);
        foreach (var g in hiddenGraphics) if (g != null) g.enabled = true;
        foreach (var m in hiddenMaskGraphics) if (m != null) m.showMaskGraphic = true;
        hiddenSiblings.Clear(); hiddenGraphics.Clear(); hiddenMaskGraphics.Clear();
    }

    private void RestoreLegacyPose()
    {
        RestoreLegacyBackground();
        exitPlaced = false;
        Transform root = exitMoveRoot; exitMoveRoot = null;
        if (root == null) return;   // 못 옮겼던 판(상단 메뉴 없음) — 되돌릴 것 없음
        root.SetPositionAndRotation(legacyPos, legacyRot); root.localScale = legacyScale;
    }

    // 켜져 있는 상단 메뉴(TopBarPlacer)의 판 — A/B 중 활성인 쪽
    private RectTransform ActiveBarPlate()
    {
        foreach (var p in FindObjectsByType<TopBarPlacer>(FindObjectsSortMode.None))
        {
            var back = FindDeep(p.transform, "UIBackplate") as RectTransform;
            if (back != null && back.gameObject.activeInHierarchy) return back;
        }
        return null;
    }

    private static float WorldSize(RectTransform r, out float height)
    {
        r.GetWorldCorners(Corners);
        height = Vector3.Distance(Corners[0], Corners[1]);
        return Vector3.Distance(Corners[1], Corners[2]);
    }

    private static Vector3 WorldCenter(RectTransform r)
    {
        r.GetWorldCorners(Corners);
        return (Corners[0] + Corners[2]) * 0.5f;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    private void Anchor()
    {
        if (headset == null) { anchorPos = transform.position; anchorFwd = Vector3.forward; anchored = true; return; }
        anchorPos = headset.position;
        // ★10-30 사용자 «실습 UI 헤드셋 방향 따라가게 하지 말고 환자랑 같이 정면 고정» — 정면 = 내 머리 → 환자 몸 중심(수평). 환자를 못 찾으면 종전대로 머리 방향
        Vector3 f = PatientLocator.TryGet(out var pp) ? pp.center - anchorPos : headset.forward;
        if (Mathf.Abs(f.x) + Mathf.Abs(f.z) < 0.05f) f = headset.forward;
        f.y = 0f;
        anchorFwd = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        anchored = true;
        // ★10-30 기기 로그: 0.35초 · 1.5초에 두 번 잡혔다(옛 «임시 기준 → 다시 잡기»). 이제 추적이 들어온 뒤에만 잡으므로 다시 잡지 않는다(사용자 «거슬린다»)
        // ★Debug.Log — ChunaLogger.Log는 릴리스 빌드에서 빠져 기기 로그에 안 남는다(10-29 «메뉴가 머리 뒤» 추적 때 확인)
        if (showDebugLog) Debug.Log($"[InfoWindows] 펼침 기준 고정 — 머리 {anchorPos:F2} · 정면 {anchorFwd:F2} · 머리 기울기 {headset.eulerAngles:F0}");
        Anchored?.Invoke(anchorPos, anchorFwd);
    }

    // 칸: 정면(0) · 왼쪽(1) · 오른쪽(2) — 그 뒤는 더 바깥으로
    private void Layout()
    {
        for (int n = 0; n < order.Count; n++)
        {
            int i = order[n];
            int k = Mathf.Max(0, seat[i]);
            float ang = k == 0 ? 0f : (k % 2 == 1 ? -1f : 1f) * sideAngle * OsDisplayScale.Density * ((k + 1) / 2);   // 10-28 OS 화면 크기로 판이 커지면 간격도 같이 벌린다
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * anchorFwd;
            Vector3 p = anchorPos + dir * radius;
            p.y = anchorPos.y + heightOffset;
            targetPos[i] = p;
            targetRot[i] = Quaternion.LookRotation(dir, Vector3.up);   // UI 캔버스는 +Z가 사용자 반대쪽
        }
        if (showDebugLog && order.Count > 0)
        {
            var sb = new System.Text.StringBuilder("[InfoWindows] 배치 ");
            for (int n = 0; n < order.Count; n++) { int k = seat[order[n]]; sb.Append(k == 0 ? " 정면=" : (k % 2 == 1 ? " 왼쪽=" : " 오른쪽=")).Append(slots[order[n]].page.name); }
            ChunaLogger.Log(sb.ToString());   // 판이 열리고 닫힐 때만 — 매 프레임 아님
        }
    }

    private void SnapAll() { for (int k = 0; k < order.Count; k++) Snap(order[k]); }
    private void Snap(int i) { slots[i].frame.SetPositionAndRotation(targetPos[i], targetRot[i]); }

    private int IndexOf(GameObject page)
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i].page == page) return i;
        return -1;
    }
}
