using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 실측(현실 환자) 모드의 경추 ROM 각도 측정.
///
/// 가상 모드의 <see cref="CervicalRomDriver"/>는 각도를 <b>명령</b>한다(대본대로 모델을 몬다).
/// 이쪽은 반대로 각도를 <b>측정</b>한다. 모델도 접촉점 콜라이더도 쓰지 않고 손 두 개만 본다.
///
/// 원리 — 양손 파지 벡터 V의 회전각을 읽는다.
///   ★V는 그 동작의 회전축과 <b>수직</b>일 때만 각이 나온다. 축과 나란하면 감도 0이다.
///     이마·후두 파지(전후축) → 굴곡·신전 · 회전 ○ / 측굴 ×
///     측두부  파지(좌우축) → 측굴 · 회전 ○      / 굴곡·신전 ×
///     술기가 원래 그렇게 잡으므로 네 동작이 전부 커버된다.
///   ★각은 <b>면에 투영해서</b> 잰다. 투영각은 파지가 축과 정확히 수직이 아니어도
///     값이 줄지 않는다(투영이 축 성분을 지운다). 줄어드는 건 감도(잡음 여유)뿐이라
///     편향이 없다 — 폐기한 midT-C 방식과 갈리는 지점이다.
///
/// 전제 — 표준자세는 정렬(t0) 시점에 고정된 것으로 본다. <b>몸통 보상 분리 로직은 없다.</b>
///        t0 이후 몸통이 움직이면 조용히 값에 섞인다. 시작 전에 고지하는 것으로 갈음한다.
///
/// 부호 — 기록값은 <b>크기</b>다. 어느 방향인지는 단계가 이미 알고 있어 부호가 필요 없고,
///        부호를 쓰면 좌우 손이 바뀌었을 때 뒤집히는 함정이 생긴다. 로그에는 부호를 남긴다.
/// </summary>
public class CervicalRomRealityMeasure : MonoBehaviour, ICervicalRomGaugeSource
{
    public enum Stage
    {
        Idle,             // 대기
        // ★AwaitShoulders 제거(2026-08-31) — 기준축을 파지선에서 세우므로 어깨를 짚는 단계가 필요 없다.
        AwaitNeutral,     // 중립에서 머리를 파지 - 0점을 잡는다
        Active,           // 환자 능동
        Passive,          // 시술자 압박(끝 느낌)
        Done,             // 이 방향 측정 끝
    }

    [Header("=== 손 소스 ===")]
    [Tooltip("비워 두면 씬에서 찾는다. 접촉점(콜라이더)은 안 쓰고 손끝 위치만 가져온다.")]
    [SerializeField] private CervicalGripJudge gripJudge;

    [Tooltip("판정기를 안 쓰고 직접 넣고 싶을 때. 둘 다 채워야 이쪽을 쓴다.")]
    [SerializeField] private Transform leftHandOverride;
    [SerializeField] private Transform rightHandOverride;

    [Header("=== 측정 대상 ===")]
    [SerializeField] private CervicalRomDriver.Direction direction = CervicalRomDriver.Direction.Flexion;

    [Header("=== 캡처 ===")]
    [Tooltip("양손이 이 속도 아래로 이만큼 머무르면 '정지'로 본다(초). ★씬에 값(1.5)이 박혀 있다.")]
    [SerializeField] private float holdSeconds = 1.5f;

    // ── 정지 판정 완화 (2026-09-01) ──────────────────────────────────────
    // 2026-09-01 사용자: "흔들림 값에 대한 여유가 너무 없어서 살짝만 틀어지거나 움직여도
    //   초기화돼 버리니까 오래 걸린다. 손이 잠깐 가려지기라도 하면 난리다."
    //
    // ★세 군데가 겹쳐 있었다 —
    //   ① 임계를 한 프레임만 넘어도 holdTimer가 <b>0</b>이 됐다(쌓인 게 통째로 날아감).
    //   ② 속도가 <b>양손 중 빠른 쪽</b>이라 한 손만 튀어도 전체가 리셋됐다.
    //   ③ 손을 <b>한 프레임</b> 못 읽으면 즉시 0이었다 — "가려지면 난리"의 정체.
    // ★임계값(holdSpeedThreshold) 자체는 안 건드린다. 완화만 하면 08-31의
    //   "밀어 가는 도중에 잡힌다"가 되돌아온다.

    [Tooltip("★임계를 넘는 동안 holdTimer를 0으로 죽이지 않고 <b>깎는다</b>. 초당 이 배수로 줄인다.\n" +
             "2면 1초 흔들려야 2초어치가 날아간다. 잠깐 흔들려도 누적이 남는다.\n" +
             "0이면 종전대로 즉시 0으로 떨어진다.")]
    [SerializeField] private float holdDecayRate = 2f;

    [Tooltip("★히스테리시스. 이미 정지로 쌓고 있는 동안에는 나가는 임계를 " +
             "holdSpeedThreshold × 이 값으로 둔다. 경계에서 깜빡이는 걸 없앤다.")]
    [SerializeField] private float holdSpeedExitFactor = 2f;

    [Tooltip("★손을 못 읽는 동안 타이머를 <b>얼리는</b> 시간(초). 이 안에 돌아오면 쌓인 게 그대로 살아 있다.\n" +
             "가림은 대개 한두 프레임이라 이것만으로 대부분 잡힌다.")]
    [SerializeField] private float trackingGraceSeconds = 0.35f;

    [Tooltip("★<b>각이 이만큼 넘게 움직이면 정지가 아니다</b>(도). 홀드를 시작한 시점 대비로 본다.\n\n" +
             "2026-09-01 사용자: '가는 중에 조금 느리게 가니까 중간에 읽혀버렸다.'\n" +
             "손 속도만 보면 천천히 지나가는 구간이 '정지'로 읽힌다 — 손은 느린데 각은 계속 가고 있다.\n" +
             "★게다가 holdDecayRate를 넣으면서 잠깐 빨라져도 누적이 안 죽게 돼, 이 오검출이 더 쉬워졌다.\n" +
             "  속도와 각을 <b>둘 다</b> 봐야 '느리게 지나가는 것'과 '멈춘 것'이 갈린다.\n" +
             "0 이하면 이 검사를 끈다(종전 동작).")]
    [SerializeField] private float holdAngleTolerance = 1.5f;

    [Tooltip("★0보다 크면 위 holdSeconds 대신 이 값을 쓴다. 0이면 씬 값을 그대로 쓴다.\n\n" +
             "2026-08-31 사용자: '압박에서 1.5초 하니까 중간에 그냥 인식해버린다'.\n" +
             "압박은 밀어 가는 도중에도 손이 잠깐 느려지는 구간이 있어서 1.5초로는 끝점 전에 잡힌다.\n" +
             "★holdSeconds는 씬에 직렬화돼 있어 코드 기본값이 안 먹는다(규칙 7). " +
             "이 필드는 신규라 먹으므로 인스펙터를 안 거치고 바꿀 수 있다.")]
    [SerializeField] private float holdSecondsOverride = 2.5f;

    // ── 간소 게이팅 (2026-09-03 사용자 지시) ─────────────────────────────
    // "홀드 시간이 너무 길어서 한 번 튀면 몇 초를 기다려야 해 과정이 안 끝나고 늘어진다.
    //  사용자는 '이거 왜 안 되지'가 돼 버린다. 조금은 정확도가 밀리더라도 간결하게."
    //
    // ★두 가지가 겹쳐서 늘어졌다.
    //   ①유지 시간 2.5초가 길다.
    //   ②★<b>각도 앵커에서 벗어나면 타이머를 통째로 0으로 죽인다.</b> 속도 쪽은 holdDecayRate로
    //     깎기만 하는데 각도 쪽만 0이라, 엄지가 한 번 구르면(신전에서 특히) 2.5초를 처음부터 다시 센다.
    // ★전부 신규 필드라 씬 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).
    //   기존 holdSecondsOverride·holdAngleTolerance는 씬에 굳어 있어 못 건드린다.

    [Header("=== 간소 게이팅 (2026-09-03) ===")]
    [Tooltip("★켜면 아래 두 값이 holdSecondsOverride·holdAngleTolerance를 대신한다.\n" +
             "끄면 종전 값(2.5초 × 1.5도)으로 돌아간다.")]
    [SerializeField] private bool simplifiedGating = true;

    [Tooltip("간소 모드의 정지 유지 시간(초). 종전 2.5초.\n" +
             "★2026-09-03 재조정: 1.2초는 <b>너무 짧았다</b> — '이동하는 중에 주춤하는 사이에 찍혀버려'.\n" +
             "  1.8초. 늘어짐은 시간이 아니라 아래 유예·감쇠가 막는다.")]
    [SerializeField] private float simpleHoldSeconds = 1.8f;

    [Tooltip("간소 모드의 각도 여유(도). 종전 1.5도.\n" +
             "★이게 '천천히 지나가는 것'과 '멈춘 것'을 가르는 값이다 — 너무 키우면\n" +
             "  주춤하는 사이에 찍힌다. 3.5도는 헐거웠다(2026-09-03) → 2.5도.\n" +
             "  엄지가 구르며 생기는 2~3도 흔들림은 <b>여유가 아니라 아래 유예</b>가 흡수한다.")]
    [SerializeField] private float simpleHoldAngleTolerance = 2.5f;

    [Tooltip("각도 앵커에서 벗어나도 이만큼은 봐준다(초). 한 프레임 튐으로 타이머를 잃지 않게 한다.\n" +
             "★튐 흡수는 <b>여기가</b> 한다. 여유(각도)를 키우는 것과 역할이 다르다 —\n" +
             "  여유를 키우면 느리게 지나가는 것도 정지로 읽히지만, 유예는 <b>짧은 것만</b> 봐준다.\n" +
             "0이면 종전처럼 벗어나는 즉시 처리한다.")]
    [SerializeField] private float holdAngleGraceSeconds = 0.25f;

    /// <summary>실제로 쓸 정지 유지 시간.</summary>
    private float HoldSeconds => simplifiedGating && simpleHoldSeconds > 0f
        ? simpleHoldSeconds
        : (holdSecondsOverride > 0f ? holdSecondsOverride : holdSeconds);

    /// <summary>실제로 쓸 각도 여유(도).</summary>
    private float HoldAngleToleranceNow => simplifiedGating && simpleHoldAngleTolerance > 0f
        ? simpleHoldAngleTolerance
        : holdAngleTolerance;

    [Tooltip("정지로 인정할 손 속도(m/s). ★씬에 값이 있으니 인스펙터 값이 먹는다.")]
    [SerializeField] private float holdSpeedThreshold = 0.03f;

    [Tooltip("★손 속도 저역통과 시간상수(초). 0이면 생값.\n\n" +
             "한 프레임 위치 차분을 그대로 속도로 쓰면 트래킹 지터가 그대로 튀어서, " +
             "가만히 있어도 임계를 넘나들며 정지가 안 잡힌다(2026-08-31 사용자 지적: '감도가 너무 타이트').\n" +
             "평활을 걸면 임계를 크게 올리지 않고도 잘 잡힌다 — 임계를 올리는 건 " +
             "'움직이는 중에도 확정되는' 반대쪽 문제를 만든다.")]
    [SerializeField] private float speedSmoothing = 0.15f;

    [Tooltip("정지만으로 다음 단계로 넘어간다. 끄면 키/메뉴로만 넘어간다.")]
    [SerializeField] private bool advanceOnHold = true;

    [Tooltip("능동·압박 끝점을 정지로 확정할 때, 최소 이만큼은 움직였어야 한다(도).\n" +
             "이게 없으면 중립에서 가만히 있다가 0도로 확정돼 버린다.")]
    [SerializeField] private float minAngleToMark = 5f;

    [Tooltip("★압박은 능동 각보다 이만큼 <b>더</b> 가야 확정한다(도).\n\n" +
             "이게 없으면 능동 끝점에서 손을 그대로 둔 채 잠깐 멈추기만 해도 " +
             "압박이 즉시 확정돼 '수동 = 능동, 차이값 0'이 된다. 능동 끝에서 잠깐 멈추는 건 " +
             "자연스러운 동작이라 거의 매번 밟는다(2026-08-31 실측).\n\n" +
             "사람이면 기능이 아무리 떨어져도 수동에서는 이만큼은 더 밀린다는 전제다. " +
             "손 떨림보다는 크고, 실제 끝느낌 여유(설계서상 5~10도)보다는 작게 잡는다.")]
    [SerializeField] private float minPassiveGain = 3f;

    [Header("=== 파지 게이트 (2026-08-31) ===")]
    [Tooltip("★끄면 종전대로 '정지 1.5초'만으로 0점을 잡는다. 게이트가 말썽이면 여기부터 끄고 본다.")]
    [SerializeField] private bool requireGripGate = true;

    [Tooltip("시상면 파지(이마·후두)의 양손 간격 허용 범위(m). 사람 머리 앞뒤 길이 대역이다.")]
    [SerializeField] private Vector2 sagittalGripRange = new Vector2(0.14f, 0.26f);

    [Tooltip("측두부 파지(관상면·횡단면)의 양손 간격 허용 범위(m). 사람 머리 좌우 폭 대역이다.")]
    [SerializeField] private Vector2 temporalGripRange = new Vector2(0.11f, 0.22f);

    [Tooltip("★양손 간격 <b>하한</b>을 이 값까지 내린다(m). 0 이하면 위 범위를 그대로 쓴다.\n\n" +
             "2026-08-31 실측: 이마·후두를 감싸 잡으면 엄지·검지 중점이 안쪽으로 들어와 " +
             "머리 앞뒤 20cm가 <b>12~16cm로 읽힌다</b>. 씬에 박힌 시상면 하한 0.14가 그 대역 한가운데라 " +
             "정상 파지가 여러 번 거절됐다('한 번에 안 되고 손을 좀 트니까 됐다'의 정체).\n\n" +
             "★<b>내리기만 한다</b>(Mathf.Min). 측두 하한은 이미 0.11이라 이 값이 그걸 끌어올리지 않는다.\n" +
             "★위 두 Vector2는 씬에 직렬화돼 있어 코드에서 못 바꾼다(규칙 7). 이 필드는 신규라 먹는다.")]
    [SerializeField] private float gripSpanMinOverride = 0.12f;

    // ★새 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).
    //   ★일부러 <b>넓게</b> 잡았다. 첫 판은 "얼마나 나오나"를 재는 판이다 —
    //     좁혀 놓고 시작하면 파지가 안 잡혀 아무것도 못 잰다(08-31에 하한 14cm로 그 일을 겪었다).
    //     실측 cm가 나오면 그때 좁힌다.
    [Tooltip("★<b>엄지 단독</b>일 때 쓰는 양손 간격 범위(m).\n" +
             "기존 범위는 엄지·검지 중점 기준이라 엄지 단독에는 안 맞는다.\n" +
             "첫 실측용으로 넓게 열어 뒀다 — 실제 cm를 확인한 뒤 좁힌다.")]
    [SerializeField] private Vector2 thumbOnlySpanRange = new Vector2(0.05f, 0.35f);

    [Tooltip("★<b>기본 꺼짐</b>. 핀치 폭으로도 막을지.\n\n" +
             "2026-08-31 실측: 켜 뒀더니 <b>정상적인 앞뒤 파지가 막혔다</b>. " +
             "이마·후두를 잡으면 엄지와 검지가 머리를 사이에 두고 벌어져 15~20cm가 나온다 — " +
             "'집는' 파지가 아니라 '감싸는' 파지라서 핀치 폭이라는 신호 자체가 맞지 않았다.\n\n" +
             "그래서 지금은 <b>표시만</b> 하고 판정에는 안 쓴다. 리드아웃의 실측치를 보고 " +
             "쓸 만한 대역이 있다고 판단되면 그때 켠다.")]
    [SerializeField] private bool usePinchGate = false;

    [Tooltip("한 손의 엄지 끝 ↔ 검지 끝 거리 허용 범위(m). usePinchGate가 켜져 있을 때만 판정에 쓴다.")]
    [SerializeField] private Vector2 pinchWidthRange = new Vector2(0.01f, 0.20f);

    [Tooltip("0점을 잡은 뒤 파지가 이 비율만큼 더 벗어나야 '풀렸다'로 본다.\n" +
             "★잡을 때와 같은 기준으로 풀면 경계에서 깜빡인다.")]
    [SerializeField] private float releaseHysteresis = 0.25f;

    [Tooltip("풀림이 이만큼 지속돼야 실제로 해제한다(초). 트래킹이 한 프레임 튀는 것에 안 넘어가려고 둔다.")]
    [SerializeField] private float releaseGraceSeconds = 0.35f;

    // ── 어깨 기준선 (2026-09-01) ────────────────────────────────────────
    // 2026-09-01 사용자: "실측모드에서는 체크리스트를 빼고, 대신 환자의 양어깨에 손을 올려
    //   중심선을 그리는 게 있어야 할 것 같다. 그래야 환자가 몸을 틀었는지 눈으로 볼 때 도움이 된다."
    //
    // ★<b>측정에는 안 쓴다.</b> 각도는 종전대로 파지선에서 세운 축으로 잰다.
    //   어깨는 <b>보이는 기준</b>일 뿐이다 — 축선 3개와 중심선을 여기 고정해 두면
    //   환자가 몸을 틀었을 때 손 파지가 그 기준에서 어긋나는 게 눈에 보인다.
    //   ★어깨선으로 축을 세우는 것(몸통 보상 분리)은 별개 문제다. 설계서 미결로 남아 있고,
    //     어깨선은 굴곡·신전의 회전축과 나란해서 그쪽은 어차피 C7이 따로 필요하다.
    //
    // ★08-31에 지운 AwaitShoulders와 <b>목적이 다르다</b>. 그때는 축을 유도하려던 것이라
    //   파지선으로 되니 필요가 없어졌다. 이번 것은 표시 기준이다.

    [Header("=== 어깨 기준선 (2026-09-01) ===")]
    [Tooltip("★끄면 어깨를 안 짚고 바로 파지로 간다(종전 동작).")]
    [SerializeField] private bool requireReference = true;

    // ── 평가 진행 (2026-09-02) ────────────────────────────────────────────
    // ★전부 새 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).

    [Tooltip("★같은 파지를 쓰는 방향끼리는 중립을 <b>다시 안 잡는다</b>.\n" +
             "시상면(굴곡·신전) 1회 · 측두(측굴 2 + 회전 2) 1회.\n" +
             "손을 떼면 자동으로 무효화되고 다시 잡게 된다.")]
    [SerializeField] private bool carryNeutralWithinGrip = true;

    [Tooltip("★평가 문구 — 절차(어디를 어떻게 잡아라)를 화면에 안 띄운다.\n" +
             "끄면 종전의 자세한 안내로 돌아간다.")]
    [SerializeField] private bool evaluationGuidance = true;

    [Tooltip("★평가에서 <b>능동·수동·차이 숫자와 '몇 도 더'를 화면에서 숨긴다</b>(2026-09-03 지시).\n" +
             "★<b>지우는 게 아니다</b> — results에 그대로 쌓이고 _rom.csv·결과지에는 정상으로 나간다.\n" +
             "  힌트를 줄이자는 것뿐이라, 끄면 종전처럼 다 보인다.\n" +
             "★신규 필드라 코드 기본값이 먹는다(규칙 7).")]
    [SerializeField] private bool hideResultNumbers = true;

    /// <summary>
    /// 평가에서 힌트가 되는 표시를 감추는가.
    /// ★감추는 것: <b>능동·수동·차이 숫자</b>, <b>"N도 더"</b>, <b>절차 사슬(중립 ▶ 능동 ▶ 압박)</b>.
    /// ★남기는 것: 방향 이름, 현재 각도, 홀드 게이지, 방향 목록.
    ///   그건 절차를 알려 주는 게 아니라 "지금 먹히고 있나"를 보는 것이다 —
    ///   정지로만 넘어가는 구조라 이게 없으면 왜 안 넘어가는지 알 수가 없다.
    /// ★값은 그대로 쌓인다. results·_rom.csv·결과지에는 정상으로 나간다.
    /// </summary>
    private bool HideHints => evaluationGuidance && hideResultNumbers;

    [Tooltip("★<b>정방향으로 간 각만</b> 센다. 굴곡을 재는 중에 뒤로 젖히면 각이 안 쌓인다.\n" +
             "끄면 종전대로 크기만 봐서, 반대로 움직여도 능동·압박이 잡힌다.\n" +
             "★Play에서 정방향인데 바늘이 거꾸로 가면 부호 규약이 뒤집힌 것이니 이걸 끄고 알릴 것.")]
    [SerializeField] private bool requireForwardDirection = true;

