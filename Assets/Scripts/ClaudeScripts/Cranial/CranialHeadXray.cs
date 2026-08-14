using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;         // HandVisual
using Oculus.Interaction.Input;   // HandJointId
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 환자에 "사용자 손이 근접"하면 환자를 통으로 반투명(피부색 유지, 알파만)으로 만들어
/// 두개골(skullOverlay) 파지점을 가이드한다. ★한번 켜지면 손이 멀어져도 유지(래치).
///
/// 규칙:
/// - 손(handPoints)이 proximityTarget에 activateDistance 이내로 들어오면 반투명 ON.
/// - 한번 ON 되면 손이 멀어져도 유지. OFF는 시나리오 종료(또는 arm 스텝 이탈, 강제 원복)에서만.
/// - 환자 하위 모든 렌더러를 반투명하되 "옷(Shirt/Jeans/Boots)"·skullOverlay·gripHighlights는 제외(불투명).
/// - 각 렌더러 원래 디퓨즈 텍스처를 복사한 반투명 인스턴스로 교체 → 피부색 유지, 알파만.
///
/// ★골격표시(PracticeSettingsController) 연동 무조건 차단:
/// - Awake(골격표시 Start보다 먼저)에서 "진짜 불투명 원본"을 캡처.
/// - 반투명/복원 모두 이 캡처본 기준 → 골격표시가 런타임에 뭘 바꿔도 크래니얼이 우선(자기 원본으로 덮음),
///   복원도 진짜 불투명으로. 생성 인스턴스는 HideAndDontSave라 씬에 저장 안 됨.
/// </summary>
[DisallowMultipleComponent]
public class CranialHeadXray : MonoBehaviour
{
    [Header("환자 / 오버레이 참조")]
    [Tooltip("환자 모델 루트. 비우면 태그 \"Patient\" 자동 탐색.")]
    [SerializeField] private Transform patientRoot;
    [Tooltip("반투명 동안 켤 두개골(뼈) 모델. 평소엔 비활성.")]
    [SerializeField] private GameObject skullOverlay;
    [Tooltip("함께 켤 파지 위치 표시(선택).")]
    [SerializeField] private GameObject[] gripHighlights;

    [Header("손 근접 트리거")]
    [Tooltip("(선택) 손 위치를 직접 지정. 비워두면 아래 HandVisual/자동탐색으로 해결됨.")]
    [SerializeField] private Transform[] handPoints;
    [Tooltip("(선택) 라이브 HandVisual. 비우면 씬에서 자동 탐색.")]
    [SerializeField] private HandVisual[] handVisuals;
    [Tooltip("handPoints·handVisuals가 비었을 때 씬의 라이브 HandVisual을 자동으로 찾는다. (권장 ON)")]
    [SerializeField] private bool autoFindHands = true;
    [Tooltip("근접 거리를 잴 기준점(보통 환자 머리). 비우면 아래 이름으로 머리 본 자동탐색 → skullOverlay → patientRoot 순 폴백.")]
    [SerializeField] private Transform proximityTarget;
    [Tooltip("proximityTarget이 비었을 때 환자 하위에서 찾을 머리 본 이름(부분일치).")]
    [SerializeField] private string headBoneNameContains = "CC_Base_Head";
    [Tooltip("이 거리(m) 이내로 손이 들어오면 반투명 ON.")]
    [SerializeField] private float activateDistance = 0.2f;

    [Header("시작 시 자동 발동")]
    [Tooltip("씬 시작(Play)하자마자 손 근접 없이 바로 xray ON(피부 투명 + 골격 표시). 골격 추종 테스트/데모용.")]
    [SerializeField] private bool activateOnStart = false;
    [Tooltip("active 상태에서 매 프레임 xray 머티리얼을 재적용해, 골격표시(PracticeSettings) 등이 " +
             "런타임에 피부 머티리얼을 되돌려도 xray가 유지되게 한다.")]
    [SerializeField] private bool reassertWhileActive = true;

    [Header("발동 허용 구간")]
    [Tooltip("xray를 쓰는 SubStep의 conditionType 목록. 여기 없는 단계(진단3·재평가·시작/종료 안내 등 " +
             "conditionType이 빈 단계)로 넘어가면 xray를 끄고 환자를 원복한다.\n" +
             "★비우면 '모든 단계에서 허용'이 되어 원복이 일어나지 않는다 — 반드시 채울 것.")]
    [SerializeField] private string[] activeOnConditionTypes =
        { "cranialTouch", "cranialGrip", "cranialPressure", "cranialDepthBreath" };

    [Tooltip("CSV conditionParams에 이 토큰이 있으면 conditionType과 무관하게 xray를 쓰고, " +
             "손 근접을 기다리지 않고 단계 진입 즉시 켠다.\n" +
             "★근접 기준점이 '환자 머리'라, 손이 머리에서 멀리 떨어지는 술기(흉추 교정 = 손이 등에 있음)는 " +
             "근접 트리거가 영영 성립하지 않기 때문이다. 빈 문자열이면 이 기능을 끈다.")]
    [SerializeField] private string forceXrayParamToken = "xray";

