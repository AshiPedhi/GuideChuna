using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SubStep 데이터 (Inspector 편집 가능)
/// </summary>
[Serializable]
public class SubStepData
{
    [Header("SubStep 정보")]
    [Tooltip("SubStep 번호")]
    public int subStepNo;

    [Tooltip("소요 시간 (초) - 0이면 무제한")]
    public int duration;

    [Header("안내 내용")]
    [Tooltip("화면에 표시될 텍스트 (선택사항)")]
    [TextArea(2, 4)]
    public string textInstruction;

    [Tooltip("음성으로 안내될 내용")]
    [TextArea(3, 6)]
    public string voiceInstruction;

    [Header("핸드 트래킹")]
    [Tooltip("핸드 포즈 CSV 파일명 (.csv 확장자 제외)")]
    public string handTrackingFileName;

    [Header("진행 조건")]
    [Tooltip("조건 타입: None/HandPose/PatientAnimation/Narration/Duration/Manual")]
    public string conditionType = "None";

    [Tooltip("조건 관련 추가 파라미터 (JSON 형식 또는 간단한 문자열)")]
    public string conditionParams;

    [Header("환자 모델 애니메이션")]
    [Tooltip("환자 모델 애니메이션 클립 이름 (Animator State 이름)")]
    public string patientAnimationClip;

    [Header("이동 감지 타입")]
    [Tooltip("이동 감지 방식: position(위치 기반), rotation(회전 기반), 비어있으면 자동 감지")]
    public string movementType;

    [Header("가이드 영상 구간")]
    [Tooltip("영상 시작 시간 (분:초 형식, 예: 1:30)")]
    public string videoStartTime;

    [Tooltip("영상 끝 시간 (분:초 형식, 예: 2:45)")]
    public string videoEndTime;

    [Header("접촉 감지 부위")]
    [Tooltip("환자 접촉 감지 부위: Head, HeadAndShoulder, Chest (비어있으면 HeadAndShoulder)")]
    public string contactTarget;

    [Header("피벗 설정 (시나리오별 회전 중심)")]
    [Tooltip("피벗 부위: Neck, LeftShoulder, RightShoulder (비어있으면 Neck)")]
    public string pivotTarget;

    [Tooltip("각도 측정 평면 축: Z=측굴, Y=회전, X=굴신 (비어있으면 기본값 유지)")]
    public string pivotPlaneAxis;

    [Tooltip("각도 반전 여부: true/false (비어있으면 false)")]
    public string invertAngle;

    /// <summary>
    /// 접촉 감지 부위 Enum으로 변환 (기본값: HeadAndShoulder)
    /// </summary>
    /// <summary>
    /// contactTarget 문자열을 ContactTarget enum으로 변환
    /// 역할 기반: "PostureGuide" → 자세지시용
    /// 부위 기반: "Head", "Shoulder", "Chest" 등 직접 지정도 가능
    /// 비어있으면 null 반환 (ScenarioConfig의 기본값 사용)
    /// </summary>
    public ContactTarget? GetContactTargetOrNull()
    {
        if (string.IsNullOrEmpty(contactTarget))
            return null;

        switch (contactTarget.Trim().ToLower())
        {
            case "postureguide":
                return null; // 특수 처리: ScenarioConfig.postureGuideContactTarget 사용
            case "head":
                return ContactTarget.Head;
            case "headandshoulder":
                return ContactTarget.HeadAndShoulder;
            case "shoulder":
                return ContactTarget.Shoulder;
            case "chest":
                return ContactTarget.Chest;
            case "chestandshoulder":
                return ContactTarget.ChestAndShoulder;
            case "leftarm":
                return ContactTarget.LeftArm;
            case "rightarm":
                return ContactTarget.RightArm;
            case "back":
            case "waist":
            case "허리":
            case "등":
                return ContactTarget.Back;
            default:
                return null;
        }
    }