    [Tooltip("압박을 생략했을 때 깎는 점수(1회당).\n" +
             "★폭을 작게 잡았다 — 점수가 낮게 나오면 거부감이 생긴다(2026-09-02 사용자).")]
    [SerializeField] private float passiveSkipPenalty = 5f;

    [Tooltip("파지를 놓쳐 다시 잡았을 때 깎는 점수(1회당).")]
    [SerializeField] private float gripReleasePenalty = 2f;

    [Tooltip("아무리 깎여도 이 아래로는 안 내려간다.\n" +
             "★6방향을 전부 생략해도 이 점수는 남는다 — 학습자가 납득할 하한이다.")]
    [SerializeField] private float minRomScore = 60f;

    [Tooltip("양손을 어깨에 올렸다고 볼 간격(m). 사람 어깨 폭 대역이다.")]
    [SerializeField] private Vector2 shoulderSpanRange = new Vector2(0.28f, 0.55f);

    // ★새 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).
    //   2026-09-02 사용자: "처음 어깨 중립 잡을때 간격 줄여줘."
    //   ★어깨선도 <b>같은 점</b>에서 긋는다 — CaptureShoulders가 TryGetHands를 타고,
    //     그게 TryGetPinchPoint다. 엄지 단독이면 어깨선도 엄지끝↔엄지끝이 된다.
    //     기존 28~55cm는 엄지·검지 <b>중점</b> 기준이라 그대로 쓰면 안 맞는다.
    //   ★첫 판은 재는 판이라 하한을 넉넉히 내렸다. 거절 문구가 실제 cm를 찍어 준다.
    [Tooltip("★<b>엄지 단독</b>일 때 쓰는 어깨 폭 대역(m).\n" +
             "기존 대역은 엄지·검지 중점 기준이라 엄지 단독에는 안 맞는다.\n" +
             "실제 cm를 확인한 뒤 좁힌다.")]
    [SerializeField] private Vector2 thumbOnlyShoulderSpanRange = new Vector2(0.15f, 0.55f);

    /// <summary>지금 쓸 어깨 폭 대역. 엄지 단독이면 전용 대역을 쓴다.</summary>
    private Vector2 ShoulderSpanRangeNow
        => (gripJudge != null && gripJudge.IsThumbOnly) ? thumbOnlyShoulderSpanRange : shoulderSpanRange;

    [Tooltip("양손 높이 차 허용(m). 어깨는 좌우가 대체로 같은 높이다 —\n" +
             "머리를 잡은 것과 구분하는 데 이게 제일 잘 듣는다.")]
    [SerializeField] private float shoulderLevelTolerance = 0.12f;

    [Tooltip("중심선 길이(m). 어깨 중점에서 위아래로 절반씩 뻗는다.")]
    [SerializeField] private float midlineLength = 0.70f;

    [Tooltip("★중심선을 측정 내내 띄운다(2026-09-01 사용자 선택). 끄면 짚을 때만 잠깐 보인다.")]
    [SerializeField] private bool midlineAlwaysOn = true;

    [SerializeField] private Color midlineColor = new Color(0.45f, 1f, 0.85f, 0.9f);

    [Tooltip("어깨선(좌우 어깨를 잇는 <b>수평선</b>)도 같이 그린다.")]
    [SerializeField] private bool showShoulderLine = true;

    [Tooltip("★어깨선(수평선) 길이(m). 어깨 중점에서 좌우로 절반씩 뻗는다.\n" +
             "0이면 손을 짚은 자리 그대로다(종전 동작).\n" +
             "2026-09-01 사용자: 길이를 말한 건 세로 중심선이 아니라 이 수평선이었다.")]
    [SerializeField] private float shoulderLineLength = 0.9f;

    // ── 면별 방향 부호 (2026-09-01) ──────────────────────────────────────
    // ★"뒤집지 말라"는 <b>단계가 바뀔 때 저절로 뒤집히는 것</b>을 말한 것이다. 그건 없앴다.
    //   여기 셋은 세션 내내 <b>안 변하는 고정 상수</b>다 — 한 번 맞춰 두면 다시 안 바뀐다.
    //
    // ★이 부호는 계산으로 못 정한다. 교육 쪽 CervicalRomDriver.AxisOf의 부호도
    //   08-24에 Play에서 눈으로 확인해 뒤집어 둔 것이라고 그 코드에 적혀 있다.
    //   09-01에 내가 이걸 추론으로 맞히려다 세 번 틀렸고 맞던 시상면까지 뒤집었다.
    //   확정되면 AxisFor의 기본 부호를 그 값으로 굳히고 이 셋은 지운다.

    [Header("=== 면별 방향 부호 (확정용) ===")]
    [Tooltip("굴곡·신전이 반대로 기울면 켠다.")]
    [SerializeField] private bool flipSagittal;

    [Tooltip("좌측굴·우측굴이 반대로 기울면 켠다.")]
    [SerializeField] private bool flipCoronal;

    [Tooltip("좌회전·우회전이 반대로 기울면 켠다.")]
    [SerializeField] private bool flipTransverse;

    [Tooltip("★<b>횡단면 0°가 환자 뒤를 가리키면</b> 이것만 켠다. 다른 면은 안 건드린다.\n\n" +
             "각도기는 <b>회전일 때만</b> 0°로 Torso.forward를 쓴다(나머지는 Torso.up).\n" +
             "관상면 축은 axFwd를 따로 쓰므로, 여기만 뒤집으면 횡단면 0°만 움직인다.\n\n" +
             "★기준틀의 전후축(refFwd) 자체를 뒤집으면 안 된다 — 관상면 축이 딸려 와서\n" +
             "  측굴·회전 네 방향이 같이 깨진다(rom-frame-verify 전수 검증, 2026-09-01).\n" +
             "  09-01 사용자: '내가 필요한 건 횡단면의 앞뒤 전환인데 왜 다른 단면까지 바뀌냐.'")]
    [SerializeField] private bool transverseZeroFlip;

    [Tooltip("★시술자가 환자 <b>뒤</b>에 서서 어깨를 짚으면 켠다(기본). 마주 보고 짚으면 끈다.\n\n" +
             "손 두 개와 헤드셋만으로는 환자의 앞뒤를 알아낼 수가 없다. 서는 자리가 좌우축의\n" +
             "부호를 정하고, 그 하나가 여섯 방향을 전부 좌우한다.\n" +
             "★rom-frame-verify 전수 검증 결과 <b>맞는 조합은 서는 자리마다 하나씩</b>이다 —\n" +
             "  뒤에 서면 r−l, 마주 보면 l−r. 그 외에는 어딘가 반드시 깨진다.")]
    [SerializeField] private bool operatorBehindPatient = true;

    [Header("=== 건전성 검사 ===")]
    [Tooltip("파지 벡터의 면 성분이 이 비율보다 작으면 '이 파지로는 못 잰다'로 본다.\n" +
             "0.34 = 축과 20도 이내. 감도가 0에 가까워 잡음만 읽힌다.")]
    [SerializeField] private float minPerpRatio = 0.34f;

    [Tooltip("머리는 강체라 양손 거리가 보존돼야 한다. 이 비율을 넘게 변하면 파지가 미끄러진 것.")]
    [SerializeField] private float slipTolerance = 0.15f;

    [Tooltip("파지 벡터 저역통과 시간상수(초). 0이면 생값.")]
    [SerializeField] private float smoothing = 0.08f;

    // ── 추적 튐 방지 (2026-09-01) ────────────────────────────────────────
    // 2026-09-01 사용자: "신전이랑 굴곡할 때 숙여지는 방향 쪽을 지탱하는 손이
    //   환자 머리에 가려져서 손이 튀어버린다."
    //
    // ★손 위치는 XRHand_*Tip <b>Transform</b>에서 온다. Transform은 추적을 놓쳐도
    //   위치를 계속 내놓기 때문에 "지금 못 믿겠다"는 신호가 이 층에는 없다.
    //   신뢰도를 붙이려면 OVRHand 배선이나 com.unity.xr.hands가 새로 필요하다(둘 다 지금 없다).
    //
    // ★대신 <b>머리가 강체</b>라는 것을 쓴다. 파지 중인 손은 사람이 낼 수 있는 속도가
    //   뻔한데, 추적이 튈 때는 한 프레임에 그보다 훨씬 멀리 순간이동한다.
    //   그 프레임을 <b>버린다</b>(손을 못 읽은 것과 같이 취급). 배선도 패키지도 안 늘린다.

    [Tooltip("파지 중인 손이 낼 수 있다고 보는 최대 속도(m/s). 이보다 빨리 뛰면 추적이 튄 것으로 보고 그 프레임을 버린다.\n" +
             "★0 이하면 이 검사를 끈다.")]
    [SerializeField] private float maxHandSpeed = 1.2f;

    [Tooltip("★버리기만 하다 영영 못 따라가는 걸 막는다. 이만큼 계속 거절되면 " +
             "'손이 진짜로 그리 갔다'고 보고 새 위치를 받아들인다(초).")]
    [SerializeField] private float maxRejectSeconds = 0.5f;

    // ── 한 손 이어가기 (2026-09-01) ──────────────────────────────────────
    // 2026-09-01 사용자: "신전에서 뒤통수 쪽 손이 많이 가려져서 인식이 안 되거나 하고,
    //   그러다 보니 위치 어긋남이 생기기도 해. 한 손만 인식돼도 이어서 되게 할 수 있나?"
    //
    // ★된다. 그리고 <b>지금은</b> 된다 — 08-31에는 못 했다.
    //   각은 원래 양손 벡터 V = R − L의 회전으로 잰다. 한 손을 잃으면 V가 없어져
    //   "각이 얼어붙을 뿐"이라 실효가 없었다.
    //   ★어깨에서 <b>회전 중심(목)</b>을 잡아 두면서 사정이 달라졌다.
    //     머리는 강체라 목 둘레로 돈다. 손 한 점도 그 축 둘레의 원 위를 움직이므로,
    //     <b>그 점 하나의 회전각이 곧 목의 회전각</b>이다. 중심을 알기 때문에 성립한다.
    //   ★대신 미끄러짐을 못 잡는다 — 양손일 때는 손 사이 거리가 보존되는지로 걸렀는데,
    //     한 손이면 그 검사가 성립하지 않는다. 그래서 오래는 못 버티게 시간을 끊는다.

    [Tooltip("★한 손만 읽혀도 각을 이어서 낸다. 회전 중심(목)을 알기 때문에 가능하다.\n" +
             "끄면 양손이 다 읽힐 때만 각이 나온다(종전 동작).")]
    [SerializeField] private bool singleHandFallback = true;

    [Tooltip("★한 손으로 버티는 최대 시간(초). 이걸 넘으면 각을 안 낸다.\n" +
             "한 손이면 파지 미끄러짐을 검사할 수가 없어서, 오래 끌면 조용히 틀린 값이 쌓인다.")]
    [SerializeField] private float singleHandMaxSeconds = 4f;

    [Header("=== 표시 ===")]
    [SerializeField] private bool showReadout = true;
    [SerializeField] private bool showAxes = true;

    [Tooltip("단계 진행·정지 게이지·방향 목록을 같이 보여준다.\n" +
             "★정지로 넘어가는 구조라 게이지가 없으면 홀드가 먹고 있는지 알 수가 없다.")]
    [SerializeField] private bool showProgress = true;

    [Header("=== 각도기 (180도 반원) ===")]
    [Tooltip("★실습모드의 CervicalRomPlaneGauge와 무관한 자체 구현이다.\n" +
             "그쪽은 13개 술기가 공유하는 파일이라 건드리지 않는다.")]
    [SerializeField] private bool showGauge = true;

    [Tooltip("0도를 가운데 두고 좌우로 이만큼씩. 90이면 반원 180도가 된다.")]
    [SerializeField] private float gaugeHalfSpan = 90f;

    [SerializeField] private float gaugeRadius = 0.30f;
    [SerializeField] private float majorStep = 10f;
    [SerializeField] private float minorStep = 5f;
    [SerializeField] private float microStep = 1f;
    [SerializeField] private float majorTickLength = 0.055f;
    [SerializeField] private float minorTickLength = 0.035f;
    [SerializeField] private float microTickLength = 0.018f;
    [SerializeField] private float tickWidth = 0.0035f;
    [SerializeField] private float gaugeLabelSize = 0.035f;
    [SerializeField] private float gaugeLabelOffset = 0.028f;

    [SerializeField] private Color tickColor = new Color(0.85f, 0.90f, 1f, 0.95f);
    [SerializeField] private Color microColor = new Color(0.60f, 0.68f, 0.80f, 0.55f);
    [SerializeField] private Color zeroLineColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color needleColor = new Color(1f, 0.85f, 0.20f);
    [SerializeField] private Color activeMarkColor = new Color(0.35f, 0.85f, 1f);
    [SerializeField] private Color passiveMarkColor = new Color(1f, 0.45f, 0.35f);
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float readoutSize = 0.05f;

    [Tooltip("★글씨 크기 배율(안내문 + 눈금 숫자).\n" +
             "2026-08-31 사용자: '작은데다 가까워서 흐려 글씨가 안 보였다'.\n" +
             "★<b>2026-09-01 정정</b>: 08-31에 '신규 필드라 코드 기본값이 먹는다'고 적었는데, " +
             "그 뒤 씬을 저장하면서 1.8이 굳었다. 이제는 코드에서 못 바꾼다 — " +
             "안내문 크기는 아래 readoutScaleOverride로 바꾼다.")]
    [SerializeField] private float textScale = 1.8f;

    [Tooltip("안내문을 기준점보다 이만큼 위에 띄운다(m). 손과 겹치지 않게 띄운다.")]
    [SerializeField] private float readoutRise = 0.34f;

    [Tooltip("★안내문이 눈에서 이보다 가까우면 밀어낸다(m). VR은 너무 가까우면 초점이 안 맞아 흐리다.")]
    [SerializeField] private float readoutMinDistance = 0.55f;

    // ── 안내문 배치 덮어쓰기 (2026-09-01) ────────────────────────────────
    // ★위 textScale·readoutRise는 <b>이미 씬에 직렬화됐다</b>(1.8 / 0.34). 163번 줄 툴팁의
    //   "신규 필드라 코드 기본값이 먹는다"는 08-31 당시엔 맞았지만 그 뒤 씬을 저장하면서
    //   값이 굳었다 — 지금은 코드에서 못 바꾼다(규칙 7). 그래서 holdSecondsOverride와
    //   같은 방식으로 덮어쓰기 필드를 따로 둔다.
    // 2026-09-01 사용자: "실측할 때 환자 머리가 아니라 그거 보려고 고개를 위로 살짝 올려야 해서 불편하다."

    [Tooltip("★켜면 아래 두 값이 textScale·readoutRise를 대신한다. 끄면 씬(인스펙터) 값을 쓴다.")]
    [SerializeField] private bool overrideReadoutPlacement = true;

    [Tooltip("안내문을 기준점보다 이만큼 위에 띄운다(m). 씬 값은 0.34였다.\n" +
             "★각도기 반지름이 0.30이라 이보다 낮추면 눈금 호와 겹칠 수 있다 — " +
             "그때는 음수로 내려 각도기 <b>아래</b>로 빼는 편이 낫다.\n" +
             "★2026-09-03 컨펌: '손에 좀 더 가깝게' → 0.18 → 0.10.")]
    [SerializeField] private float readoutRiseOverride = 0.10f;

    [Tooltip("★<b>안내문 전용</b> 글씨 배율. textScale은 눈금 숫자까지 같이 키워서 따로 뒀다.\n" +
             "씬의 textScale은 1.8이고 readoutSize는 0.05다 → 1.8이면 종전과 같은 크기.")]
    [SerializeField] private float readoutScaleOverride = 2.6f;

    /// <summary>실제로 쓸 안내문 높이(m).</summary>
    private float ReadoutRiseNow => overrideReadoutPlacement ? readoutRiseOverride : readoutRise;

    /// <summary>실제로 쓸 안내문 글씨 배율.</summary>
    private float ReadoutScaleNow => overrideReadoutPlacement ? readoutScaleOverride : textScale;

    // ── 안내문을 진행 UI로 보내기 (2026-09-03) ───────────────────────────
    // 2026-09-03 사용자: "이미 진행 UI가 있는데 손 위에 별도 UI가 뜨는 게 가독성이 떨어진다.
    //   정보는 진행Root에 띄우되, 그 값이 지금처럼 손 근처에 따라왔으면 좋겠다."
    // → 글자는 진행Root(ScenarioGuideUIController)가 그리고, 진행Root를 손을 따라 옮기는 것은
    //   CervicalRomMeasurementBridge가 한다(실측 진입/이탈이 이미 거기서 대칭으로 처리된다).
    //
    // ★showReadout은 씬에 1이 직렬화돼 있어 코드 기본값으로는 못 끈다(규칙 7).
    //   그래서 <b>신규 필드</b>로 손잡이를 따로 둔다 — 신규 필드라 코드 기본값이 그대로 먹는다.

    // ★★[미사용 2026-09-03] 이 필드는 씬에 <b>1로 굳었다</b>. 코드 기본값을 false로 바꿔도
    //   씬 값이 이겨서(규칙 7) 안내문이 손 옆에도 진행Root에도 안 그려지는 상태가 됐다 —
    //   사용자: "왜 아무것도 안 보이는데."
    //   → 더 이상 읽지 않는다. 판단은 아래 <see cref="ReadoutClaimed"/>(런타임 플래그)가 한다.
    //     런타임 대입은 직렬화를 타지 않으므로 씬 값에 안 진다.
    [SerializeField] private bool routeReadoutToGuideUI;

    /// <summary>
    /// 진행 UI가 안내문을 가져갔는가. <b>브리지가 매 프레임 정한다</b>(런타임 전용).
    /// ★아무도 안 가져가면 종전대로 손 옆 월드 텍스트로 그린다 — 그게 기본이다.
    /// </summary>
    public bool ReadoutClaimed { get; private set; }

    /// <summary>진행 UI가 안내문을 가져간다/돌려준다. 가져간 쪽이 돌려준다.</summary>
    public void ClaimReadout(bool on) => ReadoutClaimed = on;

    /// <summary>안내문을 진행 UI가 그리는가.</summary>
    public bool RouteReadoutToGuideUI => ReadoutClaimed;

    // ★측정 동결 (2026-09-03). 결과 단계처럼 "다 쟀다"는 자리에서 켠다.
    //   표시는 그대로 두고 <b>확정과 0점 무효화만</b> 멈춘다 — 값이 덧씌워지는 것을 막는 것이 목적이다.
    private bool frozen;

    /// <summary>측정을 얼리거나 푼다. 켠 쪽이 끈다.</summary>
    public void SetFrozen(bool on)
    {
        if (frozen == on) return;
        frozen = on;
        if (showDebugLogs) Debug.Log($"[실측] 측정 {(on ? "동결" : "해제")}");
    }

    /// <summary>
    /// 마지막으로 만든 안내문. 진행 UI로 보낼 때 브리지가 읽는다.
    /// ★내용이 바뀔 때만 새로 만든다(아래 dedup 조건) — 매 프레임 문자열을 만들지 않는다.
    /// </summary>
    public string ReadoutText { get; private set; } = "";

    /// <summary>
    /// 안내문을 놓던 자리(월드). 진행 UI를 여기에 맞추면 종전과 같은 높이에 온다.
    /// ★뒤로·아래로 미는 것은 브리지의 오프셋이 한다 — 여기는 종전 자리 그대로 둔다.
    /// </summary>
    public Vector3 ReadoutAnchor => pivot + Vector3.up * ReadoutRiseNow;

    [Tooltip("★<b>양손 파지 중점</b>에서 이만큼 올린 곳을 각도기 기준점으로 쓴다(표시 전용, 각도에는 영향 없음).\n" +
             "음수를 넣으면 파지 위치보다 아래에 뜬다 — 머리에 가려 안 보일 때 쓴다.\n" +
             "★씬에 값이 직렬화돼 있으므로 코드 기본값이 아니라 인스펙터 값이 먹는다.")]
    [SerializeField] private float pivotRise = 0.10f;
    [SerializeField] private float axisLength = 0.18f;

    [Tooltip("★축 끝에 '환자 오른쪽·왼쪽·앞·뒤·위'를 글씨로 붙인다.\n" +
             "선만 그으면 어느 쪽이 환자 오른쪽인지 볼 방법이 없다 — 부호 확인용이다(2026-09-01).")]
    [SerializeField] private bool showAxisLabels = true;

