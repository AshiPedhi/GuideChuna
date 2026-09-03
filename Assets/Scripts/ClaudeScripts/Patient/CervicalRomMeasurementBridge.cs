using System.Collections.Generic;
using UnityEngine;
using ChunaTraining;

/// <summary>
/// 실측모드 전용 브리지. CSV의 <b>phase = 실측</b> 단계와 <see cref="CervicalRomRealityMeasure"/>를 잇는다.
///
/// ★교육모드의 <see cref="CervicalRomScenarioBridge"/>와 <b>완전히 별개</b>다.
///   그쪽은 대본 각도를 모델에 얹는 물건이고, 이쪽은 실제 사람을 잰다.
///   모드는 DifficultyManager가 가르고, 어느 단계를 돌릴지는 ScenarioConfig의
///   measurementPhases 화이트리스트가 이미 걸러 준다 — 여기서 또 거르지 않는다.
///
/// 진행 방식 — 측정기가 '양손 정지'로 스스로 확정하고, 브리지는 그 결과를 보고 substep을 넘긴다.
///   기준정렬 : 십자축이 잡히면      → 다음
///   파지     : 0점이 잡히면          → 다음
///   능동     : 능동 끝점이 기록되면  → 다음
///   압박     : 압박 끝점이 기록되면  → 다음
///   중립복귀 : 중립 근처로 돌아오면  → 다음
/// </summary>
public class CervicalRomMeasurementBridge : MonoBehaviour
{
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private CervicalRomRealityMeasure measure;
    [SerializeField] private ChunaPathEvaluator evaluator;

    [Tooltip("표준자세 체크리스트. 실측에서는 안 쓴다(2026-09-01) — 교육모드용으로 남는다.")]
    [SerializeField] private PostureChecklistUI checklist;

    [Tooltip("실습 각도기. 실측에 들어오면 출처를 측정기로 갈아 끼우고, 나갈 때 되돌린다.\n" +
             "★비우면 자동 탐색. 못 찾으면 각도기 없이 진행한다(경고만 남긴다).")]
    [SerializeField] private CervicalRomPlaneGauge planeGauge;

    [Header("=== 현실 전환 (2026-08-31) ===")]
    [Tooltip("패스스루·환자 표시를 쥔 컨트롤러. 비우면 자동 탐색.")]
    [SerializeField] private PracticeSettingsController practiceSettings;

    [Tooltip("실측 중에 <b>추가로</b> 숨길 오브젝트.\n" +
             "★배경(방 모델) 안에 있는 것은 패스스루가 배경째 끄므로 여기 넣을 필요가 없다.")]
    [SerializeField] private GameObject[] alsoHideInMeasurement;

    [Tooltip("이름으로 찾아 숨길 오브젝트. 배선 없이 동작한다.\n\n" +
             "기본값 '추나 테이블' = Assets/_JDH/추나 테이블.fbx 인스턴스로,\n" +
             "위치초기화 오브젝트/GameObject/ChunaObject 아래에 있다. 배경 밖이라 패스스루로는 안 꺼진다.\n\n" +
             "★이름이 바뀌면 조용히 못 찾는다 — 그때는 경고 로그가 뜨니 그걸 보고 고칠 것.")]
    [SerializeField] private string[] hideByNameInMeasurement = { "추나 테이블" };

    [Tooltip("이 이름의 시나리오에서만 돈다.")]
    [SerializeField] private string scenarioName = "경추ROM측정";

    [Tooltip("중립 복귀로 인정할 각(도).")]
    [SerializeField] private float neutralTolerance = 6f;

    [Tooltip("한 substep을 넘긴 뒤 이만큼은 다시 안 넘긴다(초). 연속 진행 방지.")]
    [SerializeField] private float advanceCooldown = 0.6f;

    // ── 진행 UI를 손 근처로 (2026-09-03) ─────────────────────────────────
    // 2026-09-03 사용자: "정보를 진행Root에 표시하되, 그 값이 지금 정보처럼 손 근처에 따라왔으면 좋겠다.
    //   지금 자리보다 좀 더 뒤로, 크게, 높이는 살짝 낮춰서 고개를 안 올려도 보이게."
    //
    // ★교육 브리지(CervicalRomScenarioBridge)도 같은 진행Root를 옮긴다 — 다만 그쪽은
    //   실측이면 손을 떼도록 고쳤다(PlaceProgressUI). 두 곳이 같은 트랜스폼을 밀면
    //   프레임마다 서로 되돌린다.
    // ★<b>우리가 옮긴 것만 우리가 되돌린다</b> — 진입할 때 원래 자리를 적어 두고 나갈 때 복원한다.
    //   (07-27 xray 사고가 켠 쪽과 끄는 쪽이 달라서 생긴 형태였다.)
    // ★전부 신규 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).

    [Header("=== 진행 UI 손 추종 (2026-09-03) ===")]
    [Tooltip("옮길 UI 루트. 비우면 교육 브리지의 슬롯 → 이름('진행Root') 순으로 찾는다.")]
    [SerializeField] private Transform progressRoot;

