// ★2026-10-19 정정: 08-31에 «미사용» 배너를 달았는데 <b>틀렸다.</b> 씬 배선은 없지만
//   RuntimeInitializeOnLoadMethod로 Play마다 저절로 붙어 돈다(오늘 로그의 [AnimWatcher] 줄이 전부 여기서 나왔다).
//   deadscan의 두 조건(씬 guid · 이름 참조)으로는 안 잡히는 경우다 — 자동 생성 속성도 봐야 한다.
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
            LogBodyPose("substep 전환");
        }

        // ③ 몸이 실제로 누워 있나(2026-10-19) — 복와위에서 환자가 선 채 사용자를 마주 보던 것(간헐)을 잰다.
        //   로그로는 '복와위 대기'가 정상 재생 중이었는데 화면은 서 있었다 → 상태 이름이 아니라 몸 방향을 직접 본다.
        //   ★기울기가 15° 넘게 바뀔 때만 찍는다(자기 침묵).
        float tilt = BodyTiltDeg();
        if (tilt >= 0f && (lastTilt < 0f || Mathf.Abs(tilt - lastTilt) > 15f))
        {
            LogBodyPose(lastTilt < 0f ? "첫 측정" : $"몸 기울기 변화 {lastTilt:F0}° → {tilt:F0}°");
        }
    }

    private float lastTilt = -1f;
    private Transform hips, head;

    /// <summary>골반 → 머리 방향이 세로선과 이루는 각(°). 서면 ≈0 · 누우면 ≈90. 못 재면 -1.</summary>
    private float BodyTiltDeg()
    {
        if (target == null || !target.isHuman) return -1f;
        if (hips == null) hips = target.GetBoneTransform(HumanBodyBones.Hips);
        if (head == null) head = target.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || head == null) return -1f;
        Vector3 d = head.position - hips.position;
        if (d.sqrMagnitude < 1e-6f) return -1f;
        return Vector3.Angle(d, Vector3.up);
    }

    private void LogBodyPose(string why)
    {
        float tilt = BodyTiltDeg();
        lastTilt = tilt;
        Transform t = target.transform;
        Transform p = t.parent;
        Debug.Log($"<color=magenta>[AnimWatcher·자세] {why} — 몸 기울기 {tilt:F0}° ({(tilt < 0f ? "못 잼" : tilt > 60f ? "누움" : tilt < 30f ? "★섬" : "기움")}) · " +
                  $"컨트롤러 '{(target.runtimeAnimatorController != null ? target.runtimeAnimatorController.name : "없음")}' · " +
                  $"c9 로컬회전 {t.localEulerAngles:F1} 월드회전 {t.eulerAngles:F1} · " +
                  $"부모 '{(p != null ? p.name : "-")}' 회전 {(p != null ? p.eulerAngles.ToString("F1") : "-")} · " +
                  $"c9 로컬위치 {t.localPosition:F2} · substep='{lastSubStep}'</color>");
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