    [SerializeField] private float axisLabelSize = 0.03f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;
    [Tooltip("에디터 Play에서 키로 진행한다. VR에서는 정지로 넘어간다.")]
    [SerializeField] private bool useKeyboard = true;

    // --- 상태 ---
    private Stage stage = Stage.Idle;
    private bool frameReady;
    private bool neutralReady;

    private Vector3 axRight, axUp, axFwd;   // t0에 세운 십자축 (환자 기준틀)
    private Vector3 pivot;                  // 표시 기준점
    private float gripWidth;

    private Vector3 v0;                     // 중립 파지 벡터
    private float len0;
    private Vector3 vNow;                   // 저역통과를 거친 현재 파지 벡터
    private bool vNowValid;

    private Vector3 prevLeft, prevRight;
    private bool prevValid;
    private float holdTimer;

    // --- 어깨 기준선 (2026-09-01) ---
    private bool refReady;
    private Vector3 shoulderL, shoulderR, shoulderMid;
    private Vector3 refRight, refUp, refFwd;   // 몸통 기준틀 (표시 전용)

    // --- 추적 튐 필터 / 유실 유예 (2026-09-01) ---
    // ★손마다 따로 본다. 한쪽만 가려지는 게 실제 상황이라, 한 손이 튀었다고
    //   멀쩡한 다른 손까지 버리면 한 손 이어가기가 성립하지 않는다.
    private Vector3 acceptedLeft, acceptedRight;  // 마지막으로 믿기로 한 손 위치
    private bool acceptedValidL, acceptedValidR;
    private float rejectSecondsL, rejectSecondsR; // 연속으로 거절한 시간(손별)
    private bool leftOk, rightOk;                 // 이번 프레임에 믿을 수 있는가

    // 한 손 이어가기용 — 중립에서 회전 중심 → 각 손
    private Vector3 anglePivot, radL0, radR0;
    private float singleHandSeconds;
    private float lostSeconds;                    // 손을 못 읽은 채 흐른 시간
    private float holdAnchorAngle;                // 이번 홀드를 시작한 시점의 각
    private bool holdAnchorValid;

    // ★어디서 시간이 나갔는지 가르는 계수기. 방향이 끝날 때 로그로 남긴다 —
    //   09-01 실측에서 "신전이 134초"였는데 CSV의 StepTime 하나로는 원인을 못 갈랐다.
    private int rejectedFrames;      // 튐으로 버린 프레임
    private int trackingRelocks;     // 너무 오래 거절해 새 위치를 받아들인 횟수
    private int holdResets;          // 흔들려서 홀드가 깎여 0까지 간 횟수
    private float lostTotal;         // 손을 못 읽은 총 시간(초)
    private bool relockedThisFrame;         // 이 프레임에 손이 다시 잡혔는가 - 그 프레임은 속도를 안 잰다
    private float peakAngle;                // 이 단계에서 본 최대 각 - minAngleToMark 판정용
    private float angleExcursionSeconds;    // 각도 앵커를 벗어나 있은 시간(초). 유예 판정용.
    private float passiveBaseAngle;         // 압박 단계의 출발선 = 능동으로 도달한 각(크기)
    private float releaseTimer;             // 파지가 풀린 채 흐른 시간
    private float smoothedSpeed;            // 저역통과를 거친 손 속도 - 정지 판정용
    private string lastWarn;                // 마지막 실패 사유(화면에 띄운다)
    private float lastWarnTime = -99f;
    private float lastGripSpan, lastPinchL, lastPinchR;   // 리드아웃 표시용 실측치

    // ★active·passive는 <b>부호 있는</b> 각이다. 각도기 지침이 어느 쪽으로 가는지에 쓴다.
    //   기록·표시에 나가는 값은 Mathf.Abs를 거친 크기다.
    private struct Result
    {
        public float active, passive;
        public bool hasActive, hasPassive;

        // ★이 방향에서 시간이 어디로 나갔는지. 방향이 끝나는 순간 계수기를 여기 찍는다.
        //   결과 CSV까지 실려 나간다 — logcat은 40분이면 밀려서 09-01에 통째로 잃었다.
        public int holdResets, rejectedFrames, relocks;

        // ── 평가 계수기 (2026-09-02) ──────────────────────────────────
        // ★추적 계수기(위)와 성격이 다르다. 위는 <b>기계가 잘 읽었나</b>고,
        //   이건 <b>사람이 절차를 밟았나</b>다. 감점은 이쪽만 본다.
        public bool passiveSkipped;   // 압박을 안 하고 중립으로 돌아왔다
        public int gripReleases;      // 이 방향에서 손을 뗀 횟수
        public float lostSeconds;
    }
    private readonly Result[] results = new Result[7];

    private float appliedReadoutScale = -1f;   // 안내문에 실제로 얹은 배율

    // --- 표시 오브젝트 ---
    private Transform root;
    private TextMeshPro readout;
    private LineRenderer lineRight, lineUp, lineFwd, lineNeutral, lineNow;
    private LineRenderer lineMidline, lineShoulder;
    private TextMeshPro labelRightPos, labelRightNeg, labelFwdPos, labelFwdNeg, labelUpPos;
    private LineRenderer needle, activeMark, passiveMark;
    private Material sharedMaterial;

    // --- 각도기 ---
    private Mesh gaugeMesh;
    private MeshFilter gaugeFilter;
    private readonly List<Vector3> gVerts = new List<Vector3>(2048);
    private readonly List<int> gTris = new List<int>(3072);
    private readonly List<Color> gCols = new List<Color>(2048);
    private readonly List<TextMeshPro> gaugeLabels = new List<TextMeshPro>(24);
    private CervicalRomDriver.Direction builtDirection = CervicalRomDriver.Direction.None;
    private int builtStamp = -1;    // 정렬·0점을 다시 잡으면 올라간다
    private int frameStamp;

    // ★Update에서 문자열을 새로 만들지 않는다(VR 프레임 예산).
    //   보여줄 값이 바뀐 프레임에만 다시 만든다.
    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
    private Stage shownStage = (Stage)(-1);
    private int shownAngle = int.MinValue;
    private int shownWarn = -1;
    private int shownHold = -99;    // 정지 게이지는 10칸으로 양자화해 그 칸이 바뀔 때만 다시 만든다
    private int shownMask = -1;     // 어느 방향이 끝났는지 비트마스크
    private int shownWarnSig;
    private int shownGrip = -1;     // 파지폭·핀치폭을 1mm로 양자화한 표식(0점 전에만 쓴다)

    public Stage CurrentStage => stage;
    public bool FrameReady => frameReady;
    public bool NeutralReady => neutralReady;
    public CervicalRomDriver.Direction MeasuredDirection => direction;

    public bool HasActive(CervicalRomDriver.Direction d) => results[(int)d].hasActive;
    public bool HasPassive(CervicalRomDriver.Direction d) => results[(int)d].hasPassive;

    /// <summary>
    /// 이 방향의 측정값(도, <b>크기</b>). 하나도 안 쟀으면 false.
    /// ★결과 화면이 읽는다 — 종전에는 결과 수집기가 교육용 드라이버만 봐서
    ///   실측 값이 결과창에 아예 안 나왔다(2026-08-31 사용자 지적).
    /// </summary>
    public bool TryGetResult(CervicalRomDriver.Direction d, out float activeDeg, out float passiveDeg,
                             out bool hasActive, out bool hasPassive)
    {
        activeDeg = passiveDeg = 0f;
        hasActive = hasPassive = false;

        int i = (int)d;
        if (i <= 0 || i >= results.Length) return false;

        Result r = results[i];
        hasActive = r.hasActive;
        hasPassive = r.hasPassive;
        activeDeg = Mathf.Abs(r.active);
        passiveDeg = Mathf.Abs(r.passive);
        return hasActive || hasPassive;
    }

    /// <summary>한 방향이라도 잰 게 있는가.</summary>
    public bool HasAnyResult
    {
        get
        {
            for (int i = 1; i < results.Length; i++)
                if (results[i].hasActive || results[i].hasPassive) return true;
            return false;
        }
    }

    /// <summary>브리지가 단계에 맞춰 방향을 지정한다. 0점은 파지가 바뀌므로 다시 잡는다.</summary>
    public void SetDirection(CervicalRomDriver.Direction d, bool keepNeutral = false)
    {
        // ★★같은 방향이면 <b>아무것도 하지 않는다</b>(2026-08-31 수정).
        //   브리지가 이 함수를 <b>매 프레임</b> 부른다(ApplyDirectionFor). 그런데 종전 조건
        //   `direction == d && (keepNeutral || !neutralReady)`는 <b>중립이 잡혀 있으면 조기 반환을 안 해서</b>,
        //   방향 단계에 들어간 순간부터 매 프레임 neutralReady를 지우고 holdTimer를 0으로 되돌렸다.
        //   증상 = "파지는 잡히는데 굴곡 단계에서 게이지가 영영 안 차고 안 넘어간다".
        //   같은 방향을 다시 지정하는 건 아무 의미가 없으므로 그냥 나간다.
        if (direction == d) return;

        // ★떠나기 전에 계수기를 <b>지금</b> 방향 칸에 넣는다. 안 그러면 다음 방향에 얹힌다.
        FlushTrackingCounters("방향 전환");

        // ★★<b>같은 파지면 중립을 이어간다</b>(2026-09-02 사용자 지시).
        //   손을 안 뗀다는 전제다. 실제로 떼면 UpdateGripRelease가 잡아 중립을 무효화하고
        //   다시 잡게 하므로, 전제가 깨져도 조용히 틀리지 않는다.
        bool carryNeutral = keepNeutral
                            || (carryNeutralWithinGrip && neutralReady && SameGripGroup(direction, d));

        direction = d;
        if (!carryNeutral)
        {
            neutralReady = false;
            stage = Stage.AwaitNeutral;
        }
        else
        {
            // 중립은 그대로 두고 바로 능동을 잰다.
            stage = Stage.Active;
        }
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;

        // ★방향이 바뀌면 튐 필터의 기준 위치도 놓아 준다.
        //   안 놓으면 새 파지 위치를 '튄 것'으로 보고 maxRejectSeconds만큼 거절한다.
        acceptedValidL = acceptedValidR = false; rejectSecondsL = rejectSecondsR = 0f; lostSeconds = 0f;

        frameStamp++;

        // ★평가 문구 — <b>방법을 알려주지 않는다</b>(2026-09-02 사용자 지시).
        //   "어디를 잡아라"(GripHintFor)는 술기 그 자체라 평가에서 빼야 한다.
        //   반면 "파지 후 정지"는 술기가 아니라 <b>앱이 값을 잡는 조건</b>이라 남긴다.
        if (carryNeutral) Mark($"-> {Label(direction)}");
        else if (evaluationGuidance) Mark($"-> {Label(direction)} — 파지 후 중립에서 정지");
        else Mark($"-> {Label(direction)}. {GripHintFor(direction)} 파지 후 중립에서 정지하세요.");
    }

    /// <summary>중립 근처로 돌아왔는가 — 복귀 substep을 넘길 조건이다.</summary>
    public bool IsBackToNeutral(float toleranceDeg)
        => TryGetAngle(out float deg, out _, out _) && deg <= toleranceDeg;

    /// <summary>지금 읽히는 각(도). 크기다. 못 재는 상태면 false.</summary>
    public bool TryGetAngle(out float degrees, out float perpRatio, out float signed)
    {
        degrees = 0f; perpRatio = 0f; signed = 0f;
        if (!neutralReady) return false;

        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return false;

        // ★한 손만 읽히면 <b>그 손의 회전</b>으로 잰다(2026-09-01).
        //   머리는 강체라 목(anglePivot) 둘레로 돈다. 손 한 점도 그 축 둘레의 원 위를
        //   움직이므로, 그 점의 회전각이 곧 목의 회전각이다 — 중심을 알기 때문에 성립한다.
        //   ★양손이 다 읽히면 종전대로 파지 벡터를 쓴다. 지렛대가 길어 잡음에 더 강하다.
        if (!vNowValid || !(leftOk && rightOk))
        {
            if (!singleHandFallback || !(leftOk || rightOk)) return false;
            if (singleHandSeconds > singleHandMaxSeconds) return false;

            Vector3 rad0 = leftOk ? radL0 : radR0;
            Vector3 radNow = (leftOk ? acceptedLeft : acceptedRight) - anglePivot;
            if (rad0.sqrMagnitude < 1e-6f) return false;

            Vector3 pa = Vector3.ProjectOnPlane(rad0, axis);
            Vector3 pb = Vector3.ProjectOnPlane(radNow, axis);
            if (pa.sqrMagnitude < 1e-8f || pb.sqrMagnitude < 1e-8f) return false;

            perpRatio = pa.magnitude / Mathf.Max(1e-6f, rad0.magnitude);
            signed = Vector3.SignedAngle(pa, pb, axis);
            degrees = Mathf.Abs(signed);
            return true;
        }

        Vector3 a = Vector3.ProjectOnPlane(v0, axis);
        Vector3 b = Vector3.ProjectOnPlane(vNow, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        perpRatio = a.magnitude / Mathf.Max(1e-6f, v0.magnitude);
        signed = Vector3.SignedAngle(a, b, axis);
        degrees = Mathf.Abs(signed);
        return true;
    }

    /// <summary>파지가 미끄러졌는가 - 머리가 강체라 양손 거리는 보존돼야 한다.</summary>
    public bool IsSlipping(out float ratio)
    {
        ratio = 1f;
        if (!neutralReady || !vNowValid || len0 < 1e-4f) return false;
        ratio = vNow.magnitude / len0;
        return Mathf.Abs(ratio - 1f) > slipTolerance;
    }

    private Vector3 AxisFor(CervicalRomDriver.Direction d)
    {
        // ★★반대 방향은 축 <b>부호가 반대</b>다(2026-09-01 수정).
        //   종전에는 짝마다 같은 축을 돌려줬다. 크기만 기록할 때는 그래도 됐지만,
        //   각도기가 이 축으로 <b>어느 쪽으로 기울지</b>를 정하면서 드러났다 —
        //   굴곡과 신전이 같은 쪽으로 기울어, 신전인데 앞으로 기울고
        //   앞쪽에 남아야 할 굴곡 마킹이 반대편으로 넘어갔다.
        //
        //   ★부호 규약은 CervicalRomDriver.AxisOf 그대로다. 거기 부호는 08-24에
        //     Play에서 눈으로 확인해 뒤집어 둔 것이라, 여기서 새로 정하면 안 되고 맞춰야 한다.
        //       굴곡 −좌우 / 신전 +좌우 · 우측굴 −전후 / 좌측굴 +전후 · 우회전 +수직 / 좌회전 −수직
        //
        //   기록값은 종전대로 크기(Mathf.Abs)라 이 부호가 측정 결과를 바꾸지 않는다.
        //   ★2026-09-01 — 이 부호는 <b>계산으로 못 정한다.</b> 교육 쪽 AxisOf 부호도 08-24에
        //     Play에서 눈으로 확인해 뒤집어 둔 것이다. 나는 09-01에 이걸 추론으로 맞히려다
        //     세 번 틀렸고, 마지막에는 <b>맞던 시상면까지 뒤집었다.</b>
        //     그래서 면마다 따로 뒤집을 수 있게 두고, 확정되면 여기 기본값을 그 값으로 굳힌다.
        //     ★이건 단계마다 뒤집는 게 아니다. 세션 내내 안 변하는 <b>고정 상수</b>다.
        float sag = flipSagittal ? -1f : 1f;
        float cor = flipCoronal ? -1f : 1f;
        float tra = flipTransverse ? -1f : 1f;

        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:        return -axRight * sag;
            case CervicalRomDriver.Direction.Extension:      return  axRight * sag;
            case CervicalRomDriver.Direction.LateralRight:   return  axFwd * cor;
            case CervicalRomDriver.Direction.LateralLeft:    return -axFwd * cor;
            case CervicalRomDriver.Direction.RotationRight:  return -axUp * tra;
            case CervicalRomDriver.Direction.RotationLeft:   return  axUp * tra;
            default:                                         return Vector3.zero;
        }
    }

    private void Awake()
    {
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();
        if (font == null) font = KoreanFontResolver.Resolve();
        EnsureGaugeProxy();
        ResetAll();
    }

    /// <summary>
    /// 실습 각도기가 읽을 대리 트랜스폼. 표시물(<c>root</c>)과 <b>수명이 다르다</b> —
    /// 각도기는 리드아웃이 꺼져 있어도 매 프레임 이걸 읽으므로 여기서 따로 만들고 따로 지운다.
    /// </summary>
    private void EnsureGaugeProxy()
    {
        if (proxyPivot == null)
        {
            var go = new GameObject("실측_각도기기준점") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            proxyPivot = go.transform;
        }
        if (proxyTorso == null)
        {
            var go = new GameObject("실측_각도기몸통") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            proxyTorso = go.transform;
        }
    }

    private void OnDestroy()
    {
        TearDownVisuals();
        if (proxyPivot != null) Destroy(proxyPivot.gameObject);
        if (proxyTorso != null) Destroy(proxyTorso.gameObject);
    }

    /// <summary>★꺼지면 표시물을 걷는다. 교육모드에서 실측 리드아웃이 남아 있으면 안 된다.</summary>
    private void OnDisable() => TearDownVisuals();

    // ================= 진행 =================

    [ContextMenu("0 - 처음부터")]
    public void ResetAll()
    {
        stage = Stage.AwaitNeutral;
        frameReady = neutralReady = false;
        vNowValid = prevValid = false;
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;
        for (int i = 0; i < results.Length; i++) results[i] = default;

        // ★어깨 기준도 놓는다. 세션마다 다시 잡는다 — 환자가 바뀌면 어깨도 바뀐다.
        refReady = false;
        acceptedValidL = acceptedValidR = false; rejectSecondsL = rejectSecondsR = 0f; lostSeconds = 0f;
        holdResets = 0; rejectedFrames = 0; trackingRelocks = 0; lostTotal = 0f;
        gripAnchorValid = false; angleExcursionSeconds = 0f;
        fixedGaugeAnchorValid = false;

        Mark(requireReference
            ? "처음부터 - 양손을 환자 양어깨에 올리고 정지하세요."
            : "처음부터 - 중립에서 머리를 파지하고 정지하세요.");
    }

