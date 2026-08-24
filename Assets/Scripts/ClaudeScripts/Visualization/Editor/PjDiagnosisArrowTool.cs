using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 두개골 PJ 교정의 <b>진단(평가)·재평가</b> 화살표를 <b>좌·우 자세별로</b> 배선한다.
/// 겸해서 교정 단계의 오른손 화살표가 어느 그룹에도 안 묶여 있던 것을 바로잡고, 역할 색을 넣는다.
///
/// ★진단은 한 substep 안에 자세가 둘이다 (2026-08-18 실측)
///   PJ 진단1 스테이지 = ⓐ 왼손 후두 + 오른손 측두 / ⓑ 왼손 측두 + 오른손 후두.
///   두 자세는 <b>파지점이 서로 다른 좌우 반전 형태</b>다 — 측두 파지점의 리그 로컬 x가
///   ⓐ −0.064 / ⓑ +0.064로 정확히 반대편이다. 그래서 한쪽 화살표를 양쪽에 돌려쓸 수 없다.
///   화살표 그룹은 (시나리오·국면·단계·subStep)으로만 매칭돼 이 전환을 가르지 못했으므로,
///   ForceArrowGroup에 <c>poseNo</c>를 두고 CranialAdjustmentController가 자세가 바뀔 때
///   ForceArrowDirector.SetPose를 부르게 했다(SkeletonFocusController.SetPose와 같은 경로).
///   파지점이 ShowOnlyCurrentPoseGrips로 '현재 자세만' 보여 주는 규약과 짝이 맞는다.
///
/// ★재평가는 같은 스테이지를 다시 쓴다
///   CSV 재평가 8-1도 <c>cranialTouch 진단1;hold=3</c>이라 자세 객체가 동일하다 →
///   재평가 그룹은 진단과 <b>같은 화살표</b>를 참조한다(새로 만들지 않는다).
///
/// ★같이 고치는 것 — 교정 단계 오른손 화살표 2개가 미아(loose)였다
///   기존 그룹 5개가 전부 왼손 화살표만 arrows[]에 들고 있어, 오른손 외회전·내회전은
///   Director의 loose로 빠져 자기 showWhen(교정국면_파지제외)으로 판정됐다
///   = <b>교정 국면 내내 내회전·외회전이 동시에</b> 켜져 있었다. 짝이 든 그룹에 넣어 준다.
///
/// ★방향쌍을 이름으로 판단하지 않는다
///   오브젝트 이름(굴곡/신전/외회전/내회전)은 옛 라벨이고, CSV 단계가 정정되면서(bcb9f54)
///   실제 겨냥과 어긋났다. <b>'전환' 그룹이 어느 왼손 화살표를 쓰는가</b>를 근거로 삼는다 —
///   CSV 전환 단계가 "왼손 굴곡 / 오른손 외회전"이라 그 그룹이 든 쌍이 굴곡·외회전이다.
///   진단·재평가도 굴곡·외회전을 확인하는 단계다.
///
/// ★비파괴: 오브젝트를 지우지 않는다. 화살표·그룹 모두 이름이 같으면 새로 만들지 않고 값만 갱신(멱등).
///   기존 그룹의 arrows[]에서 빼는 일도 없고 더하기만 한다. 전부 Undo로 되돌아간다.
///
/// ※새로 만든 진단 화살표의 <b>방향은 눈으로 확인할 것</b>. 원본과 같은 쪽 자세는 각도를 그대로 복사하고,
///   반대쪽 자세는 시상면(x=0) 기준으로 미러링하지만, 원본 자체가 손으로 맞춘 값이라 보정이 필요할 수 있다.
/// </summary>
public static class PjDiagnosisArrowTool
{
    // 교정 화살표 — 처음 만든 도구가 붙인 이름. 짝을 찾는 열쇠로만 쓰고 방향 판단에는 쓰지 않는다.
    private const string LeftOfPairA  = "회전 (왼손 굴곡)";
    private const string RightOfPairA = "회전 (오른손 외회전)";
    private const string LeftOfPairB  = "회전 (왼손 신전)";
    private const string RightOfPairB = "회전 (오른손 내회전)";

    private const string TruthStep = "전환";      // 방향쌍의 근거가 되는 기존 그룹(= 굴곡·외회전)

    // CSV(두개골PJ교정.csv) 실측값
    private const string PhaseDiagnosis = "평가";
    private const string StepDiagnosis  = "진단";
    private const string PhaseReassess  = "재평가";
    private const string StepReassess   = "재평가";

