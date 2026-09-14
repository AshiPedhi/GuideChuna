using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 실측 프로토타입 — <b>손목 메뉴로 마커를 찍고 이어서 각을 읽는 도구</b>(2026-09-14 신설).
///
/// ★기존 실측(<c>경추ROM실측</c> 시나리오 · <c>CervicalRomRealityMeasure</c>)과 <b>완전히 별개</b>다.
///   서로 참조하지 않는다 — 사용자 결정.
/// ★<b>순서를 강제하지 않는다.</b> 정해진 절차를 밟는 게 목적이 아니라 <b>기록</b>이 목적이다.
///
/// 쓰는 법 — 빈 오브젝트에 이 컴포넌트 하나만 붙이면 된다.
///   손목 메뉴·마커·선·글자를 <b>전부 런타임에 만든다</b>(씬 편집이 필요 없다).
///
/// 조작
///   ① 왼손 검지로 <b>오른 손목 메뉴</b>의 버튼을 누른다(반대도 된다).
///   ② [찍기]   — 누른 손의 <b>반대편 검지 끝</b>에 마커를 만든다. 고른 마커가 있으면 거기서 선이 이어진다.
///   ③ [고르기] — 검지로 기존 마커를 만지면 골라진다. 다음에 찍는 마커가 거기 이어진다.
///   ④ [이름]   — 고른 마커의 역할을 차례로 바꾼다(목 → 머리1 → …).
///   ⑤ [각도]   — 고른 마커의 각 읽는 법을 바꾼다(수평면 / 손 회전 / 없음).
///   ⑥ [묶음]   — 지금 묶음 이름을 바꾼다. 새로 찍는 마커가 이 묶음에 들어간다.
///   ⑦ [지우기] — 고른 마커를 지운다.
/// </summary>
public class RomMarkerLab : MonoBehaviour
{
    // ── 역할·묶음 목록 ────────────────────────────────────────────────
    // ★순환 선택이라 목록 순서가 곧 누르는 순서다. 자주 쓰는 것을 앞에 둔다.
    [Header("=== 목록 ===")]
    [Tooltip("[이름] 버튼이 차례로 돌리는 역할 목록.")]
    [SerializeField]
    private string[] roles =
    {
        "목", "머리1(중립)", "머리2(능동)", "머리3(압박)",
        "어깨L", "어깨R", "기준점",
    };

    [Tooltip("[묶음] 버튼이 차례로 돌리는 묶음 목록.")]
    [SerializeField]
    private string[] groups = { "굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전" };

    // ── 크기·색 ──────────────────────────────────────────────────────
    [Header("=== 모양 ===")]
    [Tooltip("마커 구체 지름(m).")]
    [SerializeField] private float markerSize = 0.018f;

    [Tooltip("고른 마커를 이만큼 키운다(배수).")]
    [SerializeField] private float selectedScale = 1.6f;

    [Tooltip("마커를 잇는 선 굵기(m).")]
    [SerializeField] private float lineWidth = 0.004f;