    /// <summary>
    /// ★<b>파지선에서 기준축을 세운다.</b> 어깨를 짚는 단계를 없앤 자리다(2026-08-31).
    ///
    /// 어깨 정렬이 실제로 주던 정보는 <c>axRight</c> <b>하나뿐</b>이었다 —
    /// <c>axUp</c>은 원래 <c>Vector3.up</c>(중력)이고 <c>axFwd</c>는 거기서 파생이었다.
    /// 그런데 그 하나를 <b>파지 자체가 준다</b>. 술기가 면마다 파지를 바꾸기 때문이다.
    ///
    ///   시상면 파지(이마·후두) → 손 선 = <b>전후축</b> → axFwd 직접, axRight는 외적
    ///   관상면·횡단면 파지(양 측두) → 손 선 = <b>좌우축</b> → axRight 직접
    ///
    /// 이건 설계서 R1("기준 벡터는 회전축과 수직이어야 한다")을 뒤집어 읽은 것이다.
    /// 파지를 그 면에 맞게 바꾸는 이유가 원래 이것이었다.
    ///
    /// ★한계 — 어깨선은 <b>몸통</b> 기준이고 파지선은 <b>머리</b> 기준이다.
    ///   중립이라고 잡은 자세에서 머리가 이미 틀어져 있으면 축도 같이 틀어진다.
    ///   준비 단계의 표준자세 체크리스트가 그 역할을 대신한다.
    ///   ★틀어져도 <b>조용히 틀리지는 않는다</b> — perpRatio가 떨어져 "못 잽니다"로 드러난다.
    /// </summary>
    private bool CaptureFrameFromGrip(Vector3 l, Vector3 r)
    {
        // ★★기준틀은 어깨에서 <b>한 번</b> 세우고 끝이다(2026-09-01 사용자 지시).
        //   "각도기 위치랑 각도 실시간으로 바꾸지 마. 단면은 한번 생성되면 그걸로 끝인 거야.
        //    왜 초기화를 시키는 건데."
        //
        //   종전에는 방향마다 CaptureNeutral → 여기로 들어와 축을 <b>다시</b> 만들었다.
        //   파지선은 '오른손 − 왼손'이라 손을 바꿔 잡으면 부호가 통째로 뒤집힌다.
        //   그래서 굴곡을 재고 신전으로 넘어가는 사이에 축이 뒤집혀,
        //   ★앞쪽에 남아 있어야 할 굴곡 마킹이 신전 쪽으로 넘어가고
        //     신전인데 각도기가 앞으로 기울었다.
        //
        //   어깨선이 곧 좌우축이고, 거기서 세 축이 전부 나온다 —
        //   굴곡·신전은 좌우축 둘레, 측굴은 전후축 둘레, 회전은 수직축 둘레다.
        //   해부학적으로도 파지선보다 어깨선이 낫다(파지선은 좌우축에 <b>가까울</b> 뿐이다).
        //
        //   ★0점(v0)은 방향마다 다시 잡는다. 그건 파지가 바뀌면 당연히 달라지는 값이고,
        //     축과 달리 뒤집힘의 원인이 아니다.
        if (refReady)
        {
            axRight = refRight;
            axUp = refUp;
            axFwd = refFwd;

            gripWidth = (r - l).magnitude;
            pivot = (l + r) * 0.5f + Vector3.up * pivotRise;
            frameReady = true;
            // ★frameStamp를 올리지 않는다 — 틀이 안 바뀌었는데 올리면 각도기를 다시 세운다.
            return true;
        }

        // 어깨 기준을 안 쓰는 경우(requireReference 꺼짐)만 종전 경로를 탄다.
        axUp = Vector3.up;                                   // XR 월드는 중력 정렬이다
        Vector3 flat = Vector3.ProjectOnPlane(r - l, axUp);
        if (flat.sqrMagnitude < 1e-6f)
        {
            Warn("파지선이 수직에 가깝습니다 - 양손 높이를 비슷하게 맞춰 잡으세요.");
            return false;
        }
        flat.Normalize();

        if (IsSagittalGrip(direction))
        {
            axFwd = flat;                                    // 이마·후두 = 전후축
            axRight = Vector3.Cross(axUp, axFwd).normalized; // Unity: Cross(up, forward) = right
        }
        else
        {
            axRight = flat;                                  // 양 측두 = 좌우축
            axFwd = Vector3.Cross(axRight, axUp).normalized; // Unity: Cross(right, up) = forward
        }


        gripWidth = (r - l).magnitude;
        pivot = (l + r) * 0.5f + axUp * pivotRise;           // ★각도기는 원래 손 중점 기준이었다
        frameReady = true;
        frameStamp++;
        return true;
    }

    /// <summary>이 방향을 시상면 파지(이마·후두)로 재는가. 굴곡·신전만 그렇다.</summary>
    private static bool IsSagittalGrip(CervicalRomDriver.Direction d)
        => d == CervicalRomDriver.Direction.Flexion || d == CervicalRomDriver.Direction.Extension;

    /// <summary>
    /// 두 방향이 <b>같은 파지</b>를 쓰는가. 같으면 중립을 다시 잡을 이유가 없다.
    ///
    /// ★2026-09-02 사용자: "신전 굴곡은 같은 위치 파지니까 중립으로 돌아온 뒤에
    ///   다시 중립 값 측정할 거 없이 하고, 측굴 우회전도 좌우 포함 4구분에서
    ///   처음만 중립 파지 하고 그 후에는 손을 안 떼는 전제로 유지해서 자동으로 넘어가게 하자."
    ///
    ///   시상면(이마·후두) = 굴곡·신전 2개 / 측두(양 측두) = 측굴 2개 + 회전 2개 = 4개.
    /// ★중립을 이어가도 <b>되는</b> 근거: CaptureNeutral이 잡는 기준틀(axRight·axFwd)이
    ///   파지선에서 나오므로, 파지가 같으면 기준틀도 같다. 방향마다 다른 건 축 선택(AxisFor)뿐이다.
    /// </summary>
    private static bool SameGripGroup(CervicalRomDriver.Direction a, CervicalRomDriver.Direction b)
        => a != CervicalRomDriver.Direction.None && b != CervicalRomDriver.Direction.None
           && IsSagittalGrip(a) == IsSagittalGrip(b);

    // ── 파지 게이트 ───────────────────────────────────────────────────────
    // ★"정지 1.5초"만으로 0점을 잡으면 <b>허공에서 손이 멈춘 것</b>도 파지로 본다.
    //   실측은 가상 환자도 콜라이더도 없어서 접촉으로 확인할 방법이 없다.
    //   대신 손 자체가 두 가지를 말해 준다 — 양손 간격과 핀치 폭.
    //
    // ★조건을 빡빡하게 만드는 것보다 <b>틀렸을 때 되돌아오는 것</b>이 중요하다.
    //   그래서 확정 뒤에도 파지가 풀리면 0점을 무효화한다. 잘못 잡았으면 손을 떼고
    //   다시 잡는 게 자연스러운 동작이고, 그 동작이 그대로 복구 신호가 된다.

    /// <summary>지금 이 방향에 맞는 양손 간격 범위. 하한은 <see cref="gripSpanMinOverride"/>까지 내려간다.</summary>
    private Vector2 GripSpanRange(CervicalRomDriver.Direction d)
    {
        // ★★엄지 단독이면 <b>다른 범위</b>를 쓴다(2026-09-02).
        //   기존 범위(시상 12~26cm)는 <b>엄지·검지 중점</b> 사이 거리로 실측해 잡은 값이다.
        //   엄지만 쓰면 각 손의 기준점이 <b>핀치 폭의 절반</b>만큼 옮겨 앉는데,
        //   이 파지는 감싸는 파지라 엄지-검지가 15~20cm 벌어진다(08-31 실측).
        //   즉 손마다 7~10cm씩 움직인다 — 기존 범위로는 파지가 <b>영영 안 잡힐 수 있다.</b>
        //   ★어느 쪽으로 옮겨 앉는지는 <b>추론하지 않는다</b>(규칙 9). 넓게 열어 두고 재서 좁힌다.
        //   화면의 거절 문구가 실제 cm를 찍어 주므로 그게 곧 자 노릇을 한다.
        if (gripJudge != null && gripJudge.IsThumbOnly) return thumbOnlySpanRange;

        Vector2 r = IsSagittalGrip(d) ? sagittalGripRange : temporalGripRange;
        if (gripSpanMinOverride > 0f) r.x = Mathf.Min(r.x, gripSpanMinOverride);
        return r;
    }

    /// <summary>
    /// 지금 환자 머리를 잡고 있다고 볼 만한가. <paramref name="slack"/>은 해제 판정용 여유(0이면 확정 기준).
    /// </summary>
    private bool IsGripPlausible(Vector3 l, Vector3 r, float slack, out string why)
    {
        why = null;

        float span = Vector3.Distance(l, r);
        lastGripSpan = span;

        Vector2 range = GripSpanRange(direction);
        float pad = (range.y - range.x) * slack;
        if (span < range.x - pad || span > range.y + pad)
        {
            why = $"양손 간격 {span * 100f:F0}cm — {range.x * 100f:F0}~{range.y * 100f:F0}cm 범위 밖";
            return false;
        }

        bool okL = TryPinch(GripFingerTip.Side.Left, out float wl);
        bool okR = TryPinch(GripFingerTip.Side.Right, out float wr);
        lastPinchL = wl; lastPinchR = wr;

        // ★손끝을 못 읽으면 <b>막지 않는다</b>. 판정기가 없다고 진행이 죽으면 원인을 못 찾는다.
        if (!okL || !okR) return true;

        // ★핀치는 기본적으로 표시만 한다. 위 usePinchGate 툴팁 참조.
        if (!usePinchGate) return true;

        float lo = pinchWidthRange.x - (pinchWidthRange.y - pinchWidthRange.x) * slack;
        float hi = pinchWidthRange.y + (pinchWidthRange.y - pinchWidthRange.x) * slack;
        if (wl < lo || wl > hi || wr < lo || wr > hi)
        {
            why = $"핀치 폭 L{wl * 100f:F1} R{wr * 100f:F1}cm — {pinchWidthRange.x * 100f:F0}~{pinchWidthRange.y * 100f:F0}cm 범위 밖";
            return false;
        }
        return true;
    }

    private bool TryPinch(GripFingerTip.Side side, out float width)
    {
        width = 0f;
        if (leftHandOverride != null && rightHandOverride != null) return false;   // 직접 주입 모드엔 손가락이 없다
        return gripJudge != null && gripJudge.TryGetPinchWidth(side, out width);
    }

    /// <summary>0점을 잡은 뒤 파지가 풀렸으면 무효화하고 '중립 대기'로 되돌린다.</summary>
    private void UpdateGripRelease(bool has, Vector3 l, Vector3 r)
    {
        // ★얼려 두면 손을 떼도 0점을 무효화하지 않는다. 안 그러면 결과 단계에서 손을 내리는 순간
        //   AwaitNeutral로 떨어지고, 거기서 잠깐 정지하면 <b>마지막 방향을 다시 재기 시작한다</b>.
        //   2026-09-03 사용자: "마지막 좌회전은 측정이 끝났는데도 계속 주황글씨로 다시 측정이 되네."
        if (frozen) { releaseTimer = 0f; return; }

        if (!requireGripGate || !neutralReady) { releaseTimer = 0f; return; }

        bool held = has && IsGripPlausible(l, r, releaseHysteresis, out _);
        releaseTimer = held ? 0f : releaseTimer + Time.deltaTime;
        if (releaseTimer < releaseGraceSeconds) return;

        releaseTimer = 0f;
        neutralReady = false;
        frameReady = false;
        stage = Stage.AwaitNeutral;
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;
        frameStamp++;

        // ★손 뗀 횟수를 <b>센다</b>(2026-09-02 사용자 지시).
        //   "손을 안 뗀다"는 전제로 중립을 이어가므로, 전제가 깨진 횟수가 곧 감점 근거다.
        //   ★같은 파지를 이어 쓰는 동안 손을 떼면 그 뒤 방향들도 중립을 다시 잡게 되는데,
        //     그건 자동으로 그렇게 된다 — neutralReady를 여기서 내렸기 때문이다.
        int i = (int)direction;
        if (i > 0 && i < results.Length) results[i].gripReleases++;

        Mark("파지가 풀렸습니다 — 다시 잡고 중립에서 정지하세요.");
    }

    /// <summary>어깨 기준선이 잡혔는가. 실측 '준비' 단계를 넘길 조건이다.</summary>
    public bool ReferenceReady => refReady;

    // ================= 실습 각도기에 값 대기 (ICervicalRomGaugeSource) =================
    //
    // 2026-09-01 사용자: "각도기는 실습용 각도기를 쓰자, 실측용 말고."
    //
    // ★자체 반원 각도기(showGauge)는 지우지 않는다(사용자: "일단은 없애지 말아봐").
    //   usePracticeGauge가 켜져 있으면 그리지만 않는다. 되돌릴 자리가 남아 있어야 한다.
    // ★각도기는 그리기만 한다. 능동·수동을 정하는 건 이 클래스다 — 종전 그대로다.

    [Header("=== 실습 각도기 사용 (2026-09-01) ===")]
    [Tooltip("★켜면 실측에서도 CervicalRomPlaneGauge(실습 각도기)를 쓴다.\n" +
             "자체 반원 각도기는 그리지 않는다(코드는 남는다).")]
    [SerializeField] private bool usePracticeGauge = true;

    [Tooltip("참고치(임상 최대각)를 읽을 드라이버. 비우면 씬에서 찾는다.\n" +
             "★실측은 참고치를 스스로 안 갖는다 — 45·90 같은 임상값은 드라이버가 들고 있다.")]
    [SerializeField] private CervicalRomDriver referenceDriver;

    [Tooltip("★<b>측정용</b> 회전 중심 — 어깨 중점에서 이만큼 올린 곳(m). 목이 도는 자리다.\n" +
             "한 손만 읽힐 때 그 손의 회전각을 이 점 둘레로 잰다. 표시 위치와는 별개다.")]
    [SerializeField] private float gaugePivotRise = 0.12f;

    // ★★<b>손잡이를 나눈다</b>(2026-09-02). 종전에는 gaugePivotRise 하나가
    //   ①각도기를 <b>그릴 자리</b>와 ②한 손 이어가기의 <b>회전 중심</b>에 같이 쓰였다.
    //   그래서 "각도기를 어깨선으로 내려 달라"가 회전 중심까지 끌어내리는 요청이 돼 버린다.
    //
    //   2026-09-02 사용자: "각도기는 어깨축 잡은거 기준으로 나와줄래? 횡단면의 경우에
    //     그 축에서 좀더 올라와 있는 위치에 나와서 어깨선이랑 평행하는 선이 생겨서 지저분해 보이거든."
    //   → 횡단면 각도기는 <b>수평 원반</b>이라, 어깨선보다 12cm 위에 뜨면 어깨선과 나란한
    //     선이 하나 더 생긴다. 0으로 두면 어깨선 위에 겹쳐 앉아 그 선이 사라진다.
    //
    // ★새로 추가한 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).
    //   수직 다이얼(시상면·관상면)이 너무 낮게 앉으면 인스펙터에서 이것만 올린다.
    [Tooltip("★<b>표시용</b> — 각도기를 어깨선에서 이만큼 올려 그린다(m).\n" +
             "0이면 어깨축에 그대로 앉는다(횡단면에서 어깨선과 나란한 군더더기 선이 안 생긴다).\n" +
             "측정용 회전 중심(gaugePivotRise)과는 <b>별개</b>다 — 여기를 만져도 각도값은 안 변한다.")]
    [SerializeField] private float gaugeDrawRise = 0f;

    [Tooltip("★각도기를 <b>파지 지점</b>에 그린다(2026-09-03 지시). 끄면 종전대로 어깨 중점에 그린다.\n" +
             "0점을 잡은 자리에 고정되고, 0점 전에는 손을 따라온다.\n" +
             "★자리만 바뀐다 — 각도값에는 영향이 없다.\n" +
             "★신규 필드라 코드 기본값이 먹는다(규칙 7).")]
    [SerializeField] private bool gaugeAtGrip = true;

    /// <summary>0점을 잡은 순간의 파지 중점(월드). 각도기를 여기에 고정한다.</summary>
    private Vector3 gripAnchor;
    private bool gripAnchorValid;

    // ★각도기가 Transform의 position·rotation을 매 프레임 읽는다. 실측은 붙일 본이 없으므로
    //   대리 오브젝트를 만들어 우리가 얹는다. 씬에 저장되면 안 되므로 DontSave다.
    private Transform proxyPivot, proxyTorso;

    // ★★2026-09-03: <b>모드마다 각도기 하나</b>다.
    //   실습 → 실습 각도기(판·채움 포함) / 실측 → <b>실측 전용 180도 반원</b>.
    //   09-01에 실측이 실습 각도기를 빌려 쓰도록 바꿨는데, 그러면 실측 각도기가 영영 안 뜬다
    //   (아래 UpdateGauge의 조건이 !UsePracticeGauge다).
    //   사용자: "실측용 각도기도 보여주라니까 그것도 안 나왔네."
    // ★usePracticeGauge는 씬에 1이 굳어 있어 코드로 못 끈다(규칙 7) → 신규 필드로 뒤집는다.
    [Tooltip("★실측 전용 180도 반원을 그린다(2026-09-03 지시로 되살림).\n" +
             "★실습 각도기와 <b>같이</b> 뜬다 — 둘 다 보여 달라는 지시다. 겹치지 않게\n" +
             "  실습 각도기는 practiceGaugeRise만큼 위로 올려 둔다.")]
    [SerializeField] private bool useRealityGauge = true;

    // ★실습 각도기 쪽 출처는 계속 우리가 물린다. 그래야 두 각도기가 <b>같은 값</b>을 가리킨다.
    //   (끄면 실습 각도기가 교육 드라이버를 읽어 실측과 다른 각을 그린다.)
    public bool UsePracticeGauge => usePracticeGauge;

    [Tooltip("실습 각도기를 파지 지점에서 이만큼 <b>위로</b> 올린다(m).\n" +
             "★둘을 같이 띄우면 같은 자리에 겹친다 — 실측 각도기는 파지 지점,\n" +
             "  실습 각도기는 그 위에 둔다. 축은 둘이 같다.")]
    [SerializeField] private float practiceGaugeRise = 0.42f;

    [Tooltip("★실습 각도기를 <b>0점을 잡은 자리에 못 박는다</b>(2026-09-03 지시).\n" +
             "끄면 실측 각도기처럼 0점 전에는 손을 따라온다.\n\n" +
             "사용자: '얘는 손에 있는 정보 따라서 움직일 필요 없는데.'\n" +
             "실측 각도기는 지금 재는 값을 보여 주므로 파지에 붙어 있어야 하지만,\n" +
             "실습 각도기는 눈금판이라 <b>가만히 있어야 읽힌다</b>.")]
    [SerializeField] private bool practiceGaugeFixed = true;

    /// <summary>0점을 잡은 순간에만 갱신되는 앵커. 실습 각도기를 여기에 못 박는다.</summary>
    private Vector3 fixedGaugeAnchor;
    private bool fixedGaugeAnchorValid;

    [Tooltip("★<b>실측 각도기 반지름 덮어쓰기</b>(m). 0이면 씬의 gaugeRadius를 쓴다.\n" +
             "gaugeRadius는 씬에 0.3이 굳어 있어 코드로 못 바꾼다(규칙 7) — 그래서 신규 필드를 둔다.\n" +
             "2026-09-03 사용자: '실측용 각도기 반지름 줄여'.")]
    [SerializeField] private float gaugeRadiusOverride = 0.20f;

    /// <summary>실제로 쓸 실측 각도기 반지름.</summary>
    private float GaugeRadiusNow => gaugeRadiusOverride > 0f ? gaugeRadiusOverride : gaugeRadius;

    [Tooltip("★<b>B(실측 전용 각도기)</b>를 파지 위치에서 이만큼 <b>내린다</b>(m). 목 부근이 목표다.\n" +
             "2026-09-03 사용자: '양손 파지 위치에서 조금 아래 목 부근으로 내려서\n" +
             "시각적으로는 목축이 기우는 것처럼 보여 주고 싶다.'\n" +
             "★내려도 <b>각도값은 안 변한다</b> — 그리는 자리만 바뀐다.")]
    [SerializeField] private float realityGaugeDrop = 0.18f;

    /// <summary>
    /// B(실측 전용 각도기)를 그릴 중심. 파지 지점에서 목 쪽으로 내린 자리다.
    /// ★안내문·축선과 <b>다른 자리</b>다 — 그쪽은 파지 지점을 그대로 쓴다.
    /// </summary>
    private Vector3 GaugeCenter
        => (gripAnchorValid ? gripAnchor : pivot) - Vector3.up * realityGaugeDrop;

    /// <summary>결과 단계에서 각도기를 접는다. 접는 쪽이 편다.</summary>
    public void SetGaugeHidden(bool on) => gaugeForceHidden = on;
    private bool gaugeForceHidden;

    private CervicalRomDriver RefDriver
    {
        get
        {
            if (referenceDriver == null) referenceDriver = FindFirstObjectByType<CervicalRomDriver>();
            return referenceDriver;
        }
    }

    CervicalRomDriver.Direction ICervicalRomGaugeSource.CurrentDirection => direction;

    Transform ICervicalRomGaugeSource.Pivot => proxyPivot;
    Transform ICervicalRomGaugeSource.Torso => proxyTorso;

    Vector3 ICervicalRomGaugeSource.CurrentWorldAxis => AxisFor(direction);
    Vector3 ICervicalRomGaugeSource.WorldAxisFor(CervicalRomDriver.Direction d) => AxisFor(d);

    float ICervicalRomGaugeSource.MaxAngle
        => RefDriver != null ? RefDriver.MaxAngleFor(direction) : 0f;
    float ICervicalRomGaugeSource.MaxAngleFor(CervicalRomDriver.Direction d)
        => RefDriver != null ? RefDriver.MaxAngleFor(d) : 0f;