    [Header("단계마다 원복")]
    [Tooltip("★기본 ON. **xray를 쓰지 않는 단계로 넘어갈 때** xray를 끄고 환자 모델을 원래 모습으로 되돌린다.\n" +
             "xray를 쓰는 단계끼리 연속될 때는 끄지 않고 그대로 유지한다(깜빡임 방지).\n" +
             "끄면 한번 켜진 xray가 시나리오 끝까지 래치된다.")]
    [SerializeField] private bool restoreEachSubStep = true;
    [Tooltip("단계 전환으로 원복한 뒤 근접 감지를 다시 켜기까지의 대기 시간(초). " +
             "0이면 손이 아직 머리에 있을 때 같은 프레임에 다시 켜져서 원복이 안 보인다(깜빡임).")]
    [SerializeField] private float rearmDelaySeconds = 0.6f;

    [Header("반투명 제외 (항상 불투명)")]
    [Tooltip("이 이름 포함 렌더러는 반투명·숨김 모두 제외 = 항상 불투명 유지. 옷(Shirt/Jeans/Boots)만.")]
    [SerializeField] private string[] excludeNameContains = { "Shirt", "Jeans", "Boots" };

    [Tooltip("CSV conditionParams로 xray를 강제한 단계에서만 함께 반투명해지는 옷.\n" +
             "흉추 술기는 등 뒤 파지점이 상의에 가려 안 보이므로 Shirt만 넣는다.\n" +
             "바지·신발은 볼 필요가 없어 제외하고, 손 근접으로 켜지는 두개골 xray는 영향 없다.")]
    [SerializeField] private string[] forcedTransparentNameContains = { "Shirt" };

    [Header("반투명 중 숨김 (머리카락 + 눈·이빨 등 얼굴 특수부위)")]
    [Tooltip("xray ON 동안 완전히 숨길 렌더러 이름(부분일치). OFF 시 자동 복원. " +
             "머리카락 + 눈·눈그림자·눈물선·치아·각막·혀·눈꺼풀(특수 셰이더라 반투명이 안 먹어 떠 보이므로 숨김). " +
             "저장/재컴파일 직전엔 EditorSafety가 복원하므로 씬에 꺼짐이 굳지 않는다.")]
    [SerializeField] private string[] hideWhenActiveNameContains =
        { "Undercut_fade", "Half_up", "CC_Base_Eye", "EyeOcclusion", "TearLine", "CC_Base_Teeth", "Cornea", "Tongue", "Eyelash" };

    [Header("반투명 (피부색 유지, 알파만)")]
    [Tooltip("색 보정. 흰색이면 원색 그대로.")]
    [SerializeField] private Color skinTint = Color.white;
    [Tooltip("★이 값만 조절. 0=완전투명, 1=불투명.")]
    [SerializeField, Range(0f, 1f)] private float alpha = 0.35f;
    [Tooltip("가장자리 진하게(형태 가독성). 0이면 순수 알파.")]
    [SerializeField, Range(0f, 1f)] private float rimBoost = 0f;
    [SerializeField, Range(0.2f, 8f)] private float rimPower = 2.5f;

    [Tooltip("반투명 셰이더(GuideChuna/HeadXray). 비워 두면 Shader.Find로 찾는다.\n" +
             "★빌드에서 xray가 안 되면 이게 원인이다 — 어떤 머티리얼도 참조하지 않는 셰이더는 빌드에서 " +
             "제거돼 Shader.Find가 null을 돌려준다. 여기에 직접 할당하면(=씬이 셰이더를 참조하므로) 확실히 포함된다. " +
             "Project Settings > Graphics > Always Included Shaders 등록도 같은 효과.")]
    [SerializeField] private Shader xrayShaderAsset;

    [Header("디버그")]
    [SerializeField] private bool debugLog = false;

    // 대상 렌더러 + Awake 캡처한 진짜 원본(골격표시 연동 차단의 핵심)
    private readonly List<Renderer> targets = new List<Renderer>();
    private readonly List<Material[]> trueOriginals = new List<Material[]>();
    private readonly List<Material[]> appliedXray = new List<Material[]>();  // 재적용용(싸움 방지)
    private readonly List<bool> isClothing = new List<bool>();               // targets와 인덱스 정렬
    private readonly List<Material> createdMats = new List<Material>();
    private readonly List<Renderer> hideTargets = new List<Renderer>();  // 반투명 중 숨길 대상(머리카락 등)
    private readonly List<Renderer> hiddenByMe = new List<Renderer>();   // 실제로 내가 끈 것만(정확 복원)
    /// <summary>이 substep이 conditionParams 옵트인으로 xray를 강제하는가(근접 트리거 생략).</summary>
    private bool forceOnThisSubStep;
    private Shader xrayShader;
    private bool captured;
    private bool active;   // 반투명 상태(래치)
    private bool armed;    // 근접 감지 허용 여부
    private float rearmAt; // 이 시각 전까지는 근접 감지 보류(단계 전환 원복이 보이도록)

