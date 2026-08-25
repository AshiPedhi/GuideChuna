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

    /// <summary>
    /// 특정 시나리오에서 특정 UI만 <b>헤드셋 기준 배치를 건너뛰고</b> 정해진 자리에 못박는다.
    /// 경추ROM처럼 벽면에 붙여 두고 쓰는 화면을 위한 것이다.
    /// </summary>
    [System.Serializable]
    public class FixedPlacement
    {
        [Tooltip("이 시나리오일 때만 적용한다(ScenarioConfig.scenarioName과 같은 값). 비우면 모든 시나리오.")]
        public string scenarioName = "경추ROM측정";

        [Tooltip("고정할 UI 루트. ★uiTargets에 들어 있지 않아도 된다 — 이 목록만으로 배치한다.")]
        public Transform target;

        [Tooltip("월드 위치")]
        public Vector3 position = new Vector3(0f, 1.5f, 3.25f);

        [Tooltip("월드 회전 (오일러)")]
        public Vector3 eulerAngles = Vector3.zero;

        [Tooltip("균일 스케일. 끄면 스케일은 손대지 않는다.")]
        public bool applyScale = true;
        public float scale = 8f;
    }

    [Header("=== 시나리오별 고정 배치 ===")]
    [Tooltip("여기 걸린 대상은 그 시나리오에서 헤드셋 기준 배치를 <b>건너뛰고</b> 적힌 자리에 고정된다.\n" +
             "★비워 두면 종전과 완전히 같다 — 다른 술기에는 아무 영향이 없다.\n" +
             "위치를 손으로 잡은 뒤 컨텍스트 메뉴 '고정 배치 — 현재 씬 값 담기'를 쓰면 값이 채워진다.")]
    [SerializeField] private FixedPlacement[] fixedPlacements;

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

        // ★고정 배치는 여기서 바로 건다. ScenarioBootstrapper가 Awake(-100)에서 시나리오를
        //   정하므로 이 시점에 이미 알 수 있다. 예전엔 ScenarioManager가 채워지길 기다리느라
        //   정보패널이 헤드셋 기준으로 한 번 떴다가 <b>모드를 고르고 진입한 뒤에야</b>
        //   고정 자리로 튀었다.
        if (fixedPlacements != null && fixedPlacements.Length > 0)
        {
            fixedAppliedEarly = ApplyFixedPlacementsAll() > 0;
        }
    }

    /// <summary>Awake에서 이미 고정 배치를 걸었는가. 걸었으면 기다리는 코루틴이 필요 없다.</summary>
    private bool fixedAppliedEarly;

    void Start()
    {
        // ★고정 배치는 헤드셋과 <b>무관하게</b> 따로 건다.
        //   예전엔 ApplyPositionInternal 안에 넣었는데, 그 함수는 헤드셋 Y가 0.5~2.5m를
        //   벗어나면 대상 루프에 닿기도 전에 return false 한다. 에디터에서 헤드셋 없이
        //   바로 Play로 들어가면 카메라 높이가 그 범위를 벗어나 고정 배치까지 통째로
        //   건너뛰어졌다(2026-08-25 사용자 확인). 고정 배치는 헤드셋이 필요 없다.
        // Awake에서 이미 걸렸으면 기다릴 게 없다. 부트스트래퍼가 없는 예외 상황에서만 코루틴이 돈다.
        if (!fixedAppliedEarly && fixedPlacements != null && fixedPlacements.Length > 0)
        {
            StartCoroutine(ApplyFixedWhenScenarioKnown());
        }

        if (autoPositionOnStart)
        {
            // Start() 시점엔 OVR 트래킹이 아직 헤드셋 위치를 갱신하지 않아 (0,0,0)에 머무는 경우가 많음
            // → 헤드셋 Y가 유효한 값으로 점프할 때까지 대기 후 배치
            headsetRoutine = StartCoroutine(PositionWhenHeadsetReady());
        }
    }

    /// <summary>
    /// 시나리오 이름이 잡히면 고정 배치를 건다. 헤드셋 트래킹과 무관하게 돈다.
    /// ★로비를 거치지 않고 에디터에서 바로 Play로 들어가도 여기서 걸린다.
    /// </summary>
    private IEnumerator ApplyFixedWhenScenarioKnown()
    {
        const float maxWait = 10f;
        float waited = 0f;

        while (waited < maxWait)
        {
            if (!string.IsNullOrEmpty(CurrentScenarioName()))
            {
                int applied = ApplyFixedPlacementsAll();
                if (applied == 0)
                {
                    ChunaLogger.Log($"[ScenarioUIPositioner] 시나리오 '{CurrentScenarioName()}'에 걸린 고정 배치가 없습니다 — 평소대로 헤드셋 기준 배치합니다.");
                }
                yield break;
            }
            yield return null;
            waited += Time.deltaTime;
        }

        ChunaLogger.LogWarning($"[ScenarioUIPositioner] ⚠ {maxWait:F0}s 안에 시나리오 이름을 못 잡아 고정 배치를 건너뜁니다. " +
                               "ScenarioBootstrapper가 도는지 확인하세요.");
    }

    /// <summary>지금 시나리오에 걸린 고정 배치를 전부 적용한다. 적용 건수를 돌려준다.</summary>
    private int ApplyFixedPlacementsAll()
    {
        if (fixedPlacements == null) return 0;

        string current = CurrentScenarioName();
        int applied = 0;

        foreach (var p in fixedPlacements)
        {
            if (p == null || p.target == null) continue;
            if (!string.IsNullOrEmpty(p.scenarioName) && p.scenarioName != current) continue;

            p.target.SetPositionAndRotation(p.position, Quaternion.Euler(p.eulerAngles));
            if (p.applyScale) p.target.localScale = Vector3.one * p.scale;
            applied++;

            ChunaLogger.Log($"[ScenarioUIPositioner] 📌 '{p.target.name}' 고정 배치 — 시나리오 '{current}' · " +
                            $"위치 {p.position} · 회전 {p.eulerAngles} · " +
                            $"스케일 {(p.applyScale ? p.scale.ToString("F2") : "유지")} (헤드셋 기준 배치 건너뜀)");
        }
        return applied;
    }

    private Coroutine headsetRoutine;

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

        // ★고정 배치가 걸려 있으면 시나리오 이름이 잡힐 때까지 조금 더 기다린다.
        //   헤드셋은 안정됐는데 ScenarioBootstrapper가 아직 CSV를 안 물렸으면
        //   시나리오를 모른 채 헤드셋 기준으로 배치해 버리고 그대로 잠긴다.
        if (fixedPlacements != null && fixedPlacements.Length > 0)
        {
            const float maxScenarioWait = 3f;
            float waited = 0f;
            while (string.IsNullOrEmpty(CurrentScenarioName()) && waited < maxScenarioWait)
            {
                yield return null;
                waited += Time.deltaTime;
            }
            if (waited > 0f)
            {
                ChunaLogger.Log($"[ScenarioUIPositioner] 시나리오 확인까지 {waited:F2}s 대기 — " +
                                $"'{CurrentScenarioName()}'");
            }
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

            // ★고정 배치가 걸린 대상은 헤드셋 기준 계산을 건너뛴다.
            if (ApplyFixedPlacement(uiTarget)) continue;

            uiTarget.position = targetPosition;

            if (lookAtHeadset)
            {
                Vector3 lookDirection = targetPosition - headsetPosition;
                lookDirection.y = 0;
                if (lookDirection.sqrMagnitude > 0.001f)
                    uiTarget.rotation = Quaternion.LookRotation(lookDirection);
            }
        }

        // ★고정 대상을 다시 못박는다.
        //   고정 대상이 uiTargets의 <b>자식</b>일 수 있다(실제 배선이 그렇다 —
        //   uiTargets=UI Group, 고정 대상=그 아래 정보패널Root). 부모를 헤드셋 쪽으로
        //   돌리면 자식의 월드 회전이 같이 돌아가서, 위치·크기는 남고 각도만 시선을 따라갔다.
        //   같은 오브젝트일 때만 건너뛰는 걸로는 이 경우를 못 막는다.
        ApplyFixedPlacementsAll();
        return true;
    }

    /// <summary>
    /// 이 대상에 지금 시나리오용 고정 배치가 걸려 있으면 적용하고 true.
    /// 걸린 게 없으면 false — 부르는 쪽이 평소대로 헤드셋 기준 배치를 한다.
    /// </summary>
    private bool ApplyFixedPlacement(Transform uiTarget)
    {
        FixedPlacement placement = FindFixedPlacement(uiTarget);
        if (placement == null) return false;

        // 이미 ApplyFixedWhenScenarioKnown이 걸어 뒀을 것이다. 여기서는 헤드셋 기준
        // 배치가 그 위를 덮어쓰지 않게 막는 역할만 한다.
        uiTarget.SetPositionAndRotation(placement.position, Quaternion.Euler(placement.eulerAngles));
        if (placement.applyScale) uiTarget.localScale = Vector3.one * placement.scale;
        return true;
    }

    private FixedPlacement FindFixedPlacement(Transform uiTarget)
    {
        if (fixedPlacements == null || fixedPlacements.Length == 0) return null;

        string current = CurrentScenarioName();
        for (int i = 0; i < fixedPlacements.Length; i++)
        {
            FixedPlacement p = fixedPlacements[i];
            if (p == null || p.target != uiTarget) continue;
            if (!string.IsNullOrEmpty(p.scenarioName) && p.scenarioName != current) continue;
            return p;
        }
        return null;
    }

    /// <summary>
    /// 이번 씬의 시나리오 이름. 아직 안 잡혔으면 빈 문자열.
    ///
    /// ★<b>부트스트래퍼를 먼저 본다.</b> 그쪽은 Awake(-100)에서 이미 정하는데,
    ///   <see cref="ScenarioManager.CurrentScenario"/>는 사용자가 실습/평가 모드를 고르고
    ///   진입한 뒤에야 채워진다. 후자를 기다렸더니 정보패널이 헤드셋 기준으로 한 번 떴다가
    ///   모드를 고른 뒤에야 고정 자리로 튀었다(2026-08-25 사용자 확인).
    /// </summary>
    private string CurrentScenarioName()
    {
        ScenarioConfig config = ScenarioBootstrapper.SelectedConfig;
        if (config != null && !string.IsNullOrEmpty(config.scenarioName)) return config.scenarioName;

        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        ScenarioData data = scenarioManager != null ? scenarioManager.CurrentScenario : null;
        return data != null ? data.scenarioName : string.Empty;
    }

    private ScenarioManager scenarioManager;

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

        // ★StopAllCoroutines를 쓰면 안 된다. 고정 배치 코루틴까지 같이 죽는다
        //   (예전 주석의 "이 컴포넌트는 PositionWhenHeadsetReady만 돌린다"는 이제 사실이 아니다).
        if (headsetRoutine != null) StopCoroutine(headsetRoutine);
        headsetRoutine = StartCoroutine(PositionWhenHeadsetReady());

        // 고정 배치는 헤드셋을 기다릴 이유가 없으니 지금 바로 다시 건다.
        ApplyFixedPlacementsAll();

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
    /// <summary>
    /// 씬에서 UI를 손으로 원하는 자리에 놓은 뒤 이걸 부르면 그 값이 고정 배치에 담긴다.
    /// 좌표를 손으로 받아 적다 틀리는 것보다 이쪽이 확실하다.
    /// </summary>
    [ContextMenu("고정 배치 — 현재 씬 값 담기")]
    private void CaptureFixedPlacements()
    {
        if (fixedPlacements == null || fixedPlacements.Length == 0)
        {
            ChunaLogger.LogWarning("[ScenarioUIPositioner] 고정 배치 항목이 없습니다. 먼저 추가하고 target을 지정하세요.");
            return;
        }

        int captured = 0;
        foreach (var p in fixedPlacements)
        {
            if (p == null || p.target == null) continue;
            p.position = p.target.position;
            p.eulerAngles = p.target.eulerAngles;
            p.scale = p.target.localScale.x;
            captured++;
            ChunaLogger.Log($"  {p.target.name} ← 위치 {p.position} · 회전 {p.eulerAngles} · 스케일 {p.scale:F2}");
        }
        UnityEditor.EditorUtility.SetDirty(this);
        ChunaLogger.Log($"[ScenarioUIPositioner] 고정 배치 {captured}건을 현재 씬 값으로 담았습니다. 씬을 저장하세요.");
    }

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
