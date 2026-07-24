using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 골격이 환자를 따라가도록 SkeletonRigFollower(본 페어)를 구성하는 셋업/분석 도구.
///
/// 이 골격은 표준 Humanoid가 아니라 머리·목이 커스텀 본 체인(Bip001 Neck1~4 등)으로 구동되고,
/// 메시 대부분이 본에 붙은 리지드 MeshRenderer라 "본"만 구동하면 메시가 자동으로 따라온다.
/// 분석은 Unity 런타임에서 GetComponentsInChildren로 수행하므로 프리팹 인스턴스여도 전부 읽힌다.
///
/// 흐름: 자동탐색 → [골격 구조 분석](콘솔) → [머리·목 자동 페어](4분절 배분) → [오프셋 캡처].
/// </summary>
public class SkeletonRebindTool : EditorWindow
{
    private Transform patientRoot;    // 환자 c9
    private Transform skeletonRoot;   // 근육골격
    private bool runInEditMode = true;
    private bool includeNeckCurve = false;   // 목 분절 배분(머리 위치 흔들림 위험 → 기본 OFF)
    private Vector2 scroll;
    private string report = "";

    [MenuItem("GuideChuna/골격 리바인딩 (환자 리그에 이식)")]
    public static void Open()
    {
        var w = GetWindow<SkeletonRebindTool>("골격 리바인딩");
        w.minSize = new Vector2(520, 460);
        w.AutoFind();
    }