    /// <summary>현재 반투명(xray)이 켜져 있는지. 에디터 세이프티가 저장 직전 판단에 사용.</summary>
    public bool IsXrayActive => active;

    private void Awake()
    {
        CaptureTargets(); // ★골격표시(Start)보다 먼저 → 진짜 불투명 원본 확보
        armed = (activeOnConditionTypes == null || activeOnConditionTypes.Length == 0);
    }

    private void Start()
    {
        // 다른 Start(골격표시 캐싱 등) 이후 한 번 더 늦게 켜기 위해 다음 프레임에 발동.
        if (activateOnStart) StartCoroutine(ActivateNextFrame());
    }

    private System.Collections.IEnumerator ActivateNextFrame()
    {
        yield return null;               // 골격표시 Start의 머티리얼 캐싱/적용이 끝난 뒤
        Activate();
        if (debugLog) Debug.Log("[CranialHeadXray] activateOnStart → 시작 시 xray ON");
    }

    private void OnEnable()
    {
        var ev = ScenarioEventSystem.Instance;
        ev.OnSubStepStarted += HandleSubStepStarted;
        ev.OnScenarioCompleted += HandleScenarioEnd;
    }

    private void OnDisable()
    {
        var ev = ScenarioEventSystem.Instance;
        ev.OnSubStepStarted -= HandleSubStepStarted;
        ev.OnScenarioCompleted -= HandleScenarioEnd;
        Deactivate();
    }

    private void HandleSubStepStarted(SubStepData subStep)
    {
        if (subStep == null) return;

        // 이 단계가 xray를 쓰는 단계인가. (목록이 비면 '항상 허용' = 예전 동작)
        bool listEmpty = (activeOnConditionTypes == null || activeOnConditionTypes.Length == 0);
        // ★conditionParams 옵트인: conditionType이 목록에 없어도 xray를 쓰고, 근접 없이 즉시 켠다.
        bool forced = !string.IsNullOrEmpty(forceXrayParamToken) &&
                      !string.IsNullOrEmpty(subStep.conditionParams) &&
                      subStep.conditionParams.IndexOf(forceXrayParamToken,
                                                      StringComparison.OrdinalIgnoreCase) >= 0;
        bool wantsXray = listEmpty || forced || IsTrigger(subStep.conditionType);

        // ★2026-08-12 사용자 지시: "전환할 때 환자 불투명 필요 없음. 그냥 반투명한 상태로 유지해."
        //   PJ의 '전환'처럼 판정 없이 안내만 하는 단계는 conditionType이 비어 있어 wantsXray=false가 되고,
        //   그 순간 환자가 불투명으로 튀었다가 다음 단계에서 다시 반투명이 됐다.
        //   판정이 없는 단계는 '중간 다리'일 뿐이므로 현재 상태를 그대로 이어간다.
        bool narrationOnly = string.IsNullOrWhiteSpace(subStep.conditionType);
        bool keepThrough = narrationOnly && active;

        armed = wantsXray || keepThrough;
        forceOnThisSubStep = forced;

        // ★ xray를 **안 쓰는** 단계(진단3·재평가·종료 등)로 넘어갈 때만 환자 모델을 원복한다.
        //   xray 단계끼리 연속될 때는 그대로 유지 — 매 단계 껐다 켜면 깜빡이고 골격 관찰이 끊긴다.
        if (restoreEachSubStep && active && !wantsXray && !keepThrough)
        {
            Deactivate();
            rearmAt = Time.time + Mathf.Max(0f, rearmDelaySeconds);   // 손이 아직 머리에 있어도 곧바로 재점등되지 않게
            if (debugLog) Debug.Log($"[CranialHeadXray] xray 미사용 단계 진입 — 환자 모델 원복 (재감지까지 {rearmDelaySeconds:0.0}초)");
        }

        if (debugLog)
            Debug.Log($"[CranialHeadXray] arm={armed} xray사용단계={wantsXray} " +
                      $"(conditionType='{subStep.conditionType}', active={active})");
    }

    private void HandleScenarioEnd(ScenarioData _)
    {
        Deactivate();   // 시나리오 끝나면 래치 해제 + 불투명 복원
        forceOnThisSubStep = false;
        armed = (activeOnConditionTypes == null || activeOnConditionTypes.Length == 0);
    }