    [Tooltip("★끄면 진행Root를 안 건드린다(종전 동작 — 고정 포인트에 그대로 있는다).")]
    [SerializeField] private bool followProgressRoot = true;

    [Tooltip("헤드셋 앞으로 이만큼 띄운다(m).\n" +
             "★<b>이게 크기 손잡이다.</b> 캔버스를 키우지 않고 거리로만 조절한다\n" +
             "  (2026-09-03 사용자: '스케일은 건들지 말아봐').\n" +
             "★실측으로 좁힌 값 — 0.65는 각도기(환자 머리 0.5~0.8m)를 가렸고,\n" +
             "  1.15는 스케일 없이는 너무 작았다. 그 사이다.")]
    [SerializeField] private float followDistance = 0.9f;

    [Tooltip("눈높이에서 이만큼 <b>내린다</b>(m). 양수면 내려간다.\n" +
             "★고개를 올리지 않아도 보이게 하는 값이다 — 환자 머리보다 아래에 와야 한다.")]
    [SerializeField] private float followDrop = 0.30f;

    [Tooltip("★<b>기본 1 = 크기를 안 건드린다</b>(2026-09-03 사용자 지시: '스케일은 건들지 말아봐').\n" +
             "1이 아닐 때만 원래 크기에 이 값을 곱한다.\n" +
             "★멀어서 작아 보이면 <b>여기가 아니라 followDistance</b>를 줄이는 게 맞다 —\n" +
             "  캔버스를 키우면 글씨·여백 비율이 씬 설정과 어긋난다.")]
    [SerializeField] private float progressScale = 1f;

    // ── 게으른 추종 (2026-09-03 사용자 지시) ──────────────────────────────
    // "헤드셋에 딱 붙어서 따라오면 드드득하고 계속 움직여 잔상이 남고 눈이 피로하다.
    //  회전에 맞춰 일정 범위를 정하고, 벗어날 때만 부드럽게 따라올 것."
    // → 데드존 안에서는 <b>완전히 정지</b>한다. 벗어나면 SmoothDamp로 따라가고,
    //   안착 각까지 들어오면 다시 멈춘다(히스테리시스). 매 프레임 미세하게 움직이지 않는다.

    [Tooltip("헤드셋 정면에서 이 각도(도) 안에 있으면 <b>움직이지 않는다</b>.\n" +
             "★이게 잔상의 해법이다 — 조금이라도 따라 움직이면 계속 떨린다.")]
    [Range(5f, 60f)] [SerializeField] private float followDeadZoneDeg = 25f;

    [Tooltip("따라가기 시작하면 이 각도(도) 안에 들어올 때까지 간다. 데드존보다 작아야 한다.")]
    [Range(1f, 30f)] [SerializeField] private float followSettleDeg = 6f;

    [Tooltip("자리가 이만큼(m) 어긋나도 따라간다. 고개 회전이 아니라 <b>몸이 움직인</b> 경우다.")]
    [SerializeField] private float followMoveDeadZone = 0.35f;

    [Tooltip("따라가는 부드러움(초). 클수록 천천히 쫓아온다. 0.3~0.5가 눈이 편하다.")]
    [SerializeField] private float followSmoothTime = 0.35f;

    [Tooltip("★<b>바라보는 각</b>은 자리와 따로 맞춘다 — 판은 제자리에 있어도 정면은 늘 사용자 쪽이다.\n" +
             "이 각(도)보다 어긋나야 돌기 시작한다. 회전은 자리 이동과 달리 잔상을 안 남기므로\n" +
             "자리 데드존(followDeadZoneDeg)보다 훨씬 좁게 둘 수 있다.")]
    [Range(0.5f, 15f)] [SerializeField] private float faceDeadZoneDeg = 3f;

    [Tooltip("바라보는 각을 맞추는 부드러움(초).")]
    [SerializeField] private float faceSmoothTime = 0.25f;

    [Tooltip("실측 정보를 진행 UI의 지시문 칸에 써 넣는다.\n" +
             "★평가 모드 지시문은 방향 이름 한 단어뿐이라 그 칸이 사실상 비어 있다.")]
    [SerializeField] private bool pushReadoutToGuideUI = true;

    [SerializeField] private bool showDebugLogs = true;

    private string advancedKey;      // 이미 넘긴 substep 표식
    private float lastAdvanceTime = -99f;
    private bool active;
    private bool warnedNoMeasure;

    private ScenarioGuideUIController guideUI;
    private ScenarioConditionManager conditionManager;
    private bool resultToggleShown;

    // 진행 UI — 우리가 옮긴 것만 우리가 되돌린다.
    private bool progressCaptured;
    private Vector3 progressHomePos;
    private Quaternion progressHomeRot;
    private Vector3 progressHomeScale;
    private bool followPlaced;         // 이번 추종에서 한 번은 갖다 놨는가
    private bool followChasing;        // 데드존을 벗어나 쫓는 중인가
    private Vector3 followVelocity;    // SmoothDamp용

    // 준비 단계 토글 잠금 — 잠근 쪽이 푼다.
    private bool readyToggleLocked;

    // 현실 전환 — 우리가 바꾼 것만 우리가 되돌린다.
    private bool realWorldApplied;
    private bool passthroughWasOn;
    private readonly List<GameObject> hiddenByUs = new List<GameObject>(4);

