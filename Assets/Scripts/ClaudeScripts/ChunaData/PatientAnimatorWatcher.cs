// ★[미사용 2026-08-31] 씬·프리팹에 배선이 없고, 다른 스크립트에서 부르지도 않는다.
//   지우지 않고 남겨 둔다 — 읽는 사람이 "이게 지금 도는 코드"라고 오해하지 않도록 이 줄을 단다.
//   판정 근거와 재확인 방법: .claude/tools/deadscan.py
using UnityEngine;

/// <summary>
/// 환자 Animator의 상태가 바뀌거나 시간이 되감길 때 콘솔에 찍는 진단용 감시기.
///
/// ★쓰는 이유: "파지 나레이션이 끝나자마자 환자가 양손깍지로 휙 돌아간다"의 범인을 못 찾았다.
///   코드상 그 substep(파지 2.2)은 patientAnimationClip이 비어 있어 아무도 Play를 부르지 않는데
///   실제로는 포즈가 바뀐다. 그래서 '언제·무엇으로 바뀌는지'를 런타임에 직접 관찰한다.
///
/// 씬을 고칠 필요 없다 — Play를 누르면 자동으로 붙는다(RuntimeInitializeOnLoadMethod).
/// 진단이 끝나면 이 파일을 지우거나 <see cref="Enabled"/>를 false로 두면 된다.
/// </summary>
[DisallowMultipleComponent]
public class PatientAnimatorWatcher : MonoBehaviour
{
    /// <summary>false면 감시기를 아예 만들지 않는다.</summary>
    public static bool Enabled = true;

    private Animator target;
    private int lastStateHash;
    private float lastNormalized = -1f;
    private string lastSubStep = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (!Enabled) return;
        var go = new GameObject("~PatientAnimatorWatcher");
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PatientAnimatorWatcher>();
    }

    private void FindTarget()
    {
        // 활성 환자만 — 비활성 복제본(c9 (2))은 제외
        foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (a.runtimeAnimatorController == null) continue;
            if (a.name.Contains("c9") || a.CompareTag("Patient"))
            {
                target = a;
                Debug.Log($"[AnimWatcher] 감시 시작: '{a.name}' 컨트롤러='{a.runtimeAnimatorController.name}' " +
                          $"ApplyRootMotion={a.applyRootMotion}");
                return;
            }
        }
    }

    private void Update()
    {
        if (target == null)
        {
            if (Time.frameCount % 60 == 0) FindTarget();
            return;
        }

        var st = target.GetCurrentAnimatorStateInfo(0);
        string sub = CurrentSubStepLabel();

        // ① 상태(클립)가 바뀐 순간
        if (st.shortNameHash != lastStateHash)
        {
            Debug.Log($"<color=orange>[AnimWatcher] 상태 변경 → hash={st.shortNameHash} " +
                      $"(정규화시간 {st.normalizedTime:F2}, speed={target.speed}) / substep='{sub}'</color>");
            lastStateHash = st.shortNameHash;
            lastNormalized = st.normalizedTime;
            lastSubStep = sub;
            return;
        }

        // ② 같은 상태인데 시간이 뒤로 감긴 순간 (= 누군가 Play로 되감음)
        if (lastNormalized >= 0f && st.normalizedTime < lastNormalized - 0.05f)
        {
            Debug.Log($"<color=red>[AnimWatcher] 되감김! {lastNormalized:F2} → {st.normalizedTime:F2} " +
                      $"(hash={st.shortNameHash}, speed={target.speed}) / substep='{sub}'</color>");
        }
        lastNormalized = st.normalizedTime;

        if (sub != lastSubStep)
        {
            Debug.Log($"<color=cyan>[AnimWatcher] substep 전환: '{lastSubStep}' → '{sub}' " +
                      $"(현재 hash={st.shortNameHash}, 정규화시간 {st.normalizedTime:F2})</color>");
            lastSubStep = sub;
        }
    }

    private ScenarioManager cachedManager;

    private string CurrentSubStepLabel()
    {
        if (cachedManager == null)
            cachedManager = Object.FindFirstObjectByType<ScenarioManager>();
        var ss = cachedManager != null ? cachedManager.CurrentSubStep : null;
        if (ss == null) return "";
        string txt = ss.textInstruction ?? "";
        if (txt.Length > 18) txt = txt.Substring(0, 18) + "…";
        string anim = string.IsNullOrEmpty(ss.patientAnimationClip) ? "(anim없음)" : ss.patientAnimationClip;
        return $"#{ss.subStepNo} {anim} \"{txt}\"";
    }
}