    /// <summary>
    /// 자세지시 스텝인지 확인
    /// </summary>
    public bool IsPostureGuideStep()
    {
        return !string.IsNullOrEmpty(contactTarget) &&
               contactTarget.Trim().Equals("PostureGuide", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 피벗 설정이 있는지 확인
    /// </summary>
    public bool HasPivotTarget() => !string.IsNullOrEmpty(pivotTarget);

    /// <summary>
    /// 피벗 평면 축을 RotationDetectionAxis로 변환 (기본값: 변경 없음을 의미하는 null 반환)
    /// </summary>
    public ChunaPathEvaluator.RotationDetectionAxis? GetPivotPlaneAxis()
    {
        if (string.IsNullOrEmpty(pivotPlaneAxis))
            return null;

        switch (pivotPlaneAxis.Trim().ToUpper())
        {
            case "X":
                return ChunaPathEvaluator.RotationDetectionAxis.X;
            case "Y":
                return ChunaPathEvaluator.RotationDetectionAxis.Y;
            case "Z":
                return ChunaPathEvaluator.RotationDetectionAxis.Z;
            default:
                return null;
        }
    }

    /// <summary>
    /// 각도 반전 여부 (기본값: false)
    /// </summary>
    public bool GetInvertAngle()
    {
        if (string.IsNullOrEmpty(invertAngle))
            return false;

        return invertAngle.Trim().ToLower() == "true";
    }

    /// <summary>
    /// 가이드 영상 구간이 있는지 확인
    /// </summary>
    public bool HasVideoSegment() => !string.IsNullOrEmpty(videoStartTime) && !string.IsNullOrEmpty(videoEndTime);

    /// <summary>
    /// 영상 시작 시간을 초 단위로 변환
    /// </summary>
    public float GetVideoStartSeconds()
    {
        return ParseTimeToSeconds(videoStartTime);
    }

    /// <summary>
    /// 영상 끝 시간을 초 단위로 변환
    /// </summary>
    public float GetVideoEndSeconds()
    {
        return ParseTimeToSeconds(videoEndTime);
    }

    /// <summary>
    /// 분:초 형식을 초 단위로 변환
    /// 지원 형식: "1:30", "1-30", "90" (초 단위)
    /// 엑셀에서 시간으로 인식하는 문제 때문에 "-" 구분자도 지원
    /// </summary>
    private float ParseTimeToSeconds(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return 0f;

        // 공백 제거
        timeStr = timeStr.Trim();

        // ":" 또는 "-" 구분자 지원
        char[] separators = { ':', '-' };
        string[] parts = timeStr.Split(separators);

        if (parts.Length == 2)
        {
            // 분:초 또는 분-초 형식
            if (int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                return minutes * 60f + seconds;
            }
        }
        else if (parts.Length == 1)
        {
            // 초만 있는 경우 (예: "90")
            if (float.TryParse(parts[0], out float seconds))
            {
                return seconds;
            }
        }

        return 0f;
    }

    /// <summary>
    /// 핸드 트래킹이 있는지 확인
    /// </summary>
    public bool HasHandTracking() => !string.IsNullOrEmpty(handTrackingFileName);

    /// <summary>
    /// 환자 애니메이션이 있는지 확인
    /// </summary>
    public bool HasPatientAnimation() => !string.IsNullOrEmpty(patientAnimationClip);

    /// <summary>
    /// 나레이션 클립이 있는지 확인 (voiceInstruction을 클립 파일명으로 사용)
    /// </summary>
    public bool HasNarration() => !string.IsNullOrEmpty(voiceInstruction);

    /// <summary>
    /// 애니메이션 재생 모드 결정
    /// - 핸드 트래킹 O + 애니메이션 O → 진행도 동기화
    /// - 핸드 트래킹 X + 애니메이션 O → 자동 재생
    /// - 애니메이션 X → 없음
    /// </summary>
    public AnimationPlayMode GetAnimationPlayMode()
    {
        if (!HasPatientAnimation())
            return AnimationPlayMode.None;

        // touchOnce 단계의 손 녹화는 "어디를 터치하는지" 보여주는 가이드 손 전용이다.
        // 진행은 접촉 1회 + 자동 재생이 담당하므로 SyncWithUser(진행도 스크럽)로 넘기면 안 된다.
        if (HasTouchOnce())
            return AnimationPlayMode.AutoPlay;

        if (HasHandTracking())
            return AnimationPlayMode.SyncWithUser;

        return AnimationPlayMode.AutoPlay;
    }

    /// <summary>conditionParams에 touchOnce(최초 접촉으로 래치)가 지정됐는지</summary>
    public bool HasTouchOnce()
    {
        return !string.IsNullOrEmpty(conditionParams) &&
               conditionParams.ToLower().Contains("touchonce");
    }
}

/// <summary>
/// 애니메이션 재생 모드
/// </summary>
public enum AnimationPlayMode
{
    None,           // 애니메이션 없음
    AutoPlay,       // 자동 재생
    SyncWithUser    // 사용자 진행도에 동기화
}

/// <summary>
/// 환자 접촉 감지 부위
/// </summary>
public enum ContactTarget
{
    Head,               // 머리만 (경추 추나)
    HeadAndShoulder,    // 머리+어깨 - 기본값
    Shoulder,           // 어깨만 (견갑거근, 상부승모근)
    Chest,              // 흉부만 (사각근, 흉쇄유돌근, 대흉근)
    ChestAndShoulder,   // 흉부+어깨 (보조수 등 넓은 범위)
    LeftArm,            // 왼팔 (견갑거근 자세지시 등)
    RightArm,           // 오른팔
    // ※ 새 값은 반드시 끝에 추가할 것 — ScenarioConfig에 int로 직렬화돼 있어
    //    중간에 끼우면 기존 시나리오의 접촉 부위가 통째로 밀린다.
    Back                // 등·허리(흉추) - 복잡추나 흉추/늑골 술기용. 머리·어깨와 별개 콜라이더.
}

/// <summary>
/// Step 데이터 (Inspector 편집 가능)
/// </summary>
[Serializable]
public class StepData
{
    [Header("Step 정보")]
    [Tooltip("Step 번호 (0=가이드, 1~5=실제 단계)")]
    public int stepNo;

    [Tooltip("Step 이름 (예: 가이드, 평가, 세판상박회인)")]
    public string stepName;

    [Header("SubSteps")]
    [Tooltip("이 Step에 포함된 SubStep 목록")]
    public List<SubStepData> subSteps = new List<SubStepData>();

    /// <summary>
    /// 가이드 Step인지 확인
    /// </summary>
    public bool IsGuideStep() => stepNo == 0;
}

/// <summary>
/// Phase 데이터 (Inspector 편집 가능)
/// </summary>
[Serializable]
public class PhaseData
{
    [Header("Phase 정보")]
    [Tooltip("Phase 이름 (예: 평가, 중부, 전부, 후부)")]
    public string phaseName;

    [Header("Steps")]
    [Tooltip("이 Phase에 포함된 Step 목록")]
    public List<StepData> steps = new List<StepData>();
}

/// <summary>
/// 시나리오 데이터 (Inspector 편집 가능)
/// </summary>
[Serializable]
public class ScenarioData
{
    [Header("시나리오 정보")]
    [Tooltip("시나리오 번호")]
    public int scenarioNo;

    [Tooltip("시나리오 이름 (예: 상부승모근)")]
    public string scenarioName;

    [Header("Phases")]
    [Tooltip("이 시나리오에 포함된 Phase 목록")]
    public List<PhaseData> phases = new List<PhaseData>();
}

/// <summary>
/// 시나리오 컬렉션 (여러 시나리오 관리)
/// </summary>
[Serializable]
public class ScenarioCollection
{
    public List<ScenarioData> scenarios = new List<ScenarioData>();
}