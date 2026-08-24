using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChunaTraining
{
    /// <summary>
    /// 난이도 관리자
    /// 실습 난이도에 따른 안내 항목 on/off 및 설정 관리
    ///
    /// 사용법:
    /// 1. 씬에 빈 GameObject 생성 후 이 컴포넌트 추가
    /// 2. Inspector에서 각 난이도별 프리셋 설정
    /// 3. SetDifficulty()로 난이도 변경
    /// 4. 각 시스템에서 IsEnabled() / GetValue()로 설정 확인
    /// </summary>
    public class DifficultyManager : MonoBehaviour
    {
        public static DifficultyManager Instance { get; private set; }

        [Header("=== 현재 난이도 ===")]
        [SerializeField] private DifficultyLevel currentLevel = DifficultyLevel.Beginner;

        [Header("=== 진행 방식 (난이도와 별개 축) ===")]
        [Tooltip("교육 = 가상 환자로 과정을 익힌다(기존). 실측 = 패스스루에서 실제 환자를 잰다.\n" +
                 "난이도(초급~평가)는 교육 안에서만 의미가 있다.")]
        [SerializeField] private ScenarioMode currentMode = ScenarioMode.Education;

        [Header("=== 난이도별 프리셋 ===")]
        [SerializeField] private DifficultyPreset beginnerPreset;
        [SerializeField] private DifficultyPreset intermediatePreset;
        [SerializeField] private DifficultyPreset advancedPreset;
        [SerializeField] private DifficultyPreset evaluationPreset;

        [Header("=== 디버그 ===")]
        [SerializeField] private bool showDebugLogs = true;

        // 현재 활성 프리셋
        private DifficultyPreset currentPreset;

        // 이벤트
        public event EventHandler<DifficultyChangedEventArgs> OnDifficultyChanged;

        // 프로퍼티
        public DifficultyLevel CurrentLevel => currentLevel;
        public DifficultyPreset CurrentPreset => currentPreset;

        /// <summary>진행 방식(교육/실측). 난이도와 별개 축이다.</summary>
        public ScenarioMode CurrentMode => currentMode;
        public bool IsMeasurementMode => currentMode == ScenarioMode.Measurement;

        /// <summary>
        /// 진행 방식 변경.
        /// ★시나리오 진입 <b>전에</b> 정해져 있어야 한다 — phase 필터가 CSV 로드 시점에 1회만
        ///   적용되므로(ScenarioManager.LoadFromCSV), 실습 도중에 바꾸면 반영되지 않는다.
        /// </summary>
        public void SetMode(ScenarioMode newMode)
        {
            if (currentMode == newMode) return;
            currentMode = newMode;
            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[DifficultyManager] 진행 방식 변경: {newMode}</color>");
        }

        void Awake()
        {
            // 싱글톤 설정
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // 기본 프리셋 초기화
            InitializePresets();

            // 현재 난이도에 맞는 프리셋 적용
            ApplyPreset(currentLevel);
        }

        /// <summary>
        /// 기본 프리셋 초기화 (Inspector에서 설정 안 된 경우)
        /// </summary>
        private void InitializePresets()
        {
            if (beginnerPreset == null || string.IsNullOrEmpty(beginnerPreset.presetName))
            {
                beginnerPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Beginner);
            }

            if (intermediatePreset == null || string.IsNullOrEmpty(intermediatePreset.presetName))
            {
                intermediatePreset = DifficultyPreset.CreateDefault(DifficultyLevel.Intermediate);
            }

            if (advancedPreset == null || string.IsNullOrEmpty(advancedPreset.presetName))
            {
                advancedPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Advanced);
            }

            if (evaluationPreset == null || string.IsNullOrEmpty(evaluationPreset.presetName))
            {
                evaluationPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Evaluation);
            }
        }

        /// <summary>
        /// 난이도 변경
        /// </summary>
        public void SetDifficulty(DifficultyLevel newLevel)
        {
            if (currentLevel == newLevel) return;

            var previousLevel = currentLevel;
            currentLevel = newLevel;
            ApplyPreset(newLevel);

            // 이벤트 발생
            OnDifficultyChanged?.Invoke(this, new DifficultyChangedEventArgs
            {
                PreviousLevel = previousLevel,
                NewLevel = newLevel,
                NewPreset = currentPreset
            });

            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=yellow>[DifficultyManager] 난이도 변경: {previousLevel} → {newLevel}</color>");
            }
        }

        /// <summary>
        /// 프리셋 적용
        /// </summary>
        private void ApplyPreset(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Beginner:
                    currentPreset = beginnerPreset;
                    break;
                case DifficultyLevel.Intermediate:
                    currentPreset = intermediatePreset;
                    break;
                case DifficultyLevel.Advanced:
                    currentPreset = advancedPreset;
                    break;
                case DifficultyLevel.Evaluation:
                    currentPreset = evaluationPreset;
                    break;
            }

            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=cyan>[DifficultyManager] 프리셋 적용: {currentPreset.presetName}</color>");
                ChunaLogger.Log($"  - 가이드 핸드: {currentPreset.showGuideHands}");
                ChunaLogger.Log($"  - 나래이션 타입: {currentPreset.narrationType}");
                ChunaLogger.Log($"  - 시도 횟수 추적: {currentPreset.trackAttempts}");
                ChunaLogger.Log($"  - 평가 전 체험: {currentPreset.isPreEvaluationMode}");
            }
        }

        /// <summary>
        /// 특정 레벨의 프리셋 가져오기
        /// </summary>
        public DifficultyPreset GetPreset(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Beginner:
                    return beginnerPreset;
                case DifficultyLevel.Intermediate:
                    return intermediatePreset;
                case DifficultyLevel.Advanced:
                    return advancedPreset;
                case DifficultyLevel.Evaluation:
                    return evaluationPreset;
                default:
                    return beginnerPreset;
            }
        }

        #region 편의 메서드 - 시각적 안내

        public bool ShowGuideHands => currentPreset?.showGuideHands ?? true;
        public float GuideHandOpacity => currentPreset?.guideHandOpacity ?? 0.7f;
        public bool ShowHandColorFeedback => currentPreset?.showHandColorFeedback ?? true;

        #endregion

        #region 편의 메서드 - 음성 안내

        public NarrationType NarrationType => currentPreset?.narrationType ?? NarrationType.BeginnerGuided;
        public bool PlayNarration => currentPreset?.playNarration ?? true;
        public bool PlayHintAudio => currentPreset?.playHintAudio ?? true;
        public bool PlayFeedbackSound => currentPreset?.playFeedbackSound ?? true;

        #endregion

        #region 편의 메서드 - UI

        public bool ShowStepDescription => currentPreset?.showStepDescription ?? true;
        public bool ShowProgressBar => currentPreset?.showProgressBar ?? true;

        #endregion

        #region 편의 메서드 - 피드백

        public bool ShowSimilarityPercent => currentPreset?.showSimilarityPercent ?? true;
        public bool ShowDetailedScore => currentPreset?.showDetailedScore ?? true;
        public bool HighlightErrors => currentPreset?.highlightErrors ?? true;

        #endregion

        #region 편의 메서드 - 보조 기능

        public bool AutoAdvanceStep => currentPreset?.autoAdvanceStep ?? true;
        public bool UnlimitedTime => currentPreset?.unlimitedTime ?? true;

        #endregion

        #region 편의 메서드 - 평가 설정

        public float SimilarityThreshold => currentPreset?.similarityThreshold ?? 0.7f;
        public float RequiredHoldTime => currentPreset?.requiredHoldTime ?? 2f;
        public int MaxAttempts => currentPreset?.maxAttempts ?? 0;
        public bool TrackAttempts => currentPreset?.trackAttempts ?? false;
        public bool IsPreEvaluationMode => currentPreset?.isPreEvaluationMode ?? false;
        public bool IsEvaluationMode => currentLevel == DifficultyLevel.Evaluation;
        public bool IsOfficialScore => currentLevel == DifficultyLevel.Evaluation;

        #endregion

        #region 커스텀 항목 접근

        /// <summary>
        /// 커스텀 항목이 활성화되어 있는지 확인
        /// </summary>
        public bool IsCustomItemEnabled(string itemId)
        {
            if (currentPreset?.customItems == null) return false;

            var item = currentPreset.customItems.Find(x => x.itemId == itemId);
            return item?.isEnabled ?? false;
        }

        /// <summary>
        /// 커스텀 항목의 값 가져오기
        /// </summary>
        public float GetCustomItemValue(string itemId, float defaultValue = 0f)
        {
            if (currentPreset?.customItems == null) return defaultValue;

            var item = currentPreset.customItems.Find(x => x.itemId == itemId);
            return item?.value ?? defaultValue;
        }

        /// <summary>
        /// 커스텀 항목 가져오기
        /// </summary>
        public GuidanceItem GetCustomItem(string itemId)
        {
            return currentPreset?.customItems?.Find(x => x.itemId == itemId);
        }

        /// <summary>
        /// 카테고리별 커스텀 항목 목록 가져오기
        /// </summary>
        public List<GuidanceItem> GetCustomItemsByCategory(GuidanceCategory category)
        {
            if (currentPreset?.customItems == null) return new List<GuidanceItem>();

            return currentPreset.customItems.FindAll(x => x.category == category);
        }

        #endregion

        #region 에디터 유틸리티

        /// <summary>
        /// 기본 프리셋으로 리셋 (에디터용)
        /// </summary>
        [ContextMenu("Reset to Default Presets")]
        public void ResetToDefaultPresets()
        {
            beginnerPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Beginner);
            intermediatePreset = DifficultyPreset.CreateDefault(DifficultyLevel.Intermediate);
            advancedPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Advanced);
            evaluationPreset = DifficultyPreset.CreateDefault(DifficultyLevel.Evaluation);
            ApplyPreset(currentLevel);
            ChunaLogger.Log("[DifficultyManager] 기본 프리셋으로 리셋됨");
        }

        /// <summary>
        /// 난이도 순환 (테스트용)
        /// </summary>
        [ContextMenu("Cycle Difficulty")]
        public void CycleDifficulty()
        {
            var nextLevel = (DifficultyLevel)(((int)currentLevel + 1) % 4);
            SetDifficulty(nextLevel);
        }

        #endregion
    }
}
