using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 파지점 표시 정리 — <b>렌더러만 끈다.</b>
/// 메뉴: GuideChuna/파지점 표시 정리 (렌더러만 끄기)
///
/// ★왜 이 방식인가(2026-08-13 회의 + 08-17 실측):
/// 회의 결정은 "흉추 교정에서 주동수와 진단 돌기 위치를 빼고 파지점을 모두 없앤다"(이유 = 시선 분산).
/// 그런데 <b>오브젝트를 지우거나 SetActive(false)로 끄면 판정이 조용히 죽는다</b> —
/// <c>GripPointTarget</c>은 <c>GetComponent&lt;Collider&gt;()</c>로 <b>자기 자신에게 붙은</b> 콜라이더를
/// 접촉 소스로 쓰고(<c>OverlapsExpectedFinger</c>), 그게 <c>cranialGrip</c>·<c>cranialPressure</c> 판정의 근거다.
/// <b>렌더러만 끄면 콜라이더는 그대로라 판정이 살아 있다.</b>
///
/// ★끈 상태가 유지되는지 실측(08-17): 런타임에 <c>targetRenderer.enabled</c>에 <b>쓰는 코드가 없다</b>
/// (디버그 출력에서 읽기만 한다). 렌더러를 되켜는 곳은 <c>SkeletonFocusController</c>와
/// <c>CranialHeadXray</c> 두 군데뿐인데 둘 다 <b>자기가 끈 것만</b> 되살리고
/// 이미 꺼져 있는 렌더러는 건너뛴다. 그러므로 여기서 꺼 두면 그대로 남는다.
///
/// ★비파괴 — 오브젝트를 만들거나 지우지 않는다(07-30에 진단 파지점 도구가 사용자 수작업 배치를
/// 날린 전례가 있다). 전부 Undo 되고, "전부 보이기"로 한 번에 되돌릴 수 있다.
/// </summary>
public class GripPointVisibilityTool : EditorWindow
{
    private class Entry
    {
        public GripPointTarget grip;
        public Renderer rend;
        public string rig;      // 리그(시나리오) 이름
        public string slot;     // 왼손 / 오른손 / 진단(오른손) / 진단 단계 등
        public string path;     // 리그 기준 경로
        public bool check;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, bool> foldout = new Dictionary<string, bool>();
    private string filter = "";
    private Vector2 scroll;
    private string status = "";

    [MenuItem("GuideChuna/파지점 표시 정리 (렌더러만 끄기)")]
    public static void Open()
    {
        var w = GetWindow<GripPointVisibilityTool>(true, "파지점 표시 정리");
        w.minSize = new Vector2(620, 640);
        w.Scan();
    }

    /// <summary>씬의 모든 파지점(비활성 포함). 파지점은 단계별로 SetActive(false)라 활성만 찾으면 대부분 놓친다.</summary>
    private static List<GripPointTarget> FindAllGrips()
    {
        return Resources.FindObjectsOfTypeAll<GripPointTarget>()
            .Where(g => g != null
                        && !EditorUtility.IsPersistent(g)
                        && g.gameObject.scene.IsValid()
                        && (g.hideFlags & HideFlags.HideAndDontSave) == 0)
            .ToList();
    }