    // ★★<b>부호째 넘긴다</b>(2026-09-02). 종전에는 크기(|각|)만 넘겼다.
    //   각도기 바늘이 어느 쪽으로 기우는지는 축 부호(AxisFor)가 정하므로, 크기만 줘도
    //   굴곡이면 앞·신전이면 뒤로 <b>기울기는 한다</b>. 그런데 크기는 <b>어느 쪽으로 움직이든 커져서</b>,
    //   굴곡을 재는 중에 뒤로 젖혀도 바늘이 굴곡 쪽으로 나갔다.
    //   2026-09-02 사용자: "원래 진행해야할 방향의 반대쪽을 넘겨도 각도기는 앞으로 가던데 왜그런거야?"
    //
    // ★부호를 그대로 넘겨도 되는 근거: rom-frame-verify가 여섯 방향 모두 <b>+30°가 가야 할 자리</b>로
    //   간다고 확인했다(2026-09-02 --current). 바늘은 SignedAngle과 같은 축·같은 규약으로 돌므로
    //   정방향이 곧 양수다. ★이건 <b>계산</b>이다 — Play에서 눈으로 확정할 것.
    //   틀렸으면 바늘이 정방향에서 거꾸로 간다. 그때는 requireForwardDirection을 끄고 알린다.
    float ICervicalRomGaugeSource.CurrentAngle
        => PreviewActive ? previewAngle
         : TryGetAngle(out _, out _, out float sgn) ? sgn : 0f;

    // ★실측에는 '목표'가 없다. 기록된 값을 그대로 준다 — 아직이면 0이라 그 구간이 안 그려진다.
    //   즉 능동을 확정하는 순간 그 자리에 마킹이 생기고, 수동을 확정하면 그다음 구간이 생긴다.
    float ICervicalRomGaugeSource.ActiveTargetAngle
        => results[(int)direction].hasActive ? Mathf.Abs(results[(int)direction].active) : 0f;

    float ICervicalRomGaugeSource.PassiveLimitAngle
    {
        get
        {
            Result r = results[(int)direction];
            if (r.hasPassive) return Mathf.Abs(r.passive);
            return r.hasActive ? Mathf.Abs(r.active) : 0f;
        }
    }

    // 실측은 기능장애를 추첨하지 않는다. 프리뷰 전용 값이라 0이면 된다.
    float ICervicalRomGaugeSource.NominalDysfunction => 0f;
    float ICervicalRomGaugeSource.NominalPassiveGain => 0f;

    CervicalRomDriver.Measurement ICervicalRomGaugeSource.GetMeasurement(CervicalRomDriver.Direction d)
    {
        int i = (int)d;
        if (i <= 0 || i >= results.Length) return default;

        Result r = results[i];
        if (!r.hasActive && !r.hasPassive) return default;

        return new CervicalRomDriver.Measurement
        {
            recorded = true,
            maxAngle = RefDriver != null ? RefDriver.MaxAngleFor(d) : 0f,
            active = Mathf.Abs(r.active),
            passive = Mathf.Abs(r.passive),
        };
    }

    /// <summary>
    /// 각도기가 읽을 대리 트랜스폼을 매 프레임 얹는다.
    /// ★위치는 <b>어깨 중점 위</b>(목이 도는 자리)다. 어깨를 아직 안 잡았으면 손 기준으로 버틴다.
    /// ★자세는 어깨에서 세운 몸통 기준틀을 쓴다 — 각도기가 0°를 여기서 잡는다.
    /// </summary>
    private void UpdateGaugeProxy()
    {
        if (proxyPivot == null || proxyTorso == null) return;

        Vector3 up = refReady ? refUp : Vector3.up;
        Vector3 fwd = refReady ? refFwd : Vector3.forward;

        // ★각도기가 <b>회전일 때만</b> 0°로 이 forward를 쓴다. 여기만 뒤집으면
        //   횡단면 0°만 움직이고 관상면 축(axFwd)은 안 딸려 온다.
        if (transverseZeroFlip) fwd = -fwd;

        // ★그리는 자리는 <b>표시용</b> 손잡이를 쓴다. 측정용 회전 중심(gaugePivotRise)이 아니다.
        //
        // ★★<b>파지 지점에 그린다</b>(2026-09-03 사용자 지시 — "파지 위치에 시선 가기 편한 각도기").
        //   종전에는 어깨 중점(shoulderMid)에 그렸다. 그런데 시술자가 보고 있는 곳은 <b>환자 머리와 자기 손</b>이다.
        //   각도기가 어깨 높이에 있으면 눈금을 읽으려고 시선을 아래로 내려야 한다.
        //   ★자리만 옮기는 것이고 <b>각도값은 안 변한다</b> — 바늘 각은 CurrentAngle이 따로 준다.
        //   ★0점을 잡을 때의 파지 위치에 <b>고정</b>한다. 손을 따라 흔들리면 눈금을 못 읽는다
        //     (0점 전에는 손을 따라간다 — 그래야 파지 단계에서 어디에 뜰지 보인다).
        // ★실습 각도기는 <b>0점을 잡은 자리에 못 박는다</b>(2026-09-03). 눈금판이라 가만히 있어야 읽힌다.
        //   실측 각도기(pivot)는 0점 전에 손을 따라오지만, 이쪽은 안 따라온다.
        Vector3 anchor;
        if (practiceGaugeFixed && fixedGaugeAnchorValid) anchor = fixedGaugeAnchor;
        else if (gaugeAtGrip && gripAnchorValid) anchor = gripAnchor;
        else anchor = refReady ? shoulderMid : pivot;

        // ★실측 각도기가 파지 지점에 있으므로, 실습 각도기는 그 위로 올려 겹치지 않게 한다.
        //   ★축(proxyTorso)은 둘이 같다 — refFwd·refUp을 그대로 쓴다.
        float rise = gaugeDrawRise + (useRealityGauge ? practiceGaugeRise : 0f);
        proxyPivot.position = anchor + up * rise;
        proxyTorso.SetPositionAndRotation(proxyPivot.position, Quaternion.LookRotation(fwd, up));
    }

    /// <summary>
    /// 양손을 환자 양어깨에 올린 상태를 잡아 중심선을 세운다. 세션에 한 번이다.
    /// ★측정에는 안 쓴다 — 축선과 중심선을 그릴 <b>자리</b>를 정할 뿐이다.
    /// </summary>
    [ContextMenu("1 - 어깨 기준 잡기")]
    public void CaptureShoulders()
    {
        if (!TryGetHands(out Vector3 l, out Vector3 r)) { Warn("손을 못 찾았습니다."); return; }
        CaptureShouldersAt(l, r);
    }

    /// <summary>
    /// 어깨 기준을 잡는 실제 계산. 손 위치를 밖에서 넣을 수 있게 갈라 뒀다 —
    /// ★미리보기가 <b>이 함수를 그대로</b> 타야 실제와 같은 결과가 나온다.
    ///   2026-09-01에 미리보기가 카메라에서 기준틀을 따로 만들고 있었고, 그것도
    ///   '마주 본다'로 박혀 있어서 실제(기본 '뒤에 섬')와 결과가 달랐다.
    ///   사용자: "이거 미리보기랑 실제로 돌릴 때랑 다른데."
    /// </summary>
    private void CaptureShouldersAt(Vector3 l, Vector3 r)
    {
        float span = Vector3.Distance(l, r);
        Vector2 shoulderRange = ShoulderSpanRangeNow;
        if (span < shoulderRange.x || span > shoulderRange.y)
        {
            // ★거절 문구에 기준을 같이 찍는다 — 이게 대역을 좁힐 때 쓸 자다.
            Warn($"어깨 폭으로 안 보입니다({span * 100f:F0}cm — 기준 " +
                 $"{shoulderRange.x * 100f:F0}~{shoulderRange.y * 100f:F0}cm). 양손을 좌우 어깨에 올리세요.");
            holdTimer = 0f;
            return;
        }

        // ★어깨는 좌우가 대체로 같은 높이다. 머리를 잡은 것과 구분하는 데 이게 제일 잘 듣는다.
        float level = Mathf.Abs(l.y - r.y);
        if (level > shoulderLevelTolerance)
        {
            Warn($"양손 높이가 {level * 100f:F0}cm 차이납니다. 좌우 어깨에 나란히 올리세요.");
            holdTimer = 0f;
            return;
        }

        shoulderL = l; shoulderR = r;
        shoulderMid = (l + r) * 0.5f;

        // 어깨선을 좌우축으로 삼고, 월드 수직을 세워 직교틀을 만든다.
        // ★부호는 안 본다 — 그리기만 하므로 좌우가 뒤바뀌어도 중심선은 같은 자리다.
        // ★★환자 기준 오른쪽은 <b>시술자 왼손 → 오른손</b>이 아니라 그 반대다(2026-09-01 실측).
        //   시술자가 환자를 마주 보고 어깨를 짚으므로, 시술자의 오른손이 환자의 <b>왼</b>어깨에 얹힌다.
        //   r − l로 두면 기준틀 전체가 좌우 대칭으로 뒤집혀 굴곡·신전이 통째로 바뀐다 —
        //   09-01 사용자: "굴곡 방향으로 기울이면 각도기는 신전 방향으로 간다",
        //   "앞뒤가 완전 바뀌었어. 중간에 안 뒤집어지는 건 좋은데 처음 앞뒤는 맞춰야 할 거 아니야."
        //
        //   ★이건 <b>한 번</b> 정해지고 끝이다. 단계가 바뀌어도 다시 안 정한다.
        //   ★시술자가 환자 <b>뒤</b>에 서서 짚으면 다시 반대가 된다. 서는 자리를 바꾸려면
        //     여기를 같이 봐야 한다 — 손 두 개와 헤드셋만으로는 앞뒤를 알아낼 수가 없어서
        //     '마주 본다'를 규약으로 박아 둔다.
        // ★서는 자리가 좌우축의 부호를 정한다. 뒤에 서면 시술자 오른손이 환자 오른어깨에 얹혀
        //   r − l이 곧 환자 오른쪽이고, 마주 보면 반대가 된다(rom-frame-verify 전수 검증).
        refRight = operatorBehindPatient ? (r - l).normalized : (l - r).normalized;

        // ★전후축은 좌우축에서 외적으로 나오지만, 그 결과가 환자 <b>뒤</b>를 가리킨다.
        //   2026-09-01 사용자: "횡단면의 좌우 반대가 아니라 앞뒤가 반대가 돼 버렸네.
        //   사용자 뒤쪽으로 각도가 나와."
        //   ★이건 축 부호(flipTransverse)와 다른 문제다. 횡단면의 0°는 회전축이 아니라
        //     몸통의 <b>앞</b> 방향(Torso.forward)이 정한다 — 각도기가 IsRotation일 때만
        //     zeroDir로 Torso.forward를 쓰기 때문이다.
        //   ★전후축을 뒤집으면 관상면 축(axFwd)도 같이 뒤집혀 좌·우측굴이 서로 바뀐다.
        //     그때는 flipCoronal로 되돌린다 — 그러라고 면별로 나눠 뒀다.
        //   ★★전후축은 <b>절대 뒤집지 않는다</b>(2026-09-01, rom-frame-verify 전수 검증).
        //     16조합을 다 돌려 보니 전부 맞는 건 둘뿐이고 둘 다 refFwd = Cross(refRight, up)였다.
        //     여기를 뒤집으면 관상면 축(axFwd)이 딸려 <b>측굴·회전 네 방향이 같이 깨진다</b> —
        //     사용자: "내가 필요한 건 횡단면의 앞뒤 전환인데 왜 다른 단면까지 바뀌는 거냐."
        //     횡단면 0°만 바꾸려면 아래 transverseZeroFlip을 쓴다(각도기의 Torso.forward만 뒤집는다).
        refFwd = Vector3.Cross(refRight, Vector3.up);
        if (refFwd.sqrMagnitude < 1e-6f) refFwd = Vector3.forward;   // 어깨선이 수직인 병적인 경우
        refFwd.Normalize();
        // ★★수직축은 <b>월드 수직</b>이다. 외적으로 뽑으면 안 된다(2026-09-01).
        //   Cross(refFwd, refRight)로 뽑고 있었는데, 그 바로 위에서 refFwd를 뒤집으면
        //   여기가 딸려 −up이 된다 — 09-01 사용자: "횡단면만 뒤집으라니까 왜 시상면
        //   위아래가 바뀐 거야."
        //   XR 월드는 중력 정렬이고 환자는 앉아 있다. 파지 경로도 처음부터
        //   axUp = Vector3.up으로 두고 있었다. 여기만 외적을 쓰고 있었던 게 잘못이다.
        refUp = Vector3.up;

        refReady = true;
        holdTimer = 0f;
        frameStamp++;

        Mark($"어깨 기준 고정 - 어깨폭 {span * 100f:F0}cm. 이제 머리를 파지하세요.");
    }

    /// <summary>어깨 기준을 놓는다. 술기를 벗어나거나 다시 잡을 때.</summary>
    public void ClearReference() => refReady = false;

    // ================= 미리보기 (2026-09-01) =================
    //
    // 2026-09-01 사용자: "미리보기 만들어서 테스트하게 해 줘. 매번 과정 진행할 때마다 보는 거 힘들어."
    //
    // ★부호를 하나 만질 때마다 어깨 짚기 → 파지 → 능동 → 압박을 6방향 도는 건 말이 안 된다.
    //   여기서 기준틀을 <b>가짜로</b> 세우고 방향만 넘겨 가며 각도기 모양을 바로 본다.
    //   측정값은 안 건드린다 — 표시만 보는 용도다.

    [Header("=== 미리보기 ===")]
    [Tooltip("★카메라 앞에 기준틀을 가짜로 세워 각도기를 바로 띄운다. 손도 환자도 필요 없다.\n" +
             "부호를 고친 뒤 6방향을 눈으로 훑을 때 쓴다. 실제 측정에는 영향이 없다.")]
    [SerializeField] private bool previewMode;

    [Tooltip("미리보기에서 각도기가 가리킬 각(도).")]
    [Range(0f, 90f)][SerializeField] private float previewAngle = 30f;

    [Tooltip("미리보기 기준점을 카메라 앞 이만큼에 둔다(m).")]
    [SerializeField] private float previewDistance = 1.0f;

    private bool previewArmed;

    /// <summary>
    /// 어깨를 짚은 것처럼 기준틀을 세운다. 카메라가 보는 쪽을 환자 앞으로 삼는다 —
    /// 즉 <b>시술자가 환자를 마주 본다</b>고 놓는다.
    /// </summary>
    [ContextMenu("미리보기 - 기준틀 세우기")]
    public void PreviewSetupFrame()
    {
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.up * 1.6f;
        Vector3 look = cam != null ? cam.transform.forward : Vector3.forward;

        Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
        flat.Normalize();

        Vector3 mid = eye + flat * previewDistance + Vector3.down * 0.35f;

        // ★손을 <b>가짜로 놓고</b> 실제 캡처 함수를 그대로 태운다.
        //   기준틀을 여기서 직접 만들면 실제와 계산이 갈려 미리보기가 거짓말을 한다.
        //   시술자가 뒤에 서면 환자는 시술자와 같은 쪽을 보고, 마주 보면 반대를 본다.
        Vector3 patientFwd = operatorBehindPatient ? flat : -flat;
        Vector3 patientRight = Vector3.Cross(Vector3.up, patientFwd).normalized;

        Vector3 shoulderRightPos = mid + patientRight * 0.2f;
        Vector3 shoulderLeftPos = mid - patientRight * 0.2f;

        // 시술자의 오른손이 어느 어깨에 얹히는가 — 이게 서는 자리의 정의다.
        Vector3 handRight = operatorBehindPatient ? shoulderRightPos : shoulderLeftPos;
        Vector3 handLeft = operatorBehindPatient ? shoulderLeftPos : shoulderRightPos;

        refReady = false;                       // 실제 경로가 다시 세우게 놓아 준다
        CaptureShouldersAt(handLeft, handRight);
        if (!refReady)
        {
            ChunaLogger.LogWarning("[실측 미리보기] 기준틀을 못 세웠습니다 — 어깨 폭/높이 허용범위를 확인하세요.");
            return;
        }

        // 파지 축도 같은 틀로 맞춘다(각도기가 이걸 읽는다).
        axRight = refRight; axUp = refUp; axFwd = refFwd;

        frameReady = true;
        neutralReady = true;
        previewArmed = true;
        previewMode = true;          // ★체크박스를 따로 켜게 하지 않는다. 눌렀는데 아무 일도 없으면 그게 버그다.
        if (direction == CervicalRomDriver.Direction.None)
            direction = CervicalRomDriver.Direction.Flexion;

        v0 = refFwd; len0 = 0.2f; vNow = v0; vNowValid = true;
        pivot = shoulderMid + Vector3.up * pivotRise;

        enabled = true;              // 브리지가 실측 밖에서 꺼 두므로 켜 준다
        EnsureGaugeProxy();
        UpdateGaugeProxy();
        AttachPreviewGauge();

        // ★숫자로도 남긴다. 화면 라벨과 콘솔이 같은 말을 해야 믿을 수 있다.
        Vector3 gaugeFwd = proxyTorso != null ? proxyTorso.forward : refFwd;
        ChunaLogger.Log(
            $"<color=cyan>[실측 미리보기] {Label(direction)} · 시술자 위치 = 환자 " +
            $"{(operatorBehindPatient ? "뒤" : "마주")}</color>\n" +
            $"  환자 오른쪽(refRight) = {refRight}\n" +
            $"  환자 앞  (refFwd)    = {refFwd}\n" +
            $"  수직     (refUp)     = {refUp}\n" +
            $"  각도기 0°(회전)      = {gaugeFwd}" +
            (transverseZeroFlip ? "   ← transverseZeroFlip 켜짐" : "") + "\n" +
            $"  기준점(어깨 중점)     = {shoulderMid}");
    }

    /// <summary>
    /// ★미리보기의 핵심. 각도기는 <b>씬에 배선된 드라이버</b>를 읽고 있으므로,
    /// 여기서 출처를 우리로 바꿔 주지 않으면 메뉴를 눌러도 아무것도 안 나온다.
    /// (2026-09-01 사용자: "눌러도 안 나오잖아" — 이걸 빼먹었다.)
    /// </summary>
    private void AttachPreviewGauge()
    {
        var gauge = FindFirstObjectByType<CervicalRomPlaneGauge>(FindObjectsInactive.Include);
        if (gauge == null)
        {
            ChunaLogger.LogWarning("[실측 미리보기] CervicalRomPlaneGauge를 씬에서 못 찾았습니다.");
            return;
        }
        gauge.SetSource(this);
        gauge.SetRealityLook(true);
    }

    [ContextMenu("미리보기 - 다음 방향")]
    public void PreviewNextDirection()
    {
        if (!previewArmed) { PreviewSetupFrame(); return; }

        int n = (int)direction;
        direction = (CervicalRomDriver.Direction)(n >= 6 ? 1 : n + 1);

        UpdateGaugeProxy();          // 횡단면 0° 뒤집기 같은 값이 바로 반영되게
        AttachPreviewGauge();
        ChunaLogger.Log($"<color=cyan>[실측 미리보기] {Label(direction)}</color>");
    }

    [ContextMenu("미리보기 - 끄기")]
    public void PreviewClear()
    {
        previewArmed = false;
        previewMode = false;

        // ★우리가 물린 것만 우리가 되돌린다.
        var gauge = FindFirstObjectByType<CervicalRomPlaneGauge>(FindObjectsInactive.Include);
        if (gauge != null && gauge.HasExternalSource)
        {
            gauge.SetRealityLook(false);
            gauge.SetSource(null);
        }
        ResetAll();
        ChunaLogger.Log("<color=cyan>[실측 미리보기] 껐다 — 각도기를 교육 출처로 되돌렸다.</color>");
    }

    /// <summary>미리보기 중이면 각도기에 이 각을 물린다.</summary>
    private bool PreviewActive => previewMode && previewArmed;