    private void Update()
    {
        if (active || !armed) return;      // 이미 켜졌으면(래치) 아무것도 안 함
        if (Time.time < rearmAt) return;   // 단계 전환 직후: 원복이 보이도록 잠시 재감지 보류
        if (targets.Count == 0) return;
        // 옵트인 단계는 손 근접을 기다리지 않는다(손이 머리 근처에 오지 않는 술기 대응).
        if (forceOnThisSubStep || IsHandNear()) Activate();
    }

    private void LateUpdate()
    {
        // active인데 다른 시스템(골격표시 등)이 피부 머티리얼을 되돌렸으면 xray를 다시 씌운다.
        if (!active || !reassertWhileActive) return;
        for (int i = 0; i < targets.Count && i < appliedXray.Count; i++)
        {
            var r = targets[i];
            var xray = appliedXray[i];
            if (r == null || xray == null || xray.Length == 0) continue;
            var cur = r.sharedMaterials;
            if (cur == null || cur.Length != xray.Length || cur[0] != xray[0])
                r.sharedMaterials = xray;   // 드리프트 감지 시에만 재적용(대개 no-op)
        }
    }

    // === 손 위치 해석 (인스펙터 배선 없이도 동작) ===
    private readonly List<Transform> resolvedHands = new List<Transform>();
    private float nextHandScanTime;
    private float nextDistanceLogTime;

    /// <summary>손 위치 목록 확보. handPoints → handVisuals → 씬 자동탐색 순.
    /// 라이브 손 조인트는 런타임에 생성/채워지므로 확보될 때까지 주기적으로 재시도한다.</summary>
    private void EnsureHands()
    {
        for (int i = resolvedHands.Count - 1; i >= 0; i--)
            if (resolvedHands[i] == null) resolvedHands.RemoveAt(i);   // 파괴된 손 정리
        if (resolvedHands.Count > 0) return;
        if (Time.time < nextHandScanTime) return;
        nextHandScanTime = Time.time + 0.5f;

        // ① 직접 지정한 트랜스폼
        if (handPoints != null)
            foreach (var h in handPoints)
                if (h != null) resolvedHands.Add(h);
        if (resolvedHands.Count > 0) { LogHands("handPoints"); return; }

        // ② 지정한 HandVisual
        if (handVisuals != null)
            foreach (var hv in handVisuals)
                AddHandVisual(hv);
        if (resolvedHands.Count > 0) { LogHands("handVisuals"); return; }

        // ③ 씬 자동탐색 — 라이브 HandVisual만(Hand 데이터소스 있고 활성).
        //    녹화/재생용 손은 Hand가 없거나 enabled=false라 걸러진다. (ChunaLimitChecker와 동일 방식)
        if (!autoFindHands) return;
        var found = FindObjectsByType<HandVisual>(FindObjectsSortMode.None);
        foreach (var hv in found)
        {
            if (hv == null || !hv.isActiveAndEnabled || hv.Hand == null) continue;
            AddHandVisual(hv);
        }
        if (resolvedHands.Count > 0) LogHands("자동탐색");
    }

    /// <summary>HandVisual에서 손 중앙(중지 MCP) 조인트를 손 위치로 사용. 조인트 미준비면 추가 안 함(다음 스캔 재시도).</summary>
    private void AddHandVisual(HandVisual hv)
    {
        if (hv == null || hv.Joints == null) return;
        int j = (int)HandJointId.HandMiddle1;
        if (j >= 0 && j < hv.Joints.Count && hv.Joints[j] != null)
            resolvedHands.Add(hv.Joints[j]);
    }

    private void LogHands(string how)
    {
        if (debugLog) Debug.Log($"[CranialHeadXray] 손 위치 {resolvedHands.Count}개 확보({how}).");
    }

    /// <summary>근접 기준점: proximityTarget → 머리 본(이름 부분일치) → skullOverlay → 환자 루트.
    /// skullOverlay는 환자 루트 자식 프리팹이라 피벗이 머리가 아닐 수 있어 머리 본을 우선한다.</summary>
    private Transform resolvedProximity;
    private Transform ResolveProximityTarget()
    {
        if (proximityTarget != null) return proximityTarget;
        if (resolvedProximity != null) return resolvedProximity;

        Transform root = ResolveRoot();
        if (root != null && !string.IsNullOrEmpty(headBoneNameContains))
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf(headBoneNameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                resolvedProximity = t;
                if (debugLog) Debug.Log($"[CranialHeadXray] 근접 기준점 = 머리 본 '{t.name}'");
                return resolvedProximity;
            }
        }

