using UnityEngine;

namespace ChunaTraining
{
    /// <summary>
    /// DifficultyManager 사용 예제
    /// 다른 시스템에서 난이도 설정을 어떻게 활용하는지 보여줌
    /// </summary>
    public class DifficultySettingsExample : MonoBehaviour
    {
        [Header("=== 예제: 가이드 핸드 연동 ===")]
        [SerializeField] private GameObject guideHandLeft;
        [SerializeField] private GameObject guideHandRight;
        [SerializeField] private Material guideHandMaterial;

        [Header("=== 예제: UI 연동 ===")]
        [SerializeField] private GameObject progressBarUI;
        [SerializeField] private GameObject similarityDisplayUI;
        [SerializeField] private GameObject stepDescriptionUI;

        [Header("=== 예제: 오디오 연동 ===")]
        [SerializeField] private AudioSource narrationAudioSource;

        void Start()
        {
            // 난이도 변경 이벤트 구독
            if (DifficultyManager.Instance != null)
            {
                DifficultyManager.Instance.OnDifficultyChanged += OnDifficultyChanged;
                ApplyCurrentSettings();
            }
        }

        void OnDestroy()
        {
            // 이벤트 구독 해제
            if (DifficultyManager.Instance != null)
            {
                DifficultyManager.Instance.OnDifficultyChanged -= OnDifficultyChanged;
            }
        }

        /// <summary>
        /// 난이도 변경 시 호출
        /// </summary>
        private void OnDifficultyChanged(object sender, DifficultyChangedEventArgs e)
        {
            Debug.Log($"[Example] 난이도 변경됨: {e.PreviousLevel} → {e.NewLevel}");
            ApplyCurrentSettings();
        }

        /// <summary>
        /// 현재 난이도 설정 적용
        /// </summary>
        private void ApplyCurrentSettings()
        {
            var dm = DifficultyManager.Instance;
            if (dm == null) return;

            // 1. 가이드 핸드 설정
            ApplyGuideHandSettings(dm);

            // 2. UI 설정
            ApplyUISettings(dm);

            // 3. 오디오 설정
            ApplyAudioSettings(dm);

            Debug.Log($"[Example] 설정 적용 완료 - {dm.CurrentPreset.presetName}");
        }

        /// <summary>
        /// 가이드 핸드 설정 적용 예제
        /// </summary>
        private void ApplyGuideHandSettings(DifficultyManager dm)
        {
            // 가이드 핸드 표시/숨김
            if (guideHandLeft != null)
                guideHandLeft.SetActive(dm.ShowGuideHands);

            if (guideHandRight != null)
                guideHandRight.SetActive(dm.ShowGuideHands);

            // 가이드 핸드 투명도 조절
            if (guideHandMaterial != null && dm.ShowGuideHands)
            {
                Color color = guideHandMaterial.color;
                color.a = dm.GuideHandOpacity;
                guideHandMaterial.color = color;
            }
        }

        /// <summary>
        /// UI 설정 적용 예제
        /// </summary>
        private void ApplyUISettings(DifficultyManager dm)
        {
            if (progressBarUI != null)
                progressBarUI.SetActive(dm.ShowProgressBar);

            if (similarityDisplayUI != null)
                similarityDisplayUI.SetActive(dm.ShowSimilarityPercent);

            if (stepDescriptionUI != null)
                stepDescriptionUI.SetActive(dm.ShowStepDescription);
        }

        /// <summary>
        /// 오디오 설정 적용 예제
        /// </summary>
        private void ApplyAudioSettings(DifficultyManager dm)
        {
            if (narrationAudioSource != null)
            {
                narrationAudioSource.mute = !dm.PlayNarration;
            }
        }

        #region 다른 시스템에서 사용하는 예제

        /// <summary>
        /// 평가 시스템에서 사용 예제
        /// </summary>
        public void ExampleEvaluationUsage()
        {
            var dm = DifficultyManager.Instance;
            if (dm == null) return;

            // 유사도 통과 기준 가져오기
            float threshold = dm.SimilarityThreshold;
            Debug.Log($"유사도 통과 기준: {threshold:P0}");

            // 홀드 시간 가져오기
            float holdTime = dm.RequiredHoldTime;
            Debug.Log($"필요 홀드 시간: {holdTime}초");

            // 최대 시도 횟수 (0 = 무제한)
            int maxAttempts = dm.MaxAttempts;
            if (maxAttempts > 0)
            {
                Debug.Log($"최대 시도 횟수: {maxAttempts}회");
            }
        }

        /// <summary>
        /// 피드백 시스템에서 사용 예제
        /// </summary>
        public void ExampleFeedbackUsage(float similarity)
        {
            var dm = DifficultyManager.Instance;
            if (dm == null) return;

            // 색상 피드백 표시 여부
            if (dm.ShowHandColorFeedback)
            {
                // 손 색상 변경 로직
                Debug.Log($"색상 피드백 활성화 - 유사도: {similarity:P0}");
            }

            // 오류 하이라이트 여부
            if (dm.HighlightErrors && similarity < dm.SimilarityThreshold)
            {
                // 오류 표시 로직
                Debug.Log("오류 하이라이트 표시");
            }

            // 재시도 안내 여부
            if (dm.ShowRetryGuidance && similarity < 0.3f)
            {
                // 재시도 안내 표시
                Debug.Log("재시도 안내 표시");
            }
        }

        /// <summary>
        /// 커스텀 항목 사용 예제
        /// </summary>
        public void ExampleCustomItemUsage()
        {
            var dm = DifficultyManager.Instance;
            if (dm == null) return;

            // 커스텀 항목 확인
            if (dm.IsCustomItemEnabled("special_hint"))
            {
                float hintDelay = dm.GetCustomItemValue("special_hint", 3f);
                Debug.Log($"특수 힌트 활성화 (지연: {hintDelay}초)");
            }

            // 카테고리별 커스텀 항목 가져오기
            var visualItems = dm.GetCustomItemsByCategory(GuidanceCategory.Visual);
            foreach (var item in visualItems)
            {
                if (item.isEnabled)
                {
                    Debug.Log($"시각 항목: {item.displayName}");
                }
            }
        }

        #endregion

        #region 테스트 메서드

        [ContextMenu("Test: Set Beginner")]
        public void TestSetBeginner()
        {
            DifficultyManager.Instance?.SetDifficulty(DifficultyLevel.Beginner);
        }

        [ContextMenu("Test: Set Intermediate")]
        public void TestSetIntermediate()
        {
            DifficultyManager.Instance?.SetDifficulty(DifficultyLevel.Intermediate);
        }

        [ContextMenu("Test: Set Advanced")]
        public void TestSetAdvanced()
        {
            DifficultyManager.Instance?.SetDifficulty(DifficultyLevel.Advanced);
        }

        #endregion
    }
}
