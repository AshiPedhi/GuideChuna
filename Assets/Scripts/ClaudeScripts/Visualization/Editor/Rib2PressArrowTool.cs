using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 제2늑골 상방변위의 <b>두상골 족방 압박</b> 화살표 하나만 만들어 배선한다.
///
/// ★왜 기존 도구를 쓰지 않는가
///   GuideChuna/힘의 방향 화살표 기본 배치 (늑골·흉추)는 매칭되는 리그의 <b>화살표 그룹을 전부 지우고
///   다시 만든다</b>(제1늑골·복와위까지 같이). 게다가 그 도구의 제2늑골 분기는
///     ⓐ 팔 거상 화살표를 subStep 3·4·5에 만드는데 현재 CSV의 거상 구간은 5·6·7이고,
///     ⓑ 사용자가 "번갈아 위아래로 뜨는" 거상 화살표를 쓰지 않기로 하고 이미 지웠다(2026-08-18).
///   그래서 그 도구를 돌리면 지운 화살표가 되살아나고 다른 리그 배선까지 흔들린다.
///   이 도구는 <b>제2늑골 리그만, 압박 화살표 하나만</b> 건드린다.
///
/// ★화살표를 새로 그리지 않고 <b>기존 것을 복제</b>한다
///   08-17에 전 화살표를 통짜 실선(박스형)·불투명으로 바꿔 놨다(displayMode=통짜왕복, useTransparency=0,
///   sizeMultiplier=1.6, 조각 1개). 옛 빌더로 새로 만들면 조각형 쐐기가 나와 ② 변환 도구를 다시 돌려야 한다.
///   제1늑골의 «힘의 방향 (왼손 족방 압박)»을 복제하면 그 설정이 그대로 따라온다.
///
/// ★붙이는 자리 = rightGrips[0]
///   제2늑골은 <b>오른손 두상골이 늑골을, 왼손이 환자 팔을</b> 잡는다.
///   실제로 leftGrips[0]은 환자 오른팔 본(CC_Base_R_Hand) 밑에 있고, rightGrips[0]만 리그 안에 있다.
///   족방으로 누르는 것은 늑골 쪽이므로 rightGrips[0]의 자식으로 둔다(환자 애니를 따라간다).
///
/// ★비파괴: 아무것도 지우지 않는다. 화살표·그룹 모두 이름이 같으면 새로 만들지 않고 값만 갱신한다(멱등).
///   전부 Undo로 되돌아간다.
///
/// ※만든 뒤 <b>씬 뷰에서 방향(로컬 +Z)을 족방으로 돌려 줘야 한다</b> — 위치·각도는 복제원본 것이라 대략값이다.
/// </summary>
public static class Rib2PressArrowTool
{
    private const string ArrowName = "힘의 방향 (두상골 족방 압박)";
    private const string GroupName = "화살표 그룹 제2늑골_상방변위 압박.전체";

    // CSV(제2늑골_상방변위.csv) 실측: 교정 국면의 압박 단계
    private const string StepName = "교정·호흡";

    // 복제 원본 — 제1늑골의 족방 압박 화살표(같은 성격, 현재 스타일 적용됨)
    private const string SourceArrowName = "힘의 방향 (왼손 족방 압박)";

