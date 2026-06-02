using System.Collections;
using UnityEngine;

/// <summary>
/// 시나리오 UI 자동 배치 컨트롤러
/// 시나리오 시작 시 헤드셋 위치를 기준으로 UI를 자동 배치
/// </summary>
public class ScenarioUIPositioner : MonoBehaviour
{
    [Header("=== UI 배치 대상 ===")]
    [Tooltip("자동 배치할 UI Canvas 또는 UI 루트 오브젝트")]
    [SerializeField] private Transform[] uiTargets;

    [Header("=== 헤드셋 참조 ===")]
    [Tooltip("헤드셋 Transform (OVR CenterEyeAnchor)")]
    [SerializeField] private Transform headsetTransform;

    [Header("=== 배치 설정 ===")]
    [Tooltip("헤드셋 전방으로부터의 거리 (미터)")]
    [SerializeField] private float forwardDistance = 1.5f;

    [Tooltip("헤드셋으로부터의 높이 오프셋 (미터)")]
    [SerializeField] private float heightOffset = 0f;

    [Tooltip("시나리오 시작 시 자동으로 UI 배치")]
    [SerializeField] private bool autoPositionOnStart = true;

    [Tooltip("UI가 항상 헤드셋을 바라보도록 설정")]
    [SerializeField] private bool lookAtHeadset = true;

    [Tooltip("인스펙터 headsetTransform 슬롯을 무시하고 항상 Camera.main으로 강제 재탐색 (가장 확실히 헤드셋 위치 잡힘)")]
    [SerializeField] private bool forceUseMainCamera = false;

    [Header("=== 헤드셋 Y 정상범위 가드 (FloorLevel 기준) ===")]
    [Tooltip("헤드셋 Y가 이 범위를 벗어나면 트래킹 미안정/글리치로 보고 배치를 건너뜀. (0,0,0) 박힘이나 일시적 튐으로 UI가 엉뚱한 높이에 잠기는 것 방지")]
    [SerializeField] private float minPlausibleHeadHeight = 0.5f;
    [SerializeField] private float maxPlausibleHeadHeight = 2.5f;

    // UI 위치 초기화가 한 번만 실행되도록 하는 플래그
    private bool hasPositionedOnce = false;

    void Awake()
    {
        // forceUseMainCamera가 켜져 있으면 인스펙터 슬롯 무시하고 Camera.main으로
        if (forceUseMainCamera && Camera.main != null)
        {
            headsetTransform = Camera.main.transform;
            ChunaLogger.Log($"[ScenarioUIPositioner] ✅ Camera.main 강제 사용: '{Camera.main.name}'");
        }

        // 헤드셋 Transform 자동 찾기
        if (headsetTransform == null)
        {
            GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (ovrCameraRig != null)
            {
                headsetTransform = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
                if (headsetTransform != null)
                {
                    ChunaLogger.Log("[ScenarioUIPositioner] ✅ CenterEyeAnchor 자동 찾기 성공");
                }
                else
                {
                    ChunaLogger.LogError("[ScenarioUIPositioner] ❌ CenterEyeAnchor를 찾을 수 없습니다!");
                }
            }
            else
            {
                ChunaLogger.LogError("[ScenarioUIPositioner] ❌ OVRCameraRig를 찾을 수 없습니다!");
            }
        }

    }

    void Start()
    {
        if (autoPositionOnStart)
        {
            // Start() 시점엔 OVR 트래킹이 아직 헤드셋 위치를 갱신하지 않아 (0,0,0)에 머무는 경우가 많음
            // → 헤드셋 Y가 유효한 값으로 점프할 때까지 대기 후 배치
            StartCoroutine(PositionWhenHeadsetReady());
        }
    }