    private void Scan()
    {
        // 어느 파지점이 컨트롤러의 어느 배열에 물려 있는지 먼저 훑는다.
        // ★왼손/오른손이 곧 주동수/보조수는 아니다 — 시나리오마다 다르므로 판단은 사용자 몫이다
        //   (흉추 신전은 회의록이 왼손인데 CSV가 hand=right인 미해결 건이 있다).
        var slotOf = new Dictionary<GripPointTarget, string>();
        foreach (var rig in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
        {
            if (rig == null || EditorUtility.IsPersistent(rig) || !rig.gameObject.scene.IsValid()) continue;
            var so = new SerializedObject(rig);
            MarkSlot(so, "leftGrips", "왼손", slotOf);
            MarkSlot(so, "rightGrips", "오른손", slotOf);
            MarkSlot(so, "diagnosisRightGrips", "진단(오른손)", slotOf);
        }

        entries.Clear();
        foreach (var g in FindAllGrips())
        {
            var so = new SerializedObject(g);
            SerializedProperty rp = so.FindProperty("targetRenderer");
            var rig = g.GetComponentInParent<CranialAdjustmentController>(true);

            entries.Add(new Entry
            {
                grip = g,
                rend = rp != null ? rp.objectReferenceValue as Renderer : null,
                rig = rig != null
                        ? (string.IsNullOrWhiteSpace(rig.ScenarioName) ? rig.gameObject.name : rig.ScenarioName)
                        : "(리그 밖)",
                slot = slotOf.TryGetValue(g, out string s) ? s : "진단 단계 등",
                path = PathUnder(g.transform, rig != null ? rig.transform : null)
            });
        }

        entries.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.rig, b.rig);
            return c != 0 ? c : string.CompareOrdinal(a.path, b.path);
        });