    [MenuItem("GuideChuna/화살표/PJ 진단·재평가 배선 (좌우 자세별)")]
    public static void Wire()
    {
        if (!TryGetRig(out CranialAdjustmentController rig)) return;

        Dictionary<string, ForceArrowBase> byName = CollectArrows(rig);
        if (!Require(byName, out ForceArrowBase leftA, out ForceArrowBase rightA,
                             out ForceArrowBase leftB, out ForceArrowBase rightB)) return;

        var log = new StringBuilder();
        log.AppendLine("[PJ 진단·재평가 화살표 배선]");

        // ── 방향쌍 확정: '전환' 그룹이 쓰는 왼손 화살표가 굴곡·외회전 쪽
        ForceArrowBase flexLeft, flexRight;
        ForceArrowGroup truth = CollectGroups(rig).Find(g => Same(ReadString(g, "stepName"), TruthStep));
        List<ForceArrowBase> truthArrows = truth != null ? ReadArrows(truth) : new List<ForceArrowBase>();
        if (truthArrows.Contains(leftA)) { flexLeft = leftA; flexRight = rightA; }
        else if (truthArrows.Contains(leftB)) { flexLeft = leftB; flexRight = rightB; }
        else
        {
            flexLeft = leftB; flexRight = rightB;
            log.AppendLine($"  ★'{TruthStep}' 그룹을 못 찾아 이름 규약으로 추정했다. Play에서 방향 확인 필요.");
        }
        log.AppendLine($"  굴곡·외회전 쌍 = 왼손[{flexLeft.name}] / 오른손[{flexRight.name}]");

        // ── 진단 자세(좌·우)의 파지점 읽기
        if (!TryGetDiagnosisPoses(rig, out List<PoseAnchors> poses))
        {
            Debug.LogError("[PJ 화살표] 진단 자세를 읽지 못했습니다. 리그의 diagnosisStages 배선을 확인하세요.\n" + log);
            return;
        }
        log.AppendLine($"  진단 자세 {poses.Count}개 발견");

        // 원본 측두 화살표가 어느 쪽에 있는지 — 같은 쪽 자세는 각도 복사, 반대쪽은 미러링
        float srcSide = LocalXInRig(rig, flexRight.transform);

        int madeArrows = 0, madeGroups = 0, updGroups = 0;
        for (int i = 0; i < poses.Count; i++)
        {
            PoseAnchors po = poses[i];
            int poseNo = i + 1;
            if (po.occiput == null || po.temple == null)
            {
                log.AppendLine($"  ★자세 {poseNo} '{po.label}' — 후두/측두 파지점이 비어 건너뜀");
                continue;
            }

            bool mirror = LocalXInRig(rig, po.temple) * srcSide < 0f;

            ForceArrowBase occ = EnsureArrow(rig, $"진단 자세{poseNo} 후두 (굴곡)", flexLeft, po.occiput, mirror, ref madeArrows);
            ForceArrowBase tem = EnsureArrow(rig, $"진단 자세{poseNo} 측두 (외회전)", flexRight, po.temple, mirror, ref madeArrows);

            SetRole(occ, HandRole.Role.보조수);   // 후두를 받치는 손 = 보조수
            SetRole(tem, HandRole.Role.주동수);   // 관골궁을 돌리는 손 = 주동수

            var pair = new[] { occ, tem };
            if (UpsertGroup(rig, $"화살표 그룹 두개골PJ교정 진단 자세{poseNo}", PhaseDiagnosis, StepDiagnosis, poseNo, pair)) madeGroups++; else updGroups++;
            if (UpsertGroup(rig, $"화살표 그룹 두개골PJ교정 재평가 자세{poseNo}", PhaseReassess, StepReassess, poseNo, pair)) madeGroups++; else updGroups++;

            log.AppendLine($"  자세 {poseNo} '{po.label}' — 후두[{po.occiput.name}] + 측두[{po.temple.name}]" +
                           $"  {(mirror ? "(미러링)" : "(각도 복사)")}");
        }
        log.AppendLine($"  화살표 신규 {madeArrows}개 / 그룹 신규 {madeGroups} · 갱신 {updGroups}");

        // ── 교정 단계: 미아 오른손 화살표를 짝이 든 그룹에 넣는다
        int adopted = 0;
        foreach (ForceArrowGroup g in CollectGroups(rig))
        {
            List<ForceArrowBase> cur = ReadArrows(g);
            ForceArrowBase need = null;
            if (cur.Contains(leftA) && !cur.Contains(rightA)) need = rightA;
            else if (cur.Contains(leftB) && !cur.Contains(rightB)) need = rightB;
            if (need == null) continue;
            cur.Add(need);
            WriteArrows(g, cur);
            adopted++;
            log.AppendLine($"     + {g.name} ← [{need.name}] 추가");
        }
        log.AppendLine($"  교정 오른손 화살표 편입: {adopted}건 (미아로 남아 교정 내내 겹쳐 뜨던 것 해소)");

        // ── 교정 화살표 역할 색
        int roled = 0;
        roled += SetRole(leftA,  HandRole.Role.보조수) ? 1 : 0;
        roled += SetRole(leftB,  HandRole.Role.보조수) ? 1 : 0;
        roled += SetRole(rightA, HandRole.Role.주동수) ? 1 : 0;
        roled += SetRole(rightB, HandRole.Role.주동수) ? 1 : 0;
        log.AppendLine($"  교정 화살표 역할 색: 왼손=보조수 / 오른손=주동수 — {roled}개 변경");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        log.AppendLine();
        log.AppendLine("★새로 만든 진단 화살표의 방향을 씬 뷰에서 확인할 것 — 미러링은 계산값이라 보정이 필요할 수 있다.");
        log.AppendLine("씬 저장을 잊지 말 것. 되돌리려면 Ctrl+Z.");
        Debug.Log(log.ToString());
    }

