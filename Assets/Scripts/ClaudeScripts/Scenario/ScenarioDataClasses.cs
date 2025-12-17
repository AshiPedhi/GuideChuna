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

        if (HasHandTracking())
            return AnimationPlayMode.SyncWithUser;

        return AnimationPlayMode.AutoPlay;
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