    [ContextMenu("2 - 중립(0점) 캡처")]
    public void CaptureNeutral()
    {
        // ★어깨 기준이 먼저다. 순서를 코드로 강제해 두지 않으면 '준비'에서 머리를 잡는 순간
        //   0점이 먼저 잡혀 어깨 단계가 통째로 건너뛰어진다.
        if (requireReference && !refReady)
        {
            Warn("어깨 기준을 먼저 잡으세요 - 양손을 환자 양어깨에.");
            holdTimer = 0f;
            return;
        }

        if (!TryGetHands(out Vector3 l, out Vector3 r)) { Warn("손을 못 찾았습니다."); return; }

        // ★파지 게이트 — 허공에서 손이 멈춘 것을 0점으로 잡지 않는다.
        if (requireGripGate && !IsGripPlausible(l, r, 0f, out string why))
        {
            Warn($"아직 파지로 안 보입니다 — {why}");
            holdTimer = 0f;      // 다시 1.5초를 채워야 한다. 같은 자리에서 계속 확정 시도하지 않게.
            return;
        }

        v0 = r - l;
        len0 = v0.magnitude;
        if (len0 < 0.03f) { Warn($"양손이 너무 붙어 있습니다({len0 * 100f:F0}cm)."); return; }

        // ★기준축을 여기서 세운다. 어깨를 짚는 별도 단계는 없앴다(2026-08-31).
        if (!CaptureFrameFromGrip(l, r)) return;

        // ★한 손 이어가기의 재료 — 회전 중심과, 중립에서 중심 → 각 손.
        //   중심은 어깨에서 잡은 목 자리다. 어깨를 안 잡았으면 손 중점으로 대신한다
        //   (그 경우 지렛대가 짧아 한 손 각은 신뢰도가 떨어진다).
        anglePivot = refReady ? shoulderMid + refUp * gaugePivotRise : (l + r) * 0.5f;
        radL0 = l - anglePivot;
        radR0 = r - anglePivot;
        singleHandSeconds = 0f;

        // ★각도기를 그릴 자리를 여기서 못 박는다. 이 뒤로는 손이 움직여도 안 따라간다 —
        //   눈금이 흔들리면 읽을 수가 없다(2026-09-03).
        gripAnchor = (l + r) * 0.5f;
        gripAnchorValid = true;

        // ★실습 각도기용 고정 앵커는 <b>여기서만</b> 갱신한다. 0점 전 손 추종에는 안 딸려 간다.
        fixedGaugeAnchor = gripAnchor;
        fixedGaugeAnchorValid = true;

        // ★표시 기준점도 파지 지점으로 데려온다. CaptureShoulders가 여기를 shoulderMid로
        //   옮겨 놓기 때문에, 그대로 두면 각도기·축선·안내문이 전부 어깨 높이에 그려진다.
        //   ★pivot은 <b>표시 전용</b>이다(각은 anglePivot이 따로 쓴다) — 옮겨도 값은 안 변한다.
        pivot = gripAnchor + Vector3.up * pivotRise;

        vNow = v0; vNowValid = true;
        neutralReady = true;
        stage = Stage.Active;
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;
        frameStamp++;

        TryGetAngle(out _, out float perp, out _);
        string verdict = perp < minPerpRatio
            ? $"★이 파지로는 {Label(direction)}을(를) 못 잽니다(면 성분 {perp:F2}). 파지를 바꾸세요."
            : $"측정 가능(면 성분 {perp:F2}).";
        Mark($"0점 고정 - 파지폭 {len0 * 100f:F0}cm. {verdict}");
    }

    [ContextMenu("3 - 능동 끝점")]
    public void MarkActiveEnd()
    {
        if (!neutralReady) { Warn("중립 캡처가 먼저입니다."); return; }
        // ★★<b>피크가 아니라 지금 각</b>을 적는다(2026-09-02, 되돌림).
        //   한때 피크로 적었다. 09-02 10:48판에 수동(71.6)이 능동(72.3)보다 작게 남은 걸 보고
        //   "게이트는 피크, 기록은 현재각"의 어긋남을 없애려 한 것이었다.
        //   그런데 17:22판에서 <b>좌회전 수동 98.5도</b>(참고치 90)·신전 90.9도가 나왔다 —
        //   한 번 잘못 돌린 각이 그대로 끝점으로 박힌 것이다.
        //   사용자: "피크치로 하니까 한번 실수로 돌린 말도 안되는 각이 끝에 찍혀버리잖아."
        //   ★끝점은 <b>시술자가 멈춰서 끝이라고 정한 자리</b>다. 지나간 최대치가 아니다.
        //     피크는 게이트(여기까지는 갔다)로만 쓰고, 기록은 확정 순간의 각으로 되돌린다.
        if (!TryGetAngle(out float deg, out _, out float signed)) { Warn("각을 못 읽습니다."); return; }

        int i = (int)direction;
        results[i].active = signed; results[i].hasActive = true;   // 부호째 담는다 — 지침이 어느 쪽인지
        stage = Stage.Passive;
        holdTimer = 0f; peakAngle = 0f;

        // ★압박의 출발선을 여기서 못 박는다. 이게 없으면 손을 그대로 둔 채 1.5초만 지나도
        //   압박이 확정돼 '수동 = 능동'이 된다.
        passiveBaseAngle = deg;

        // ★평가에서는 <b>다음에 뭘 하라는 말을 안 한다</b>(2026-09-02 사용자 지시).
        //   "이제 끝 느낌까지 압박하세요 — N도를 넘겨야 잡힙니다"는 절차와 통과 기준을
        //   통째로 알려주는 문구였다. 잰 값만 남긴다 — 그건 결과지 절차가 아니다.
        if (evaluationGuidance) Mark($"{Label(direction)} 능동 {deg:F1}도");
        else Mark($"{Label(direction)} 능동 {deg:F1}도 (부호 {signed:+0.0;-0.0}). " +
                  $"이제 끝 느낌까지 압박하세요 — {deg + minPassiveGain:F0}도를 넘겨야 잡힙니다.");
    }

    /// <summary>
    /// 압박을 <b>안 하고</b> 중립으로 돌아왔다 — 생략으로 기록하고 이 방향을 끝낸다.
    ///
    /// ★2026-09-02 사용자: "능동 압박 안내 문구 없이 자율로 수행해야 하는데, 만약 안 하고
    ///   중립으로 돌아오는 경우에는 그냥 완료시키고 단계 생략한 걸로 감점 하자."
    /// ★이게 <b>09-01 신전이 막혔던 자리</b>다. 종전에는 압박이 확정돼야만 다음으로 갔기 때문에,
    ///   능동이 86.8도까지 읽혀 89.8도를 넘길 수 없게 되자 그 방향에서 아무 데도 못 갔다.
    ///   수동 0.0으로 남은 그 행이 이 함수가 필요하다는 증거다.
    /// </summary>
    public void MarkPassiveSkipped()
    {
        int i = (int)direction;
        if (i <= 0 || i >= results.Length) return;
        if (results[i].hasPassive || results[i].passiveSkipped) return;   // 이미 정해졌다

        results[i].passiveSkipped = true;
        stage = Stage.Done;
        holdTimer = 0f;

        Mark($"{Label(direction)} 압박 생략");
        FlushTrackingCounters("압박 생략");
    }

    [ContextMenu("4 - 압박 끝점")]
    public void MarkPassiveEnd()
    {
        if (!neutralReady) { Warn("중립 캡처가 먼저입니다."); return; }
        // ★능동과 같은 이유로 <b>지금 각</b>을 적는다 — MarkActiveEnd 주석 참조.
        if (!TryGetAngle(out float deg, out _, out float signed)) { Warn("각을 못 읽습니다."); return; }

        int i = (int)direction;
        results[i].passive = signed; results[i].hasPassive = true;
        stage = Stage.Done;
        holdTimer = 0f;

        float gain = results[i].hasActive ? deg - Mathf.Abs(results[i].active) : float.NaN;
        Mark($"{Label(direction)} 수동 {deg:F1}도 (부호 {signed:+0.0;-0.0}) · 능동 대비 {gain:F1}도. " +
             "다음 방향으로 넘기거나 재파지 후 0점을 다시 잡으세요.");

        FlushTrackingCounters("압박 확정");
    }

    /// <summary>
    /// 지금 방향에서 시간이 어디로 나갔는지 <b>그 방향 칸에</b> 옮겨 담고 누산기를 비운다.
    /// ★09-01 실기 테스트에서 신전이 134초였는데, 결과 CSV에는 StepTime 하나뿐이라
    ///   파지 거절인지·홀드 리셋인지·게인 미달인지 <b>가를 방법이 없었다.</b>
    ///
    /// ★★<b>압박 확정에서만 부르면 안 된다</b>(09-02 수정). 종전에는 <c>MarkPassiveEnd</c>
    ///   한 곳에서만 불렀다. 그래서 압박을 확정 못 한 채 다음 방향으로 넘어가면
    ///   ①그 방향 행은 계수기가 통째로 <b>0</b>으로 남고
    ///   ②비우지도 않아서 그 몫이 <b>다음 방향 행에 얹혔다.</b>
    ///   09-01 16:38판 신전이 정확히 이 모양이다 — 능동 86.8인데 네 계수기가 전부 0.
    ///
    /// ★<b>더한다</b>(대입이 아니다). 압박을 확정한 뒤 재파지해서 더 쓴 시간도 남아야 한다.
    ///   비우고 더하므로 두 번 불러도 값이 늘지 않는다 — 방향 전환·결과 수집에서 안심하고 부른다.
    /// </summary>
    private void FlushTrackingCounters(string why)
    {
        bool any = holdResets > 0 || rejectedFrames > 0 || trackingRelocks > 0 || lostTotal > 0f;

        // ★먼저 결과에 찍는다. 로그는 밀려도 이건 CSV로 나간다.
        int i = (int)direction;
        if (i > 0 && i < results.Length)
        {
            results[i].holdResets += holdResets;
            results[i].rejectedFrames += rejectedFrames;
            results[i].relocks += trackingRelocks;
            results[i].lostSeconds += lostTotal;
        }

        if (showDebugLogs && any)
        {
            ChunaLogger.Log($"<color=cyan>[실측/{Label(direction)}] {why} · " +
                            $"홀드 리셋 {holdResets}회 · 튐 버림 {rejectedFrames}프레임 · " +
                            $"재잠금 {trackingRelocks}회 · 손 유실 {lostTotal:F1}초</color>");
        }

        holdResets = 0; rejectedFrames = 0; trackingRelocks = 0; lostTotal = 0f;
    }

    /// <summary>
    /// 아직 어느 방향 칸에도 안 들어간 계수기를 밀어 넣는다.
    /// ★결과를 걷기 <b>직전</b>에 부른다 — 마지막 방향은 방향 전환이 안 일어나 흘릴 자리가 없다.
    /// </summary>
    public void FlushPendingDiagnostics() => FlushTrackingCounters("결과 수집");

    // ================= 평가 (2026-09-02) =================

    /// <summary>이 방향에서 압박을 생략했는가.</summary>
    public bool WasPassiveSkipped(CervicalRomDriver.Direction d)
    {
        int i = (int)d;
        return i > 0 && i < results.Length && results[i].passiveSkipped;
    }

    /// <summary>이 방향에서 파지를 놓친 횟수.</summary>
    public int GripReleasesFor(CervicalRomDriver.Direction d)
    {
        int i = (int)d;
        return i > 0 && i < results.Length ? results[i].gripReleases : 0;
    }

    /// <summary>압박을 생략한 방향 수.</summary>
    public int PassiveSkipCount
    {
        get
        {
            int n = 0;
            for (int i = 1; i < results.Length; i++) if (results[i].passiveSkipped) n++;
            return n;
        }
    }

    /// <summary>파지를 놓친 총 횟수.</summary>
    public int GripReleaseCount
    {
        get
        {
            int n = 0;
            for (int i = 1; i < results.Length; i++) n += results[i].gripReleases;
            return n;
        }
    }

    /// <summary>
    /// ROM 평가 점수. 100에서 <b>절차를 안 밟은 만큼만</b> 깎는다.
    ///
    /// ★<b>각도 값 자체는 점수에 안 넣는다.</b> 가동범위는 환자의 상태지 시술자의 실력이 아니다 —
    ///   환자가 뻣뻣하다고 시술자가 감점되면 그 점수는 아무것도 뜻하지 않는다.
    /// ★<b>폭을 작게 잡고 하한을 뒀다</b>(2026-09-02 사용자: "점수가 너무 낮게 나오면
    ///   학생들이 반발하기도 하고 은근 민감한 영역이라 최대한 거부감은 없게 하고 싶어").
    ///   기본값이면 6방향을 전부 생략해도 100 − 30 = 70점이고, 하한 60 아래로는 안 내려간다.
    /// </summary>
    public float RomScore
    {
        get
        {
            float s = 100f - PassiveSkipCount * passiveSkipPenalty - GripReleaseCount * gripReleasePenalty;
            return Mathf.Clamp(s, minRomScore, 100f);
        }
    }

    /// <summary>
    /// 그 방향의 진단 계수기. 각도가 아니라 <b>왜 오래 걸렸는지</b>를 담는다.
    /// 아직 안 끝난 방향이면 전부 0이다.
    /// </summary>
    public void GetDiagnostics(CervicalRomDriver.Direction d,
                               out int holdResetCount, out int rejectedFrameCount,
                               out int relockCount, out float lostTrackingSeconds)
    {
        holdResetCount = 0; rejectedFrameCount = 0; relockCount = 0; lostTrackingSeconds = 0f;

        int i = (int)d;
        if (i <= 0 || i >= results.Length) return;

        Result r = results[i];
        holdResetCount = r.holdResets;
        rejectedFrameCount = r.rejectedFrames;
        relockCount = r.relocks;
        lostTrackingSeconds = r.lostSeconds;
    }

    [ContextMenu("5 - 다음 방향")]
    public void NextDirection()
    {
        // ★떠나기 전에 계수기를 지금 방향 칸에 넣는다(SetDirection과 같은 이유).
        FlushTrackingCounters("방향 전환");

        int n = (int)direction;
        n = n >= 6 ? 1 : n + 1;
        direction = (CervicalRomDriver.Direction)n;

        // ★0점은 방향마다 다시 잡는다. 파지가 바뀌면 v0가 통째로 달라진다.
        neutralReady = false;
        stage = Stage.AwaitNeutral;
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;

        // ★방향이 바뀌면 튐 필터의 기준 위치도 놓아 준다.
        //   안 놓으면 새 파지 위치를 '튄 것'으로 보고 maxRejectSeconds만큼 거절한다.
        acceptedValidL = acceptedValidR = false; rejectSecondsL = rejectSecondsR = 0f; lostSeconds = 0f;

        frameStamp++;
        Mark($"-> {Label(direction)}. {GripHintFor(direction)} 파지 후 중립에서 정지하세요.");
    }

    [ContextMenu("9 - 결과 찍기")]
    public void DumpResults()
    {
        sb.Clear();
        sb.AppendLine("<color=cyan>[실측ROM] 측정 결과</color>");
        for (int i = 1; i <= 6; i++)
        {
            Result r = results[i];
            if (!r.hasActive && !r.hasPassive) continue;
            string a = r.hasActive ? $"{Mathf.Abs(r.active):F1}도" : "-";
            string p = r.hasPassive ? $"{Mathf.Abs(r.passive):F1}도" : "-";
            string d = (r.hasActive && r.hasPassive)
                ? $"{Mathf.Abs(r.passive) - Mathf.Abs(r.active):F1}도" : "-";
            sb.AppendLine($"  {Label((CervicalRomDriver.Direction)i)}  능동 {a} · 수동 {p} · 차이 {d}");
        }
        ChunaLogger.Log(sb.ToString());
    }

    // ================= 매 프레임 =================