    [MenuItem("GuideChuna/화살표/PJ 배선 점검 (읽기 전용)")]
    public static void Audit()
    {
        if (!TryGetRig(out CranialAdjustmentController rig)) return;

        var sb = new StringBuilder();
        sb.AppendLine("[PJ 화살표 점검] 리그 = " + rig.name);

        var arrows = new List<ForceArrowBase>(rig.GetComponentsInChildren<ForceArrowBase>(true));
        List<ForceArrowGroup> groups = CollectGroups(rig);

        var owned = new HashSet<ForceArrowBase>();
        foreach (ForceArrowGroup g in groups)
            foreach (ForceArrowBase a in ReadArrows(g))
                if (a != null) owned.Add(a);

        sb.AppendLine($"\n  그룹 {groups.Count}개");
        foreach (ForceArrowGroup g in groups)
        {
            var names = new List<string>();
            foreach (ForceArrowBase a in ReadArrows(g)) names.Add(a != null ? a.name : "(빈 칸)");
            string pose = g.PoseNo > 0 ? $" / 자세{g.PoseNo}" : "";
            sb.AppendLine($"    · {g.name}");
            sb.AppendLine($"        {g.DescribeMatch()}{pose}   화살표 [{string.Join(", ", names)}]");
        }

        sb.AppendLine($"\n  화살표 {arrows.Count}개");
        int loose = 0;
        foreach (ForceArrowBase a in arrows)
        {
            bool isLoose = !owned.Contains(a) && a.GetComponentInParent<ForceArrowGroup>(true) == null;
            if (isLoose) loose++;
            sb.AppendLine($"    {(isLoose ? "★미아" : "  소속")} {a.name}   {a.DescribeMatch()}");
        }
        if (loose > 0)
            sb.AppendLine("\n  ★미아 = 어느 그룹에도 없어 자기 showWhen으로만 판정된다. 기본값이면 교정 국면 내내 켜진다.");

        if (TryGetDiagnosisPoses(rig, out List<PoseAnchors> poses))
        {
            sb.AppendLine($"\n  진단 자세 {poses.Count}개");
            foreach (PoseAnchors p in poses)
                sb.AppendLine($"    · {p.label}   후두={(p.occiput ? p.occiput.name : "없음")}  측두={(p.temple ? p.temple.name : "없음")}");
        }

        Debug.Log(sb.ToString());
    }

    // ─────────────────────────── 진단 자세 읽기 ───────────────────────────

    private struct PoseAnchors
    {
        public string label;
        public Transform occiput;   // 손바닥이 받치는 후두 파지점
        public Transform temple;    // 엄지가 걸리는 측두(관골궁) 파지점
    }