    [SerializeField] private Color markerColor = new Color(0.25f, 0.8f, 1f, 1f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color lineColor = new Color(0.6f, 0.95f, 0.6f, 0.9f);

    [Tooltip("마커 옆 글자 크기.")]
    [SerializeField] private float labelSize = 1.2f;

    // ── 손목 메뉴 ────────────────────────────────────────────────────
    [Header("=== 손목 메뉴 ===")]
    [Tooltip("손목에서 메뉴를 얼마나 띄울지(m). 손등 쪽으로 민다.")]
    [SerializeField] private Vector3 menuOffset = new Vector3(0f, 0.05f, 0.02f);

    [Tooltip("버튼 하나의 지름(m).")]
    [SerializeField] private float buttonSize = 0.022f;

    [Tooltip("버튼 사이 간격(m).")]
    [SerializeField] private float buttonGap = 0.028f;

    [Tooltip("검지 끝이 버튼 중심에서 이 거리 안에 들어오면 눌린 것으로 본다(m).")]
    [SerializeField] private float pressRadius = 0.022f;

    [Tooltip("한 번 누른 뒤 이만큼은 다시 안 눌린다(초). 손 떨림으로 연타되는 것을 막는다.")]
    [SerializeField] private float pressCooldown = 0.45f;

    [Tooltip("검지 끝이 마커에 이만큼 가까우면 고른다(m).")]
    [SerializeField] private float pickRadius = 0.03f;

    // ── 자유 모드(패스스루) ──────────────────────────────────────────
    [Header("=== 자유 모드 ===")]
    [Tooltip("켜면 시작하자마자 <b>패스스루를 켜고 배경·가상환자·UI를 전부 끈다</b>.\n" +
             "★이 프로토타입의 기본값이다 — 정해진 과정 없이 실제 환자를 재는 도구라\n" +
             "  기존 씬의 구성요소가 남아 있으면 방해만 된다(2026-09-14 사용자 지시).\n" +
             "★우리가 끈 것만 되돌린다. 원래 꺼져 있던 것은 건드리지 않는다.")]
    [SerializeField] private bool freeModeOnStart = true;

    [Tooltip("추가로 끌 오브젝트 이름. 캔버스가 아니어서 안 꺼지는 것이 있으면 여기 적는다.\n" +
             "★못 찾으면 조용히 넘어가지 않고 경고를 남긴다 — 이름이 바뀌었을 수 있다.")]
    [SerializeField] private string[] alsoHideByName;

    [Header("=== 진단 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // ── 상태 ─────────────────────────────────────────────────────────
    private readonly List<RomLabMarker> markers = new List<RomLabMarker>();
    private readonly Dictionary<RomLabMarker, LineRenderer> lines = new Dictionary<RomLabMarker, LineRenderer>();
    private readonly Dictionary<RomLabMarker, TextMeshPro> labels = new Dictionary<RomLabMarker, TextMeshPro>();

    private RomLabMarker selected;
    private int groupIndex;
    private Transform root;

    private Transform leftIndex, rightIndex, leftWrist, rightWrist;
    private WristMenu leftMenu, rightMenu;

    private readonly StringBuilder sb = new StringBuilder(64);   // ★매 프레임 새 문자열을 만들지 않는다
    private Camera cam;
    private TMP_FontAsset font;   // ★한글 폰트. 안 넣으면 TMP 기본 폰트라 글자가 통째로 깨진다.

    private readonly List<GameObject> hiddenByUs = new List<GameObject>();
    private PracticeSettingsController practiceSettings;
    private bool passthroughWasOn;
    private bool freeModeApplied;

    /// <summary>손목에 붙는 버튼 묶음. 버튼은 반대 손 검지로 누른다.</summary>
    private class WristMenu
    {
        public Transform anchor;
        public readonly List<Transform> buttons = new List<Transform>();
        public readonly List<string> ids = new List<string>();
        public float nextPressTime;
    }

    // ── 수명 ─────────────────────────────────────────────────────────

    private void Start()
    {
        root = new GameObject("[실측랩] 마커").transform;
        root.SetParent(transform, false);
        cam = Camera.main;
        font = KoreanFontResolver.Resolve();

        if (freeModeOnStart) EnterFreeMode();

        ResolveHands();
        BuildMenus();

        ChunaLogger.Log("<color=cyan>[실측랩] 시작 — " +
                        "왼검지 " + (leftIndex != null ? "O" : "★없음") +
                        " · 오른검지 " + (rightIndex != null ? "O" : "★없음") +
                        " · 왼손목 " + (leftWrist != null ? "O" : "★없음") +
                        " · 오른손목 " + (rightWrist != null ? "O" : "★없음") +
                        " · 폰트 " + (font != null ? font.name : "★없음") + "</color>");
    }

    private void Update()
    {
        // ★손 뼈는 런타임에 생긴다 — 못 찾았으면 계속 다시 찾는다(2026-08-24 실측).
        if (leftIndex == null || rightIndex == null || leftWrist == null || rightWrist == null)
        {
            ResolveHands();
            if (leftMenu == null || rightMenu == null) BuildMenus();
        }

        PlaceMenu(leftMenu, leftWrist);
        PlaceMenu(rightMenu, rightWrist);

        // 왼손 검지로 오른 손목 메뉴를, 오른손 검지로 왼 손목 메뉴를 누른다.
        PollMenu(rightMenu, leftIndex, true);
        PollMenu(leftMenu, rightIndex, false);

        RefreshVisuals();
    }

    // ── 자유 모드 ────────────────────────────────────────────────────

    /// <summary>
    /// 패스스루를 켜고 <b>기존 씬의 배경·가상환자·UI를 전부 치운다</b>(2026-09-14 사용자 지시:
    /// "디폴트가 패스쓰루 모드에서 정해진 과정 없이 자유롭게 하는 거라
    ///  기존 씬에서 배경이랑 UI는 싹 꺼야 해").
    ///
    /// ★<b>배경은 따로 안 끈다</b> — 패스스루를 켜면 방 모델째 사라지고 추나 베드도 그 안에 있다
    ///   (08-31 실측). 여기서 치울 것은 가상 환자와 UI다.
    /// ★★<b>우리가 끈 것만 우리가 되돌린다.</b> 원래 꺼져 있던 것은 목록에 안 담는다 —
    ///   켠 쪽과 끄는 쪽이 다르면 상태가 샌다(07-27 xray 사고가 그 형태였다).
    /// </summary>
    private void EnterFreeMode()
    {
        if (freeModeApplied) return;
        freeModeApplied = true;

        practiceSettings = FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);
        if (practiceSettings != null)
        {
            passthroughWasOn = practiceSettings.IsRealityModeOn;
            if (!passthroughWasOn) practiceSettings.SetRealityMode(true);
            practiceSettings.SetPatientBodyVisible(false);
        }
        else
        {
            ChunaLogger.LogWarning("[실측랩] PracticeSettingsController가 없어 패스스루·환자 숨김을 못 겁니다 — " +
                                   "배경이 그대로 보이면 이것 때문입니다.");
        }

        hiddenByUs.Clear();

        // ★UI는 캔버스 단위로 끈다. 이름으로 하나씩 찾으면 이름이 바뀔 때마다 조용히 죽는다.
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++) HideOne(canvases[i].gameObject);

        // 캔버스가 아니라서 안 꺼지는 것(월드 글자·게이지 등)은 이름으로 받는다.
        if (alsoHideByName != null)
        {
            for (int i = 0; i < alsoHideByName.Length; i++)
            {
                string n = alsoHideByName[i];
                if (string.IsNullOrWhiteSpace(n)) continue;
                GameObject go = GameObject.Find(n.Trim());
                if (go == null)
                {
                    ChunaLogger.LogWarning($"[실측랩] '{n}'을(를) 못 찾아 숨기지 못했습니다 — 이름이 바뀌었는지 확인하세요.");
                    continue;
                }
                HideOne(go);
            }
        }

        ChunaLogger.Log($"<color=cyan>[실측랩] 자유 모드 — 패스스루 {(passthroughWasOn ? "이미 켜져 있었음" : "켬")} · " +
                        $"캔버스 {canvases.Length}개 검사 · 우리가 끈 것 {hiddenByUs.Count}개</color>");
    }

    /// <summary>원래 켜져 있던 것만 끈다. 끈 것만 기억했다가 나갈 때 되돌린다.</summary>
    private void HideOne(GameObject go)
    {
        if (go == null || !go.activeSelf) return;
        if (go.transform.IsChildOf(transform)) return;   // 우리 것은 끄지 않는다
        go.SetActive(false);
        hiddenByUs.Add(go);
    }

    private void ExitFreeMode()
    {
        if (!freeModeApplied) return;
        freeModeApplied = false;

        for (int i = 0; i < hiddenByUs.Count; i++)
            if (hiddenByUs[i] != null) hiddenByUs[i].SetActive(true);
        hiddenByUs.Clear();

        if (practiceSettings != null)
        {
            practiceSettings.SetPatientBodyVisible(true);
            // ★우리가 켠 것만 되돌린다 — 사용자가 미리 켜 둔 패스스루는 그대로 둔다.
            if (!passthroughWasOn) practiceSettings.SetRealityMode(false);
        }

        ChunaLogger.Log("<color=cyan>[실측랩] 자유 모드 해제 — 껐던 것을 되돌렸다.</color>");
    }

    private void OnDisable() => ExitFreeMode();

    // ── 손 찾기 ──────────────────────────────────────────────────────

    /// <summary>
    /// 손 뼈를 찾는다. ★<see cref="CervicalGripJudge"/>가 쓰는 것과 <b>같은 방법</b>이다 —
    /// 손 루트는 <see cref="ChunaPathEvaluator"/>가 들고 있고, 뼈는 이름으로 찾는다.
    /// ★뼈 이름의 좌우 접두로 가르면 안 된다(이 프로젝트는 오른손 리그도 b_l_* 이다).
    ///   좌우는 <b>어느 손 루트 아래인지</b>로만 가른다.
    /// </summary>
    private void ResolveHands()
    {
        ChunaPathEvaluator evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (evaluator == null) return;

        Transform l = HandRoot(evaluator, "playerLeftHand");
        Transform r = HandRoot(evaluator, "playerRightHand");

        if (l != null)
        {
            if (leftIndex == null) leftIndex = FindBone(l, "index", "Tip") ?? FindBone(l, "index", "3");
            if (leftWrist == null) leftWrist = FindBone(l, "wrist", "") ?? l;
        }
        if (r != null)
        {
            if (rightIndex == null) rightIndex = FindBone(r, "index", "Tip") ?? FindBone(r, "index", "3");
            if (rightWrist == null) rightWrist = FindBone(r, "wrist", "") ?? r;
        }
    }

    private static Transform HandRoot(ChunaPathEvaluator evaluator, string field)
    {
        System.Reflection.FieldInfo info = typeof(ChunaPathEvaluator).GetField(
            field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Component c = info != null ? info.GetValue(evaluator) as Component : null;
        return c != null ? c.transform : null;
    }

    private static Transform FindBone(Transform root, string keyword, string suffix)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name.ToLowerInvariant();
            if (!n.Contains(keyword)) continue;
            if (suffix.Length > 0 && !all[i].name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) continue;
            return all[i];
        }
        return null;
    }