        int hidden = entries.Count(e => e.rend != null && !e.rend.enabled);
        int noRend = entries.Count(e => e.rend == null);
        status = $"파지점 {entries.Count}개 — 숨김 {hidden}개 / 표시 {entries.Count - noRend - hidden}개" +
                 (noRend > 0 ? $" / targetRenderer 미배선 {noRend}개(끌 수 없음)" : "");
    }

    private static void MarkSlot(SerializedObject so, string field, string label,
                                 Dictionary<GripPointTarget, string> map)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null || !p.isArray) return;
        for (int i = 0; i < p.arraySize; i++)
        {
            var g = p.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g == null) continue;
            if (map.TryGetValue(g, out string prev))
            {
                if (prev.IndexOf(label, System.StringComparison.Ordinal) < 0) map[g] = prev + "+" + label;
            }
            else map[g] = label;
        }
    }

    /// <summary>리그 기준 상대 경로. 리그를 못 찾으면 이름만.</summary>
    private static string PathUnder(Transform t, Transform root)
    {
        var parts = new List<string>();
        Transform cur = t;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "파지점의 표시만 끕니다 — 오브젝트도 콜라이더도 그대로라 판정은 살아 있습니다.\n" +
            "★오브젝트를 지우거나 SetActive로 끄면 안 됩니다. GripPointTarget이 자기 자신에게 붙은 " +
            "콜라이더를 접촉 소스로 쓰기 때문에 cranialGrip·cranialPressure 판정이 조용히 죽습니다.\n" +
            "전부 Undo 되고, 맨 아래 '전부 보이기'로 한 번에 되돌릴 수 있습니다.",
            MessageType.Info);

        EditorGUILayout.LabelField(status, EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("다시 스캔", GUILayout.Width(80))) Scan();
            EditorGUILayout.LabelField("이름 필터", GUILayout.Width(60));
            filter = EditorGUILayout.TextField(filter);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("체크 고르기", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("보이는 것 전부")) CheckIf(e => e.rend != null && e.rend.enabled);
            if (GUILayout.Button("왼손만")) CheckIf(e => e.slot.Contains("왼손"));
            if (GUILayout.Button("오른손만")) CheckIf(e => e.slot.Contains("오른손"));
            if (GUILayout.Button("진단만")) CheckIf(e => e.slot.Contains("진단"));
            if (GUILayout.Button("체크 해제")) CheckIf(e => false);
        }
        EditorGUILayout.LabelField(
            "★왼손/오른손이 곧 주동수/보조수는 아닙니다 — 시나리오마다 달라 판단은 직접 하셔야 합니다.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        string lastRig = null;
        bool open = true;
        foreach (Entry e in entries)
        {
            if (!string.IsNullOrEmpty(filter) &&
                e.path.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                e.rig.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (e.rig != lastRig)
            {
                lastRig = e.rig;
                if (!foldout.TryGetValue(e.rig, out open)) open = true;
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    open = EditorGUILayout.Foldout(open, $"■ {e.rig}", true);
                    foldout[e.rig] = open;
                    string rigName = e.rig;
                    if (GUILayout.Button("이 리그 전부 체크", GUILayout.Width(120)))
                        CheckIf(x => x.rig == rigName);
                }
            }
            if (!open) continue;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                e.check = EditorGUILayout.Toggle(e.check, GUILayout.Width(18));

                string state = e.rend == null ? "미배선" : (e.rend.enabled ? "표시" : "숨김");
                Color old = GUI.color;
                if (e.rend == null) GUI.color = new Color(1f, 0.6f, 0.6f);
                else if (!e.rend.enabled) GUI.color = new Color(0.6f, 0.6f, 0.6f);
                EditorGUILayout.LabelField(state, GUILayout.Width(42));
                GUI.color = old;

                EditorGUILayout.LabelField(e.slot, GUILayout.Width(96));

                bool req = e.grip == null || e.grip.IsRequired;
                if (!req) GUI.color = new Color(1f, 0.8f, 0.4f);
                EditorGUILayout.LabelField(req ? "필수" : "선택", GUILayout.Width(32));
                GUI.color = old;

                EditorGUILayout.LabelField(e.grip != null ? e.grip.Finger.ToString() : "", GUILayout.Width(52));
                if (GUILayout.Button(e.path, EditorStyles.miniButton))
                {
                    Selection.activeGameObject = e.grip.gameObject;
                    EditorGUIUtility.PingObject(e.grip.gameObject);
                }
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("필수 / 선택", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "'선택'으로 빼면 접촉·색 표시는 그대로인데 파지 성립을 막지 않습니다.\n" +
            "★호흡 게이트가 '모든 파지점'이라 트래킹이 불안한 손가락 하나가 튈 때마다 호흡 누적이 0으로 " +
            "초기화됩니다(새끼손가락이 대표적). 배선을 빼는 것과 다릅니다 — 보되 통과를 막지 않습니다.",
            EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(1f, 0.9f, 0.7f);
            if (GUILayout.Button("새끼손가락 전부 → 선택", GUILayout.Height(22)))
                SetRequired(e => e.grip != null && e.grip.Finger == CranialFinger.Pinky, false);
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("체크한 것 → 선택", GUILayout.Height(22)))
                SetRequired(e => e.check, false);
            if (GUILayout.Button("체크한 것 → 필수", GUILayout.Height(22)))
                SetRequired(e => e.check, true);
        }

        EditorGUILayout.Space();
        int checkedCount = entries.Count(e => e.check);
        GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
        if (GUILayout.Button($"체크한 {checkedCount}개 숨기기 (렌더러만 끔)", GUILayout.Height(28)))
            Apply(false, true);
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button($"체크한 {checkedCount}개 다시 보이기", GUILayout.Height(22)))
            Apply(true, true);

        EditorGUILayout.Space();
        if (GUILayout.Button("전부 보이기 — 되돌리기 (씬 전체)", GUILayout.Height(22)))
            Apply(true, false);

        DrawBraceSection();
        DrawXraySection();
    }

    // ── 환자 반투명(xray) 진하기 ────────────────────────────────────────
    //
    // ★2026-08-17 사용자 보고: "손이 환자 뒤쪽으로 넘어가면 잘 안 보인다".
    //   실측 결과 xray가 안 켜진 게 아니었다 — 흉추 CSV 두 개 모두 `xray` 토큰이 10번씩 들어 있고,
    //   옷도 forcedTransparentNameContains={"Shirt"}로 같이 투명해진다.
    //   반투명은 ZWrite를 끄므로 뒤의 손도 그려진다. 문제는 <b>환자 피부색이 alpha만큼 위에 얹혀
    //   손이 흐려지는 것</b>이다. 그래서 알파를 낮추는 게 곧 손 가독성이다.
    // ★alpha는 기존 직렬화 필드라 코드 기본값이 씬 인스턴스에 안 먹는다 → 여기서 밀어 넣는다.

    private float xrayAlpha = 0.2f;

    private void DrawXraySection()
    {
        var xrays = Resources.FindObjectsOfTypeAll<CranialHeadXray>()
            .Where(x => x != null && !EditorUtility.IsPersistent(x) && x.gameObject.scene.IsValid())
            .ToList();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("■ 환자 반투명(xray) 진하기", EditorStyles.boldLabel);

        if (xrays.Count == 0)
        {
            EditorGUILayout.LabelField("   씬에 CranialHeadXray가 없습니다.", EditorStyles.miniLabel);
            return;
        }

        var cur = new List<string>();
        foreach (var x in xrays)
        {
            SerializedProperty p = new SerializedObject(x).FindProperty("alpha");
            cur.Add(p != null ? p.floatValue.ToString("0.00") : "?");
        }

        EditorGUILayout.LabelField($"   {xrays.Count}개 — 현재 alpha {string.Join(", ", cur)}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            "낮출수록 환자가 투명해져 뒤에 있는 손이 또렷해집니다(0=완전투명 / 1=불투명).\n" +
            "★alpha는 씬에 저장돼 있어 코드 기본값이 안 먹습니다 — 이 버튼으로만 바뀝니다.",
            EditorStyles.wordWrappedMiniLabel);

        xrayAlpha = EditorGUILayout.Slider("alpha", xrayAlpha, 0f, 1f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("0.20 (권장)", GUILayout.Width(90))) xrayAlpha = 0.2f;
            if (GUILayout.Button("0.35 (기존)", GUILayout.Width(90))) xrayAlpha = 0.35f;
        }

        GUI.backgroundColor = new Color(0.75f, 1f, 0.8f);
        if (GUILayout.Button($"씬에 alpha {xrayAlpha:0.00} 적용", GUILayout.Height(24)))
            SetXrayAlpha(xrays, xrayAlpha);
        GUI.backgroundColor = Color.white;
    }

    private void SetXrayAlpha(List<CranialHeadXray> xrays, float value)
    {
        int n = 0;
        foreach (var x in xrays)
        {
            var so = new SerializedObject(x);
            SerializedProperty p = so.FindProperty("alpha");
            if (p == null || Mathf.Approximately(p.floatValue, value)) continue;
            p.floatValue = value;
            if (so.ApplyModifiedProperties()) n++;
        }

        if (n > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"xray alpha {value:0.00} 적용 — {n}개" +
                 "\n★Ctrl+S로 씬을 저장해야 유지됩니다. Play로 들어가 손이 또렷해졌는지 보세요.";
        Debug.Log("[xray 진하기] " + status);
    }

    // ── 이마 견착 마커 ──────────────────────────────────────────────────
    //
    // ★파지점과 사정이 다르다. ShoulderBraceGuide.SetShown()이 견착 국면마다
    //   markerRenderer.enabled = on 으로 <b>직접 켜기 때문에</b> 씬에서 렌더러를 꺼 봐야 되켜진다.
    //   그래서 컴포넌트에 hideMarker 스위치를 두고 그것을 켠다.
    // ★견착 동작을 없애는 게 아니라 표시만 지우는 것이다(2026-08-13 회의).
    //   이 가이드는 표시 전용이고 판정에 관여하지 않는다.

    private void DrawBraceSection()
    {
        var braces = Resources.FindObjectsOfTypeAll<ShoulderBraceGuide>()
            .Where(b => b != null && !EditorUtility.IsPersistent(b) && b.gameObject.scene.IsValid())
            .ToList();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("■ 이마 견착 마커", EditorStyles.boldLabel);

        if (braces.Count == 0)
        {
            EditorGUILayout.LabelField("   씬에 ShoulderBraceGuide가 없습니다.", EditorStyles.miniLabel);
            return;
        }

        int hidden = braces.Count(b =>
        {
            SerializedProperty p = new SerializedObject(b).FindProperty("hideMarker");
            return p != null && p.boolValue;
        });
        EditorGUILayout.LabelField($"   {braces.Count}개 — 표시 끔 {hidden}개 / 켬 {braces.Count - hidden}개",
                                   EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            "이마에 밀착하면 HMD 시야를 가려서 마커를 끕니다. 견착 동작·CSV 흐름은 그대로입니다.\n" +
            "★씬에서 렌더러를 직접 꺼도 소용없습니다 — 견착 국면마다 코드가 다시 켭니다.",
            EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
            if (GUILayout.Button($"견착 마커 {braces.Count}개 표시 끄기", GUILayout.Height(24)))
                SetBraceHidden(braces, true);
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("다시 켜기", GUILayout.Height(24), GUILayout.Width(90)))
                SetBraceHidden(braces, false);
        }

        // ── 견착 위치·임계 ──────────────────────────────────────────────
        EditorGUILayout.Space(4);
        var stabs = Resources.FindObjectsOfTypeAll<CranialPostureStabilizer>()
            .Where(s => s != null && !EditorUtility.IsPersistent(s) && s.gameObject.scene.IsValid())
            .ToList();

        EditorGUILayout.LabelField($"견착 위치·임계 (자세 안정기 {stabs.Count}개)", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField(
            "마커 높이 = 이마 기준점에서 위로 얼마나 띄울지. 올릴수록 마커가 이마 위쪽에 붙습니다.\n" +
            "견착 거리 = 헤드셋과 이마 기준점이 이 거리 안으로 들어와야 '견착'으로 인정합니다. " +
            "★줄일수록 더 숙여야 걸립니다. 풀림 거리는 자동으로 +8cm(히스테리시스, 임계 근처 깜빡임 방지).",
            EditorStyles.wordWrappedMiniLabel);

        braceOffsetY = EditorGUILayout.Slider("마커 높이 (m)", braceOffsetY, 0f, 0.35f);
        engageCm = EditorGUILayout.Slider("견착 거리 (cm)", engageCm, 8f, 40f);

        if (GUILayout.Button($"적용 — 마커 높이 {braceOffsetY:0.000}m / 견착 {engageCm:0}cm", GUILayout.Height(22)))
            ApplyBracePose(braces, stabs);
    }

    private float braceOffsetY = 0.20f;   // 씬 현재값 0.154 → 조금 올린 제안치
    private float engageCm = 24f;         // 씬 현재값 30cm → 더 숙여야 걸리게

    /// <summary>견착 마커 높이와 자세 안정기 임계를 씬 인스턴스에 밀어 넣는다(둘 다 직렬화 필드라 코드 기본값이 안 먹는다).</summary>
    private void ApplyBracePose(List<ShoulderBraceGuide> braces, List<CranialPostureStabilizer> stabs)
    {
        int nb = 0, ns = 0;

        foreach (var b in braces)
        {
            var so = new SerializedObject(b);
            SerializedProperty p = so.FindProperty("localOffset");
            if (p == null) continue;
            Vector3 v = p.vector3Value;
            if (Mathf.Approximately(v.y, braceOffsetY)) continue;
            v.y = braceOffsetY;
            p.vector3Value = v;
            if (so.ApplyModifiedProperties()) nb++;
        }

        float engage = engageCm * 0.01f;
        foreach (var s in stabs)
        {
            var so = new SerializedObject(s);
            SerializedProperty e = so.FindProperty("engageDistance");
            SerializedProperty r = so.FindProperty("releaseDistance");
            if (e == null) continue;
            e.floatValue = engage;
            // ★release는 engage보다 커야 한다 — 같거나 작으면 임계에서 계속 깜빡인다.
            if (r != null) r.floatValue = engage + 0.08f;
            if (so.ApplyModifiedProperties()) ns++;
        }

        if (nb > 0 || ns > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"견착 마커 높이 {braceOffsetY:0.000}m — {nb}개 / 견착 거리 {engageCm:0}cm(풀림 {engageCm + 8:0}cm) — {ns}개" +
                 "\n★Ctrl+S로 씬을 저장해야 유지됩니다. 값이 맞는지는 Play에서 실제로 숙여보며 맞추세요.";
        Debug.Log("[견착 위치·임계] " + status);
    }

    private void SetBraceHidden(List<ShoulderBraceGuide> braces, bool hide)
    {
        int n = 0;
        foreach (var b in braces)
        {
            var so = new SerializedObject(b);
            SerializedProperty p = so.FindProperty("hideMarker");
            if (p == null || p.boolValue == hide) continue;
            p.boolValue = hide;
            if (so.ApplyModifiedProperties()) n++;   // Undo 등록 + 더티 표시까지 처리한다
        }

        if (n > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"이마 견착 마커 {n}개 {(hide ? "표시 끔" : "다시 켬")}" +
                 "\n★Ctrl+S로 씬을 저장해야 유지됩니다. 견착 동작 자체는 그대로입니다.";
        Debug.Log("[견착 마커] " + status);
    }

    private void CheckIf(System.Func<Entry, bool> pred)
    {
        foreach (Entry e in entries) e.check = pred(e);
    }

    /// <summary>파지점의 필수/선택을 바꾼다. 렌더러는 건드리지 않는다.</summary>
    private void SetRequired(System.Func<Entry, bool> pred, bool required)
    {
        int n = 0;
        var log = new StringBuilder();
        foreach (Entry e in entries)
        {
            if (e.grip == null || !pred(e)) continue;
            var so = new SerializedObject(e.grip);
            SerializedProperty p = so.FindProperty("required");
            if (p == null || p.boolValue == required) continue;
            p.boolValue = required;
            if (so.ApplyModifiedProperties())
            {
                n++;
                if (log.Length < 3000) log.AppendLine($"  · [{e.rig}] {e.path}");
            }
        }

        if (n > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"파지점 {n}개를 '{(required ? "필수" : "선택")}'으로 바꿨습니다.\n" + log +
                 "★Ctrl+S로 씬을 저장해야 유지됩니다. 접촉·색 표시는 그대로이고 성립 판정에서만 빠집니다.";
        Debug.Log("[파지점 필수/선택] " + status);
        Scan();
    }

    /// <summary>렌더러 enabled만 바꾼다. <paramref name="onlyChecked"/>가 false면 씬 전체가 대상.</summary>
    private void Apply(bool visible, bool onlyChecked)
    {
        var log = new StringBuilder();
        int changed = 0, noRend = 0;

        foreach (Entry e in entries)
        {
            if (onlyChecked && !e.check) continue;
            if (e.rend == null) { noRend++; continue; }
            if (e.rend.enabled == visible) continue;

            Undo.RecordObject(e.rend, visible ? "파지점 표시" : "파지점 표시 끄기");
            e.rend.enabled = visible;
            EditorUtility.SetDirty(e.rend);
            changed++;
            if (log.Length < 4000) log.AppendLine($"  · [{e.rig}] {e.path}");
        }

        if (changed > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"{(visible ? "표시" : "숨김")} {changed}개 적용" +
                 (noRend > 0 ? $" / targetRenderer 미배선 {noRend}개는 건너뜀" : "") +
                 "\n★Ctrl+S로 씬을 저장해야 유지됩니다. 콜라이더는 건드리지 않았으므로 판정은 그대로입니다.";
        Debug.Log("[파지점 표시 정리] " + status + "\n" + log);
        Scan();
    }
}