    /// <summary>
    /// 헤드셋 트래킹이 유효·안정될 때까지 UI를 기본 위치에 그대로 둔 채 대기하다가, 준비되면 "한 번만" 배치하고 잠금.
    /// 매 프레임 추종하지 않으므로 고정 전 UI가 시선을 따라 끌려다니거나 회전하지 않음.
    /// 단일 시점 체크의 OVR 디폴트값 오인은 Y 정상범위 가드 + 연속 안정 프레임 판정으로 회피.
    /// </summary>
    private IEnumerator PositionWhenHeadsetReady()
    {
        if (headsetTransform == null)
        {
            ChunaLogger.LogError("[ScenarioUIPositioner] headsetTransform이 null입니다!");
            yield break;
        }

        const float maxWaitTime = 5f;        // 트래킹이 끝내 안 잡힐 때의 대기 상한
        const int requiredStableFrames = 4;  // 유효 범위 안에서 이만큼 연속 안정해야 배치
        const float stableThreshold = 0.03f; // 프레임 간 이동이 이 값(m) 미만이면 안정으로 판정
        const float fallbackAfter = 1f;      // 이 시간 동안 계속 비정상이면 Camera.main 폴백

        float elapsed = 0f;
        int stableCount = 0;
        Vector3 prevPos = headsetTransform.position;
        bool triedMainCameraFallback = false;

        while (elapsed < maxWaitTime)
        {
            yield return null; // OVR LateUpdate 반영된 다음 프레임까지 대기
            elapsed += Time.deltaTime;

            Vector3 pos = headsetTransform.position;

            // Y가 사람 머리 높이 범위 밖 = 트래킹 미안정/글리치 → 배치 보류, UI는 기본 위치 그대로(추종 안 함)
            if (pos.y < minPlausibleHeadHeight || pos.y > maxPlausibleHeadHeight)
            {
                stableCount = 0;
                prevPos = pos;

                // 슬롯이 끝내 트래킹을 못 받으면 Camera.main으로 한 번 교체 (에디터 Quest Link 대응)
                if (!triedMainCameraFallback && elapsed > fallbackAfter &&
                    Camera.main != null && Camera.main.transform != headsetTransform)
                {
                    ChunaLogger.LogWarning($"[ScenarioUIPositioner] ↪ 헤드셋 슬롯 트래킹 미수신 → Camera.main 폴백: '{headsetTransform.name}' → '{Camera.main.name}'");
                    headsetTransform = Camera.main.transform;
                    triedMainCameraFallback = true;
                }
                continue;
            }

            // 유효 범위 — 직전 프레임 대비 이동량으로 안정성 판정 (글리치 한 프레임에 잠기는 것 방지)
            if (Vector3.Distance(pos, prevPos) < stableThreshold)
                stableCount++;
            else
                stableCount = 0;

            prevPos = pos;

            if (stableCount >= requiredStableFrames)
                break;
        }

        // 준비 완료(또는 타임아웃) → 한 번만 배치하고 잠금. 추종/드래그 없음
        if (ApplyPositionInternal())
        {
            hasPositionedOnce = true;
            ChunaLogger.Log($"[ScenarioUIPositioner] ✅ 헤드셋 안정화 후 1회 배치·잠금 (대기 {elapsed:F2}s, 헤드셋='{headsetTransform.name}', Y={headsetTransform.position.y:F2}m)");
        }
        else
        {
            // 끝내 유효 위치를 못 잡음 — UI는 기본 위치 유지, 다음 RepositionWhenReady에서 재시도
            ChunaLogger.LogWarning($"[ScenarioUIPositioner] ⚠ {maxWaitTime}s 내 유효 헤드셋 위치 미확보 — UI 기본 위치 유지 (재시도 대기)");
        }
    }

    /// <summary>
    /// UI 요소들을 헤드셋 위치 기준으로 배치 (시나리오당 한 번만 실행됨)
    /// </summary>
    public void PositionUIElements()
    {
        if (hasPositionedOnce)
        {
            ChunaLogger.Log("[ScenarioUIPositioner] 이미 UI 배치가 완료되었습니다. 건너뜁니다.");
            return;
        }

        if (!ApplyPositionInternal()) return;

        hasPositionedOnce = true;
        ChunaLogger.Log($"[ScenarioUIPositioner] 총 {uiTargets.Length}개 UI 배치 완료 (이후 재실행 방지)");
    }