    private void Update()
    {
        // ★실측모드가 아니면 아무것도 하지 않는다(2026-08-28 사용자 지적).
        //   브리지가 켜고 끄는 것에만 기대면, 브리지가 없거나 순서가 어긋난 순간
        //   교육모드 화면에 실측 안내와 각도기가 끼어든다. 스스로도 막는다.
        // ★미리보기는 실측모드가 아니어도 돈다. 부호를 고칠 때마다 술기를 타고 들어가는 건
        //   말이 안 된다(2026-09-01 사용자 요청). 표시만 하고 측정은 안 한다.
        if (PreviewActive)
        {
            EnsureGaugeProxy();
            UpdateGaugeProxy();
            UpdateVisuals();
            return;
        }

        if (!IsMeasurementMode())
        {
            if (root != null) TearDownVisuals();
            return;
        }

        float frameDt = Mathf.Max(1e-4f, Time.deltaTime);
        bool read = TryGetHands(out Vector3 l, out Vector3 r);

        // ★손마다 따로 거른다. 한쪽만 가려지는 게 실제 상황이라, 한 손이 튀었다고
        //   멀쩡한 다른 손까지 버리면 한 손 이어가기가 성립하지 않는다.
        leftOk = rightOk = false;
        if (read)
        {
            leftOk = AcceptHand(ref acceptedLeft, ref acceptedValidL, ref rejectSecondsL, l, frameDt,
                                out bool reL);
            rightOk = AcceptHand(ref acceptedRight, ref acceptedValidR, ref rejectSecondsR, r, frameDt,
                                 out bool reR);
            relockedThisFrame = reL || reR;
        }
        else relockedThisFrame = false;
        l = acceptedLeft; r = acceptedRight;

        bool has = leftOk && rightOk;             // 양손이 다 믿을 만한가
        bool anyHand = leftOk || rightOk;

        // 한 손으로 버틴 시간 — 오래 끌면 미끄러짐을 못 잡아 조용히 틀린 값이 쌓인다.
        if (has || !anyHand) singleHandSeconds = 0f;
        else singleHandSeconds += frameDt;

        if (has)
        {
            Vector3 raw = r - l;
            if (!vNowValid || smoothing <= 0f)
            {
                vNow = raw;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));
                vNow = Vector3.Lerp(vNow, raw, k);
            }
            vNowValid = true;
        }
        else
        {
            vNowValid = false;

            // ★손을 아직 못 읽는 동안에도 안내가 보여야 한다. 안 그러면 바닥(월드 원점)에 뜬다.
            if (!neutralReady)
            {
                Camera cam = Camera.main;
                pivot = cam != null
                    ? cam.transform.position + cam.transform.forward * 0.6f + Vector3.down * 0.15f
                    : Vector3.up * 1.2f;
            }
        }

        // ★확정 전이어도 실측치를 갱신해 둔다 — 리드아웃에 숫자를 띄워야 임계를 맞출 수 있다.
        if (has && !neutralReady)
        {
            IsGripPlausible(l, r, 0f, out _);

            // ★0점을 잡기 전에는 기준점을 <b>손을 따라</b> 옮긴다.
            //   안 그러면 pivot이 (0,0,0) = 월드 원점(바닥)이라 안내 문구와 수치가 발밑에 뜬다.
            //   어깨 단계를 없애면서 생긴 구멍이다 — 예전엔 어깨를 짚는 순간 잡혔다.
            //   0점을 잡은 뒤에는 고정한다. 각도기가 손을 따라 흔들리면 눈금을 못 읽는다.
            pivot = (l + r) * 0.5f + Vector3.up * pivotRise;

            // ★파지 단계에서도 각도기가 손을 따라와야 "여기에 뜬다"가 보인다. 0점을 잡으면 고정된다.
            gripAnchor = (l + r) * 0.5f;
            gripAnchorValid = true;
        }

        EnsureGaugeProxy();
        UpdateGaugeProxy();

        // ★한 손만 읽혀도 각이 나오면 진행을 이어 간다(회전 중심을 알기 때문에 가능하다).
        //   미끄러짐을 못 잡으므로 오래는 안 버틴다.
        bool singleOk = singleHandFallback && !has && anyHand && neutralReady
                        && singleHandSeconds <= singleHandMaxSeconds;
        bool usable = has || singleOk;

        UpdateGripRelease(usable, l, r);
        UpdateHold(usable, l, r);
        if (useKeyboard) ReadKeys();
        UpdateVisuals();
    }

    /// <summary>양손이 멈춰 있으면 단계를 넘긴다. VR에서 버튼 없이 진행하는 유일한 손잡이다.</summary>
    private void UpdateHold(bool has, Vector3 l, Vector3 r)
    {
        // ★얼려 두면 확정을 안 한다 — 결과 단계에서 마지막 방향이 다시 측정되는 것을 막는다.
        if (frozen) { holdTimer = 0f; return; }

        float dt = Mathf.Max(1e-4f, Time.deltaTime);

        // ★손을 못 읽는 동안(가림·튐 포함) 타이머를 <b>얼린다</b>. 종전에는 즉시 0이었다.
        if (!has)
        {
            prevValid = false;
            lostSeconds += dt;
            lostTotal += dt;
            if (lostSeconds > trackingGraceSeconds && holdTimer > 0f)
            {
                holdTimer = 0f;
                holdResets++;
            }
            return;
        }

        // ★★<b>한 손만 다시 잡힌 프레임도 같은 가드를 태운다</b>(2026-09-02).
        //   양손을 다 놓쳤다 돌아오는 경우(!prevValid)에는 아래 가드가 있었는데,
        //   <b>한 손만</b> 거절됐다 풀리는 경우에는 그 가드를 안 탔다. 그 프레임의 점프가
        //   그대로 속도 계산에 들어가 '움직이는 중'으로 읽혀 홀드를 깼다.
        //   09-02 17:22판 좌회전 홀드 리셋 105회에 이게 섞여 있다.
        if (relockedThisFrame)
        {
            prevLeft = l; prevRight = r; prevValid = true;
            lostSeconds = 0f;
            return;
        }

        // ★복귀 첫 프레임은 변위를 못 잰다(끊긴 만큼 순간이동한 것처럼 보인다).
        //   갱신만 하고 넘어간다 — 여기서 재면 그 값이 곧바로 리셋을 부른다.
        if (!prevValid)
        {
            prevLeft = l; prevRight = r; prevValid = true;
            lostSeconds = 0f;
            return;
        }
        lostSeconds = 0f;

        float raw = Mathf.Max((l - prevLeft).magnitude, (r - prevRight).magnitude) / dt;
        prevLeft = l; prevRight = r;

        // ★생 속도는 프레임마다 크게 튄다. 평활을 거쳐야 '멈췄다'가 안정적으로 잡힌다.
        if (speedSmoothing <= 0f) smoothedSpeed = raw;
        else
        {
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, speedSmoothing));
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, raw, k);
        }
        float speed = smoothedSpeed;

        // ★피크를 <b>부호째</b> 기억한다(2026-09-02). 게이트는 종전부터 이 피크를 봤는데
        //   기록은 확정 순간에 각을 다시 읽고 있었다 — 그 어긋남이 아래 MarkActiveEnd 주석의 결함이다.
        // ★이 줄은 튐 필터를 통과한 프레임에서만 돈다. TryGetAngle이 leftOk·rightOk를 요구하고,
        //   그 둘은 AcceptHand(속도 기반 튐 거르기)가 정한다. 튄 프레임의 각은 여기 오지 않는다.
        // ★★<b>반대로 간 각은 안 센다</b>(2026-09-02). 종전에는 크기로 봐서, 굴곡을 재는 중에
        //   뒤로 젖혀도 피크가 쌓이고 능동·압박이 잡혔다. 정방향이 양수라 부호를 그대로 쓰면 걸러진다
        //   (음수는 0에서 출발한 peakAngle을 영영 못 넘는다).
        if (neutralReady && TryGetAngle(out float deg, out _, out float sgn))
        {
            float advance = requireForwardDirection ? sgn : deg;
            if (advance > peakAngle) peakAngle = advance;
        }

        // ★히스테리시스 — 이미 쌓고 있는 중이면 나가는 임계를 높여 경계 깜빡임을 없앤다.
        float exitSpeed = holdSpeedThreshold * Mathf.Max(1f, holdSpeedExitFactor);
        bool still = holdTimer > 0f ? speed <= exitSpeed : speed <= holdSpeedThreshold;

        // ★각도 게이트 — 손이 느려도 각이 계속 가고 있으면 정지가 아니다.
        //   홀드를 시작한 시점의 각을 기억해 두고, 거기서 벗어나면 처음부터 다시 센다.
        if (still && HoldAngleToleranceNow > 0f && neutralReady
            && TryGetAngle(out float nowDeg, out _, out _))
        {
            if (holdTimer <= 0f)
            {
                holdAnchorAngle = nowDeg;       // 이번 홀드의 기준각
                holdAnchorValid = true;
                angleExcursionSeconds = 0f;
            }
            else if (holdAnchorValid && Mathf.Abs(nowDeg - holdAnchorAngle) > HoldAngleToleranceNow)
            {
                // ★한 프레임 튄 것과 진짜로 계속 가는 것을 가른다(2026-09-03).
                //   종전에는 벗어나는 <b>즉시</b> holdTimer를 0으로 죽였다. 엄지가 접점에서 한 번
                //   구르면 그것만으로 2~3도가 흔들리는데, 그때마다 유지 시간을 처음부터 다시 셌다.
                //   사용자: "한 번 튀면 몇 초를 기다려야 해서 과정이 안 끝나고 늘어진다."
                angleExcursionSeconds += dt;

                if (angleExcursionSeconds >= holdAngleGraceSeconds)
                {
                    // 유예를 넘겼다 = 정말로 아직 가고 있다. 지금 각을 새 기준으로 삼는다.
                    holdAnchorAngle = nowDeg;
                    angleExcursionSeconds = 0f;
                    still = false;   // ★0으로 죽이지 않는다 — 아래 holdDecayRate가 깎는다.
                }
            }
            else
            {
                angleExcursionSeconds = 0f;   // 여유 안으로 돌아왔다
            }
        }

        if (still)
        {
            holdTimer += dt;
        }
        else if (holdDecayRate > 0f)
        {
            // ★0으로 죽이지 않고 깎는다. 잠깐 흔들려도 쌓인 게 남는다.
            float before = holdTimer;
            holdTimer = Mathf.Max(0f, holdTimer - dt * holdDecayRate);
            if (before > 0f && holdTimer <= 0f) holdResets++;
        }
        else
        {
            if (holdTimer > 0f) holdResets++;
            holdTimer = 0f;
        }

        if (!advanceOnHold || holdTimer < HoldSeconds) return;

        switch (stage)
        {
            case Stage.AwaitNeutral:
                // ★어깨 기준이 아직이면 그것부터 잡는다. 같은 '정지'가 두 가지를 잡는 셈인데,
                //   순서가 하나뿐이라 헷갈릴 여지가 없다 — 어깨 → 파지.
                if (requireReference && !refReady) CaptureShoulders();
                else CaptureNeutral();
                break;
            case Stage.Active:
                if (peakAngle >= minAngleToMark) MarkActiveEnd();
                break;
            case Stage.Passive:
                // ★능동 각보다 minPassiveGain 이상 <b>더</b> 가야 확정한다.
                //   minAngleToMark(중립 대비)만 보면 능동 끝점에서 이미 만족해 즉시 확정된다.
                if (peakAngle >= passiveBaseAngle + minPassiveGain) MarkPassiveEnd();
                break;
        }
        holdTimer = 0f;
    }

    private void ReadKeys()
    {
        if (Input.GetKeyDown(KeyCode.F2)) CaptureNeutral();
        else if (Input.GetKeyDown(KeyCode.F3)) MarkActiveEnd();
        else if (Input.GetKeyDown(KeyCode.F4)) MarkPassiveEnd();
        else if (Input.GetKeyDown(KeyCode.F5)) NextDirection();
        else if (Input.GetKeyDown(KeyCode.F6)) DumpResults();
        else if (Input.GetKeyDown(KeyCode.F7)) ResetAll();
    }

    /// <summary>지금 실측모드인가. DifficultyManager가 없으면 아니라고 본다(안전한 쪽).</summary>
    private static bool IsMeasurementMode()
        => ChunaTraining.DifficultyManager.Instance != null
           && ChunaTraining.DifficultyManager.Instance.IsMeasurementMode;

    private bool TryGetHands(out Vector3 left, out Vector3 right)
    {
        left = Vector3.zero;
        right = Vector3.zero;

        if (leftHandOverride != null && rightHandOverride != null)
        {
            left = leftHandOverride.position;
            right = rightHandOverride.position;
            return true;
        }

        if (gripJudge == null) return false;
        // ★엄지·검지 파지 지점만 쓴다. 손목·손바닥은 안 본다(2026-08-28 사용자 지시).
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Left, out Vector3 l)) return false;
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Right, out Vector3 r)) return false;
        left = l; right = r;
        return true;
    }

    /// <summary>
    /// 추적이 튄 프레임을 걸러낸다. 사람 손이 낼 수 없는 속도로 순간이동했으면
    /// <b>그 프레임을 통째로 버린다</b> — 가려진 손이 엉뚱한 데로 튀는 걸 이렇게 막는다.
    /// ★버리기만 하면 손이 진짜로 옮겨갔을 때 영영 못 따라가므로,
    ///   <see cref="maxRejectSeconds"/>를 넘게 계속 거절되면 새 위치를 받아들인다.
    /// </summary>
    /// <returns>이 프레임을 써도 되면 true.</returns>
    private bool AcceptHand(ref Vector3 accepted, ref bool valid, ref float rejectSec,
                            Vector3 p, float dt, out bool relocked)
    {
        relocked = false;

        if (maxHandSpeed <= 0f) { accepted = p; valid = true; return true; }

        if (!valid)
        {
            accepted = p; valid = true; rejectSec = 0f;
            return true;
        }

        // ★★<b>허용 거리를 경과 시간에 비례시킨다</b>(2026-09-02).
        //   종전에는 <c>maxHandSpeed * dt</c>, 즉 <b>한 프레임치</b>만 허용했다. 그런데 한 번 거절되면
        //   기준점이 옛 위치에 얼어붙는데 손은 계속 움직이므로, 그 뒤 프레임도 전부 옛 위치에서
        //   멀어 <b>계속</b> 거절됐다. 결국 maxRejectSeconds(0.5초)를 다 채워야 풀렸다 —
        //   <b>한 번의 튐이 0.5초 정지를 불렀다.</b>
        //   09-02 17:22판 우회전이 그 증거다: 튐 버림 993프레임 · 재잠금 28회 → 993/28 ≈ 35프레임,
        //   72fps에서 딱 0.5초다. 993번 튄 게 아니라 <b>28번 튀고 그때마다 0.5초를 버린 것</b>이다.
        //   "사람 손이 낼 수 있는 거리"는 원래 경과 시간에 비례한다. 그대로 재면 된다 —
        //   진짜 순간이동은 여전히 못 따라잡아 종전처럼 재잠금으로 풀린다.
        float elapsed = dt + rejectSec;           // 마지막으로 받아들인 뒤 흐른 시간

        if ((p - accepted).magnitude > maxHandSpeed * elapsed)
        {
            rejectSec += dt;
            if (rejectSec < maxRejectSeconds)
            {
                rejectedFrames++;
                return false;                     // 버린다. 마지막으로 받아들인 위치를 그대로 쓴다.
            }
            trackingRelocks++;                    // 너무 오래 거절했다 — 진짜로 그리 간 것으로 본다.
            relocked = true;
        }
        else if (rejectSec > 0f)
        {
            // 거절하다가 정상 범위로 돌아왔다. 이것도 위치가 건너뛴 것이라 속도를 재면 안 된다.
            relocked = true;
        }

        rejectSec = 0f;
        accepted = p;
        return true;
    }

    // ================= 표시 =================

    private void UpdateVisuals()
    {
        if (!showReadout && !showAxes && !showGauge) { TearDownVisuals(); return; }
        if (root == null) BuildVisuals();

        // ★배율을 Play 중에 만져도 바로 보이게 다시 얹는다. 값이 바뀐 프레임에만 대입한다 —
        //   fontSize 대입은 TMP 재빌드를 부르므로 매 프레임 넣으면 안 된다.
        if (readout != null && !Mathf.Approximately(appliedReadoutScale, ReadoutScaleNow))
        {
            appliedReadoutScale = ReadoutScaleNow;
            readout.fontSize = readoutSize * 100f * Mathf.Max(0.1f, appliedReadoutScale);
        }

        bool slip = IsSlipping(out float slipRatio);
        bool measurable = TryGetAngle(out float deg, out float perp, out float shownSigned);
        int warn = slip ? 2 : (measurable && perp < minPerpRatio ? 1 : 0);

        if (showAxes && frameReady) UpdateAxisLines();
        // ★중심선은 0점(frameReady)과 무관하다 — 어깨를 짚은 순간부터 측정 내내 떠 있다.
        UpdateMidline();
        UpdateGauge();

        // ★진행 UI로 보내는 동안에도 <b>문자열은 만들어야 한다</b> — 그리는 쪽만 바뀐 것이다.
        if (!showReadout && !ReadoutClaimed) return;
        if (readout == null) return;

        // ★숫자도 부호째 띄운다(2026-09-02). 바늘이 −30을 가리키는데 숫자가 +30이면
        //   둘이 서로 다른 말을 한다 — 그건 없느니만 못하다.
        //   반대로 움직이는 동안 음수가 뜨는 게 곧 "지금 반대다"라는 신호가 된다.
        int shown = measurable
                    ? Mathf.RoundToInt(requireForwardDirection ? shownSigned : deg)
                    : int.MinValue + 1;
        int holdStep = HoldBarSteps();
        int mask = DoneMask();

        // ★파지 수치는 0점을 잡기 <b>전에만</b> 띄운다. 그때만 변화를 감지하면 된다.
        //   1mm 단위로 양자화해 매 프레임 문자열을 새로 만들지 않는다(VR 프레임 예산).
        int gripSig = neutralReady ? -1
            : Mathf.RoundToInt(lastGripSpan * 1000f) * 10000
            + Mathf.RoundToInt(lastPinchL * 1000f) * 100
            + Mathf.RoundToInt(lastPinchR * 1000f);

        int warnSig = HasFreshWarn ? (lastWarn != null ? lastWarn.GetHashCode() : 1) : 0;
        if (stage == shownStage && shown == shownAngle && warn == shownWarn
            && holdStep == shownHold && mask == shownMask && gripSig == shownGrip
            && warnSig == shownWarnSig) return;
        shownWarnSig = warnSig;
        shownStage = stage; shownAngle = shown; shownWarn = warn;
        shownHold = holdStep; shownMask = mask; shownGrip = gripSig;

        sb.Clear();

        // ★평가에서는 <b>절차 사슬(중립 ▶ 능동 ▶ 압박)을 숨긴다</b>(2026-09-03 사용자 지시).
        //   다음에 뭘 해야 하는지를 그대로 알려 주는 줄이라, 평가에서는 힌트다.
        //   ★홀드 게이지·방향 목록은 남긴다 — 그건 절차가 아니라 <b>지금 먹히고 있나</b>를 보는 것이다.
        //     정지로만 넘어가는 구조라 게이지가 없으면 왜 안 넘어가는지 알 수가 없다.
        if (showProgress && !HideHints)
        {
            sb.Append("<size=70%>");
            AppendStageChain();
            sb.Append("</size>\n");
        }

        switch (stage)
        {
            case Stage.AwaitNeutral:
                sb.Append(requireReference && !refReady
                    ? "양손을 환자 양어깨에 - 정지"
                    : "중립에서 머리를 파지 - 정지");
                // ★실측치를 같이 띄운다. 임계를 맞추려면 실제 숫자를 봐야 한다(2026-08-31).
                AppendGripNumbers();
                break;
            default:
                sb.Append(Label(direction));
                sb.Append("  ");
                if (measurable) { sb.Append(shown); sb.Append('도'); } else sb.Append("--");
                break;
        }

        // ★평가에서는 능동·수동 숫자를 <b>숨긴다</b>(2026-09-03 사용자 지시 — 힌트 최소화).
        //   ★삭제가 아니다. results에는 그대로 쌓이고 _rom.csv·결과지에도 그대로 나간다.
        //     화면에서만 안 보이게 하는 것이다.
        if (neutralReady && !HideHints)
        {
            Result res = results[(int)direction];
            sb.Append('\n');
            sb.Append(res.hasActive ? $"능동 {Mathf.Abs(res.active):F0}도" : "능동 -");
            sb.Append(res.hasPassive ? $"   수동 {Mathf.Abs(res.passive):F0}도" : "   수동 -");
            if (res.hasActive && res.hasPassive)
                sb.Append($"   차이 {Mathf.Abs(res.passive) - Mathf.Abs(res.active):F0}도");

            // ★압박이 아직 게인을 못 채웠으면 얼마나 더 가야 하는지 알려준다.
            //   안 그러면 "멈췄는데 왜 안 넘어가지"로 보인다 — 정지로만 진행하는 구조라 치명적이다.
            if (stage == Stage.Passive && !res.hasPassive)
            {
                float need = passiveBaseAngle + minPassiveGain - peakAngle;
                if (need > 0.5f)
                    sb.Append($"   <color=#ffcc55>{need:F0}도 더</color>");
            }
        }

        if (warn == 2) sb.Append($"\n<color=#ff6b6b>파지 미끄러짐 {(slipRatio - 1f) * 100f:+0;-0}%</color>");
        else if (warn == 1) sb.Append($"\n<color=#ffcc55>이 파지로는 못 잽니다 (면 {perp:F2})</color>");

        // ★확정에 실패한 이유를 그대로 띄운다. 몇 초 뒤 사라진다.
        if (HasFreshWarn) sb.Append($"\n<size=70%><color=#ff8a65>{lastWarn}</color></size>");

        if (showProgress)
        {
            sb.Append("\n<size=70%>");
            AppendHoldBar(holdStep);
            sb.Append('\n');
            AppendDirectionRow();
            sb.Append("</size>");
        }

        ReadoutText = sb.ToString();

        // ★진행 UI가 그리면 월드 텍스트는 접는다. 둘 다 켜 두면 같은 글이 두 군데 뜬다 —
        //   그게 바로 09-03에 지적받은 가독성 문제다.
        if (ReadoutClaimed)
        {
            if (readout.gameObject.activeSelf) readout.gameObject.SetActive(false);
            return;
        }
        if (!showReadout) return;
        if (!readout.gameObject.activeSelf) readout.gameObject.SetActive(true);

        readout.text = ReadoutText;

        // ★★<b>손 옆에 띄운다</b>(2026-09-03). 종전에는 pivot을 썼는데, 어깨를 짚고 나면
        //   pivot이 shoulderMid로 옮겨간다(CaptureShoulders). 그래서 안내문이 <b>어깨 높이로 내려가</b>
        //   "손 쪽에 따라오던 정보가 안 보인다"가 됐다. 각도기가 겪던 것과 같은 병이다.
        //   → 각도기와 같은 앵커(파지 지점)를 쓴다. 0점 전에는 손을 따라오고, 0점 뒤에는 고정된다.
        Vector3 readoutBase = gripAnchorValid ? gripAnchor : pivot;

        // ★손 바로 위라 눈에서 40cm쯤 떨어지는데, VR에서 그 거리는 초점이 안 맞아 흐리다.
        //   최소 거리를 두고 밀어낸다(2026-08-31 사용자: '가까워서 흐린가 글씨가 안 보였다').
        Vector3 readoutPos = readoutBase + Vector3.up * ReadoutRiseNow;
        Camera rcam = Camera.main;
        if (rcam != null)
        {
            Vector3 toReadout = readoutPos - rcam.transform.position;
            float dist = toReadout.magnitude;
            if (dist > 1e-3f && dist < readoutMinDistance)
                readoutPos = rcam.transform.position + toReadout / dist * readoutMinDistance;
        }
        readout.transform.position = readoutPos;
        FaceCamera(readout.transform);
    }

    // ---- 진행 표시 ----

    /// <summary>
    /// 파지 실측치 — 양손 간격과 좌우 핀치 폭. 범위 안이면 초록, 밖이면 주황.
    /// ★임계값(<see cref="pinchWidthRange"/> 등)이 전부 추정값이라, 실제 숫자가 보여야 맞출 수 있다.
    /// </summary>
    private void AppendGripNumbers()
    {
        sb.Append("\n<size=65%>");

        // ★손을 못 읽으면 그 사실이 제일 먼저 보여야 한다.
        //   손 트래킹이 안 붙은 것과 게이트에 걸린 것은 완전히 다른 문제다.
        if (!vNowValid)
        {
            sb.Append("<color=#ff6b6b>손을 못 읽습니다 — 손 트래킹 / GripFingerTip 배선 확인</color></size>");
            return;
        }

        Vector2 range = GripSpanRange(direction);
        bool spanOk = lastGripSpan >= range.x && lastGripSpan <= range.y;

        sb.Append(spanOk ? "<color=#7ad67a>" : "<color=#ffcc55>");
        sb.Append($"간격 {lastGripSpan * 100f:F0}cm");
        if (!spanOk) sb.Append($"(허용 {range.x * 100f:F0}~{range.y * 100f:F0})");
        sb.Append("</color>  ");

        // 핀치는 판정에 안 쓰면 회색으로 — 막고 있는 것처럼 보이면 안 된다.
        sb.Append(usePinchGate ? "<color=#7ad67a>" : "<color=#909090>");
        sb.Append($"핀치 {lastPinchL * 100f:F1}/{lastPinchR * 100f:F1}cm");
        sb.Append(usePinchGate ? "" : "(참고)");
        sb.Append("</color></size>");
    }

    /// <summary>중립 → 능동 → 압박. 지금 어디인지와 뭐가 끝났는지만 본다.</summary>
    private void AppendStageChain()
    {
        Result res = results[(int)direction];
        AppendChainItem("중립", stage == Stage.AwaitNeutral, neutralReady);
        sb.Append(" ▶ ");
        AppendChainItem("능동", stage == Stage.Active, res.hasActive);
        sb.Append(" ▶ ");
        AppendChainItem("압박", stage == Stage.Passive, res.hasPassive);
    }

    private void AppendChainItem(string label, bool current, bool done)
    {
        if (current) sb.Append("<color=#ffcc55><b>");
        else if (done) sb.Append("<color=#7ad67a>");
        else sb.Append("<color=#808080>");
        sb.Append(label);
        sb.Append(current ? "</b></color>" : "</color>");
    }

    /// <summary>
    /// 정지 게이지 칸 수(0~10). 매 프레임 문자열을 다시 만들지 않으려고 양자화한다.
    ///   −1 = 지금 정지해도 넘어갈 단계가 아니다 · −2 = 아직 최소 각도만큼 안 움직였다
    /// </summary>
    private int HoldBarSteps()
    {
        if (!advanceOnHold || HoldSeconds <= 0f) return -1;
        switch (stage)
        {
            case Stage.AwaitNeutral:
                break;
            case Stage.Active:
            case Stage.Passive:
                if (peakAngle < minAngleToMark) return -2;
                break;
            default:
                return -1;
        }
        return Mathf.Clamp(Mathf.RoundToInt(holdTimer / HoldSeconds * 10f), 0, 10);
    }

    private void AppendHoldBar(int steps)
    {
        if (steps == -1) { sb.Append("<color=#808080>키로 진행</color>"); return; }
        if (steps == -2)
        {
            sb.Append($"<color=#808080>움직임 대기 ({minAngleToMark:F0}도 이상)</color>");
            return;
        }

        sb.Append(steps >= 10 ? "<color=#7ad67a>정지 " : "정지 ");
        for (int i = 0; i < 10; i++) sb.Append(i < steps ? '■' : '□');
        if (steps >= 10) sb.Append("</color>");
    }

    private int DoneMask()
    {
        int m = 0;
        for (int i = 1; i <= 6; i++) if (results[i].hasPassive) m |= 1 << i;
        return m;
    }

    /// <summary>여섯 방향 중 어디까지 왔는지. ● 끝 · ▶ 지금 · ○ 아직.</summary>
    private void AppendDirectionRow()
    {
        for (int i = 1; i <= 6; i++)
        {
            if (i > 1) sb.Append(' ');
            var d = (CervicalRomDriver.Direction)i;

            if (d == direction) sb.Append("<color=#ffcc55>▶");
            else if (results[i].hasPassive) sb.Append("<color=#7ad67a>●");
            else sb.Append("<color=#808080>○");

            sb.Append(Label(d));
            sb.Append("</color>");
        }
    }

    // ---- 180도 각도기 ----
    //
    // ★실습모드의 CervicalRomPlaneGauge와 코드를 한 줄도 공유하지 않는다.
    //   그쪽은 driver의 <b>대본 각도</b>를 그리는 물건이고 13개 술기가 같이 쓴다.
    //   이쪽은 t0 기준틀 위에 손으로 잰 각을 그린다.
    //
    // 0도를 한가운데 두고 좌우로 gaugeHalfSpan(기본 90)씩 — 합쳐서 180도 반원이다.
    // 굴곡을 재는 중이라도 반대쪽(신전 쪽) 눈금이 같이 깔린다.

    private static bool IsRotationDir(CervicalRomDriver.Direction d)
        => d == CervicalRomDriver.Direction.RotationLeft || d == CervicalRomDriver.Direction.RotationRight;

    /// <summary>각도기의 0도가 가리키는 방향. 회전만 정면 기준이고 나머지는 머리 위쪽이다.</summary>
    private Vector3 GaugeZero => IsRotationDir(direction) ? axFwd : axUp;

    /// <summary>각도기 위 한 점의 방향(월드). 부호는 측정 부호와 같은 축을 쓴다.</summary>
    private Vector3 GaugeDir(float degrees)
        => Quaternion.AngleAxis(degrees, AxisFor(direction)) * GaugeZero;

    private static bool OnStep(float degrees, float step)
        => step > 0f && Mathf.Abs(degrees % step) < 0.001f;

    private void UpdateGauge()
    {
        // ★실습 각도기를 쓰는 동안에는 자체 반원을 안 그린다(2026-09-01). 코드는 남겨 둔다 —
        //   되돌릴 자리가 있어야 한다(사용자: "실측 각도기는 일단 없애지 말아봐").
        // ★실습 각도기와 <b>같이</b> 뜬다(2026-09-03). 종전에는 !UsePracticeGauge라 실습 각도기를
        //   빌려 쓰는 동안 실측 각도기가 영영 안 떴다 — "실측용 각도기도 보여주라니까".
        bool on = showGauge && useRealityGauge && !gaugeForceHidden && neutralReady && frameReady;

        if (gaugeFilter != null && gaugeFilter.gameObject.activeSelf != on)
            gaugeFilter.gameObject.SetActive(on);
        if (needle != null && needle.enabled != on) needle.enabled = on;
        if (activeMark != null) activeMark.enabled = on;
        if (passiveMark != null) passiveMark.enabled = on;

        if (!on)
        {
            for (int i = 0; i < gaugeLabels.Count; i++)
                if (gaugeLabels[i] != null) gaugeLabels[i].gameObject.SetActive(false);
            return;
        }

        if (direction != builtDirection || frameStamp != builtStamp) RebuildGauge();

        if (TryGetAngle(out _, out _, out float signed))
            SetLine(needle, GaugeCenter, GaugeCenter + GaugeDir(signed) * GaugeRadiusNow);
        else
            SetLine(needle, GaugeCenter, GaugeCenter);

        Result r = results[(int)direction];
        SetMark(activeMark, r.hasActive, r.active);
        SetMark(passiveMark, r.hasPassive, r.passive);
    }

    /// <summary>기록된 각에 바깥쪽 굵은 눈금을 남긴다. 지침과 달리 중심까지 안 온다.</summary>
    private void SetMark(LineRenderer lr, bool has, float signedDeg)
    {
        if (lr == null) return;
        if (!has) { SetLine(lr, GaugeCenter, GaugeCenter); return; }
        Vector3 d = GaugeDir(signedDeg);
        SetLine(lr, GaugeCenter + d * (GaugeRadiusNow * 0.72f), GaugeCenter + d * (GaugeRadiusNow * 1.08f));
    }

    private void RebuildGauge()
    {
        builtDirection = direction;
        builtStamp = frameStamp;

        gVerts.Clear(); gTris.Clear(); gCols.Clear();

        float half = Mathf.Max(5f, gaugeHalfSpan);
        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return;

        // 미세 → 보조 → 주 순으로 쌓는다. 굵은 눈금이 나중에 와야 위에 보인다.
        if (microStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += microStep)
            {
                if (OnStep(a, minorStep) || OnStep(a, majorStep)) continue;
                AddTickQuad(a, microTickLength, tickWidth * 0.7f, microColor, axis);
            }
        }
        if (minorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += minorStep)
            {
                if (OnStep(a, majorStep)) continue;
                AddTickQuad(a, minorTickLength, tickWidth, tickColor, axis);
            }
        }
        if (majorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += majorStep)
                AddTickQuad(a, majorTickLength, tickWidth * 1.4f, tickColor, axis);
        }

        // 0도 기준선 — 중심에서 눈금까지 통짜로 긋는다. 어디가 중립인지가 제일 중요하다.
        AddTickQuad(0f, GaugeRadiusNow, tickWidth * 1.6f, zeroLineColor, axis);

        gaugeMesh.Clear();
        gaugeMesh.SetVertices(gVerts);
        gaugeMesh.SetColors(gCols);
        gaugeMesh.SetTriangles(gTris, 0);
        gaugeMesh.RecalculateBounds();

        RebuildGaugeLabels(half);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실측ROM] 각도기 재생성 — {Label(direction)} · " +
                            $"{-half:F0}~{half:F0}도({half * 2f:F0}도) · 눈금 {gVerts.Count / 4}개</color>");
    }

    private void AddTickQuad(float degrees, float length, float width, Color c, Vector3 axis)
    {
        Vector3 d = GaugeDir(degrees);
        Vector3 side = Vector3.Cross(d, axis);
        if (side.sqrMagnitude < 1e-10f) return;
        side = side.normalized * (width * 0.5f);

        Vector3 outer = GaugeCenter + d * GaugeRadiusNow;
        Vector3 inner = GaugeCenter + d * (GaugeRadiusNow - length);

        int b = gVerts.Count;
        gVerts.Add(inner - side); gVerts.Add(inner + side);
        gVerts.Add(outer + side); gVerts.Add(outer - side);
        for (int i = 0; i < 4; i++) gCols.Add(c);

        gTris.Add(b); gTris.Add(b + 1); gTris.Add(b + 2);
        gTris.Add(b); gTris.Add(b + 2); gTris.Add(b + 3);
    }

    /// <summary>주눈금 숫자. ★양쪽 다 <b>절댓값</b>으로 적는다 — 각도기지 좌표축이 아니다.</summary>
    private void RebuildGaugeLabels(float half)
    {
        int needed = majorStep > 0f ? Mathf.FloorToInt(half * 2f / majorStep) + 1 : 0;
        while (gaugeLabels.Count < needed)
        {
            var go = new GameObject($"눈금{gaugeLabels.Count}") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            var tm = go.AddComponent<TextMeshPro>();
            if (font != null) tm.font = font;
            tm.fontSize = gaugeLabelSize * 100f * Mathf.Max(0.1f, textScale);
            tm.transform.localScale = Vector3.one * 0.01f;
            tm.alignment = TextAlignmentOptions.Center;
            tm.textWrappingMode = TextWrappingModes.NoWrap;
            tm.raycastTarget = false;
            gaugeLabels.Add(tm);
        }

        int index = 0;
        for (float a = -half; a <= half + 0.001f && index < gaugeLabels.Count; a += majorStep, index++)
        {
            TextMeshPro tm = gaugeLabels[index];
            tm.gameObject.SetActive(true);
            tm.text = Mathf.Abs(a) < 0.001f ? "0" : $"{Mathf.Abs(a):F0}";
            tm.color = Mathf.Abs(a) < 0.001f ? zeroLineColor : tickColor;
            tm.transform.position = GaugeCenter + GaugeDir(a) * (GaugeRadiusNow + gaugeLabelOffset);
        }
        for (int i = index; i < gaugeLabels.Count; i++) gaugeLabels[i].gameObject.SetActive(false);
    }

    private void UpdateAxisLines()
    {
        // ★축선 3개는 <b>어깨 중점</b>에 고정한다(2026-09-01 사용자 지시).
        //   손을 따라다니면 기준이 될 수가 없다 — 환자가 몸을 틀었는지 보려면
        //   기준이 몸에 붙어 가만히 있어야 한다. 어깨를 안 잡았으면 종전대로 손 기준이다.
        //   ★그리는 자리만 바뀐다. 각을 재는 축(axRight/axUp/axFwd)은 파지선에서 세운 그대로다.
        Vector3 axisAt = refReady ? shoulderMid : pivot;

        SetLine(lineRight, axisAt, axisAt + axRight * axisLength);
        SetLine(lineUp, axisAt, axisAt + axUp * axisLength);
        SetLine(lineFwd, axisAt, axisAt + axFwd * axisLength);

        UpdateAxisLabels(axisAt);
    }

    /// <summary>
    /// 축 끝에 이름을 붙인다. ★선만 그으면 어느 쪽이 환자 오른쪽인지 볼 방법이 없다 —
    /// 부호가 맞는지 눈으로 확인하려면 이게 있어야 한다(2026-09-01 사용자 요청).
    /// </summary>
    private void UpdateAxisLabels(Vector3 at)
    {
        if (!showAxisLabels) { HideAxisLabels(); return; }

        float d = axisLength * 1.12f;
        PlaceAxisLabel(ref labelRightPos, "환자 오른쪽", at + axRight * d, new Color(1f, 0.45f, 0.45f));
        PlaceAxisLabel(ref labelRightNeg, "환자 왼쪽", at - axRight * d, new Color(1f, 0.45f, 0.45f));
        PlaceAxisLabel(ref labelFwdPos, "앞", at + axFwd * d, new Color(0.5f, 0.7f, 1f));
        PlaceAxisLabel(ref labelFwdNeg, "뒤", at - axFwd * d, new Color(0.5f, 0.7f, 1f));
        PlaceAxisLabel(ref labelUpPos, "위", at + axUp * d, new Color(0.5f, 1f, 0.55f));
    }

    private void PlaceAxisLabel(ref TextMeshPro tm, string text, Vector3 pos, Color c)
    {
        if (tm == null)
        {
            var go = new GameObject($"축라벨_{text}") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            tm = go.AddComponent<TextMeshPro>();
            if (font != null) tm.font = font;
            tm.fontSize = axisLabelSize * 100f;
            tm.transform.localScale = Vector3.one * 0.01f;
            tm.alignment = TextAlignmentOptions.Center;
            tm.fontStyle = FontStyles.Bold;
            tm.textWrappingMode = TextWrappingModes.NoWrap;
            tm.raycastTarget = false;
            tm.text = text;
            tm.color = c;
        }
        if (!tm.gameObject.activeSelf) tm.gameObject.SetActive(true);
        tm.transform.position = pos;
        FaceCamera(tm.transform);
    }

    private void HideAxisLabels()
    {
        if (labelRightPos != null) labelRightPos.gameObject.SetActive(false);
        if (labelRightNeg != null) labelRightNeg.gameObject.SetActive(false);
        if (labelFwdPos != null) labelFwdPos.gameObject.SetActive(false);
        if (labelFwdNeg != null) labelFwdNeg.gameObject.SetActive(false);
        if (labelUpPos != null) labelUpPos.gameObject.SetActive(false);
    }

    /// <summary>
    /// 환자 정중선. 어깨 중점에서 위아래로 뻗는다 —
    /// 환자가 몸을 틀면 파지선이 이 선에서 어긋나는 게 눈에 보인다.
    /// </summary>
    private void UpdateMidline()
    {
        bool on = refReady && (midlineAlwaysOn || stage == Stage.AwaitNeutral);
        if (!on)
        {
            SetLine(lineMidline, shoulderMid, shoulderMid);
            SetLine(lineShoulder, shoulderMid, shoulderMid);
            return;
        }

        Vector3 half = refUp * (midlineLength * 0.5f);
        SetLine(lineMidline, shoulderMid - half, shoulderMid + half);

        if (showShoulderLine)
        {
            // ★길이를 주면 어깨 중점에서 좌우로 절반씩 뻗는다. 0이면 짚은 자리 그대로다.
            if (shoulderLineLength > 0f)
            {
                Vector3 halfW = refRight * (shoulderLineLength * 0.5f);
                SetLine(lineShoulder, shoulderMid - halfW, shoulderMid + halfW);
            }
            else SetLine(lineShoulder, shoulderL, shoulderR);
        }
        else SetLine(lineShoulder, shoulderMid, shoulderMid);

        if (neutralReady)
        {
            Vector3 half0 = v0.normalized * (len0 * 0.5f);
            SetLine(lineNeutral, pivot - half0, pivot + half0);
            if (vNowValid)
            {
                Vector3 d = vNow * 0.5f;
                SetLine(lineNow, pivot - d, pivot + d);
            }
        }
        else
        {
            SetLine(lineNeutral, pivot, pivot);
            SetLine(lineNow, pivot, pivot);
        }
    }

    private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    private void BuildVisuals()
    {
        var go = new GameObject("RealityMeasureVisuals") { hideFlags = HideFlags.DontSave };
        root = go.transform;
        sharedMaterial = CreateMaterial();

        lineMidline = CreateLine("중심선", midlineColor);
        lineShoulder = CreateLine("어깨선", midlineColor * 0.8f);
        lineRight = CreateLine("축_좌우", new Color(1f, 0.35f, 0.35f));
        lineUp = CreateLine("축_수직", new Color(0.4f, 1f, 0.45f));
        lineFwd = CreateLine("축_전후", new Color(0.4f, 0.6f, 1f));
        lineNeutral = CreateLine("중립파지", new Color(1f, 1f, 1f, 0.65f));
        lineNow = CreateLine("현재파지", new Color(1f, 0.85f, 0.2f));

        needle = CreateLine("지침", needleColor);
        needle.widthMultiplier = 0.009f;
        activeMark = CreateLine("능동마크", activeMarkColor);
        activeMark.widthMultiplier = 0.008f;
        passiveMark = CreateLine("수동마크", passiveMarkColor);
        passiveMark.widthMultiplier = 0.008f;

        var gm = new GameObject("각도기180") { hideFlags = HideFlags.DontSave };
        gm.transform.SetParent(root, false);
        gaugeFilter = gm.AddComponent<MeshFilter>();
        MeshRenderer gr = gm.AddComponent<MeshRenderer>();
        gr.sharedMaterial = sharedMaterial;
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gr.receiveShadows = false;
        gaugeMesh = new Mesh { name = "RealityGauge180" };
        gaugeMesh.MarkDynamic();
        gaugeFilter.sharedMesh = gaugeMesh;
        builtDirection = CervicalRomDriver.Direction.None;
        builtStamp = -1;

        var t = new GameObject("현재각") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(root, false);
        readout = t.AddComponent<TextMeshPro>();
        if (font != null) readout.font = font;
        readout.fontSize = readoutSize * 100f * Mathf.Max(0.1f, ReadoutScaleNow);
        readout.transform.localScale = Vector3.one * 0.01f;
        readout.alignment = TextAlignmentOptions.Center;
        readout.fontStyle = FontStyles.Bold;
        readout.textWrappingMode = TextWrappingModes.NoWrap;
        readout.raycastTarget = false;
    }

    private LineRenderer CreateLine(string name, Color c)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(root, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = 0.006f;
        lr.numCapVertices = 2;
        lr.sharedMaterial = sharedMaterial;
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// <summary>★게이지와 같은 방식이다. Sprites/Default가 빌드에서 스트립되면 Standard로 떨어진다.</summary>
    private static Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { name = "RealityMeasureMat", renderQueue = 3000 };
    }

    private void TearDownVisuals()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
        }
        if (sharedMaterial != null)
        {
            if (Application.isPlaying) Destroy(sharedMaterial); else DestroyImmediate(sharedMaterial);
        }
        if (gaugeMesh != null)
        {
            if (Application.isPlaying) Destroy(gaugeMesh); else DestroyImmediate(gaugeMesh);
        }

        root = null; readout = null; sharedMaterial = null;
        lineRight = lineUp = lineFwd = lineNeutral = lineNow = null;
        lineMidline = lineShoulder = null;
        labelRightPos = labelRightNeg = labelFwdPos = labelFwdNeg = labelUpPos = null;
        needle = activeMark = passiveMark = null;
        gaugeMesh = null; gaugeFilter = null;
        gaugeLabels.Clear();
        builtDirection = CervicalRomDriver.Direction.None; builtStamp = -1;
        shownStage = (Stage)(-1); shownAngle = int.MinValue; shownWarn = -1;
        shownHold = -99; shownMask = -1;
    }

    private static void FaceCamera(Transform t)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
    }

    // ================= 잡동사니 =================

    private static string Label(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:       return "굴곡";
            case CervicalRomDriver.Direction.Extension:     return "신전";
            case CervicalRomDriver.Direction.LateralRight:  return "우측굴";
            case CervicalRomDriver.Direction.LateralLeft:   return "좌측굴";
            case CervicalRomDriver.Direction.RotationRight: return "우회전";
            case CervicalRomDriver.Direction.RotationLeft:  return "좌회전";
            default:                                        return "-";
        }
    }

    private static string GripHintFor(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension: return "이마·후두";
            default:                                    return "양 측두부";
        }
    }

    private void Mark(string message)
    {
        shownStage = (Stage)(-1);   // 다음 프레임에 표시를 다시 만들게 한다
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[실측ROM] {message}</color>");
    }

    /// <summary>
    /// ★실패 사유는 <b>화면에도</b> 띄운다. 로그로만 내보내면 헤드셋을 쓴 사람은 못 본다 —
    /// 2026-08-31에 "게이지는 차는데 안 넘어간다"의 원인을 화면에서 알 수 없었던 이유가 이것이다.
    /// </summary>
    private void Warn(string message)
    {
        ChunaLogger.LogWarning($"[실측ROM] {message}");
        lastWarn = message;
        lastWarnTime = Time.time;
        shownStage = (Stage)(-1);   // 다음 프레임에 표시를 다시 만들게 한다
    }

    private const float WarnShowSeconds = 4f;
    private bool HasFreshWarn => !string.IsNullOrEmpty(lastWarn)
                                 && Time.time - lastWarnTime < WarnShowSeconds;
}
