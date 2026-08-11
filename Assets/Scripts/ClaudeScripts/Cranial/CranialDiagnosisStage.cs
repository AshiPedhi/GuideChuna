using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진단 단계에서 "한 손"이 짚어야 하는 파지점 묶음.
///
/// ★ 3점 파지(엄지·검지·새끼)와 손바닥 파지는 별개 동작이다.
///    자세마다 필요한 슬롯만 채우고 나머지는 비워 둔다 — 비운 슬롯은 판정하지 않는다.
///      · 손바닥으로 감싸거나 받치는 손 → 손바닥 파지점만
///        (OM 진단1 = 양손 측두부 감싸기, OM 진단2 = 양손 후두부 베개, PM·PJ = 후두부 받치는 손)
///      · 3점 파지 하는 손             → 엄지·검지·새끼 파지점만
///    한 슬롯도 안 채우면 "이 손은 할 일 없음"으로 통과 처리된다(한손 자세 지원).
/// </summary>
[System.Serializable]
public class CranialHandGrips
{
    [Tooltip("손바닥 파지점 (손바닥으로 감싸거나 받치는 자세). 손 중앙(중지 MCP)에 큰 콜라이더가 붙는다.")]
    public GripPointTarget palmGrip;
    [Tooltip("엄지 파지점 (3점 파지)")]
    public GripPointTarget thumbGrip;
    [Tooltip("검지 파지점 (3점 파지)")]
    public GripPointTarget indexGrip;
    [Tooltip("새끼 파지점 (3점 파지)")]
    public GripPointTarget pinkyGrip;

    /// <summary>이 손에 배선된 파지점이 하나라도 있는가(전부 비었으면 판정 대상 아님).</summary>
    public bool HasAny =>
        palmGrip != null || thumbGrip != null || indexGrip != null || pinkyGrip != null;

    /// <summary>배선된 파지점을 dst에 추가(null 슬롯은 건너뜀).</summary>
    public void CollectInto(List<GripPointTarget> dst)
    {
        if (dst == null) return;
        if (palmGrip != null) dst.Add(palmGrip);
        if (thumbGrip != null) dst.Add(thumbGrip);
        if (indexGrip != null) dst.Add(indexGrip);
        if (pinkyGrip != null) dst.Add(pinkyGrip);
    }

    /// <summary>배선된 파지점 중 <b>하나라도</b> 손이 닿아 있는가(포즈 인식은 보지 않는다).
    /// 가이드손을 '손을 대면 숨기는' 판단에만 쓴다 — 판정용이 아니다.</summary>
    public bool AnyTouched()
    {
        if (palmGrip  != null && palmGrip.IsTouched)  return true;
        if (thumbGrip != null && thumbGrip.IsTouched) return true;
        if (indexGrip != null && indexGrip.IsTouched) return true;
        if (pinkyGrip != null && pinkyGrip.IsTouched) return true;
        return false;
    }

    /// <summary>배선된 파지점이 전부 접촉했는가. 배선이 하나도 없으면 true(할 일 없음).</summary>
    public bool AllGripped()
    {
        if (palmGrip  != null && !palmGrip.IsGripped)  return false;
        if (thumbGrip != null && !thumbGrip.IsGripped) return false;
        if (indexGrip != null && !indexGrip.IsGripped) return false;
        if (pinkyGrip != null && !pinkyGrip.IsGripped) return false;
        return true;
    }
}

/// <summary>
/// 진단 단계 안의 "자세" 하나 = 양손 파지점 세트.
/// 예) PM·PJ 진단은 자세 2개(ⓐ왼손 후두부+오른손 3점 / ⓑ왼손 3점+오른손 후두부)로 좌우를 번갈아 확인한다.
/// </summary>
[System.Serializable]
public class CranialDiagnosisPose
{
    [Tooltip("자세 이름(로그·디버그 표시용). 예: 양손 측두부 감싸기 / ⓐ왼손 후두부+오른손 3점")]
    public string label = "자세";

