// ★시험 전용(0단계) · 2026-10-22
//   Play 중에 열린 씬의 환자(Humanoid Animator)에 PatientArmIkProbe를 런타임으로 붙인다.
//   씬 파일은 고치지 않는다 — Play를 멈추면 컴포넌트·목표 구·힌트 구가 전부 사라지는 것이 맞다.
using UnityEditor;
using UnityEngine;

public static class PatientArmIkProbeMenu
{
    private const string MenuRight = "GuideChuna/시험/환자 팔 IK 0단계 (오른팔 · Play 중)";
    private const string MenuLeft = "GuideChuna/시험/환자 팔 IK 0단계 (왼팔 · Play 중)";

    [MenuItem(MenuRight)]
    private static void AttachRight() => Attach(PatientArmIkProbe.ArmSide.Right);   // 돌려받는 값은 버린다

    [MenuItem(MenuLeft)]
    private static void AttachLeft() => Attach(PatientArmIkProbe.ArmSide.Left);

    // 10-22: 목표 구를 정해진 경로로 자동으로 움직이며 잰다(PatientArmIkSim) — 에이전트 브리지 «menu»로도 부른다
    private const string SimRight = "GuideChuna/시험/환자 팔 IK 시뮬레이션 (오른팔 · Play 중)";
    private const string SimLeft = "GuideChuna/시험/환자 팔 IK 시뮬레이션 (왼팔 · Play 중)";

    [MenuItem(SimRight)]
    private static void SimulateRight() => Simulate(PatientArmIkProbe.ArmSide.Right);

    [MenuItem(SimLeft)]
    private static void SimulateLeft() => Simulate(PatientArmIkProbe.ArmSide.Left);

    [MenuItem(MenuRight, true)]
    [MenuItem(MenuLeft, true)]
    [MenuItem(SimRight, true)]
    [MenuItem(SimLeft, true)]
    private static bool Validate() => EditorApplication.isPlaying;

    private static void Simulate(PatientArmIkProbe.ArmSide side)
    {
        PatientArmIkProbe probe = Attach(side);
        if (probe == null) return;
        PatientArmIkSim sim = probe.gameObject.AddComponent<PatientArmIkSim>();
        sim.Init(probe);
        Debug.Log($"[팔IK시뮬] {probe.gameObject.name}에 붙였다 — side={side}. 끝나면 «[팔IK시뮬] … 끝» 로그가 찍힌다.");
    }

    private static PatientArmIkProbe Attach(PatientArmIkProbe.ArmSide side)
    {
        Animator patient = FindPatient();
        if (patient == null)
        {
            Debug.LogWarning("[팔IK] 환자 Animator를 못 찾았다 — 이름 c9이거나 팔 본이 CC_Base_로 시작하는 Humanoid가 없다.");
            return null;
        }

        // 같은 쪽 팔에 이미 붙어 있으면 새로 붙이지 않고 선택만 한다(두 개가 같은 뼈를 돌리면 결과가 겹친다).
        foreach (PatientArmIkProbe existing in patient.GetComponents<PatientArmIkProbe>())
        {
            if (existing.Side == side)
            {
                Debug.Log($"[팔IK] 이미 붙어 있다 — {patient.name} side={side}. 선택만 한다.");
                Selection.activeObject = existing;
                return existing;
            }
        }

        // 10-22: 핸드 트래킹으로 잡기 — 환자 하나에 하나(양손 · 양팔을 함께 맡는다)
        if (patient.GetComponent<PatientArmIkHandGrab>() == null) patient.gameObject.AddComponent<PatientArmIkHandGrab>();

        PatientArmIkProbe probe = patient.gameObject.AddComponent<PatientArmIkProbe>();
        probe.Side = side; // Start 전이라 여기서 정한다
        Debug.Log($"[팔IK] {patient.name}에 런타임으로 붙였다 — side={side}. 목표 구 «[팔IK] 목표»를 씬 뷰에서 끌거나, 핸드 트래킹으로 환자 손목을 주먹 · 핀치로 잡아 옮긴다. (Play를 멈추면 사라진다)");
        Selection.activeGameObject = patient.gameObject;
        return probe;
    }

    /// <summary>이름이 c9인 Humanoid를 먼저, 없으면 팔 본이 CC_Base_로 시작하는 Humanoid를 찾는다.</summary>
    private static Animator FindPatient()
    {
        Animator fallback = null;
        foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!a.isHuman) continue;
            Transform upper = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (upper == null || !upper.name.StartsWith("CC_Base_")) continue;
            if (a.name == "c9") return a;
            if (fallback == null) fallback = a;
        }
        return fallback;
    }
}