    [MenuItem("GuideChuna/화살표/제2늑골 족방 압박 만들기")]
    public static void Create()
    {
        CranialAdjustmentController rig = FindRig("제2늑골");
        if (rig == null)
        {
            Debug.LogError("[제2늑골 화살표] 씬에서 제2늑골 리그를 찾지 못했습니다. TrainingScene을 연 뒤 다시 실행하세요.");
            return;
        }

        Transform ribGrip = FirstGrip(rig, "rightGrips");
        if (ribGrip == null)
        {
            Debug.LogError("[제2늑골 화살표] rightGrips(늑골 두상골 파지점)가 비어 있습니다. 리그 인스펙터를 확인하세요.");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine("[제2늑골 족방 압박 화살표]");
        log.AppendLine($"  리그 = {rig.name} / 부착 위치 = {ribGrip.name}");

        // ── 화살표: 이미 있으면 그대로 쓰고, 없으면 제1늑골 것을 복제한다
        ForceArrowBase arrow = FindArrow(rig, ArrowName);
        if (arrow == null)
        {
            ForceArrowBase src = FindCloneSource();
            if (src == null)
            {
                Debug.LogError($"[제2늑골 화살표] 복제할 원본 «{SourceArrowName}»을 찾지 못했습니다. " +
                               "제1늑골 리그의 화살표 이름이 바뀌었는지 확인하세요.");
                return;
            }

            var copy = (ForceArrowBase)Object.Instantiate(src, ribGrip);
            copy.name = ArrowName;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = src.transform.localScale;
            Undo.RegisterCreatedObjectUndo(copy.gameObject, "제2늑골 압박 화살표");
            arrow = copy;
            log.AppendLine($"  화살표 신규 — «{src.name}»({src.GetComponentInParent<CranialAdjustmentController>(true)?.name}) 복제");
            log.AppendLine("  ★씬 뷰에서 로컬 +Z가 족방(발 쪽)을 향하도록 회전시킬 것 — 복제원본 각도라 대략값이다.");
        }
        else
        {
            log.AppendLine("  화살표 기존 것 재사용 (위치·각도 손대지 않음)");
        }

        // ── 역할 색: 늑골을 누르는 손이 힘을 주는 손 = 주동수
        if (SetRole(arrow, HandRole.Role.주동수))
            log.AppendLine("  역할 색 = 주동수(진녹)");

        // ── 그룹: 교정·호흡 단계 전체
        bool madeGroup = UpsertGroup(rig, GroupName, StepName, arrow);
        log.AppendLine($"  그룹 {(madeGroup ? "신규" : "갱신")} — 특정 단계만 / {StepName} / 단계 전체");
        log.AppendLine("  ※거상(팔 올리고 내리기) 화살표는 만들지 않았다 — 사용자 결정으로 폐기된 것이다.");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        log.AppendLine();
        log.AppendLine("씬 저장을 잊지 말 것. 되돌리려면 Ctrl+Z.");
        Debug.Log(log.ToString());

        Selection.activeGameObject = arrow.gameObject;
    }

    // ─────────────────────────── 헬퍼 ───────────────────────────

    private static CranialAdjustmentController FindRig(string keyword)
    {
        foreach (CranialAdjustmentController c in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
        {
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            if (c.name.Contains(keyword) || ScenarioNameOf(c).Contains(keyword)) return c;
        }
        return null;
    }

    private static string ScenarioNameOf(CranialAdjustmentController rig)
    {
        SerializedProperty p = new SerializedObject(rig).FindProperty("scenarioName");
        return p != null ? (p.stringValue ?? "") : "";
    }

    private static Transform FirstGrip(CranialAdjustmentController rig, string arrayName)
    {
        SerializedProperty arr = new SerializedObject(rig).FindProperty(arrayName);
        if (arr == null || !arr.isArray) return null;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g != null) return g.transform;
        }
        return null;
    }

    private static ForceArrowBase FindArrow(CranialAdjustmentController rig, string name)
    {
        foreach (ForceArrowBase a in rig.GetComponentsInChildren<ForceArrowBase>(true))
            if (a != null && a.name == name) return a;
        return null;
    }

    /// <summary>제1늑골 리그의 족방 압박 화살표. 못 찾으면 늑골 리그의 아무 직선 화살표라도 쓴다.</summary>
    private static ForceArrowBase FindCloneSource()
    {
        ForceArrowBase fallback = null;
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            var rig = a.GetComponentInParent<CranialAdjustmentController>(true);
            if (rig == null || !rig.name.Contains("제1늑골")) continue;
            if (a.name == SourceArrowName) return a;
            if (fallback == null && a is ForceArrow) fallback = a;
        }
        return fallback;
    }

    private static bool UpsertGroup(CranialAdjustmentController rig, string name, string step, ForceArrowBase arrow)
    {
        ForceArrowGroup grp = null;
        foreach (ForceArrowGroup g in rig.GetComponentsInChildren<ForceArrowGroup>(true))
            if (g.name == name) { grp = g; break; }

        bool created = false;
        if (grp == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "제2늑골 압박 화살표 그룹");
            go.transform.SetParent(rig.transform, false);
            grp = Undo.AddComponent<ForceArrowGroup>(go);
            created = true;
        }

        var so = new SerializedObject(grp);
        so.FindProperty("showWhen").enumValueIndex = (int)ForceArrowBase.ShowScope.특정_단계만;
        so.FindProperty("stepName").stringValue = step;
        so.FindProperty("phaseName").stringValue = "";
        so.FindProperty("subStepNo").intValue = 0;      // 단계 전체 — 압박은 교정·호흡 내내 준다
        so.FindProperty("subStepNos").stringValue = "";
        so.FindProperty("scenarioName").stringValue = "";
        SerializedProperty arr = so.FindProperty("arrows");
        arr.ClearArray();
        arr.InsertArrayElementAtIndex(0);
        arr.GetArrayElementAtIndex(0).objectReferenceValue = arrow;
        so.ApplyModifiedProperties();
        Record(grp);
        return created;
    }

    private static bool SetRole(ForceArrowBase a, HandRole.Role role)
    {
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
}