        resolvedProximity = skullOverlay != null ? skullOverlay.transform : root;
        if (debugLog && resolvedProximity != null)
            Debug.Log($"[CranialHeadXray] 근접 기준점 = 폴백 '{resolvedProximity.name}' (머리 본 '{headBoneNameContains}' 못 찾음)");
        return resolvedProximity;
    }

    private bool IsHandNear()
    {
        EnsureHands();
        if (resolvedHands.Count == 0) return false;

        float sqr = activateDistance * activateDistance;
        float nearest = float.MaxValue;
        bool near = false;

        // ★기준점 = 머리 본 <b>+ 지금 켜져 있는 파지점들</b> (2026-08-12).
        //   예전엔 머리 하나뿐이라 두개골에서만 쓸모가 있었다. 늑골은 쇄골 밑, 흉추는 등이라
        //   손이 파지점에 가 있어도 머리에서 멀어 xray가 안 켜졌다.
        //   → 이 술기에서 실제로 잡아야 하는 지점 근처면 켜지게 한다.
        CollectProximityPoints();

        for (int i = 0; i < resolvedHands.Count; i++)
        {
            var h = resolvedHands[i];
            if (h == null) continue;
            for (int t = 0; t < proximityPoints.Count; t++)
            {
                Transform tp = proximityPoints[t];
                if (tp == null) continue;
                float d2 = (h.position - tp.position).sqrMagnitude;
                if (d2 < nearest) nearest = d2;
                if (d2 <= sqr) near = true;
            }
        }
        if (proximityPoints.Count == 0) return false;

        // 거리 튜닝용 로그(0.5초 간격)
        if (debugLog && nearest < float.MaxValue && Time.time >= nextDistanceLogTime)
        {
            nextDistanceLogTime = Time.time + 0.5f;
            Debug.Log($"[CranialHeadXray] 손↔기준점({proximityPoints.Count}개) 최단 " +
                      $"{Mathf.Sqrt(nearest) * 100f:F1}cm (발동 ≤{activateDistance * 100f:F0}cm)");
        }
        return near;
    }

    private readonly List<Transform> proximityPoints = new List<Transform>();

    /// <summary>근접 판정 기준점을 모은다 — 머리 본 + 현재 활성화된 파지점 전부.</summary>
    private void CollectProximityPoints()
    {
        proximityPoints.Clear();

        Transform head = ResolveProximityTarget();
        if (head != null) proximityPoints.Add(head);

        // 켜져 있는 파지 구체만 — 그 단계에서 실제로 잡아야 하는 지점이다.
        // ★UnityEngine.Object로 명시 — 이 파일은 using System이 있어 Object가 모호해진다.
        foreach (GripPointTarget g in UnityEngine.Object.FindObjectsByType<GripPointTarget>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (g != null) proximityPoints.Add(g.transform);
    }

    private bool IsTrigger(string conditionType)
    {
        if (string.IsNullOrEmpty(conditionType) || activeOnConditionTypes == null) return false;
        for (int i = 0; i < activeOnConditionTypes.Length; i++)
            if (string.Equals(activeOnConditionTypes[i]?.Trim(), conditionType.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private Transform ResolveRoot()
    {
        if (patientRoot != null) return patientRoot;
        GameObject tagged = null;
        try { tagged = GameObject.FindWithTag("Patient"); } catch { /* 태그 미정의 무시 */ }
        return tagged != null ? tagged.transform : null;
    }

    /// <summary>대상 렌더러 수집 + 진짜 불투명 원본 캡처(옷·오버레이·하이라이트 제외).</summary>
    private void CaptureTargets()
    {
        if (captured) return;
        Transform root = ResolveRoot();
        if (root == null)
        {
            if (debugLog) Debug.LogWarning("[CranialHeadXray] 환자 루트를 찾지 못함.");
            return;
        }

        targets.Clear();
        trueOriginals.Clear();
        hideTargets.Clear();
        isClothing.Clear();
        CacheRigRoots();   // 두개골 리그(파지 구체 등)를 xray 대상에서 빼기 위해
        var oic = StringComparison.OrdinalIgnoreCase;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;

            // ★상의(Shirt)만 '함께 캡처'해 두고, 실제 반투명 적용 여부는 단계마다 Activate에서 정한다
            //   (캡처는 Awake에 한 번뿐이라 이때 빼 버리면 나중에 투명하게 만들 수단이 없다).
            //   바지·신발은 예전처럼 항상 불투명 — 캡처 대상에서 아예 뺀다.
            bool forcedClothing = NameContainsAny(r, forcedTransparentNameContains, oic);

            // ★제외 검사를 먼저 — 리그(파지 구체 등)가 숨김 토큰에 우연히 걸려 사라지는 일 방지.
            if (IsExcluded(r, oic, allowClothing: forcedClothing)) continue;
            // 머리카락 등: 반투명 대상이 아니라 "숨김" 대상. (반투명 인스턴스 생성 안 함)
            if (NameContainsAny(r, hideWhenActiveNameContains, oic)) { hideTargets.Add(r); continue; }
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) continue;

            targets.Add(r);
            trueOriginals.Add((Material[])mats.Clone()); // ★진짜 원본(불투명)
            isClothing.Add(forcedClothing);   // true = 강제 xray 단계에서만 반투명
        }

        captured = true;
        if (debugLog) Debug.Log($"[CranialHeadXray] 대상 렌더러 {targets.Count}개 캡처(옷 제외), 숨김 대상 {hideTargets.Count}개(머리카락).");
    }

    private static bool NameContainsAny(Renderer r, string[] tokens, StringComparison oic)
    {
        if (r == null || tokens == null) return false;
        string n = r.gameObject.name;
        foreach (var t in tokens)
            if (!string.IsNullOrEmpty(t) && n.IndexOf(t, oic) >= 0) return true;
        return false;
    }

    /// <summary>씬의 두개골 리그(CranialAdjustmentController) 루트들 — xray 대상에서 제외할 서브트리.
    /// ★리그가 환자 모델(c9) 하위(CC_Base_Head 밑)에 붙어 있어서, 막지 않으면
    ///   파지 구체까지 xray 머티리얼로 바뀌어 파지 색(초록/흰색)이 안 보인다.</summary>
    private Transform[] rigRoots;

    private void CacheRigRoots()
    {
        var rigs = FindObjectsByType<CranialAdjustmentController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var list = new List<Transform>(rigs != null ? rigs.Length : 0);
        if (rigs != null)
            foreach (var rig in rigs)
                if (rig != null) list.Add(rig.transform);
        rigRoots = list.ToArray();
    }

    private bool IsExcluded(Renderer r, StringComparison oic) => IsExcluded(r, oic, false);

    /// <summary><paramref name="allowClothing"/>=true면 옷도 대상에 포함시킨다(캡처 단계에서 사용).</summary>
    private bool IsExcluded(Renderer r, StringComparison oic, bool allowClothing)
    {
        if (!allowClothing && NameContainsAny(r, excludeNameContains, oic)) return true;

        // ★골격(skeletal_system)은 xray 대상이 아니다 — 2026-08-12.
        //   xray는 '피부를 투과해 뼈를 보는' 기능인데, 뼈까지 같이 반투명으로 덮어쓰고 있었다.
        //   그 탓에 ①뼈가 흐려져 관찰이 어렵고 ②부위별 색상이 반투명 인스턴스로 교체돼 사라졌다.
        for (Transform t = r.transform; t != null; t = t.parent)
            if (t.name.IndexOf("skeletal_system", oic) >= 0) return true;

        if (skullOverlay != null && r.transform.IsChildOf(skullOverlay.transform)) return true;

        if (gripHighlights != null)
            foreach (var g in gripHighlights)
                if (g != null && r.transform.IsChildOf(g.transform)) return true;

        // ★ 두개골 리그 하위(파지 구체·깊이 가이드·리듬 지표·호흡 HUD 등)는 전부 제외.
        //   이걸 안 막으면 xray가 파지 구체 머티리얼까지 교체하고 reassertWhileActive가 매 프레임 덮어써서
        //   "xray 켜면 파지 색이 안 변한다"가 된다.
        if (rigRoots != null)
            foreach (var t in rigRoots)
                if (t != null && r.transform.IsChildOf(t)) return true;

        // 리그 밖에 따로 놓인 파지점까지 안전하게 제외(부모 어딘가에 GripPointTarget이 있으면 파지 구체다).
        if (r.GetComponentInParent<GripPointTarget>(true) != null) return true;

        return false;
    }

    [ContextMenu("Activate (반투명 ON)")]
    public void Activate()
    {
        if (active) return;
        // 에디트 모드 미리보기도 허용. 씬 저장/재컴파일 직전에는 CranialHeadXrayEditorSafety가
        // 자동으로 Deactivate(원본 복원)하므로, 임시 머티리얼이 씬에 None으로 굳는 사고는 발생하지 않는다.
        if (!captured) CaptureTargets();
        if (targets.Count == 0) return;

        if (xrayShader == null)
        {
            // 인스펙터 직접 할당(빌드 포함 보장) → 이름 탐색 순으로 해석한다.
            xrayShader = xrayShaderAsset != null ? xrayShaderAsset : Shader.Find("GuideChuna/HeadXray");
            if (xrayShader == null)
            {
                // ★에디터에선 항상 찾아지지만 빌드에선 null이 될 수 있다(참조 없는 셰이더는 빌드에서 제거됨).
                //   Project Settings > Graphics > Always Included Shaders에 등록하거나
                //   위 xrayShaderAsset에 직접 할당하면 해결된다. 조용히 실패하면 원인 추적이 어려워 Error로 남긴다.
                Debug.LogError("[CranialHeadXray] 셰이더 'GuideChuna/HeadXray'를 찾지 못해 xray를 켤 수 없습니다. " +
                               "빌드라면 Graphics 설정의 Always Included Shaders 등록 또는 xrayShaderAsset 직접 할당이 필요합니다.");
                return;
            }
        }

        createdMats.Clear();
        appliedXray.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            var r = targets[i];
            if (r == null) { appliedXray.Add(null); continue; }

            // 상의는 CSV로 xray를 강제한 단계(= 흉추)에서만 반투명. 그 외에는 불투명 유지.
            if (i < isClothing.Count && isClothing[i] && !forceOnThisSubStep)
            {
                appliedXray.Add(null);   // 인덱스 정렬 유지(재적용 루프가 targets와 짝을 맞춘다)
                continue;
            }

            var orig = trueOriginals[i]; // ★항상 진짜 원본 기준

            var swapped = new Material[orig.Length];
            for (int s = 0; s < orig.Length; s++)
            {
                if (orig[s] == null) { swapped[s] = null; continue; }
                var inst = MakeTransparent(orig[s]);
                swapped[s] = inst;
                createdMats.Add(inst);
            }
            r.sharedMaterials = swapped;
            appliedXray.Add(swapped);   // 재적용용 보관
        }

        if (skullOverlay != null) skullOverlay.SetActive(true);
        SetHighlights(true);
        HideHair(true);   // 머리카락 숨김(반투명 중)

        active = true;
        if (debugLog) Debug.Log($"[CranialHeadXray] 반투명 ON(래치) — {targets.Count}개(옷 제외), 머리카락 {hiddenByMe.Count}개 숨김, alpha={alpha}");
    }

    [ContextMenu("Deactivate (반투명 OFF)")]
    public void Deactivate()
    {
        if (!active) return;
        RestoreAll();

        if (skullOverlay != null) skullOverlay.SetActive(false);
        SetHighlights(false);

        active = false;
        if (debugLog) Debug.Log("[CranialHeadXray] 반투명 OFF — 원복");
    }

    /// <summary>진짜 원본으로 복원(골격표시가 무슨 짓을 했든 불투명 원본으로).</summary>
    [ContextMenu("★ 강제 원복 (불투명 복구)")]
    private void RestoreAll()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var r = targets[i];
            if (r != null && i < trueOriginals.Count && trueOriginals[i] != null)
                r.sharedMaterials = trueOriginals[i];
        }
        for (int i = 0; i < createdMats.Count; i++)
            if (createdMats[i] != null) DestroySafe(createdMats[i]);
        createdMats.Clear();
        appliedXray.Clear();
        HideHair(false);   // 머리카락 복원
        active = false;
    }

    /// <summary>머리카락 등 hideTargets를 숨기거나(on=true) 복원(false)한다.
    /// ★숨김은 Play 중에만 — 에디트 모드에서 renderer.enabled=false가 씬에 저장되어 눌어붙는 것 방지.
    /// 복원은 언제든 안전(내가 끈 것만 다시 켬).</summary>
    private void HideHair(bool on)
    {
        if (on)
        {
            // 에디트 모드에서도 숨기되, 저장/재컴파일 직전에 EditorSafety가 Deactivate→복원하므로
            // renderer.enabled=false가 씬에 굳지 않는다.
            hiddenByMe.Clear();
            foreach (var r in hideTargets)
                if (r != null && r.enabled) { r.enabled = false; hiddenByMe.Add(r); }
        }
        else
        {
            foreach (var r in hiddenByMe)
                if (r != null) r.enabled = true;
            hiddenByMe.Clear();
        }
    }

    /// <summary>원본 머티리얼의 디퓨즈 텍스처를 복사한 반투명 인스턴스.</summary>
    private Material MakeTransparent(Material src)
    {
        var m = new Material(xrayShader);
        m.hideFlags = HideFlags.HideAndDontSave; // ★씬에 저장 안 됨

        Texture tex = null;
        if (src.HasProperty("_DiffuseMap")) tex = src.GetTexture("_DiffuseMap");
        if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
        if (tex == null && src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
        if (tex != null) m.SetTexture("_MainTex", tex);

        m.SetColor("_Color", skinTint);
        m.SetFloat("_Alpha", alpha);
        m.SetFloat("_RimBoost", rimBoost);
        m.SetFloat("_RimPower", rimPower);
        return m;
    }

    private static void DestroySafe(UnityEngine.Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private void SetHighlights(bool on)
    {
        if (gripHighlights == null) return;
        foreach (var g in gripHighlights)
            if (g != null) g.SetActive(on);
    }

    /// <summary>손 해석 결과 + 현재 거리 덤프(진단). Play 중 실행 권장.</summary>
    [ContextMenu("진단: 손 근접 상태")]
    private void DumpHandProximity()
    {
        resolvedHands.Clear();
        nextHandScanTime = 0f;
        resolvedProximity = null;
        EnsureHands();

        Transform tgt = ResolveProximityTarget();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CranialHeadXray] armed={armed} active={active} 기준점='{(tgt != null ? tgt.name : "없음")}' 발동거리={activateDistance * 100f:F0}cm");
        sb.AppendLine($"  손 위치 {resolvedHands.Count}개 (handPoints={(handPoints?.Length ?? 0)}, handVisuals={(handVisuals?.Length ?? 0)}, 자동탐색={autoFindHands})");
        foreach (var h in resolvedHands)
        {
            if (h == null) continue;
            string d = tgt != null ? $"{Vector3.Distance(h.position, tgt.position) * 100f:F1}cm" : "-";
            sb.AppendLine($"    {h.name} → {d}");
        }
        if (resolvedHands.Count == 0)
            sb.AppendLine("    ⚠ 손을 못 찾음 — Play 중이 아니거나(조인트 미생성) 라이브 HandVisual이 비활성. Play 중에 다시 실행해 보세요.");
        Debug.Log(sb.ToString());
    }

    /// <summary>눈·이빨·머리카락 등이 어떤 이유로 꺼진 채 남았을 때(씬에 눌어붙음 포함) 강제로 다시 켠다.
    /// 실행 후 씬을 저장하면 눌어붙은 renderer.enabled=0이 정리된다.</summary>
    [ContextMenu("★ 눈·이빨·머리카락 다시 켜기")]
    private void ForceShowFaceAndHair()
    {
        Transform root = ResolveRoot();
        if (root == null) { Debug.LogWarning("[CranialHeadXray] 환자 루트를 찾지 못함."); return; }
        var oic = StringComparison.OrdinalIgnoreCase;
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if ((NameContainsAny(r, excludeNameContains, oic) || NameContainsAny(r, hideWhenActiveNameContains, oic))
                && !r.enabled) { r.enabled = true; n++; }
        }
        hiddenByMe.Clear();
        Debug.Log($"[CranialHeadXray] 눈·이빨·머리카락 렌더러 {n}개 다시 켬. (에디트 모드면 씬 저장 필요)");
    }

    /// <summary>환자 하위 렌더러/반투명 대상 여부 덤프(진단).</summary>
    [ContextMenu("진단: 렌더러/머티리얼 덤프")]
    private void DumpRenderers()
    {
        Transform root = ResolveRoot();
        if (root == null) { Debug.LogWarning("[CranialHeadXray] 환자 루트를 찾지 못함."); return; }
        var rs = root.GetComponentsInChildren<Renderer>(true);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CranialHeadXray] 환자='{root.name}', 렌더러 {rs.Length}개");
        var oic = StringComparison.OrdinalIgnoreCase;
        foreach (var r in rs)
        {
            string tag = NameContainsAny(r, hideWhenActiveNameContains, oic) ? "[숨김]"
                       : IsExcluded(r, oic) ? "[제외]" : "[반투명]";
            sb.AppendLine($"  {tag} {r.gameObject.name}  슬롯 {r.sharedMaterials.Length}  enabled={r.enabled}");
        }
        Debug.Log(sb.ToString());
    }
}