    /// <summary>
    /// 헤드셋 기준 위치를 계산해 UI에 1회 적용하는 핵심 로직. 헤드셋 Y가 정상범위를 벗어나면 적용하지 않고 false 반환.
    /// </summary>
    private bool ApplyPositionInternal()
    {
        if (headsetTransform == null) return false;
        if (uiTargets == null || uiTargets.Length == 0) return false;

        Vector3 headsetPosition = headsetTransform.position;

        // OVR 트래킹 미안정/글리치 방어: 헤드셋 Y가 사람 머리 높이 범위를 벗어나면 적용 보류.
        // (FloorLevel 기준 ~0.5~2.5m) — (0,0,0) 박힘이나 일시적 튐으로 UI가 엉뚱한 높이에 잠기는 것 방지.
        // 추종 코루틴에서 매 프레임 호출되므로, 정상 프레임이 들어올 때까지 UI는 직전 위치/기본 위치 유지.
        if (headsetPosition.y < minPlausibleHeadHeight || headsetPosition.y > maxPlausibleHeadHeight)
            return false;

        Vector3 headsetForward = headsetTransform.forward;
        headsetForward.y = 0;
        headsetForward.Normalize();

        Vector3 targetPosition = new Vector3(
            headsetPosition.x + headsetForward.x * forwardDistance,
            headsetPosition.y + heightOffset,
            headsetPosition.z + headsetForward.z * forwardDistance
        );

        foreach (var uiTarget in uiTargets)
        {
            if (uiTarget == null) continue;

            uiTarget.position = targetPosition;

            if (lookAtHeadset)
            {
                Vector3 lookDirection = targetPosition - headsetPosition;
                lookDirection.y = 0;
                if (lookDirection.sqrMagnitude > 0.001f)
                    uiTarget.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
        return true;
    }

    /// <summary>
    /// 수동으로 UI 재배치 (설정 변경 후 호출 가능)
    /// 플래그를 리셋하여 강제로 재배치합니다.
    /// </summary>
    [ContextMenu("Reposition UI")]
    public void RepositionUI()
    {
        hasPositionedOnce = false;
        PositionUIElements();
    }

    /// <summary>
    /// UI 위치 초기화 플래그를 리셋합니다.
    /// (새 시나리오 시작 시 호출)
    /// </summary>
    public void ResetPositionFlag()
    {
        hasPositionedOnce = false;
        ChunaLogger.Log("[ScenarioUIPositioner] UI 위치 초기화 플래그 리셋");
    }

    /// <summary>
    /// 헤드셋 트래킹이 안정화될 때까지 잠시 추종한 뒤 안착시키며 재배치.
    /// 단일 프레임 스냅샷(ResetPositionFlag+PositionUIElements)과 달리 동결 감지/Camera.main 폴백/Y 정상범위 가드를
    /// 그대로 거치므로, 트래킹이 미안정한 한 프레임에 잘못된 위치로 잠기지 않음.
    /// 시나리오 시작 등 사용자 액션 시점(OVR 안정 보장)에 호출 — 에디터 Quest Link/단독 빌드 모두 안전.
    /// </summary>
    public void RepositionWhenReady()
    {
        hasPositionedOnce = false;
        StopAllCoroutines();              // 이 컴포넌트는 PositionWhenHeadsetReady만 돌리므로 안전
        StartCoroutine(PositionWhenHeadsetReady());
        ChunaLogger.Log("[ScenarioUIPositioner] 헤드셋 안정화 재배치 시작");
    }

    /// <summary>이미 한 번 배치가 완료됐는지 여부.</summary>
    public bool HasPositioned => hasPositionedOnce;

    /// <summary>
    /// 아직 한 번도 배치되지 않았을 때만 재배치를 시도. 이미 배치됐으면 위치를 그대로 유지.
    /// 시나리오 시작 시 잘 잡혀있는 UI가 또 재조정되어 움직이는 것을 방지하기 위한 안전망 호출.
    /// (트래킹이 끝내 안 잡혀 미배치 상태로 시작하는 예외 케이스만 이때 배치)
    /// </summary>
    public void EnsurePositionedWhenReady()
    {
        if (hasPositionedOnce)
        {
            ChunaLogger.Log("[ScenarioUIPositioner] 이미 배치 완료 — 시나리오 시작 시 재배치 건너뜀 (위치 유지)");
            return;
        }
        RepositionWhenReady();
    }

    /// <summary>
    /// 배치 거리 설정
    /// </summary>
    public void SetForwardDistance(float distance)
    {
        forwardDistance = distance;
        ChunaLogger.Log($"[ScenarioUIPositioner] 전방 거리 변경: {distance}m");
    }

    /// <summary>
    /// 높이 오프셋 설정
    /// </summary>
    public void SetHeightOffset(float offset)
    {
        heightOffset = offset;
        ChunaLogger.Log($"[ScenarioUIPositioner] 높이 오프셋 변경: {offset}m");
    }

    /// <summary>
    /// UI 대상 추가
    /// </summary>
    public void AddUITarget(Transform uiTarget)
    {
        if (uiTarget == null) return;

        // 배열 확장
        var newTargets = new Transform[uiTargets.Length + 1];
        for (int i = 0; i < uiTargets.Length; i++)
        {
            newTargets[i] = uiTargets[i];
        }
        newTargets[uiTargets.Length] = uiTarget;
        uiTargets = newTargets;

        ChunaLogger.Log($"[ScenarioUIPositioner] UI 대상 추가: {uiTarget.name}");
    }

    /// <summary>
    /// 현재 헤드셋 기준 목표 위치 계산
    /// </summary>
    public Vector3 CalculateTargetPosition()
    {
        if (headsetTransform == null)
        {
            ChunaLogger.LogWarning("[ScenarioUIPositioner] headsetTransform이 null입니다!");
            return Vector3.zero;
        }

        Vector3 headsetPosition = headsetTransform.position;
        Vector3 headsetForward = headsetTransform.forward;

        headsetForward.y = 0;
        headsetForward.Normalize();

        return new Vector3(
            headsetPosition.x + headsetForward.x * forwardDistance,
            headsetPosition.y + heightOffset,
            headsetPosition.z + headsetForward.z * forwardDistance
        );
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // 디버그용 Gizmo 표시
        if (headsetTransform != null)
        {
            Vector3 targetPos = CalculateTargetPosition();

            // 목표 위치 표시 (노란색 구)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetPos, 0.1f);

            // 헤드셋에서 목표 위치로의 선
            Gizmos.color = Color.green;
            Gizmos.DrawLine(headsetTransform.position, targetPos);
        }
    }
#endif
}
