using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;         // HandVisual
using Oculus.Interaction.Input;   // HandJointId

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

    [Header("발동 허용 구간 (선택)")]
    [Tooltip("이 conditionType의 SubStep에서만 근접 감지. 비우면 항상 감지. (래치는 종료까지 유지)")]
    [SerializeField] private string[] activeOnConditionTypes = { "cranialGrip" };

    [Header("반투명 제외 (항상 불투명)")]
    [Tooltip("이 이름 포함 렌더러는 반투명 제외. 옷 + 특수 셰이더(눈·각막·치아 등 — 머티리얼 교체 시 사라짐).")]
    [SerializeField] private string[] excludeNameContains =
        { "Shirt", "Jeans", "Boots", "CC_Base_Eye", "EyeOcclusion", "TearLine", "CC_Base_Teeth", "Cornea", "Tongue", "Eyelash" };

    [Header("반투명 (피부색 유지, 알파만)")]
    [Tooltip("색 보정. 흰색이면 원색 그대로.")]
    [SerializeField] private Color skinTint = Color.white;
    [Tooltip("★이 값만 조절. 0=완전투명, 1=불투명.")]
    [SerializeField, Range(0f, 1f)] private float alpha = 0.35f;
    [Tooltip("가장자리 진하게(형태 가독성). 0이면 순수 알파.")]
    [SerializeField, Range(0f, 1f)] private float rimBoost = 0f;
    [SerializeField, Range(0.2f, 8f)] private float rimPower = 2.5f;

    [Header("디버그")]
    [SerializeField] private bool debugLog = false;

    // 대상 렌더러 + Awake 캡처한 진짜 원본(골격표시 연동 차단의 핵심)
    private readonly List<Renderer> targets = new List<Renderer>();
    private readonly List<Material[]> trueOriginals = new List<Material[]>();
    private readonly List<Material[]> appliedXray = new List<Material[]>();  // 재적용용(싸움 방지)
    private readonly List<Material> createdMats = new List<Material>();
    private Shader xrayShader;
    private bool captured;
    private bool active;   // 반투명 상태(래치)
    private bool armed;    // 근접 감지 허용 여부

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
        // activeOnConditionTypes 비면 항상 armed. 아니면 해당 스텝에서만 근접 감지 시작.
        armed = (activeOnConditionTypes == null || activeOnConditionTypes.Length == 0)
                || IsTrigger(subStep.conditionType);
        if (debugLog) Debug.Log($"[CranialHeadXray] arm={armed} (conditionType='{subStep.conditionType}', active={active})");
    }

    private void HandleScenarioEnd(ScenarioData _)
    {
        Deactivate();   // 시나리오 끝나면 래치 해제 + 불투명 복원
        armed = (activeOnConditionTypes == null || activeOnConditionTypes.Length == 0);
    }

    private void Update()
    {
        if (active || !armed) return;      // 이미 켜졌으면(래치) 아무것도 안 함
        if (targets.Count == 0) return;
        if (IsHandNear()) Activate();
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

        Transform tgt = ResolveProximityTarget();
        if (tgt == null) return false;

        Vector3 p = tgt.position;
        float sqr = activateDistance * activateDistance;
        float nearest = float.MaxValue;
        bool near = false;

        for (int i = 0; i < resolvedHands.Count; i++)
        {
            var h = resolvedHands[i];
            if (h == null) continue;
            float d2 = (h.position - p).sqrMagnitude;
            if (d2 < nearest) nearest = d2;
            if (d2 <= sqr) near = true;
        }

        // 거리 튜닝용 로그(0.5초 간격)
        if (debugLog && nearest < float.MaxValue && Time.time >= nextDistanceLogTime)
        {
            nextDistanceLogTime = Time.time + 0.5f;
            Debug.Log($"[CranialHeadXray] 손↔기준점 최단 {Mathf.Sqrt(nearest) * 100f:F1}cm (발동 ≤{activateDistance * 100f:F0}cm)");
        }
        return near;
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
        var oic = StringComparison.OrdinalIgnoreCase;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            if (IsExcluded(r, oic)) continue;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) continue;

            targets.Add(r);
            trueOriginals.Add((Material[])mats.Clone()); // ★진짜 원본(불투명)
        }

        captured = true;
        if (debugLog) Debug.Log($"[CranialHeadXray] 대상 렌더러 {targets.Count}개 캡처(옷 제외).");
    }

    private bool IsExcluded(Renderer r, StringComparison oic)
    {
        string n = r.gameObject.name;
        if (excludeNameContains != null)
            foreach (var t in excludeNameContains)
                if (!string.IsNullOrEmpty(t) && n.IndexOf(t, oic) >= 0) return true;

        if (skullOverlay != null && r.transform.IsChildOf(skullOverlay.transform)) return true;

        if (gripHighlights != null)
            foreach (var g in gripHighlights)
                if (g != null && r.transform.IsChildOf(g.transform)) return true;

        return false;
    }

    [ContextMenu("Activate (반투명 ON)")]
    public void Activate()
    {
        if (active) return;
        if (!captured) CaptureTargets();
        if (targets.Count == 0) return;

        if (xrayShader == null)
        {
            xrayShader = Shader.Find("GuideChuna/HeadXray");
            if (xrayShader == null)
            {
                Debug.LogWarning("[CranialHeadXray] 셰이더 'GuideChuna/HeadXray'를 찾지 못함. (빌드 시 Always Included Shaders 등록 필요)");
                return;
            }
        }

        createdMats.Clear();
        appliedXray.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            var r = targets[i];
            if (r == null) { appliedXray.Add(null); continue; }
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

        active = true;
        if (debugLog) Debug.Log($"[CranialHeadXray] 반투명 ON(래치) — {targets.Count}개(옷 제외), alpha={alpha}");
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
        active = false;
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
            sb.AppendLine($"  {(IsExcluded(r, oic) ? "[제외]" : "[반투명]")} {r.gameObject.name}  슬롯 {r.sharedMaterials.Length}");
        Debug.Log(sb.ToString());
    }
}