#if UNITY_EDITOR
/// <summary>
/// ★에디트 모드에서 xray가 켜진 채로 씬을 저장하거나 스크립트가 재컴파일되면
/// 임시(HideAndDontSave) 머티리얼 참조가 씬에 None(fileID:0)으로 굳어 렌더러가 마젠타로 깨진다.
/// 이를 막기 위해 "저장/재컴파일 직전"에 활성 xray를 모두 Deactivate(원본 복원)하고,
/// 저장 후에는 다시 Activate해 미리보기를 유지한다. → 씬 파일엔 항상 원본 머티리얼이 저장됨.
/// </summary>
[InitializeOnLoad]
static class CranialHeadXrayEditorSafety
{
    static readonly List<CranialHeadXray> suspended = new List<CranialHeadXray>();

    static CranialHeadXrayEditorSafety()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
    }

    // 저장 직전: 활성 xray를 원본으로 되돌려 두고(그 상태가 파일에 기록됨) 기억해 둔다.
    static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (Application.isPlaying) return;
        suspended.Clear();
        foreach (var x in UnityEngine.Object.FindObjectsByType<CranialHeadXray>(FindObjectsSortMode.None))
            if (x != null && x.IsXrayActive) { x.Deactivate(); suspended.Add(x); }
    }

    // 저장 후: 미리보기 복원(다시 반투명 ON).
    static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
    {
        if (Application.isPlaying) { suspended.Clear(); return; }
        foreach (var x in suspended) if (x != null) x.Activate();
        suspended.Clear();
    }

    // 재컴파일 직전: trueOriginals(비직렬화)가 소실되기 전에 원본 복원.
    static void OnBeforeReload()
    {
        if (Application.isPlaying) return;
        foreach (var x in UnityEngine.Object.FindObjectsByType<CranialHeadXray>(FindObjectsSortMode.None))
            if (x != null && x.IsXrayActive) x.Deactivate();
    }
}
#endif
