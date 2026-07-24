using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 골격(해부) 모델의 특정 본이 환자 리그의 특정 본을 따라 회전하게 한다.
/// 환자가 고개를 움직이면 골격 머리·목도 같이 움직인다. 골격 전용 애니메이션 불필요.
///
/// ★구조 이해(중요): 골격 메시(두개골·경추·늑골 등)는 대부분 "본에 붙은 리지드 MeshRenderer"라
/// 부모 본이 돌면 자동으로 따라온다. 따라서 렌더러를 하나하나 배선할 필요가 없고 "본"만 구동하면 된다.
///
/// ★회전만, 위치는 안 건드림: 골격과 환자는 비율이 달라 위치를 옮기면 메시가 폭발한다.
///
/// ★목 다분절 배분(weight): 골격 목은 여러 분절(Bip001 Neck1~4 등)인데 환자 목은 더 적다.
/// 각 분절이 환자 머리 회전의 "일부"만 받도록 weight로 나눠 주면 자연스러운 목 곡선이 나온다.
/// (weight<1이면 캡처 시점 대비 환자 회전 델타의 일부만 적용)
///
/// 사용: pairs에 (환자 본 → 골격 본, weight)를 넣고 [현재 포즈에서 오프셋 캡처].
/// 캡처 시점의 두 본 자세를 기준(rest)으로, 이후 환자 본이 회전한 만큼(×weight) 골격 본이 회전한다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SkeletonRigFollower : MonoBehaviour
{
    [Serializable]
    public class BonePair
    {
        [Tooltip("환자 쪽 본 (읽기 전용 소스). 예: CC_Base_Head")]
        public Transform patientBone;
        [Tooltip("이 골격 본이 회전을 따라간다. 예: Bip001 Neck4 / Bip001 Head")]
        public Transform skeletonBone;
        [Tooltip("따라가는 정도(0=고정, 1=완전 추종). 목 분절 배분에 사용.")]
        [Range(0f, 1f)] public float weight = 1f;

        // 캡처된 기준 자세(rest)
        [HideInInspector] public Quaternion patientRest = Quaternion.identity;
        [HideInInspector] public Quaternion skeletonRest = Quaternion.identity;
        [HideInInspector] public bool captured;
    }

    [Header("추종 방식")]
    [Tooltip("★권장(골격이 Humanoid일 때): 환자의 휴머노이드 포즈 전체를 골격에 리타깃. " +
             "환자가 머리만 움직이면 골격 머리만 움직이고 몸통은 그대로(본별 스키닝 공유 문제 없음). " +
             "끄면 아래 pairs(본별 회전 복사) 방식.")]
    public bool useHumanoidPose = false;
    [Tooltip("휴머노이드 포즈 모드용: 따라갈 환자 Animator(Humanoid).")]
    public Animator patientAnimator;
    [Tooltip("휴머노이드 포즈 모드용: 이 골격의 Humanoid Avatar. 비우면 같은 오브젝트 Animator에서 가져옴.")]
    public Avatar skeletonAvatar;

    [Tooltip("환자 본 → 골격 본 페어 목록(본별 회전 복사 방식). [오프셋 캡처] 후 추종.")]
    public List<BonePair> pairs = new List<BonePair>();

    [Header("옵션")]
    [Tooltip("에디트 모드에서도 실시간으로 따라가게 한다(씬뷰 미리보기).")]
    public bool runInEditMode = true;

    [Tooltip("골격 본이 캡처 위치에서 이 거리(m) 이상 벗어나면 그 프레임엔 위치를 되돌린다(날아감 방지 안전장치).")]
    public float maxPositionDrift = 0.5f;
    [Tooltip("안전장치 사용 여부.")]
    public bool clampPositionDrift = true;

    [Tooltip("켜면 0.5초마다 '환자 머리 회전량 / 골격에 적용 중' 여부를 콘솔에 찍는다(진단용).")]
    public bool debugLog = false;
    private float nextLogTime;

    // 캡처 시점 각 골격 본의 월드 위치(안전장치용)
    private readonly List<Vector3> capturedWorldPos = new List<Vector3>();

    public bool HasPairs => pairs != null && pairs.Count > 0;

    private void OnValidate()
    {
        // 인스펙터에서 소스/아바타/모드를 바꾸면 다음 프레임에 포즈 핸들러 재구성.
        poseReady = false;
    }

    /// <summary>현재(정지) 자세를 기준으로 각 페어의 rest를 기록. 이후 환자가 이 자세에서 회전한 만큼 골격이 따라간다.
    /// ★환자와 골격이 대응되는 자세(둘 다 중립/정면)일 때 눌러야 방향이 맞는다.</summary>
    public void CaptureOffsets()
    {
        if (pairs == null) return;

        // ★부모가 먼저 처리되도록 골격 본 계층 깊이 오름차순 정렬.
        // Apply가 월드 회전을 절대값으로 세팅하므로, 부모를 나중에 세팅하면 자식 회전이 덮어써진다.
        pairs.Sort((a, b) =>
        {
            int da = Depth(a != null ? a.skeletonBone : null);
            int db = Depth(b != null ? b.skeletonBone : null);
            return da.CompareTo(db);
        });

        capturedWorldPos.Clear();
        foreach (var p in pairs)
        {
            if (p == null || p.patientBone == null || p.skeletonBone == null)
            {
                if (p != null) p.captured = false;
                capturedWorldPos.Add(Vector3.zero);
                continue;
            }
            p.patientRest = p.patientBone.rotation;
            p.skeletonRest = p.skeletonBone.rotation;
            p.captured = true;
            capturedWorldPos.Add(p.skeletonBone.position);
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        while (t != null) { d++; t = t.parent; }
        return d;
    }

    /// <summary>추종 정지(오프셋 지움). 본은 현재 자세 유지.</summary>
    public void ClearOffsets()
    {
        if (pairs == null) return;
        foreach (var p in pairs) if (p != null) p.captured = false;
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && !runInEditMode) return;
#endif
        if (useHumanoidPose) ApplyHumanoidPose();
        else Apply();
    }

    private void OnDisable() { DisposePose(); }

    // === 휴머노이드 포즈 리타깃 ===
    private HumanPoseHandler srcHandler, dstHandler;
    private HumanPose humanPose;
    private bool poseReady;
    private float nextPoseWarn;

    private Avatar ResolveSkeletonAvatar()
    {
        if (skeletonAvatar != null) return skeletonAvatar;
        var a = GetComponent<Animator>();
        return a != null ? a.avatar : null;
    }

    private void BuildPose()
    {
        DisposePose();
        if (patientAnimator == null || !patientAnimator.isHuman || patientAnimator.avatar == null) return;
        Avatar av = ResolveSkeletonAvatar();
        if (av == null || !av.isValid || !av.isHuman) return;

        // 골격 자체 Animator가 클립으로 간섭하지 않도록 끈다(HumanPoseHandler는 트랜스폼에 직접 씀).
        var ownAnim = GetComponent<Animator>();
        if (ownAnim != null && ownAnim != patientAnimator) ownAnim.enabled = false;

        try
        {
            srcHandler = new HumanPoseHandler(patientAnimator.avatar, patientAnimator.transform);
            dstHandler = new HumanPoseHandler(av, transform);
            poseReady = true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SkeletonRigFollower] 휴머노이드 포즈 초기화 실패: {e.Message}", this);
            DisposePose();
        }
    }

    private void DisposePose()
    {
        if (srcHandler != null) { srcHandler.Dispose(); srcHandler = null; }
        if (dstHandler != null) { dstHandler.Dispose(); dstHandler = null; }
        poseReady = false;
    }

    /// <summary>환자 휴머노이드 포즈를 골격에 리타깃(머리는 머리, 몸통은 몸통으로 매핑).</summary>
    public void ApplyHumanoidPose()
    {
        if (!poseReady) { BuildPose(); if (!poseReady) {
            if (debugLog && Time.realtimeSinceStartup >= nextPoseWarn)
            {
                nextPoseWarn = Time.realtimeSinceStartup + 1f;
                Debug.LogWarning("[SkeletonRigFollower] 휴머노이드 포즈 미초기화 — patientAnimator(Humanoid)와 골격 Avatar(Humanoid)를 확인.", this);
            }
            return;
        } }
        if (patientAnimator == null) { DisposePose(); return; }
        srcHandler.GetHumanPose(ref humanPose);
        dstHandler.SetHumanPose(ref humanPose);
    }

    /// <summary>환자 본의 회전 델타(캡처 시점 대비)를 weight만큼 골격 본에 적용.</summary>
    public void Apply()
    {
        if (pairs == null) return;

        bool log = debugLog && Time.realtimeSinceStartup >= nextLogTime;
        float maxAngle = 0f; string sample = null;

        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (p == null || !p.captured) continue;
            if (p.patientBone == null || p.skeletonBone == null) continue;

            // 환자가 캡처 시점부터 지금까지 월드공간에서 회전한 델타
            Quaternion delta = p.patientBone.rotation * Quaternion.Inverse(p.patientRest);

            if (log)
            {
                float a = Quaternion.Angle(Quaternion.identity, delta);
                if (a > maxAngle) { maxAngle = a; sample = $"{p.patientBone.name}→{p.skeletonBone.name}"; }
            }
            if (p.weight < 1f)
                delta = Quaternion.Slerp(Quaternion.identity, delta, p.weight);

            // 골격 기준 자세에 델타를 얹음(위치 미변경 → 폭발 없음)
            p.skeletonBone.rotation = delta * p.skeletonRest;

            // ★안전장치: 상위 본 회전으로 이 본의 '위치'가 크게 튀면(날아감) 위치를 캡처값으로 되돌림.
            if (clampPositionDrift && i < capturedWorldPos.Count)
            {
                Vector3 cap = capturedWorldPos[i];
                if (cap != Vector3.zero && (p.skeletonBone.position - cap).sqrMagnitude > maxPositionDrift * maxPositionDrift)
                    p.skeletonBone.position = cap;
            }
        }

        if (log)
        {
            nextLogTime = Time.realtimeSinceStartup + 0.5f;
            if (sample != null)
                Debug.Log($"[SkeletonRigFollower] 환자 머리 회전 {maxAngle:F1}° 적용 중 ({sample}) — 페어 {pairs.Count}, 캡처됨.", this);
            else
                Debug.LogWarning("[SkeletonRigFollower] 캡처된 페어가 없거나 환자 본이 안 움직임 — ③ 오프셋 캡처를 했는지, 페어의 patientBone/skeletonBone이 연결됐는지 확인.", this);
        }
    }
}