    /// <summary>diagnosisStages[0]의 자세들에서 후두(palmGrip)·측두(thumbGrip) 파지점을 뽑는다.
    /// ★어느 손인지는 자세마다 바뀌므로(ⓐ 왼손 후두 / ⓑ 오른손 후두) 좌우를 가리지 않고 둘 다 본다.</summary>
    private static bool TryGetDiagnosisPoses(CranialAdjustmentController rig, out List<PoseAnchors> result)
    {
        result = new List<PoseAnchors>();
        SerializedProperty stages = new SerializedObject(rig).FindProperty("diagnosisStages");
        if (stages == null || !stages.isArray || stages.arraySize == 0) return false;

        SerializedProperty poses = stages.GetArrayElementAtIndex(0).FindPropertyRelative("poses");
        if (poses == null || !poses.isArray) return false;

        for (int i = 0; i < poses.arraySize; i++)
        {
            SerializedProperty po = poses.GetArrayElementAtIndex(i);
            var a = new PoseAnchors
            {
                label = po.FindPropertyRelative("label")?.stringValue ?? $"자세{i + 1}",
                occiput = GripOf(po, "palmGrip"),
                temple = GripOf(po, "thumbGrip"),
            };
            result.Add(a);
        }
        return result.Count > 0;
    }

    /// <summary>자세의 왼손·오른손 중 그 슬롯이 채워진 쪽의 파지점 Transform.</summary>
    private static Transform GripOf(SerializedProperty pose, string slot)
    {
        foreach (string hand in new[] { "leftHand", "rightHand" })
        {
            SerializedProperty h = pose.FindPropertyRelative(hand);
            var g = h?.FindPropertyRelative(slot)?.objectReferenceValue as GripPointTarget;
            if (g != null) return g.transform;
        }
        return null;
    }

    // ─────────────────────────── 화살표 만들기 ───────────────────────────

    /// <summary>이름이 같은 화살표가 있으면 그대로 쓰고(위치·각도 손대지 않음), 없으면 원본을 복제해 만든다.</summary>
    private static ForceArrowBase EnsureArrow(CranialAdjustmentController rig, string name,
                                              ForceArrowBase source, Transform anchor, bool mirror, ref int made)
    {
        foreach (ForceArrowBase a in rig.GetComponentsInChildren<ForceArrowBase>(true))
            if (a != null && a.name == name) return a;

        var copy = (ForceArrowBase)Object.Instantiate(source, anchor);
        copy.name = name;
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localScale = source.transform.localScale;

        // 원본의 리그 로컬 회전을 가져와, 반대쪽 자세면 시상면(x=0) 기준으로 미러링한다.
        Quaternion srcLocal = Quaternion.Inverse(rig.transform.rotation) * source.transform.rotation;
        Quaternion wanted;
        if (mirror)
        {
            Vector3 f = srcLocal * Vector3.forward;
            Vector3 u = srcLocal * Vector3.up;
            f.x = -f.x; u.x = -u.x;
            wanted = Quaternion.LookRotation(f, u);
        }
        else wanted = srcLocal;

        copy.transform.rotation = rig.transform.rotation * wanted;

        Undo.RegisterCreatedObjectUndo(copy.gameObject, "PJ 진단 화살표");
        made++;
        return copy;
    }

    // ─────────────────────────── 공통 헬퍼 ───────────────────────────

    private static float LocalXInRig(CranialAdjustmentController rig, Transform t)
        => rig.transform.InverseTransformPoint(t.position).x;