    [Tooltip("왼손이 짚어야 하는 파지점들")]
    public CranialHandGrips leftHand = new CranialHandGrips();

    [Tooltip("오른손이 짚어야 하는 파지점들")]
    public CranialHandGrips rightHand = new CranialHandGrips();

    [Header("가이드손 (선택)")]
    [Tooltip("이 자세 전용 가이드손 녹화 파일명(Resources/HandPoseData, 확장자 없이). " +
             "비우면 CSV handTrackingFileName의 substep 공용 클립을 쓴다.\n" +
             "★가이드손은 '동작(자세)마다' 켜고 끈다 — 이 자세가 시작되면 재생, 자세가 성립하면 정지, " +
             "다음 자세로 넘어가면 그 자세 것으로 다시 재생.")]
    public string guideClipName;

    [Tooltip("클립에서 이 자세에 해당하는 구간(0~1). 좌→우를 한 클립에 이어 녹화했을 때 " +
             "앞/뒤를 나눠 쓰는 용도(예: ⓐ=0~0.5, ⓑ=0.5~1). 기본 0~1 = 클립 전체.")]
    [Range(0f, 1f)] public float guideStartRatio = 0f;
    [Range(0f, 1f)] public float guideEndRatio = 1f;

    /// <summary>이 자세의 파지점 중 하나라도 손이 닿아 있는가(가이드손 숨김 판단용).</summary>
    public bool AnyTouched() =>
        (leftHand != null && leftHand.AnyTouched()) || (rightHand != null && rightHand.AnyTouched());

    /// <summary>이 자세의 양손 파지점이 전부 접촉했는가.</summary>
    public bool AllGripped()
    {
        // 양손 모두 배선이 비어 있으면 "성립"으로 오인되어 즉시 통과하므로 미배선은 미성립 처리.
        if (leftHand == null || rightHand == null) return false;
        if (!leftHand.HasAny && !rightHand.HasAny) return false;
        return leftHand.AllGripped() && rightHand.AllGripped();
    }
}

/// <summary>
/// 진단 substep 하나에 대응하는 단계 정의.
/// CSV의 conditionType=cranialTouch + conditionParams=&lt;stageId&gt; 로 이 단계가 선택된다.
///
/// 완료 조건 = <see cref="poses"/>의 **모든 자세**가 각각 CSV의 hold= 초만큼 유지되는 것.
/// 자세가 여러 개여도 **순서는 무관**하다(먼저 채운 자세는 달성으로 남는다).
/// </summary>
[System.Serializable]
public class CranialDiagnosisStage
{
    [Tooltip("CSV conditionParams와 매칭할 단계 ID. 예: 진단1, 진단2. 대소문자·앞뒤 공백 무시.")]
    public string stageId = "진단1";

    // ★holdSeconds 필드는 삭제했다(08-11 사용자 지적).
    //   유지 시간은 CSV conditionParams의 hold=만 쓴다 — 값이 씬과 CSV 두 군데 살아 있으면
    //   어느 쪽이 적용됐는지 알 수 없어(OM에서 6초 표시 / 3초 판정으로 겪음) 오판정의 원인이 된다.
    //   CSV에 hold=를 빠뜨렸을 때의 기본값은 CranialAdjustmentController.DefaultDiagnosisHoldSeconds.

    [Tooltip("켜면 이 단계 진행 중 '숨을 마시고 / 숨을 내쉬고' 호흡 유도 메시지를 표시한다. " +
             "표시할 UI(breathingCueText)가 비어 있으면 조용히 생략하고 단계는 정상 진행된다.")]
    public bool showBreathingCue = false;

    [Tooltip("이 단계에서 취해야 하는 자세들. 전부 각각 CSV의 hold= 초만큼 채워야 완료(순서 무관). " +
             "OM 진단1·진단2는 1개, PM·PJ 진단1은 2개(좌우 교대).")]
    public CranialDiagnosisPose[] poses = new CranialDiagnosisPose[0];
}