    private void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (measure == null) measure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (checklist == null) checklist = FindFirstObjectByType<PostureChecklistUI>(FindObjectsInactive.Include);
        if (planeGauge == null) planeGauge = FindFirstObjectByType<CervicalRomPlaneGauge>(FindObjectsInactive.Include);
        if (practiceSettings == null) practiceSettings = FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);

        ChunaLogger.Log($"<color=cyan>[실측Bridge] 시작 — 측정기 {(measure != null ? "있음" : "★없음")} · " +
                        $"시나리오매니저 {(scenarioManager != null ? "있음" : "★없음")}</color>");
    }

    private void Update()
    {
        if (scenarioManager == null) return;

        if (!IsMeasurementMode() || !IsTargetScenario())
        {
            if (active)
            {
                active = false;
                advancedKey = null;
                resultToggleShown = false;
                // ★교육모드로 돌아가면 실측 표시물을 걷는다. 켠 채로 두면 화면이 겹친다.
                if (measure != null) measure.enabled = false;
                if (checklist != null) checklist.SetVisible(false);

                // ★끼운 쪽이 되돌린다. 각도기를 실측 출처에 걸어 둔 채 나가면
                //   교육모드가 대본 각도 대신 실측값을 그린다(07-27 xray 사고와 같은 형태).
                RestorePlaneGauge();
                RestoreProgressRoot();
                UnlockReadyToggle();
                if (measure != null) measure.SetFrozen(false);   // 켠 쪽이 끈다

                ExitRealWorld();
            }
            return;
        }

        // 실측모드에 들어와 있는 동안만 측정기를 켠다(교육 브리지가 꺼진 채로 붙여 둔다).
        if (measure != null && !measure.enabled) measure.enabled = true;

        if (measure == null)
        {
            if (!warnedNoMeasure)
            {
                warnedNoMeasure = true;
                ChunaLogger.LogWarning("[실측Bridge] CervicalRomRealityMeasure가 씬에 없습니다 — 실측이 진행되지 않습니다.");
            }
            return;
        }

        StepData step = scenarioManager.CurrentStep;
        SubStepData sub = scenarioManager.CurrentSubStep;
        if (step == null || sub == null) return;

        if (!active)
        {
            active = true;
            resultToggleShown = false;
            measure.ResetAll();
            if (checklist != null) checklist.ResetChecks();
            ApplyPlaneGauge();
            EnterRealWorld();
            ChunaLogger.Log("<color=cyan>[실측Bridge] 실측모드 진입 — 측정기를 초기화했다.</color>");
        }

        string name = step.stepName;
        int subNo = sub.subStepNo;
        string key = $"{name}#{subNo}";

        ApplyDirectionFor(name);

        // ★압박 방향 화살표 — 실측 substep은 x.1 능동 / x.2 압박 / x.3 복귀다.
        //   압박(x.2)에서만 켠다. 교육 브리지가 하는 것과 같은 규칙이다.
        //   ★실측에 화살표가 더 필요하다 — 정해진 끝점이 없어서 "이 방향으로 더"가 유일한 유도다.
        if (planeGauge != null && planeGauge.HasExternalSource)
        {
            bool pressing = subNo == 2 && DirectionOf(name) != CervicalRomDriver.Direction.None;
            planeGauge.SetPressGuide(pressing);
        }

        // ★실측에서는 체크리스트를 안 띄운다(2026-09-01 사용자 지시).
        //   준비 단계가 '표준자세 항목 확인'에서 '양어깨를 짚어 중심선 세우기'로 바뀌었다.
        //   ★교육모드의 체크리스트는 그대로다 — 그쪽은 CervicalRomScenarioBridge가 쥔다.
        if (checklist != null) checklist.SetVisible(false);

        // ★★진행 UI를 손 근처로 데려오는 것은 <b>재는 단계에서만</b> 한다(2026-09-03 사용자 Play).
        //   ①'준비'부터 따라오면 [다음] 토글이 손을 따라 움직여 <b>누를 수가 없다</b>.
        //     버튼이 손에 붙어 같이 도망가는 꼴이다.
        //   ②측정값이 지시문 칸을 덮어써서 <b>안내문이 뭔지 알 수가 없다</b>.
        //     준비 단계에서 읽어야 할 것은 "양어깨에 올려 중심선을 잡으세요"지 각도가 아니다.
        //   ③'결과'도 같다 — 다 잰 뒤에 측정값이 실시간으로 흔들리면 결과가 아니라 진행 중으로 보인다.
        //   → 두 단계에서는 제자리로 돌려놓고 CSV 지시문을 그대로 보여 준다.
        // ★단계마다 규약이 다르다(2026-09-03 사용자 지시).
        //   준비 : 자리는 <b>기존 진행Root 그대로</b>. 안내 멘트가 끝나면 파지 현황으로 바꾼다.
        //   측정 : 헤드셋을 게으르게 따라간다 + 측정 정보.
        //   결과 : 자리도 글도 원래대로. 측정용 숫자를 띄우지 않는다.
        // ★★<b>재는 단계를 화이트리스트로 정한다</b>(2026-09-03 재수정).
        //   종전엔 "준비·결과만 빼고 전부"였는데, 그러면 <b>'가이드'(환자 위치 설정) 단계까지</b>
        //   추종에 걸려 화면이 따라다니고 측정값이 안내문을 덮었다.
        //   2026-09-03 사용자: "환자 위치설정하는 거 나올 때도 텍스트 나오게 하라니까?"
        //   빼는 목록을 늘리는 방식은 새 단계가 생길 때마다 또 샌다 — 넣는 목록으로 뒤집는다.
        bool measuringStep = DirectionOf(name) != CervicalRomDriver.Direction.None
                             || name.EndsWith("파지", System.StringComparison.Ordinal);

        if (measuringStep)
        {
            FollowProgressRoot();
            PushReadout();
        }
        else
        {
            RestoreProgressRoot();

            // ★준비 단계 — 멘트가 끝난 뒤에만 파지 현황을 띄운다.
            //   멘트 중에 숫자로 덮으면 "무엇을 하라는 건지" 읽을 시간이 없다.
            //   자리는 안 옮긴다 — 토글이 제자리에 있어야 누를 수 있다.
            if (name == "준비" && !IsNarrationPlaying()) PushReadout();
        }

        // ★결과 단계에서는 측정을 얼린다. 안 그러면 손을 내리는 순간 0점이 풀리고,
        //   거기서 잠깐 멈추면 <b>마지막 방향을 다시 재기 시작한다</b>(09-03 사용자: 좌회전이 계속 다시 측정됨).
        measure.SetFrozen(name == "결과");

        // ★★준비 단계의 [다음] 토글을 잠근다(2026-09-03).
        //   아래 IsSatisfied가 ReferenceReady로 막고 있었는데도 넘어가던 이유가 여기다 —
        //   <b>진행 경로가 둘</b>이었다. 실측 '준비' 행의 stepNo가 0이라
        //   StepData.IsGuideStep()이 참이 되고, ScenarioGuideUIController가
        //   [다음] 토글을 무조건 띄운다. 그 토글은 아무 조건 없이 NextSubStep()을 부른다.
        //   교육 '자세정렬'(역시 stepNo 0)에서 이 구멍이 안 보였던 건 PostureChecklistUI가
        //   토글을 잠가 뒀기 때문이고, 실측은 체크리스트를 안 띄우므로 잠금도 같이 사라졌다.
        //   ★어깨를 안 짚고 넘어가면 기준틀 없이 재게 되어 <b>조용히 틀린 각</b>이 나온다.
        UpdateReadyToggleLock(name);

        if (key == advancedKey) return;
        if (Time.time - lastAdvanceTime < advanceCooldown) return;

        if (!IsSatisfied(name, subNo)) return;

        advancedKey = key;
        lastAdvanceTime = Time.time;
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[실측Bridge] {key} 완료 — 다음 단계</color>");

        // ★AutoPlay가 물고 있으면 그쪽을 끝내 준다. 직접 NextSubStep을 부르면 두 번 넘어간다.
        //   교육 브리지에서 밟은 함정과 같은 것이다.
        if (evaluator != null && evaluator.IsAutoPlayMode)
        {
            evaluator.CompleteAutoPlayExternally();
            return;
        }
        scenarioManager.NextSubStep();
    }

    /// <summary>
    /// 결과 단계에서 [다음] 토글을 띄운다. 한 번만 띄운다 —
    /// <see cref="IsSatisfied"/>는 매 프레임 불리므로 그냥 부르면 토글 상태를 계속 리셋한다.
    /// </summary>
    private void ShowResultNextToggle()
    {
        if (resultToggleShown) return;
        resultToggleShown = true;

        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (guideUI == null)
        {
            ChunaLogger.LogWarning("[실측Bridge] ScenarioGuideUIController가 없어 결과 단계에서 넘어갈 수단이 없습니다.");
            return;
        }

        guideUI.EnableStartToggle("다음");
        ChunaLogger.Log("<color=cyan>[실측Bridge] 결과 단계 — [다음] 토글을 띄웠다.</color>");
    }

    /// <summary>진행 UI 컨트롤러. 없으면 한 번만 찾는다.</summary>
    private ScenarioGuideUIController GuideUI
    {
        get
        {
            if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
            return guideUI;
        }
    }

    // ── 진행 UI 손 추종 ───────────────────────────────────────────────────

    /// <summary>옮길 진행Root. 인스펙터 → 교육 브리지 슬롯 → 이름 순으로 찾는다.</summary>
    private Transform ResolveProgressRoot()
    {
        if (progressRoot != null) return progressRoot;

        // ★이름으로 찾기보다 교육 브리지의 슬롯을 먼저 본다 — 이름은 바뀌면 조용히 죽는다(규칙 8).
        var eduBridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
        if (eduBridge != null && eduBridge.ProgressRoot != null)
        {
            progressRoot = eduBridge.ProgressRoot;
            return progressRoot;
        }

        GameObject byName = GameObject.Find("진행Root");
        if (byName != null)
        {
            progressRoot = byName.transform;
            return progressRoot;
        }

        return null;
    }

    /// <summary>
    /// 진행Root를 <b>헤드셋</b> 앞에 게으르게 붙인다.
    ///
    /// ★손이 아니라 헤드셋을 따라간다(2026-09-03 사용자 지시). 손을 따라가면
    ///   [다음] 토글이 손에 붙어 같이 도망가고, 파지 중에는 화면이 환자 머리에 겹친다.
    /// ★데드존 안에서는 <b>한 프레임도 안 움직인다</b>. 조금씩이라도 따라 움직이면
    ///   VR에서 잔상이 남아 눈이 피로하다 — 사용자가 "드드득한다"고 한 게 그것이다.
    /// </summary>
    private void FollowProgressRoot()
    {
        if (!followProgressRoot) return;

        Transform root = ResolveProgressRoot();
        if (root == null) return;

        // ★원래 자리를 한 번만 적어 둔다. 이걸 안 하면 실측을 한 번 돌 때마다 UI가 떠내려간다.
        // ★<b>로컬</b>로 적는다 — 진행Root는 'UI Group'의 자식이고, 그 부모를 ScenarioUIPositioner가
        //   헤드셋 기준으로 옮긴다(설정의 위치 초기화로 실측 중에도 다시 옮길 수 있다).
        //   월드로 적어 두면 부모가 옮겨진 뒤 옛 자리로 되돌아가 부모와 어긋난다.
        if (!progressCaptured)
        {
            progressCaptured = true;
            progressHomePos = root.localPosition;
            progressHomeRot = root.localRotation;
            progressHomeScale = root.localScale;
        }

        // ★1이면 아예 안 건드린다. 씬이 정해 둔 크기가 정답이라는 뜻이다.
        if (Mathf.Abs(progressScale - 1f) > 0.001f)
        {
            Vector3 want = progressHomeScale * Mathf.Max(0.01f, progressScale);
            if ((root.localScale - want).sqrMagnitude > 1e-8f) root.localScale = want;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 head = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return;      // 정확히 위/아래를 볼 때 — 그 프레임은 건너뛴다
        fwd.Normalize();

        Vector3 target = head + fwd * followDistance + Vector3.down * followDrop;

        // ★처음 붙일 때는 그냥 갖다 놓는다. 먼 데서 미끄러져 오면 그게 더 어지럽다.
        if (!followPlaced)
        {
            followPlaced = true;
            followChasing = false;
            followVelocity = Vector3.zero;
            root.SetPositionAndRotation(target, UprightLook(target, head));
            return;
        }

        // 지금 UI가 헤드셋 정면에서 몇 도 벗어나 있나 (수평 성분만 — 고개를 끄덕이는 건 무시한다)
        Vector3 toUi = root.position - head;
        toUi.y = 0f;
        float yawErr = toUi.sqrMagnitude > 1e-6f ? Vector3.Angle(toUi, fwd) : 0f;
        float posErr = Vector3.Distance(root.position, target);

        // 데드존 밖으로 나가면 쫓기 시작한다. 안에 있으면 <b>아무것도 하지 않는다</b>.
        if (!followChasing && (yawErr > followDeadZoneDeg || posErr > followMoveDeadZone))
            followChasing = true;

        if (followChasing)
        {
            root.position = Vector3.SmoothDamp(root.position, target, ref followVelocity,
                                               Mathf.Max(0.01f, followSmoothTime));

            // 안착하면 멈춘다. 데드존보다 좁게 잡아야 경계에서 붙었다 떨어졌다 하지 않는다.
            if (yawErr < followSettleDeg && posErr < followMoveDeadZone * 0.4f)
            {
                followChasing = false;
                followVelocity = Vector3.zero;
            }
        }

        // ★★<b>바라보는 각은 자리와 따로 맞춘다</b>(2026-09-03 사용자 지시).
        //   "사용자를 바라보는 각도로는 맞춰 줘" — 판이 제자리에 서 있더라도 <b>정면은 늘 사용자 쪽</b>이어야 한다.
        //   자리를 데드존으로 묶어 두면, 시술자가 옆으로 걸었을 때 판만 비스듬히 남아 글씨가 찌그러져 보인다.
        //   ★자리와 달리 회전은 잔상을 안 만든다 — 판이 이동하지 않고 제자리에서 각만 도는 것이라
        //     시야에 흐르는 궤적이 남지 않는다. 그래서 데드존을 훨씬 좁게(기본 3도) 둘 수 있다.
        //   ★그래도 <b>완전히 멈추는 구간</b>은 둔다. 매 프레임 미세하게 돌면 그것도 떨림이다.
        Quaternion wantRot = UprightLook(root.position, head);
        if (Quaternion.Angle(root.rotation, wantRot) > faceDeadZoneDeg)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, faceSmoothTime));
            root.rotation = Quaternion.Slerp(root.rotation, wantRot, k);
        }
    }

    /// <summary>
    /// <b>세로축(Y) 회전만</b> 준다. 판은 언제나 똑바로 서고 수평을 유지한다.
    ///
    /// ★2026-09-03 사용자: "고개 기울였다고 같이 기울면 안 된다. 평행 유지한 채로."
    ///   LookRotation에 위아래 성분이 섞인 방향을 넣으면 판이 앞뒤로 눕는다 —
    ///   진행Root는 눈높이보다 아래(followDrop)에 있으므로 반드시 그렇게 된다.
    ///   수평 성분만 남기면 기울기(pitch)도 좌우 기울임(roll)도 0이 된다.
    ///   ScenarioUIPositioner도 같은 규약을 쓴다(lookDirection.y = 0).
    /// </summary>
    private static Quaternion UprightLook(Vector3 uiPos, Vector3 headPos)
    {
        Vector3 look = uiPos - headPos;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.LookRotation(look, Vector3.up);
    }

    /// <summary>진행Root를 원래 자리·크기로 되돌린다.</summary>
    private void RestoreProgressRoot()
    {
        if (!progressCaptured) return;
        progressCaptured = false;

        Transform root = progressRoot;
        if (root == null) return;

        root.localPosition = progressHomePos;
        root.localRotation = progressHomeRot;
        root.localScale = progressHomeScale;
        followPlaced = false;
        followChasing = false;
        followVelocity = Vector3.zero;

        // ★지시문 칸도 되돌린다. 안 되돌리면 실측을 나간 뒤에도 마지막 측정값이 남아 있다 —
        //   모드만 바꾸면 substep 전환이 없어서 아무도 다시 안 써 준다.
        var label = GuideUI != null ? GuideUI.DescriptionLabel : null;
        if (label != null && scenarioManager != null && scenarioManager.CurrentSubStep != null)
            label.text = scenarioManager.CurrentSubStep.textInstruction;

        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 진행 UI를 원래 자리로 되돌렸다.</color>");
    }

    /// <summary>안내 멘트가 울리고 있는가. 조건매니저를 못 찾으면 '안 울린다'로 본다.</summary>
    private bool IsNarrationPlaying()
    {
        if (conditionManager == null)
            conditionManager = FindFirstObjectByType<ScenarioConditionManager>(FindObjectsInactive.Include);
        return conditionManager != null && conditionManager.IsNarrationPlaying;
    }

    /// <summary>측정기가 만든 안내문을 진행 UI의 지시문 칸에 쓴다.</summary>
    private void PushReadout()
    {
        if (!pushReadoutToGuideUI) return;
        if (measure == null || !measure.RouteReadoutToGuideUI) return;

        var label = GuideUI != null ? GuideUI.DescriptionLabel : null;
        if (label == null) return;

        string text = measure.ReadoutText;
        if (string.IsNullOrEmpty(text)) return;

        // ★<b>지금 칸에 뭐가 들어 있는지</b>를 보고 정한다. "내가 마지막에 뭘 넣었나"로 판단하면
        //   substep이 바뀌는 순간 ScenarioGuideUIController가 CSV 지시문으로 덮어쓰는데
        //   측정값은 그대로라 다시 안 넣게 되고, 그 단계 내내 CSV 문구가 남는다.
        // ★같은 문자열이면 대입하지 않는다 — TMP는 대입할 때마다 메시를 다시 만든다(VR 프레임 예산).
        if (string.Equals(label.text, text, System.StringComparison.Ordinal)) return;
        label.text = text;
    }

    // ── 준비 단계 토글 잠금 ───────────────────────────────────────────────

    /// <summary>
    /// '준비'(양어깨 짚기)를 안 끝냈으면 [다음] 토글을 못 누르게 한다.
    /// ★잠근 쪽이 푼다 — 다른 단계로 넘어가거나 실측을 나가면 반드시 되돌린다.
    /// </summary>
    private void UpdateReadyToggleLock(string stepName)
    {
        bool wantLock = stepName == "준비" && measure != null && !measure.ReferenceReady;

        if (!wantLock)
        {
            UnlockReadyToggle();
            return;
        }

        var toggle = GuideUI != null ? GuideUI.NextToggle : null;
        if (toggle == null) return;

        if (!readyToggleLocked)
        {
            readyToggleLocked = true;
            if (showDebugLogs)
                ChunaLogger.Log("<color=cyan>[실측Bridge] 준비 단계 — 어깨 기준선을 잡을 때까지 [다음]을 잠근다.</color>");
        }
        toggle.interactable = false;
    }

    private void UnlockReadyToggle()
    {
        if (!readyToggleLocked) return;
        readyToggleLocked = false;

        var toggle = GuideUI != null ? GuideUI.NextToggle : null;
        if (toggle != null) toggle.interactable = true;
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 어깨 기준선 성립 — [다음] 잠금을 풀었다.</color>");
    }

    private void OnDisable()
    {
        RestoreProgressRoot();
        UnlockReadyToggle();
        ExitRealWorld();
        resultToggleShown = false;
    }

    // ── 현실 전환 ─────────────────────────────────────────────────────────
    // ★실측은 "환자도 추나 베드도 배경도 없이 현실 위에 UI만 띄우고 실제 환자를 재는" 모드다
    //   (2026-08-31 사용자 정의). 배경·베드는 패스스루가 배경째 끄면서 사라지고,
    //   여기서 따로 치울 것은 가상 환자다.
    //
    // ★현실 모드(패스스루)는 설정의 스위치일 뿐이라 그쪽이 모드를 알 필요가 없다.
    //   대신 <b>우리가 켠 것만 우리가 되돌린다</b> — 사용자가 미리 켜 둔 패스스루는 나갈 때 그대로 둔다.
    //   상태를 켠 쪽과 끄는 쪽이 다르면 반드시 샌다(07-27 xray 사고가 그 형태였다).

    private void EnterRealWorld()
    {
        if (realWorldApplied) return;
        realWorldApplied = true;

        if (practiceSettings != null)
        {
            passthroughWasOn = practiceSettings.IsRealityModeOn;
            if (!passthroughWasOn) practiceSettings.SetRealityMode(true);
            practiceSettings.SetPatientBodyVisible(false);
        }
        else
        {
            ChunaLogger.LogWarning("[실측Bridge] PracticeSettingsController가 없어 패스스루·환자 숨김을 못 겁니다.");
        }

        hiddenByUs.Clear();
        if (alsoHideInMeasurement != null)
            foreach (GameObject go in alsoHideInMeasurement) HideOne(go);

        if (hideByNameInMeasurement != null)
        {
            foreach (string name in hideByNameInMeasurement)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                GameObject go = GameObject.Find(name.Trim());
                if (go == null)
                {
                    // ★조용히 넘어가면 "왜 아직 보이지"를 못 찾는다. 이름이 바뀌었을 수 있다.
                    ChunaLogger.LogWarning($"[실측Bridge] '{name}'을(를) 씬에서 못 찾아 숨기지 못했습니다 — " +
                                           "이름이 바뀌었는지 확인하세요.");
                    continue;
                }
                HideOne(go);
            }
        }

        ChunaLogger.Log($"<color=cyan>[실측Bridge] 현실 전환 — 패스스루 {(passthroughWasOn ? "이미 켜져 있었음" : "켬")} · " +
                        $"가상 환자 숨김 · 추가 숨김 {hiddenByUs.Count}개</color>");
    }

    /// <summary>원래 켜져 있던 것만 끈다. 끈 것만 기억했다가 나갈 때 되돌린다.</summary>
    private void HideOne(GameObject go)
    {
        if (go == null || !go.activeSelf) return;
        go.SetActive(false);
        hiddenByUs.Add(go);
    }

    private void ExitRealWorld()
    {
        if (!realWorldApplied) return;
        realWorldApplied = false;

        if (practiceSettings != null)
        {
            practiceSettings.SetPatientBodyVisible(true);
            if (!passthroughWasOn) practiceSettings.SetRealityMode(false);
        }

        foreach (GameObject go in hiddenByUs)
            if (go != null) go.SetActive(true);
        hiddenByUs.Clear();

        ChunaLogger.Log("<color=cyan>[실측Bridge] 현실 전환 해제 — 가상 환자·숨긴 오브젝트를 되돌렸다.</color>");
    }

    /// <summary>단계 이름으로 측정 방향을 정한다. 파지·준비 단계는 방향을 안 건드린다.</summary>
    private void ApplyDirectionFor(string stepName)
    {
        CervicalRomDriver.Direction d = DirectionOf(stepName);

        // ★★<b>파지 단계에서 방향을 미리 맞춰 둔다</b>(2026-09-02).
        //   종전에는 파지 단계에서 DirectionOf가 None이라 SetDirection을 아예 안 불렀다.
        //   그래서 <b>직전 방향이 그대로 남은 채</b> 중립을 잡았고(씬에 direction=6 좌회전이
        //   직렬화돼 있어 첫 파지는 늘 좌회전 기준이었다), 다음 방향으로 넘어가는 순간
        //   파지 그룹이 달라 중립이 버려졌다 — <b>방금 잡았는데 또 잡으라고 하는</b> 증상이다.
        //   2026-09-02 사용자: "이거 왜 파지 하고 파지를 한번 더해? 중립 파지 했잖아."
        //   ★기준틀(axRight·axFwd)도 파지 그룹으로 갈리므로, 틀린 틀로 잡았다가 버리는 낭비였다.
        //   파지 단계에서 그 그룹의 <b>첫 방향</b>으로 맞춰 두면 중립이 그대로 이어진다.
        if (d == CervicalRomDriver.Direction.None) d = GripStepDirection(stepName);

        if (d != CervicalRomDriver.Direction.None) measure.SetDirection(d);
    }

    /// <summary>
    /// 파지 단계가 준비하는 <b>그룹의 첫 방향</b>. CSV 순서를 따른다.
    /// ★단계 이름으로 매칭하므로 CSV의 stepName을 바꾸면 여기도 바꾼다(규칙 8).
    /// </summary>
    private static CervicalRomDriver.Direction GripStepDirection(string stepName)
    {
        switch (stepName)
        {
            case "시상면 파지": return CervicalRomDriver.Direction.Flexion;       // 굴곡 → 신전
            case "관상면 파지": return CervicalRomDriver.Direction.LateralRight;  // 우측굴 → 좌측굴 → 우회전 → 좌회전
            default:            return CervicalRomDriver.Direction.None;
        }
    }

    /// <summary>
    /// 실습 각도기를 실측 출처로 갈아 끼우고 실측 외형으로 바꾼다.
    /// ★판·채움을 끈다 — 실제 환자를 가린다. 눈금·지침·도달 마커·압박 방향 화살표는 그대로 온다.
    ///   실측에 화살표가 더 필요하다는 게 이걸 하는 이유의 절반이다(정해진 끝점이 없어
    ///   "이 방향으로 더"가 유일한 유도다).
    /// </summary>
    private void ApplyPlaneGauge()
    {
        if (measure == null || !measure.UsePracticeGauge) return;
        if (planeGauge == null)
        {
            ChunaLogger.LogWarning("[실측Bridge] CervicalRomPlaneGauge를 못 찾았습니다 — 각도기 없이 진행합니다.");
            return;
        }

        planeGauge.SetSource(measure);
        planeGauge.SetRealityLook(true);
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 각도기를 실측 출처로 바꿨다(판·채움 끔).</color>");
    }

    /// <summary>각도기를 교육모드 상태로 되돌린다.</summary>
    private void RestorePlaneGauge()
    {
        if (planeGauge == null) return;
        if (!planeGauge.HasExternalSource) return;   // 우리가 안 끼웠으면 안 건드린다

        planeGauge.SetPressGuide(false);
        planeGauge.ClearSticky();
        planeGauge.SetRealityLook(false);
        planeGauge.SetSource(null);
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 각도기를 교육 출처로 되돌렸다.</color>");
    }

    private static CervicalRomDriver.Direction DirectionOf(string stepName)
    {
        switch (stepName)
        {
            case "굴곡":   return CervicalRomDriver.Direction.Flexion;
            case "신전":   return CervicalRomDriver.Direction.Extension;
            case "우측굴": return CervicalRomDriver.Direction.LateralRight;
            case "좌측굴": return CervicalRomDriver.Direction.LateralLeft;
            case "우회전": return CervicalRomDriver.Direction.RotationRight;
            case "좌회전": return CervicalRomDriver.Direction.RotationLeft;
            default:       return CervicalRomDriver.Direction.None;
        }
    }

    /// <summary>이 substep을 넘겨도 되는가.</summary>
    private bool IsSatisfied(string stepName, int subNo)
    {
        // ★준비 단계 = <b>양손을 환자 양어깨에 올려 중심선을 세운다</b>(2026-09-01 사용자 지시).
        //   종전의 표준자세 체크리스트는 실측에서 뺐다. 어깨선·중심선이 그 역할을 대신한다 —
        //   체크리스트 3번 항목("검사하는 동안 어깨가 돌아가지 않게 한다")은 말로 확인받는 것보다
        //   기준선이 떠 있는 편이 실제로 보인다.
        //   ★교육모드는 그대로 체크리스트를 쓴다.
        if (stepName == "준비") return measure.ReferenceReady;

        // ★결과도 브리지가 넘기지 않는다. 대신 [다음] 토글을 띄워 사람이 읽고 넘기게 한다.
        //   토글은 stepNo 0에서만 자동으로 뜨는데 '결과'는 stepNo 12라 안 뜬다 —
        //   그래서 종전에는 여기서 통째로 멈췄다.
        if (stepName == "결과")
        {
            ShowResultNextToggle();
            return false;
        }

        // ★'기준정렬'(양손을 어깨에) 단계는 없앴다(2026-08-31).
        //   기준축을 파지선에서 세우므로 어깨를 짚을 이유가 사라졌다.
        //   옛 CSV가 남아 있어도 죽지 않게 파지와 같은 조건으로 흘려보낸다.
        if (stepName == "기준정렬" || stepName.EndsWith("파지")) return measure.NeutralReady;

        CervicalRomDriver.Direction d = DirectionOf(stepName);
        if (d == CervicalRomDriver.Direction.None) return false;

        switch (subNo)
        {
            case 1: return measure.HasActive(d);

            // ★★압박은 <b>자율</b>이다(2026-09-02 사용자 지시).
            //   압박을 하면 그 값으로 넘어가고, 안 하고 중립으로 돌아오면 <b>생략</b>으로 넘어간다.
            //   종전에는 HasPassive만 봐서, 압박이 안 잡히면 그 방향에서 영영 못 나갔다 —
            //   09-01 신전이 그 자리에서 막혔고 수동 0.0으로 남았다.
            case 2:
                if (measure.HasPassive(d)) return true;
                if (measure.IsBackToNeutral(neutralTolerance))
                {
                    measure.MarkPassiveSkipped();
                    return true;
                }
                return false;

            case 3: return measure.IsBackToNeutral(neutralTolerance);
            default: return false;
        }
    }

    private bool IsMeasurementMode()
        => DifficultyManager.Instance != null && DifficultyManager.Instance.IsMeasurementMode;

    private bool IsTargetScenario()
    {
        ScenarioData data = scenarioManager.CurrentScenario;
        return data != null && !string.IsNullOrEmpty(data.scenarioName)
               && data.scenarioName.Contains(scenarioName);
    }
}