    private static bool TryGetRig(out CranialAdjustmentController rig)
    {
        rig = null;
        var found = new List<CranialAdjustmentController>();
        foreach (CranialAdjustmentController c in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
            if (c != null && c.gameObject.scene.IsValid() && c.name.Contains("PJ")) found.Add(c);

        if (found.Count == 0)
        {
            Debug.LogError("[PJ 화살표] 씬에서 PJ 리그(CranialRig_PJ)를 찾지 못했습니다. TrainingScene을 연 뒤 다시 실행하세요.");
            return false;
        }
        if (found.Count > 1)
            Debug.LogWarning($"[PJ 화살표] PJ 리그가 {found.Count}개입니다. 첫 번째({found[0].name})를 씁니다.");
        rig = found[0];
        return true;
    }

    private static Dictionary<string, ForceArrowBase> CollectArrows(CranialAdjustmentController rig)
    {
        var d = new Dictionary<string, ForceArrowBase>();
        foreach (ForceArrowBase a in rig.GetComponentsInChildren<ForceArrowBase>(true))
            if (a != null && !d.ContainsKey(a.name)) d[a.name] = a;
        return d;
    }

    private static bool Require(Dictionary<string, ForceArrowBase> d,
                                out ForceArrowBase la, out ForceArrowBase ra,
                                out ForceArrowBase lb, out ForceArrowBase rb)
    {
        d.TryGetValue(LeftOfPairA, out la);
        d.TryGetValue(RightOfPairA, out ra);
        d.TryGetValue(LeftOfPairB, out lb);
        d.TryGetValue(RightOfPairB, out rb);

        var missing = new List<string>();
        if (la == null) missing.Add(LeftOfPairA);
        if (ra == null) missing.Add(RightOfPairA);
        if (lb == null) missing.Add(LeftOfPairB);
        if (rb == null) missing.Add(RightOfPairB);
        if (missing.Count == 0) return true;

        Debug.LogError("[PJ 화살표] PJ 리그에서 다음 교정 화살표를 못 찾았습니다 — 이름이 바뀌었는지 확인하세요:\n  " +
                       string.Join("\n  ", missing) +
                       "\n(있는 것: " + string.Join(", ", new List<string>(d.Keys).ToArray()) + ")");
        return false;
    }

    private static List<ForceArrowGroup> CollectGroups(CranialAdjustmentController rig)
        => new List<ForceArrowGroup>(rig.GetComponentsInChildren<ForceArrowGroup>(true));

    private static bool UpsertGroup(CranialAdjustmentController rig, string name,
                                    string phase, string step, int poseNo, ForceArrowBase[] arrows)
    {
        ForceArrowGroup grp = null;
        foreach (ForceArrowGroup g in rig.GetComponentsInChildren<ForceArrowGroup>(true))
            if (g.name == name) { grp = g; break; }

        bool created = false;
        if (grp == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "PJ 진단 화살표 그룹");
            go.transform.SetParent(rig.transform, false);
            grp = Undo.AddComponent<ForceArrowGroup>(go);
            created = true;
        }

        var so = new SerializedObject(grp);
        so.FindProperty("showWhen").enumValueIndex = (int)ForceArrowBase.ShowScope.특정_단계만;
        so.FindProperty("stepName").stringValue = step;
        so.FindProperty("phaseName").stringValue = phase;
        so.FindProperty("subStepNo").intValue = 0;
        so.FindProperty("subStepNos").stringValue = "";
        so.FindProperty("scenarioName").stringValue = "";
        so.FindProperty("poseNo").intValue = poseNo;
        SerializedProperty arr = so.FindProperty("arrows");
        arr.ClearArray();
        for (int i = 0; i < arrows.Length; i++)
        {
            arr.InsertArrayElementAtIndex(i);
            arr.GetArrayElementAtIndex(i).objectReferenceValue = arrows[i];
        }
        so.ApplyModifiedProperties();
        Record(grp);
        return created;
    }

    private static List<ForceArrowBase> ReadArrows(ForceArrowGroup g)
    {
        var list = new List<ForceArrowBase>();
        SerializedProperty arr = new SerializedObject(g).FindProperty("arrows");
        if (arr == null || !arr.isArray) return list;
        for (int i = 0; i < arr.arraySize; i++)
            list.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue as ForceArrowBase);
        return list;
    }

    private static void WriteArrows(ForceArrowGroup g, List<ForceArrowBase> arrows)
    {
        var so = new SerializedObject(g);
        SerializedProperty arr = so.FindProperty("arrows");
        arr.ClearArray();
        for (int i = 0; i < arrows.Count; i++)
        {
            arr.InsertArrayElementAtIndex(i);
            arr.GetArrayElementAtIndex(i).objectReferenceValue = arrows[i];
        }
        so.ApplyModifiedProperties();
        Record(g);
    }

    private static string ReadString(ForceArrowGroup g, string field)
    {
        SerializedProperty p = new SerializedObject(g).FindProperty(field);
        return p != null ? p.stringValue : null;
    }

    private static bool SetRole(ForceArrowBase a, HandRole.Role role)
    {
        if (a == null) return false;
        var so = new SerializedObject(a);
        SerializedProperty p = so.FindProperty("colorRole");
        if (p == null || p.enumValueIndex == (int)role) return false;
        p.enumValueIndex = (int)role;
        so.ApplyModifiedProperties();
        Record(a);
        return true;
    }

    private static void Record(Component c)
    {
        if (c == null) return;
        if (PrefabUtility.IsPartOfPrefabInstance(c))
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
    }

    private static bool Same(string a, string b)
    {
        if (a == null || b == null) return false;
        return a.Trim().Equals(b.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}
