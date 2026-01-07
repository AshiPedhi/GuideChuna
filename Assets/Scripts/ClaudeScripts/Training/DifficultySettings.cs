using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChunaTraining
{
    /// <summary>
    /// 실습 난이도 레벨
    /// </summary>
    public enum DifficultyLevel
    {
        Beginner,       // 초급자 - 상세 안내 + 초급 나래이션
        Intermediate,   // 중급자 - 기본 안내 + 중급 나래이션 (다른 내용)
        Advanced        // 상급자 - 가이드 없음, 시도 횟수만 기록 (평가 전 체험)
    }

    /// <summary>
    /// 나래이션 타입 - 난이도별로 다른 나래이션 재생
    /// </summary>
    public enum NarrationType
    {
        BeginnerGuided,     // 초급: "안내에 따라 손을 올려보세요", "안내하는 방향에 맞춰..."
        IntermediateSimple  // 중급/상급: "환자의 머리를 견측 회전 시키세요", "측굴합니다"
    }

    /// <summary>
    /// 안내 항목 카테고리
    /// </summary>
    public enum GuidanceCategory
    {
        Visual,         // 시각적 안내 (가이드 핸드, 화살표 등)
        Audio,          // 음성 안내 (나레이션, 효과음)
        UI,             // UI 요소 (텍스트, 진행바 등)
        Feedback,       // 피드백 (유사도 표시, 점수 등)
        Assistance      // 보조 기능 (자동 진행, 힌트 등)
    }

    /// <summary>
    /// 개별 안내 항목 설정
    /// </summary>
    [Serializable]
    public class GuidanceItem
    {
        [Tooltip("항목 고유 ID")]
        public string itemId;

        [Tooltip("항목 이름 (에디터 표시용)")]
        public string displayName;

        [Tooltip("항목 설명")]
        [TextArea(2, 4)]
        public string description;

        [Tooltip("카테고리")]
        public GuidanceCategory category;

        [Tooltip("활성화 여부")]
        public bool isEnabled = true;

        [Tooltip("추가 설정값 (필요 시)")]
        public float value = 1f;

        public GuidanceItem() { }

        public GuidanceItem(string id, string name, string desc, GuidanceCategory cat, bool enabled = true)
        {
            itemId = id;
            displayName = name;
            description = desc;
            category = cat;
            isEnabled = enabled;
        }
    }

    /// <summary>
    /// 난이도별 안내 설정 프리셋
    /// </summary>
    [Serializable]
    public class DifficultyPreset
    {
        [Tooltip("난이도 레벨")]
        public DifficultyLevel level;

        [Tooltip("프리셋 이름")]
        public string presetName;

        [Tooltip("프리셋 설명")]
        [TextArea(2, 4)]
        public string description;

        [Header("=== 시각적 안내 (Visual) ===")]
        [Tooltip("가이드 핸드 표시")]
        public bool showGuideHands = true;

        [Tooltip("가이드 핸드 투명도 (0~1)")]
        [Range(0f, 1f)]
        public float guideHandOpacity = 0.7f;

        [Tooltip("이동 경로/화살표 표시")]
        public bool showMovementPath = true;

        [Tooltip("목표 위치 표시")]
        public bool showTargetPosition = true;

        [Tooltip("실시간 손 색상 피드백 (유사도 기반)")]
        public bool showHandColorFeedback = true;

        [Header("=== 음성 안내 (Audio) ===")]
        [Tooltip("나래이션 타입 - 난이도별로 다른 내용 재생")]
        public NarrationType narrationType = NarrationType.BeginnerGuided;

        [Tooltip("단계별 나레이션 재생")]
        public bool playNarration = true;

        [Tooltip("동작 힌트 음성")]
        public bool playHintAudio = true;

        [Tooltip("성공/실패 효과음")]
        public bool playFeedbackSound = true;

        [Header("=== UI 요소 (UI) ===")]
        [Tooltip("단계 설명 텍스트 표시")]
        public bool showStepDescription = true;

        [Tooltip("진행률 바 표시")]
        public bool showProgressBar = true;

        [Tooltip("현재 각도/위치 정보 표시")]
        public bool showPositionInfo = false;

        [Tooltip("타이머 표시")]
        public bool showTimer = false;

        [Header("=== 피드백 (Feedback) ===")]
        [Tooltip("실시간 유사도 퍼센트 표시")]
        public bool showSimilarityPercent = true;

        [Tooltip("채점 결과 상세 표시")]
        public bool showDetailedScore = true;

        [Tooltip("오류 지점 하이라이트")]
        public bool highlightErrors = true;

        [Header("=== 보조 기능 (Assistance) ===")]
        [Tooltip("자동 단계 진행 (홀드 완료 시)")]
        public bool autoAdvanceStep = true;

        [Tooltip("실패 시 자동 재시도 안내")]
        public bool showRetryGuidance = true;

        [Tooltip("손 위치 힌트 (너무 벗어났을 때)")]
        public bool showPositionHint = true;

        [Tooltip("제한 시간 없음 모드")]
        public bool unlimitedTime = true;

        [Header("=== 평가 설정 (Evaluation) ===")]
        [Tooltip("유사도 통과 기준 (0~1)")]
        [Range(0f, 1f)]
        public float similarityThreshold = 0.7f;

        [Tooltip("홀드 시간 (초)")]
        public float requiredHoldTime = 2f;

        [Tooltip("최대 시도 횟수 (0 = 무제한)")]
        public int maxAttempts = 0;

        [Header("=== 시도 횟수 추적 (상급자용) ===")]
        [Tooltip("시도 횟수 기록 여부 (기존 타임아웃 시스템에서 다음 버튼 누를 때 카운트)")]
        public bool trackAttempts = false;

        [Tooltip("평가 모드 직전 체험 (상급자)")]
        public bool isPreEvaluationMode = false;

        [Header("=== 확장 항목 ===")]
        [Tooltip("커스텀 안내 항목 (확장용)")]
        public List<GuidanceItem> customItems = new List<GuidanceItem>();

        /// <summary>
        /// 기본 프리셋 생성
        /// </summary>
        public static DifficultyPreset CreateDefault(DifficultyLevel level)
        {
            var preset = new DifficultyPreset { level = level };

            switch (level)
            {
                case DifficultyLevel.Beginner:
                    preset.presetName = "초급자";
                    preset.description = "상세 안내와 함께 단계별로 따라하기";
                    // 모든 안내 활성화
                    preset.showGuideHands = true;
                    preset.guideHandOpacity = 0.8f;
                    preset.showMovementPath = true;
                    preset.showTargetPosition = true;
                    preset.showHandColorFeedback = true;
                    // 나래이션: 초급자용 상세 안내
                    preset.narrationType = NarrationType.BeginnerGuided;
                    preset.playNarration = true;
                    preset.playHintAudio = true;
                    preset.playFeedbackSound = true;
                    preset.showStepDescription = true;
                    preset.showProgressBar = true;
                    preset.showPositionInfo = true;
                    preset.showSimilarityPercent = true;
                    preset.showDetailedScore = true;
                    preset.highlightErrors = true;
                    preset.autoAdvanceStep = true;
                    preset.showRetryGuidance = true;
                    preset.showPositionHint = true;
                    preset.unlimitedTime = true;
                    preset.similarityThreshold = 0.5f;
                    preset.requiredHoldTime = 1.5f;
                    preset.maxAttempts = 0;
                    preset.trackAttempts = false;
                    preset.isPreEvaluationMode = false;
                    break;

                case DifficultyLevel.Intermediate:
                    preset.presetName = "중급자";
                    preset.description = "기본 안내와 함께 동작 수행";
                    // 기본 안내 활성화
                    preset.showGuideHands = true;
                    preset.guideHandOpacity = 0.5f;
                    preset.showMovementPath = false;
                    preset.showTargetPosition = true;
                    preset.showHandColorFeedback = true;
                    // 나래이션: 중급자용 간단 안내 (다른 내용)
                    preset.narrationType = NarrationType.IntermediateSimple;
                    preset.playNarration = true;
                    preset.playHintAudio = false;
                    preset.playFeedbackSound = true;
                    preset.showStepDescription = true;
                    preset.showProgressBar = true;
                    preset.showPositionInfo = false;
                    preset.showSimilarityPercent = true;
                    preset.showDetailedScore = true;
                    preset.highlightErrors = false;
                    preset.autoAdvanceStep = true;
                    preset.showRetryGuidance = false;
                    preset.showPositionHint = false;
                    preset.unlimitedTime = true;
                    preset.similarityThreshold = 0.65f;
                    preset.requiredHoldTime = 2f;
                    preset.maxAttempts = 0;
                    preset.trackAttempts = false;
                    preset.isPreEvaluationMode = false;
                    break;

                case DifficultyLevel.Advanced:
                    preset.presetName = "상급자";
                    preset.description = "평가 모드 직전 체험 - 가이드 없이 시도 횟수만 기록";
                    // 가이드 없음
                    preset.showGuideHands = false;
                    preset.guideHandOpacity = 0f;
                    preset.showMovementPath = false;
                    preset.showTargetPosition = false;
                    preset.showHandColorFeedback = false;
                    // 나래이션: 중급과 동일 (간단 안내)
                    preset.narrationType = NarrationType.IntermediateSimple;
                    preset.playNarration = true;
                    preset.playHintAudio = false;
                    preset.playFeedbackSound = true;
                    preset.showStepDescription = true;
                    preset.showProgressBar = false;
                    preset.showPositionInfo = false;
                    preset.showSimilarityPercent = false;
                    preset.showDetailedScore = true;
                    preset.highlightErrors = false;
                    preset.autoAdvanceStep = false;
                    preset.showRetryGuidance = false;
                    preset.showPositionHint = false;
                    preset.unlimitedTime = false;  // 시간 제한 있음
                    preset.similarityThreshold = 0.7f;
                    preset.requiredHoldTime = 2f;
                    preset.maxAttempts = 0;
                    // 상급자: 시도 횟수 추적 (기존 타임아웃 시스템 활용)
                    preset.trackAttempts = true;
                    preset.isPreEvaluationMode = true;
                    break;
            }

            return preset;
        }
    }

    /// <summary>
    /// 난이도 설정 변경 이벤트 인자
    /// </summary>
    public class DifficultyChangedEventArgs : EventArgs
    {
        public DifficultyLevel PreviousLevel { get; set; }
        public DifficultyLevel NewLevel { get; set; }
        public DifficultyPreset NewPreset { get; set; }
    }
}