    // ── 손목 메뉴 ────────────────────────────────────────────────────

    private void BuildMenus()
    {
        if (leftWrist != null && leftMenu == null) leftMenu = MakeMenu("왼손목", leftWrist);
        if (rightWrist != null && rightMenu == null) rightMenu = MakeMenu("오른손목", rightWrist);
    }

    private WristMenu MakeMenu(string name, Transform anchor)
    {
        WristMenu menu = new WristMenu();
        menu.anchor = anchor;

        // ★버튼 이름은 짧게. 손목 옆이라 글자가 길면 못 읽는다.
        string[] ids = { "찍기", "고르기", "이름", "각도", "묶음", "지우기" };
        Color[] tints =
        {
            new Color(0.3f, 0.8f, 1f), new Color(1f, 0.85f, 0.25f), new Color(0.7f, 0.9f, 0.5f),
            new Color(0.9f, 0.6f, 1f), new Color(0.5f, 0.85f, 0.9f), new Color(1f, 0.45f, 0.4f),
        };

        for (int i = 0; i < ids.Length; i++)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "[실측랩] " + name + " 버튼 " + ids[i];
            Destroy(go.GetComponent<Collider>());          // 물리는 안 쓴다 — 거리로만 판정한다
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * buttonSize;
            Paint(go, tints[i]);

            TextMeshPro cap = MakeLabel(ids[i]);
            cap.transform.SetParent(go.transform, false);
            cap.transform.localPosition = new Vector3(0f, 1.1f, 0f);

            menu.buttons.Add(go.transform);
            menu.ids.Add(ids[i]);
        }
        return menu;
    }

    private void PlaceMenu(WristMenu menu, Transform anchor)
    {
        if (menu == null || anchor == null) return;

        Vector3 basePos = anchor.TransformPoint(menuOffset);
        for (int i = 0; i < menu.buttons.Count; i++)
        {
            // 손목을 따라 일렬로 세운다. 손등 방향(앞)으로 늘어놓는다.
            menu.buttons[i].position = basePos + anchor.forward * (buttonGap * i);
            if (cam != null)
                menu.buttons[i].rotation = Quaternion.LookRotation(menu.buttons[i].position - cam.transform.position);
        }
    }

    private void PollMenu(WristMenu menu, Transform pressingTip, bool isLeftPressing)
    {
        if (menu == null || pressingTip == null) return;
        if (Time.time < menu.nextPressTime) return;

        for (int i = 0; i < menu.buttons.Count; i++)
        {
            if (Vector3.Distance(pressingTip.position, menu.buttons[i].position) > pressRadius) continue;

            menu.nextPressTime = Time.time + pressCooldown;
            // ★찍는 손은 <b>버튼을 누르지 않은 쪽</b>이다. 왼손으로 눌렀으면 오른 검지에 찍는다.
            Transform drawTip = isLeftPressing ? rightIndex : leftIndex;
            Transform drawWrist = isLeftPressing ? rightWrist : leftWrist;
            RunCommand(menu.ids[i], drawTip, drawWrist);
            return;
        }
    }

    // ── 버튼 동작 ────────────────────────────────────────────────────

    private void RunCommand(string id, Transform tip, Transform wrist)
    {
        switch (id)
        {
            case "찍기":   PlaceMarker(tip, wrist); break;
            case "고르기": PickNearest(tip);        break;
            case "이름":   CycleRole();             break;
            case "각도":   CycleAngleMode();        break;
            case "묶음":   CycleGroup();            break;
            case "지우기": DeleteSelected();        break;
        }
    }

    private void PlaceMarker(Transform tip, Transform wrist)
    {
        if (tip == null)
        {
            ChunaLogger.LogWarning("[실측랩] 찍을 손 검지를 못 찾았다 — 손을 화면에 보이게 하고 다시 눌러라.");
            return;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.position = tip.position;
        go.transform.localScale = Vector3.one * markerSize;

        RomLabMarker m = go.AddComponent<RomLabMarker>();
        m.parent = selected;                                   // ★고른 마커가 있으면 거기서 선이 이어진다
        m.group = groups.Length > 0 ? groups[groupIndex] : "";
        m.role = roles.Length > 0 ? roles[0] : "새 마커";

        // ★손 회전을 같이 적어 둔다. 나중에 [각도]로 '손 회전' 모드를 고르면 이 값이 쓰인다 —
        //   머리에 손을 올린 채 찍는 경우가 그것이다(사용자 요구 ⑤).
        if (wrist != null)
        {
            m.handAngle = SignedRoll(wrist);
            m.hasHandAngle = true;
        }

        go.name = "[실측랩] " + m.role;
        markers.Add(m);
        TextMeshPro lab = MakeLabel("");
        labels[m] = lab;
        if (m.parent != null) lines[m] = MakeLine();

        selected = m;

        if (showDebugLogs)
            ChunaLogger.Log("<color=cyan>[실측랩] 찍음 — " + m.role + " · 묶음 '" + m.group + "' · 부모 " +
                            (m.parent != null ? m.parent.role : "없음") +
                            " · 손회전 " + m.handAngle.ToString("F1") + "°</color>");
    }

    /// <summary>
    /// 손목의 회전을 각으로 바꾼다. ★머리에 손을 올린 채 고개가 돌면 손도 같이 돈다 —
    /// 그 회전을 그대로 각으로 읽는 것이 사용자 요구 ⑤다.
    /// ★부호는 <b>Play에서 눈으로 확인해 맞춰야 한다</b>(추론으로 정하지 않는다 — 규칙 9).
    /// </summary>
    private static float SignedRoll(Transform wrist)
    {
        // 손등 법선이 수평면과 이루는 각. 고개를 숙이면 손도 따라 기운다.
        float fromUp = Vector3.Angle(wrist.up, Vector3.up);
        return 90f - fromUp;
    }

    private void PickNearest(Transform tip)
    {
        if (tip == null) return;

        RomLabMarker best = null;
        float bestD = pickRadius;
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] == null) continue;
            float d = Vector3.Distance(tip.position, markers[i].transform.position);
            if (d < bestD) { bestD = d; best = markers[i]; }
        }

        if (best == null)
        {
            // ★아무것도 안 잡히면 <b>선택을 푼다</b>. 그래야 다음 마커가 외톨이로 시작한다.
            selected = null;
            if (showDebugLogs) ChunaLogger.Log("[실측랩] 고르기 — 가까운 마커가 없어 선택을 풀었다.");
            return;
        }

        selected = best;
        if (showDebugLogs)
            ChunaLogger.Log("<color=cyan>[실측랩] 고름 — " + best.role +
                            " (" + (bestD * 100f).ToString("F1") + "cm)</color>");
    }

    private void CycleRole()
    {
        if (selected == null || roles.Length == 0) return;
        int i = System.Array.IndexOf(roles, selected.role);
        selected.role = roles[(i + 1 + roles.Length) % roles.Length];
        selected.name = "[실측랩] " + selected.role;
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측랩] 이름 → " + selected.role + "</color>");
    }

    private void CycleAngleMode()
    {
        if (selected == null) return;
        selected.angleMode = (RomLabMarker.AngleMode)(((int)selected.angleMode + 1) % 3);
        if (showDebugLogs)
            ChunaLogger.Log("<color=cyan>[실측랩] " + selected.role + " 각도 → " + selected.angleMode + "</color>");
    }

    private void CycleGroup()
    {
        if (groups.Length == 0) return;
        groupIndex = (groupIndex + 1) % groups.Length;
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측랩] 묶음 → " + groups[groupIndex] + "</color>");
    }

    private void DeleteSelected()
    {
        if (selected == null) return;

        // ★이 마커를 부모로 쓰던 마커들은 <b>외톨이가 된다</b>. 지우면 선도 같이 지운다.
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] == null || markers[i].parent != selected) continue;
            markers[i].parent = null;
            LineRenderer orphan;
            if (lines.TryGetValue(markers[i], out orphan) && orphan != null) Destroy(orphan.gameObject);
            lines.Remove(markers[i]);
        }

        string gone = selected.role;

        LineRenderer mine;
        if (lines.TryGetValue(selected, out mine) && mine != null) Destroy(mine.gameObject);
        lines.Remove(selected);

        TextMeshPro lab;
        if (labels.TryGetValue(selected, out lab) && lab != null) Destroy(lab.gameObject);
        labels.Remove(selected);

        markers.Remove(selected);
        Destroy(selected.gameObject);
        selected = null;

        if (showDebugLogs) ChunaLogger.Log("<color=orange>[실측랩] 지움 — " + gone + "</color>");
    }

    // ── 그리기 ───────────────────────────────────────────────────────

    private void RefreshVisuals()
    {
        for (int i = 0; i < markers.Count; i++)
        {
            RomLabMarker m = markers[i];
            if (m == null) continue;

            bool isSel = m == selected;
            m.transform.localScale = Vector3.one * (markerSize * (isSel ? selectedScale : 1f));
            Paint(m.gameObject, isSel ? selectedColor : markerColor);

            // 선
            if (m.parent != null)
            {
                LineRenderer lr;
                if (!lines.TryGetValue(m, out lr) || lr == null)
                {
                    lr = MakeLine();
                    lines[m] = lr;
                }
                lr.SetPosition(0, m.parent.transform.position);
                lr.SetPosition(1, m.transform.position);
            }

            // 글자
            TextMeshPro label;
            if (!labels.TryGetValue(m, out label) || label == null) continue;

            sb.Length = 0;
            sb.Append(m.role);
            if (!string.IsNullOrEmpty(m.group)) { sb.Append(" ["); sb.Append(m.group); sb.Append(']'); }

            float a = m.ReadableAngle;
            if (!float.IsNaN(a))
            {
                sb.Append('\n');
                sb.Append(a.ToString("F1"));
                sb.Append('°');
                sb.Append(m.angleMode == RomLabMarker.AngleMode.FromHandRoll ? " (손)" : " (수평)");
            }
            label.text = sb.ToString();

            label.transform.position = m.transform.position + Vector3.up * (markerSize * 1.8f);
            if (cam != null)
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        }
    }

    private LineRenderer MakeLine()
    {
        GameObject go = new GameObject("[실측랩] 선");
        go.transform.SetParent(root, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.widthMultiplier = lineWidth;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        return lr;
    }

    private TextMeshPro MakeLabel(string text)
    {
        GameObject go = new GameObject("[실측랩] 글자");
        go.transform.SetParent(root, false);
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = labelSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        return t;
    }

    private static void Paint(GameObject go, Color c)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r == null) return;
        // ★공유 머티리얼을 건드리면 다른 오브젝트까지 물든다. 인스턴스를 쓴다.
        if (r.material.color != c) r.material.color = c;
    }
}