    private void AutoFind()
    {
        if (patientRoot == null)
        {
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (a == null) continue;
                if (a.name.Contains("근육골격") || a.name.Contains("Anatomy")) continue;
                if (a.name.StartsWith("c9")) { patientRoot = a.transform; break; }
                if (patientRoot == null && a.isHuman) patientRoot = a.transform;
            }
        }
        if (skeletonRoot == null)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                if ((t.name.Contains("근육골격") || t.name.Contains("Anatomy"))
                    && t.GetComponentInChildren<Renderer>(true) != null)
                { skeletonRoot = t; break; }
            }
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "골격 본 → 환자 본 페어로 회전을 복사합니다(위치 안 건드려 폭발 없음).\n" +
            "메시는 본에 붙어 자동으로 따라오므로 렌더러를 하나하나 배선할 필요가 없습니다.",
            MessageType.Info);

        patientRoot = (Transform)EditorGUILayout.ObjectField("환자 루트 (c9)", patientRoot, typeof(Transform), true);
        skeletonRoot = (Transform)EditorGUILayout.ObjectField("골격 루트 (근육골격)", skeletonRoot, typeof(Transform), true);
        if (GUILayout.Button("씬에서 자동 탐색")) { patientRoot = null; skeletonRoot = null; AutoFind(); }

        var follower = skeletonRoot != null ? skeletonRoot.GetComponent<SkeletonRigFollower>() : null;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(skeletonRoot == null))
        {
            if (GUILayout.Button("① 골격 구조 분석 (본·메시·목체인 → 콘솔 & 리포트)", GUILayout.Height(26)))
                report = Analyze();
        }

        EditorGUILayout.Space();
        runInEditMode = EditorGUILayout.ToggleLeft("에디트 모드에서도 실시간 추종(씬뷰 미리보기)", runInEditMode);
        includeNeckCurve = EditorGUILayout.ToggleLeft(
            "목 분절도 배분(곡선, 실험적 — 머리 위치가 흔들릴 수 있음)", includeNeckCurve);

        using (new EditorGUI.DisabledScope(patientRoot == null || skeletonRoot == null))
        {
            var bg2 = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 0.85f, 1f);
            if (GUILayout.Button("②-A ★휴머노이드 포즈 추종 설정 (권장: 머리→머리·몸통→몸통)", GUILayout.Height(28)))
                report = SetupHumanoidPose();
            GUI.backgroundColor = bg2;
            if (GUILayout.Button("②-B 머리 자동 페어(본별 회전, 스키닝 공유로 몸통 섞일 수 있음)", GUILayout.Height(22)))
                report = AutoPairNeck();
        }

        using (new EditorGUI.DisabledScope(follower == null || !follower.HasPairs))
        {
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("③ 현재 포즈에서 오프셋 캡처 (추종 시작)", GUILayout.Height(30)))
                report = Capture(follower);
            GUI.backgroundColor = bg;
            if (GUILayout.Button("추종 멈춤 (오프셋 지움)")) report = ClearOffsets(follower);
        }

        if (follower != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"SkeletonRigFollower 부착됨 — 페어 {follower.pairs.Count}개.\n" +
                "페어/weight 수정은 골격 인스펙터에서 직접 편집 후 ③으로 다시 캡처.",
                MessageType.None);
            if (GUILayout.Button("④ 진단: 페어 상태 + 환자 실시간 회전")) report = DiagnosePairs(follower);
            if (GUILayout.Button("⑤ 진단: 머리 후보본이 어떤 메시를 움직이나")) report = DiagnoseHeadBones();
            if (GUILayout.Button("★ 추종 해제 (컴포넌트 제거)")) report = Detach(follower);
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>머리 후보 본 각각이 "어떤 SMR(메시)을 움직이는지" 덤프.
    /// 두개골만 쓰는 본(=torso 안 건드리는 본)을 찾기 위함. torso/근육 메시가 섞여 있으면 그 본은 몸통도 움직인다.</summary>
    private string DiagnoseHeadBones()
    {
        if (skeletonRoot == null) return "골격 루트를 지정하세요.";
        var smrs = skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // 후보: 머리 체인 본들
        var skinned = CollectSkinnedBones();
        Transform sHead = FindRiggedHead(skinned);
        var chain = new List<Transform>();
        if (sHead != null)
            for (Transform c = sHead; c != null; c = c.parent)
            {
                if (c == skeletonRoot) break;
                chain.Add(c);
                if (chain.Count >= 12) break;
            }

        var sb = new StringBuilder();
        sb.AppendLine($"머리 체인 본별 → 이 본을 참조하는 메시(SMR) 목록 (torso/근육 섞이면 몸통도 움직임)");
        sb.AppendLine("★두개골 전용 본 = 메시 목록이 머리/두개골류 뿐인 본. 그 본을 주구동으로 써야 몸통 안 움직임.\n");

        foreach (var bone in chain)
        {
            var users = new List<string>();
            foreach (var smr in smrs)
            {
                if (smr == null || smr.bones == null) continue;
                foreach (var b in smr.bones)
                    if (b == bone) { users.Add(smr.name); break; }
            }
            string tag = users.Count == 0 ? "스킨X(더미)" : $"{users.Count}개 메시";
            sb.AppendLine($"● {bone.name}  [{tag}]");
            // 메시 이름 최대 10개
            for (int i = 0; i < users.Count && i < 10; i++) sb.AppendLine($"     - {users[i]}");
            if (users.Count > 10) sb.AppendLine($"     ... 외 {users.Count - 10}개");
        }
        Debug.Log("[머리본 진단]\n" + sb);
        return sb.ToString();
    }

    /// <summary>각 페어의 캡처 여부·연결·환자 본이 캡처 이후 실제 얼마나 회전했는지 진단.</summary>
    private string DiagnosePairs(SkeletonRigFollower follower)
    {
        var skinned = skeletonRoot != null ? CollectSkinnedBones() : new HashSet<Transform>();
        var sb = new StringBuilder();
        sb.AppendLine($"페어 {follower.pairs.Count}개 / runInEditMode={follower.runInEditMode} / Play중={Application.isPlaying}");
        sb.AppendLine("(환자 본을 지금 손으로 돌려보거나 Play로 애니 재생하면 '현재Δ'가 커져야 정상)");
        sb.AppendLine();
        int captured = 0, moving = 0, notSkinned = 0;
        foreach (var p in follower.pairs)
        {
            if (p == null) continue;
            string pb = p.patientBone != null ? p.patientBone.name : "∅";
            string sk = p.skeletonBone != null ? p.skeletonBone.name : "∅";
            float delta = (p.captured && p.patientBone != null)
                ? Quaternion.Angle(p.patientRest, p.patientBone.rotation) : -1f;
            bool isSkinned = p.skeletonBone != null && skinned.Contains(p.skeletonBone);
            if (p.captured) captured++;
            if (delta > 0.5f) moving++;
            if (p.skeletonBone != null && !isSkinned) notSkinned++;
            string capMark = p.captured ? "O" : "X(③안함)";
            string dMark = delta < 0 ? "-" : delta.ToString("0.0") + "도";
            string skMark = isSkinned ? "스킨O" : "스킨X";
            sb.AppendLine($"  {pb} -> {sk}  w={p.weight:0.00}  캡처={capMark}  현재Δ={dMark}  {skMark}");
        }
        sb.AppendLine();
        sb.AppendLine($"요약: 캡처됨 {captured}/{follower.pairs.Count}, 환자 회전중 {moving}, 비스킨본 {notSkinned}");
        if (captured == 0) sb.AppendLine("→ ③ [오프셋 캡처]를 안 했습니다. ③ 누르세요.");
        if (notSkinned > 0) sb.AppendLine("→ 비스킨본은 화면 메시를 안 움직입니다(더미). ②를 다시 눌러 스킨본으로 재구성.");
        if (captured > 0 && moving == 0) sb.AppendLine("→ 캡처는 됐는데 환자 본이 안 움직임: 환자 애니가 CC_Base_Head를 실제로 돌리는지 확인.");
        Debug.Log("[골격 진단]\n" + sb);
        return sb.ToString();
    }

    // === 분석 ===

    private static Transform FindDescendant(Transform root, params string[] names)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var name in names)
            foreach (var t in all)
                if (t.name == name) return t;
        return null;
    }

    /// <summary>골격 하위 본 계층 + 렌더러 보유 본 + 목/머리 체인을 콘솔과 리포트로 출력.</summary>
    private string Analyze()
    {
        var all = skeletonRoot.GetComponentsInChildren<Transform>(true);
        int rendBones = 0, smr = 0, mesh = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"골격 루트: {skeletonRoot.name}   총 Transform {all.Length}개");

        // 렌더러 통계
        foreach (var t in all)
        {
            bool hasSmr = t.GetComponent<SkinnedMeshRenderer>() != null;
            bool hasMesh = t.GetComponent<MeshRenderer>() != null;
            if (hasSmr) smr++;
            if (hasMesh) mesh++;
            if (hasSmr || hasMesh) rendBones++;
        }
        sb.AppendLine($"렌더러: SkinnedMesh {smr}개(근육류) / MeshRenderer {mesh}개(리지드 뼈메시)");
        sb.AppendLine();

        // 머리/목 체인: 머리 본에서 위로
        Transform head = FindDescendant(skeletonRoot, "Bip001 Head", "bip_head", "skull");
        Transform neck = FindDescendant(skeletonRoot, "Bip001 Neck", "Bip001 Neck4", "Bip001 Neck1");
        sb.AppendLine($"[머리 본] {(head != null ? head.name : "못 찾음")}");
        sb.AppendLine($"[목 본]  {(neck != null ? neck.name : "못 찾음")}");
        sb.AppendLine();

        if (head != null)
        {
            sb.AppendLine("=== 머리에서 위로(부모 체인) — 이 사이 본들이 목·경추 분절 ===");
            var chain = new List<Transform>();
            for (Transform c = head; c != null && c != skeletonRoot.parent; c = c.parent)
            {
                chain.Add(c);
                if (chain.Count > 25) break;
            }
            foreach (var c in chain)
            {
                string r = "";
                if (c.GetComponent<MeshRenderer>() != null) r += " [Mesh]";
                if (c.GetComponent<SkinnedMeshRenderer>() != null) r += " [SMR]";
                sb.AppendLine($"   {c.name}{r}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 경추/목 후보 본(이름에 Neck 포함) ===");
        foreach (var t in all)
            if (t.name.Contains("Neck"))
                sb.AppendLine($"   {t.name}  (자식 {t.childCount})");

        Debug.Log("[골격 구조 분석]\n" + sb);
        return sb.ToString();
    }

    // === 자동 페어(목 4분절 배분) ===

    /// <summary>골격 하위 모든 SkinnedMeshRenderer가 실제로 참조하는 본 집합.
    /// 이 집합에 든 본만 "화면 메시를 실제로 움직이는" 진짜 본이다(중복 더미 배제).</summary>
    private HashSet<Transform> CollectSkinnedBones()
    {
        var set = new HashSet<Transform>();
        foreach (var smr in skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null) continue;
            var bones = smr.bones;
            if (bones == null) continue;
            foreach (var b in bones) if (b != null) set.Add(b);
        }
        return set;
    }

    /// <summary>진짜 머리 본을 찾는다. ★스킨 메시가 참조하는 본(=화면을 움직이는 본) 우선,
    /// 그 중 부모가 bip_head 이거나 조상에 SPINE_add 가 있는(=리깅된) 것.</summary>
    private Transform FindRiggedHead(HashSet<Transform> skinned)
    {
        var all = skeletonRoot.GetComponentsInChildren<Transform>(true);
        Transform skinnedFallback = null, anyFallback = null;
        foreach (var t in all)
        {
            if (t.name != "Bip001 Head" && t.name != "bip_head") continue;
            bool isSkinned = skinned.Contains(t);
            if (anyFallback == null) anyFallback = t;
            if (isSkinned && skinnedFallback == null) skinnedFallback = t;

            bool rigged = (t.parent != null && t.parent.name == "bip_head");
            if (!rigged)
                for (Transform c = t.parent; c != null; c = c.parent)
                    if (c.name.StartsWith("SPINE_add")) { rigged = true; break; }

            if (rigged && isSkinned) return t;            // 최선: 리깅됨 + 스킨참조
        }
        return skinnedFallback != null ? skinnedFallback : anyFallback;
    }

    /// <summary>골격이 Humanoid일 때: 환자 휴머노이드 포즈를 골격에 리타깃(머리→머리, 몸통→몸통).</summary>
    private string SetupHumanoidPose()
    {
        // 환자 Animator(Humanoid)
        Animator patientAnim = patientRoot.GetComponent<Animator>();
        if (patientAnim == null) patientAnim = patientRoot.GetComponentInParent<Animator>();
        if (patientAnim == null) patientAnim = patientRoot.GetComponentInChildren<Animator>(true);
        if (patientAnim == null || !patientAnim.isHuman)
            return "환자 Humanoid Animator를 못 찾았습니다(patientRoot에 Humanoid Animator 필요).";

        // 골격 Avatar
        Animator skelAnim = skeletonRoot.GetComponent<Animator>();
        if (skelAnim == null) skelAnim = skeletonRoot.GetComponentInChildren<Animator>(true);
        Avatar skelAvatar = skelAnim != null ? skelAnim.avatar : null;
        if (skelAvatar == null)
        {
            // 프리팹 소스(FBX)에서 Avatar 서브에셋
            Object src = PrefabUtility.GetCorrespondingObjectFromSource(skeletonRoot.gameObject);
            string path = src != null ? AssetDatabase.GetAssetPath(src) : null;
            if (!string.IsNullOrEmpty(path))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Avatar av && av.isValid && av.isHuman) { skelAvatar = av; break; }
        }
        if (skelAvatar == null || !skelAvatar.isValid || !skelAvatar.isHuman)
            return "골격 Humanoid Avatar를 못 찾았습니다. 골격에 Animator+Humanoid Avatar를 올리세요.";

        // Follower를 골격 루트에 부착·설정. Avatar가 매핑하는 루트 = skelAnim.transform(있으면), 아니면 skeletonRoot.
        Transform avatarRoot = skelAnim != null ? skelAnim.transform : skeletonRoot;
        var follower = avatarRoot.GetComponent<SkeletonRigFollower>();
        if (follower == null) follower = Undo.AddComponent<SkeletonRigFollower>(avatarRoot.gameObject);
        Undo.RecordObject(follower, "휴머노이드 포즈 추종 설정");
        follower.useHumanoidPose = true;
        follower.patientAnimator = patientAnim;
        follower.skeletonAvatar = skelAvatar;
        follower.runInEditMode = runInEditMode;
        follower.debugLog = true;
        if (follower.pairs != null) follower.pairs.Clear();

        // 골격 자체 Animator 비활성(자체 클립 간섭 방지)
        if (skelAnim != null && skelAnim.enabled)
        {
            Undo.RecordObject(skelAnim, "골격 Animator 비활성");
            skelAnim.enabled = false;
        }

        EditorUtility.SetDirty(follower);
        MarkDirty();

        return "✔ 휴머노이드 포즈 추종 설정 완료.\n" +
               $"  환자: {patientAnim.name} (Humanoid)\n" +
               $"  골격 Avatar: {skelAvatar.name}\n" +
               $"  Follower 부착: {avatarRoot.name}\n\n" +
               "확인(★Play 모드에서):\n" +
               "  · 환자가 머리만 움직이면 골격도 머리만, 몸통은 그대로여야 정상\n" +
               "  · 골격 전체가 환자 몸에 겹쳐 자세를 따라감(휴머노이드 매핑)\n" +
               "  · 안 움직이면 콘솔 경고 확인(Avatar 매핑/Humanoid 여부)\n" +
               "  · 골격이 엉뚱한 위치로 가면 골격 루트 localPosition/Rotation을 0으로";
    }

    private string AutoPairNeck()
    {
        var follower = skeletonRoot.GetComponent<SkeletonRigFollower>();
        if (follower == null) follower = Undo.AddComponent<SkeletonRigFollower>(skeletonRoot.gameObject);
        Undo.RecordObject(follower, "머리·목 자동 페어");
        follower.runInEditMode = runInEditMode;
        if (follower.pairs == null) follower.pairs = new List<SkeletonRigFollower.BonePair>();
        follower.pairs.Clear();   // 기존 잘못된 페어(Bip001 Neck1~4 등) 정리 후 다시 구성
        follower.debugLog = true; // 회전이 실제로 들어가는지 콘솔로 확인

        // ★골격 자체 Animator가 켜져 있으면 자체 클립이 매 프레임 본을 덮어써 추종을 무효화한다 → 끈다.
        var ownAnim = skeletonRoot.GetComponent<Animator>();
        if (ownAnim == null) ownAnim = skeletonRoot.GetComponentInChildren<Animator>(true);
        if (ownAnim != null && ownAnim.enabled)
        {
            Undo.RecordObject(ownAnim, "골격 Animator 비활성");
            ownAnim.enabled = false;
        }

        Transform pHead = FindDescendant(patientRoot, "CC_Base_Head");
        if (pHead == null) return "환자 CC_Base_Head를 못 찾았습니다.";

        var skinned = CollectSkinnedBones();   // 화면 메시를 실제로 움직이는 본 집합

        Transform sHead = FindRiggedHead(skinned);
        if (sHead == null) return "골격 머리 본(Bip001 Head/bip_head)을 못 찾았습니다.";

        // ★핵심: bip_head/Bip001 Head는 스킨 안 된 더미라 돌려도 두개골이 안 움직인다.
        //   머리에서 위로 올라가며 "스킨된 가장 위 본"을 찾는다 = 두개골을 실제로 움직이는 본(=Bip001 Neck4).
        //   전체 머리 체인(SPINE_add 전까지)도 함께 수집해 목 분절 후보로 쓴다.
        var chain = new List<Transform>();          // sHead → ... → (SPINE_add 직전)
        for (Transform c = sHead; c != null; c = c.parent)
        {
            if (c == skeletonRoot) break;
            if (c.name.StartsWith("SPINE_add")) break;
            chain.Add(c);
            if (chain.Count >= 12) break;
        }

        Transform headDriver = null;
        foreach (var b in chain) if (skinned.Contains(b)) { headDriver = b; break; }  // 스킨된 가장 위 본
        if (headDriver == null) headDriver = sHead;   // 폴백

        // 목 분절 = headDriver 아래쪽(부모 방향)의 스킨된 본들만
        var neckChain = new List<Transform>();
        bool passedDriver = false;
        foreach (var b in chain)
        {
            if (b == headDriver) { passedDriver = true; continue; }
            if (passedDriver && skinned.Contains(b)) neckChain.Add(b);
        }

        var sb = new StringBuilder();
        int added = 0;

        // 머리: 스킨된 주 구동 본에 환자 머리 회전 100% 적용 → 두개골이 실제로 회전.
        AddPair(follower, pHead, headDriver, 1f);
        sb.AppendLine($"  [머리·주구동] CC_Base_Head → {headDriver.name}  weight 1.00  " +
                      $"{(skinned.Contains(headDriver) ? "스킨O(움직임)" : "스킨X(경고)")}");
        added++;

        // 목 분절(선택): 아래쪽 본을 회전시키면 머리 '위치'가 휘둘릴 수 있어 기본 OFF, 켜도 작은 weight.
        int n = includeNeckCurve ? neckChain.Count : 0;
        for (int i = 0; i < n; i++)
        {
            // 0.15 → 0.03 선형 감쇠(작게 유지)
            float w = n > 1 ? Mathf.Lerp(0.15f, 0.03f, (float)i / (n - 1)) : 0.1f;
            w = Mathf.Round(w * 100f) / 100f;
            AddPair(follower, pHead, neckChain[i], w);
            sb.AppendLine($"  [목{i + 1}] CC_Base_Head → {neckChain[i].name}  weight {w:0.00}");
            added++;
        }
        if (!includeNeckCurve && neckChain.Count > 0)
            sb.AppendLine($"  (목 분절 {neckChain.Count}개는 배분 안 함 — 날아감 방지. 필요하면 위 토글 ON 후 재실행)");

        // 컬링 대응
        foreach (var r in skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Undo.RecordObject(r, "updateWhenOffscreen");
            r.updateWhenOffscreen = true;
        }

        EditorUtility.SetDirty(follower);
        MarkDirty();

        var head = new StringBuilder();
        head.AppendLine($"페어 {added}개 구성(주구동 1{(n > 0 ? $" + 목 분절 {n}" : "")}).");
        head.Append(sb);
        head.AppendLine();
        head.AppendLine("★주 구동 본이 '스킨O'여야 두개골이 실제로 움직입니다(이전엔 bip_head=스킨X라 안 움직였음).");
        head.AppendLine("다음: ③ [오프셋 캡처] → Play로 확인. Play 중 ④ 진단 시 현재Δ가 커지면 추종 정상.");
        head.AppendLine("머리만으로 목도 꺾이길 원하면 '목 분절도 배분' 토글 ON 후 ② 다시.");
        return head.ToString();
    }

    private static bool AddPair(SkeletonRigFollower f, Transform patient, Transform skel, float weight)
    {
        foreach (var ex in f.pairs)
            if (ex != null && ex.skeletonBone == skel) { ex.patientBone = patient; ex.weight = weight; return true; }
        f.pairs.Add(new SkeletonRigFollower.BonePair { patientBone = patient, skeletonBone = skel, weight = weight });
        return true;
    }

    private string Capture(SkeletonRigFollower follower)
    {
        Undo.RecordObject(follower, "오프셋 캡처");
        follower.CaptureOffsets();
        follower.Apply();
        EditorUtility.SetDirty(follower);
        MarkDirty();
        int ok = 0;
        foreach (var p in follower.pairs) if (p != null && p.captured) ok++;
        return $"✔ 오프셋 캡처 {ok}/{follower.pairs.Count}개.\n\n" +
               "확인:\n" +
               "  · 환자 CC_Base_Head를 씬뷰에서 돌리면 골격 머리+경추가 곡선으로 따라오는지\n" +
               "  · Play로 클립 재생 시 골격 고개가 따라가는지\n" +
               "  · 방향 반대면: 캡처 시점 두 모델이 같은 자세였는지 확인 후 재캡처\n" +
               "  · 목이 너무 뻣뻣/과하면: 인스펙터 weight 조절 후 재캡처";
    }

    private string ClearOffsets(SkeletonRigFollower follower)
    {
        Undo.RecordObject(follower, "추종 멈춤");
        follower.ClearOffsets();
        EditorUtility.SetDirty(follower);
        MarkDirty();
        return "추종 멈춤 — 오프셋을 지웠습니다. ③으로 다시 시작.";
    }

    private string Detach(SkeletonRigFollower follower)
    {
        Undo.DestroyObjectImmediate(follower);
        MarkDirty();
        return "추종 해제 완료 — SkeletonRigFollower 제거.";
    }

    private void MarkDirty()
    {
        if (!Application.isPlaying && skeletonRoot != null)
            EditorSceneManager.MarkSceneDirty(skeletonRoot.gameObject.scene);
    }
}
