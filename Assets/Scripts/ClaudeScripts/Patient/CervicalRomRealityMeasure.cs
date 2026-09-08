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

    [Tooltip("머리 위치를 잡아 3축선이 확정되면 <b>어깨선(초록)을 접는다</b>(2026-09-08 지시).\n" +
             "★어깨선은 '어디를 짚었나'를 보여 주는 것이라 짚는 동안에만 쓸모가 있다.\n" +
             "  3축선이 서면 같은 자리에 겹쳐 두 겹으로 보인다.")]
    [SerializeField] private bool hideShoulderLineAfterHead = true;

    [Tooltip("정보창 자리·겨눔을 정할 때 <b>방향을 믿을 수 있는 최소 거리</b>(m).\n" +
             "★이보다 가까우면 방향이 프레임마다 뒤집혀 글자가 <b>홱 날아가고 회전</b>한다\n" +
             "  (2026-09-08 지적: 어깨·머리 기준을 잡을 때 손이 얼굴 가까이 오는 구간).\n" +
             "★그때는 손 위치와 무관한 <b>카메라 수평 전방</b>을 대신 쓴다.")]
    [SerializeField] private float minFlatForDirection = 0.12f;

    [Header("=== 중립 복귀 튐 감지 (2026-09-08) ===")]
    [Tooltip("한 프레임에 각이 이만큼(도) 넘게 뛰면 <b>튐</b>으로 보고 중립을 다시 잡게 한다.\n" +
             "★★<b>0이면 통째로 끈다 — 지금 기본값이다</b>(2026-09-08 되돌림).\n" +
             "  8도로 켰더니 <b>측굴·회전에서 진행이 막혔다</b> — 각이 흔들리는 구간에서\n" +
             "  튐 판정이 계속 다시 걸려 중립을 영영 안 받았다.\n" +
             "★다시 켜려면 <b>실제로 튀는 크기를 로그로 재서</b> 그보다 넉넉히 큰 값을 넣는다.\n" +
             "  Play에서 `[실측ROM] 값이 N도 튀었습니다`의 N을 모아 보면 된다.")]
    [SerializeField] private float neutralJumpDeg;

    [Tooltip("튄 뒤에 이만큼(초) 조용해야 중립을 인정한다.\n" +
             "★<b>튄 적이 없으면 이 값은 안 쓰인다</b> — 평소 흐름은 종전처럼 즉시 넘어간다.")]
    [SerializeField] private float neutralResettleSeconds = 0.6f;

    [Header("=== 정보창 바탕 (2026-09-08) ===")]
    [Tooltip("글자 뒤에 반투명 판을 깐다. 실습 정보창(CervicalRomPracticeReadout)과 <b>같은 방식</b>이다.\n" +
             "★사용자 지시: '평가모드 강체 위 텍스트도 실습처럼 바탕 해줄래?'\n" +
             "★재질은 검증된 경로를 그대로 쓴다(Sprites/Default, 없으면 Standard) —\n" +
             "  빌드에서 셰이더가 스트립돼 xray가 죽은 전례가 있는 자리다.")]
    [SerializeField] private bool showInfoBackdrop = true;
    [SerializeField] private Color infoBackdropColor = new Color(0.06f, 0.07f, 0.10f, 0.72f);
    [Tooltip("판이 글자보다 좌우로 넉넉한 정도(글자 로컬 단위)")]
    [SerializeField] private float infoBackdropPadX = 5f;
    [Tooltip("판이 글자보다 위아래로 넉넉한 정도(글자 로컬 단위)")]
    [SerializeField] private float infoBackdropPadY = 3f;

    [Tooltip("정보창에 <b>곁가지</b>를 같이 그린다 — 절차 사슬 · 파지 실측 숫자 · 능동/수동/차이 · 방향 목록.\n" +
             "★<b>기본은 끔</b>이다(2026-09-08 사용자 지시: '정보는 현재각도 유지 게이지만 남기고 비활성화').\n" +
             "  ★<b>남는 것</b>: 방향 이름 + 현재 각도 · ✔ 기록됨 · 유지 게이지 · 경고 · 단계 안내문.\n" +
             "  ★삭제가 아니다 — 접은 값들은 results·_rom.csv·결과지에 그대로 간다.")]
    [SerializeField] private bool showSecondaryInfo;

    [Tooltip("★글씨 크기 배율(안내문 + 눈금 숫자).\n" +
             "2026-08-31 사용자: '작은데다 가까워서 흐려 글씨가 안 보였다'.\n" +
             "★<b>2026-09-01 정정</b>: 08-31에 '신규 필드라 코드 기본값이 먹는다'고 적었는데, " +
             "그 뒤 씬을 저장하면서 1.8이 굳었다. 이제는 코드에서 못 바꾼다 — " +
             "안내문 크기는 readoutScale로 바꾼다.")]
    [SerializeField] private float textScale = 1.8f;

    [Tooltip("안내문을 기준점보다 이만큼 위에 띄운다(m). " +
             "★2026-09-07: 오버라이드 필드를 걷고 이 값 하나만 쓴다. 인스펙터에서 맞춘다.")]
    [SerializeField] private float readoutRise = 0.3f;

    [Tooltip("안내문 글씨 배율. 눈금 숫자까지 키우는 textScale과 달리 안내문만 키운다. " +
             "★2026-09-07: readoutScaleOverride를 이 이름으로 합쳤다. 인스펙터에서 맞춘다.")]
    [SerializeField] private float readoutScale = 2.6f;

    [Tooltip("★안내문이 눈에서 이보다 가까우면 밀어낸다(m). VR은 너무 가까우면 초점이 안 맞아 흐리다.")]
    [SerializeField] private float readoutMinDistance = 0.55f;

    [Tooltip("시술자 쪽으로 이만큼 <b>수평으로</b> 당겨 온다(m). 환자 머리에 가리는 것을 푼다.\n" +
             "★올리는 것(readoutRise)과 <b>따로</b> 둔 손잡이다 — 하나로 묶으면\n" +
             "  높이를 만질 때 거리가 같이 변한다.\n" +
             "★이 컴포넌트는 씬에 있다. 씬을 저장하면 이 값이 굳어 코드 기본값이 안 먹는다(규칙 7).")]
    [SerializeField] private float readoutPullToViewer = 0.25f;

    /// <summary>실제로 쓸 안내문 높이(m).</summary>
    // ★★2026-09-04 지시 — 안내문(진행 여부 UI)이 <b>손을 따라온다</b>.
    //   종전 앵커 gripAnchor는 0점을 잡는 순간 고정돼, 신전처럼 손이 크게 움직이는 방향에서
    //   안내문이 중립 자리에 남아 시야 밖으로 나갔다("신전할 때 아예 안 보여").
    //   ★신규 필드라 코드 기본값이 그대로 먹는다(규칙 7).
    [Tooltip("★안내문을 <b>손을 잇는 선의 가운데</b> 위에 띄운다(2026-09-04 지시).\n" +
             "양손을 잡고 있으면 두 손의 중점, 한 손만 읽히면 그 손을 쓴다.\n" +
             "끄면 종전대로 0점을 잡은 파지 지점에 고정된다(신전에서 안 보인다).\n" +
             "위로 띄우는 높이는 readoutRise다.")]
    [SerializeField] private bool readoutFollowHands = true;

    /// <summary>안내문이 따라갈 자리. 양손이면 중점, 한 손이면 그 손(월드). 매 프레임 갱신된다.</summary>
    private Vector3 handMid;
    private bool handMidValid;

    private float ReadoutRiseNow => readoutRise;

    /// <summary>실제로 쓸 안내문 글씨 배율.</summary>
    private float ReadoutScaleNow => readoutScale;

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
    // ════════════════════════════════════════════════════════════════════
    // ★★간결 표시 (2026-09-04 회의 컨펌) — "표시되는 정보가 너무 많다"
    //
    //   윗분들이 원하는 것: 어깨 십자선 하나 + 니들 하나 + 각도 숫자. 그게 전부다.
    //   눈금·숫자눈금·반원·판·마크·파지선·축이름 전부 뺀다.
    //
    //   니들 규칙(사용자 설명 그대로):
    //     · 원점  = S라인 중심 (양 어깨 중점). T라인 중심과 잇는 선은 <b>안 그린다</b>.
    //     · 굴곡·신전·측굴 → 0도는 <b>Y 수직</b>. T라인 중심이 속한 평면에서 기운다.
    //     · 회전            → 0도는 <b>정면</b>(S라인과 수직). 눕혀서 수평면에서 돈다.
    //   ★이 규칙은 이미 GaugeZero에 그대로 있다(회전이면 axFwd, 아니면 axUp).
    //     그래서 <b>각 계산은 한 줄도 안 바꾼다</b> — 09-01~09-02에 부호까지 검증한 코드다.
    //
    //   십자선은 <b>A안</b>: 지금 재는 방향에 쓰는 2축만 그린다(니들이 도는 평면을 펴는 두 축).
    //   ★B안(3축 항상)은 나중에 <b>추가만</b> 하면 된다 — 아래 minimalCrossBothAxes를 켜면 된다.
    //
    // ★전부 신규 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).
    //   usePracticeGauge·useRealityGauge·showAxisLabels는 씬에 1로 굳어 있어 코드로 못 끈다.
    //   그래서 <b>이 스위치가 그것들을 덮는다.</b>
    // ════════════════════════════════════════════════════════════════════

    [Header("=== 간결 표시 (2026-09-04 회의) ===")]
    [Tooltip("★켜면 각도기 A_2·B를 둘 다 끄고 <b>십자선 + 니들 + 각도</b>만 남긴다.\n" +
             "끄면 09-03까지의 표시(반원 각도기·눈금)로 돌아간다.")]
    [SerializeField] private bool minimalDisplay = true;

    [Tooltip("십자선 굵기(m). ★회의 요청: '십자선은 좀 얇았으면'.")]
    [SerializeField] private float minimalCrossWidth = 0.0022f;

    [Tooltip("니들 굵기(m). 십자선보다 굵어야 어느 쪽이 바늘인지 보인다.")]
    [SerializeField] private float minimalNeedleWidth = 0.005f;

    [Tooltip("십자선 팔 길이(m). 원점에서 좌우 양쪽으로 이만큼씩.")]
    [SerializeField] private float minimalCrossLength = 0.18f;

    [Tooltip("니들 길이(m).")]
    [SerializeField] private float minimalNeedleLength = 0.25f;

    [Tooltip("★B안 — 십자선 3축을 <b>항상</b> 그린다. 끄면(A안) 지금 방향에 쓰는 2축만 그린다.\n" +
             "2026-09-04: 일단 A안으로 간다. 이걸 켜는 것만으로 B안이 된다.")]
    [SerializeField] private bool minimalCrossBothAxes;

    [Tooltip("★<b>테스트 표시</b>(2026-09-04) — T중심-S중심 연결선의 각을 본 각 아래에 같이 띄운다.\n" +
             "본 각은 T라인의 회전각이고, 이건 목축이 기운 각이다. 둘을 비교해 고르려는 것이다.\n" +
             "★측정·채점·CSV에는 안 들어간다. 화면에만 뜬다 — 정하고 나면 끄면 된다.")]
    [SerializeField] private bool showNeckLineCompare = true;

    [Tooltip("★<b>어깨를 짚을 때만</b> 손바닥을 쓴다(2026-09-04 회의 지시).\n" +
             "파지·각도 측정은 종전대로 엄지다 — 여기만 갈린다.\n" +
             "손바닥 본을 못 찾으면 자동으로 종전(파지점)으로 떨어진다.")]
    [SerializeField] private bool usesPalmForShoulders = true;

    // ════════════════════════════════════════════════════════════════════
    // ★★강체 방식 (2026-09-04 설계 변경)
    //
    //   사용자: "강체와 축을 미리 잡아서 고정된 기준을 만들고, 양손으로 실제 환자의 움직임에 따라
    //            강체를 따라 움직이게 하는 방식으로 변경하면 정밀도는 떨어져도 노이즈는 줄지 않을까."
    //
    //   강체는 <b>어깨선에서 파지점보다 살짝 위까지 올라온 기둥</b>이다(사용자 지정).
    //   둘레는 측정할 때의 파지 간격 정도. 그 기둥이 고정된 축 둘레로 기울고 돈다.
    //
    //   ★기둥은 <b>표시물</b>이다. 각은 손의 반경벡터가 정한다(TryGetAngle) —
    //     기둥을 어디에 얼마나 크게 그리든 값은 안 변한다.
    //   ★코끝선 = 회전을 눈으로 보는 선. 회전은 머리 중심이 제자리라
    //     기둥의 <b>기울기</b>로는 안 보인다 — 기둥에 박힌 이 선이 도는 것으로 본다.
    // ════════════════════════════════════════════════════════════════════

    [Header("=== 강체 방식 (2026-09-04) ===")]
    [Tooltip("★켜면 손 사이 벡터가 아니라 <b>각 손의 반경벡터</b>로 각을 잰다.\n" +
             "축에 수직인 성분만 남기므로 노이즈가 줄고, <b>한 손만 남아도</b> 측정된다.\n" +
             "끄면 09-04 이전 방식(두 손 사이 벡터)으로 돌아간다.")]
    [SerializeField] private bool useRigidBodyMeasure = true;

    // ── 손값 버리기 · 방향별 가중치 (2026-09-04) ──────────────────────────
    //
    // ★★<b>속도 필터로는 못 잡는다</b>(2026-09-04 지적).
    //   사용자: "신전 상태에서 뒤통수 쪽 손이 노이즈로 밀려서 계속 땅으로 꺼지는데
    //            각도기가 그걸 따라가더라. 손값이 이상하면 버리라고 했는데 버리는 걸 제대로 못한다."
    //   기존 AcceptHand는 <b>속도</b>만 본다(maxHandSpeed 1.2m/s). 갑자기 점프하면 잡지만,
    //   스르르 미끄러져 내려가는 <b>드리프트</b>는 임계 아래라 통과한다. 그게 안 버려진 이유다.
    //
    // ★<b>강체 구속으로 잡는다.</b> 머리는 강체이므로 축원점에서 각 손까지의 <b>반경 길이</b>는
    //   보존돼야 한다. 손이 땅으로 꺼지면 반경이 길어지므로 <b>속도와 무관하게</b> 걸린다.
    //   양손 간격을 보는 IsSlipping과 같은 원리인데, 그건 경고만 하고 버리지는 않았다.

    [Tooltip("★반경이 중립에서 이 비율 넘게 변하면 <b>그 손을 버린다</b>(0.25 = ±25%).\n" +
             "머리가 강체라 축원점→손 거리는 보존돼야 한다 — 안 지켜지면 그 손이 틀린 것이다.\n" +
             "0이면 안 버린다(종전 동작).")]
    [Range(0f, 1f)][SerializeField] private float radiusDriftTolerance = 0.40f;

    // ★★방향별 손 가중치(2026-09-04 지시).
    //   사용자: "굴곡은 뒤통수 손, 신전은 이마 쪽 손에 가중치를 두라.
    //            회전도 우회전이면 오른손, 좌회전이면 왼손으로 가중치를 좀 줄래?"
    //   ★이유가 방향마다 다르다 — 시상면은 <b>가려지는 쪽</b>을 덜 믿는 것이고,
    //     회전은 <b>도는 쪽으로 크게 움직이는 손</b>을 더 믿는 것이다.
    //   ★한 손이 버려지면 남은 손이 100%를 가져간다. 가중치는 둘 다 살아 있을 때만 쓴다.
    [Tooltip("주로 믿을 손의 몫(0.5면 가중치 없음, 0.75면 3:1).\n" +
             "굴곡=뒤통수 손 · 신전=이마 손 · 우회전=오른손 · 좌회전=왼손. 측굴은 가중치 없음.")]
    [Range(0.5f, 1f)][SerializeField] private float primaryHandWeight = 0.75f;

    /// <summary>중립에서 각 손이 이마 쪽인가(양수) 뒤통수 쪽인가(음수). refFwd에 사영한 부호다.</summary>
    private float faceSideL, faceSideR;

    /// <summary>중립 반경 길이. 강체 구속 검사의 기준이다.</summary>
    private float radLen0L, radLen0R;

    [Tooltip("강체 기둥을 그린다. 끄면 각만 재고 안 그린다.")]
    [SerializeField] private bool showRigidBody = true;

    [Tooltip("기둥 꼭대기를 파지점보다 이만큼 <b>더 위로</b> 올린다(m).")]
    [SerializeField] private float rigidTopRise = 0.06f;

    [Tooltip("기둥을 환자 <b>앞쪽</b>으로 이만큼 옮긴다(m).\n" +
             "★엄지는 머리 <b>뒤쪽</b>에서 잡히므로 엄지 중점은 실제 머리 중심보다 뒤에 있다.\n" +
             "  그대로 세우면 기둥이 뒤로 밀려 보인다(2026-09-07 지적).\n" +
             "★보이는 기둥만 옮긴다 — 회전축·축원점은 그대로다(각도 계산에 안 섞인다).\n" +
             "★반대로 가면 <b>음수</b>를 넣는다. 부호는 Play에서 보고 정한다.")]
    [SerializeField] private float rigidForwardOffset = 0.07f;

    [Tooltip("기둥 굵기를 파지 간격의 몇 배로 할지. 1이면 간격이 곧 지름이다.")]
    [SerializeField] private float rigidWidthScale = 1f;

    [Tooltip("코끝선 길이(m). 0이면 안 그린다.")]
    [SerializeField] private float noseLineLength = 0.22f;

    [SerializeField] private Color rigidBodyColor = new Color(0.45f, 0.85f, 1f, 0.35f);
    [SerializeField] private Color noseLineColor = new Color(1f, 0.75f, 0.25f, 0.95f);

    // ── 머리 위치 지정 (2026-09-04 순서 변경) ────────────────────────────
    // 사용자: "최초에 어깨선 설정한 뒤에 다음 단계에서 머리 위치를 지정하고,
    //          그 다음 굴곡을 위한 시상면 파지로 가야 해."
    //
    // ★★<b>축원점 추정을 없애는 것이 목적이다.</b> 종전에는 회전 중심을
    //   <c>어깨중점 + gaugePivotRise(0.12)</c>로 <b>박아 뒀다</b>. 새 방식은 반경벡터로 각을 내므로
    //   중심이 틀리면 각이 통째로 스케일된다 — 중심이 낮으면 작게, 높으면 크게 읽힌다.
    //   사람이 직접 짚어 주면 그 추정이 사라진다.
    //
    // ★<b>양손으로 측두를 짚고 정지</b>한다(사용자 확정 A안). 어깨선 잡는 동작과 같아서 배울 게 없다.
    // ★축원점은 머리 중심에서 <b>목 아래로</b> 내린 자리다(②안). 목이 도는 자리는 머리 중심이 아니다.
    // ★강체 기둥은 <b>축원점에서 수직으로</b> 선다(사용자: "최초에는 수직으로 뻗어야 해").
    //   축원점을 머리 중심 바로 아래로 잡으므로 기둥은 <b>구성상</b> 수직이다 — 기울여 만들지 않는다.

    [Tooltip("★머리 위치를 따로 잡는 단계를 쓴다(2026-09-04). 끄면 종전대로 어깨→파지로 바로 간다.")]
    [SerializeField] private bool requireHeadCapture = true;

    [Tooltip("머리 중심에서 <b>아래로</b> 이만큼 내린 곳이 회전 중심(축원점)이다(m).\n" +
             "★목이 도는 자리는 머리 중심이 아니라 그 아래다. 값이 크면 반경이 길어져 각이 작게 읽힌다.\n" +
             "실측으로 맞출 값이다 — 우선 해부학적 어림값으로 둔다.")]
    [SerializeField] private float neckDropFromHead = 0.12f;

    /// <summary>양손 측두 중점 = 머리 중심(월드). 세션에 한 번 잡는다.</summary>
    private Vector3 headCenter;

    /// <summary>회전 중심. 머리 중심에서 목 아래로 내린 자리다. 각 계산의 원점이다.</summary>
    private Vector3 axisOrigin;
    private bool headReady;

    /// <summary>지금 쓸 회전 중심. 머리를 안 잡았으면 종전 추정으로 떨어진다.</summary>
    private Vector3 AnglePivotNow => headReady ? axisOrigin : anglePivot;

    /// <summary>
    /// 양손으로 측두를 짚은 자리에서 머리 중심과 회전 중심을 잡는다.
    /// ★어깨선이 먼저다 — 기준틀 없이는 축이 없다.
    /// </summary>
    [ContextMenu("2 - 머리 위치 잡기")]
    public void CaptureHead()
    {
        if (!refReady) { Warn("어깨 기준선이 먼저입니다."); return; }
        if (!TryGetHands(out Vector3 l, out Vector3 r)) { Warn("손을 못 찾았습니다."); return; }

        float span = Vector3.Distance(l, r);
        Vector2 range = GripSpanRange(CervicalRomDriver.Direction.LateralRight);   // 측두 파지 대역
        if (span < range.x || span > range.y)
        {
            Warn($"측두 파지로 안 보입니다({span * 100f:F0}cm — 기준 " +
                 $"{range.x * 100f:F0}~{range.y * 100f:F0}cm). 양손을 머리 양 측면에 대세요.");
            holdTimer = 0f;
            return;
        }

        headCenter = (l + r) * 0.5f;

        axisOrigin = headCenter - Vector3.up * Mathf.Max(0f, neckDropFromHead);
        headReady = true;
        holdTimer = 0f;
        frameStamp++;

        // ★기둥은 여기서 <b>수직으로</b> 만든다. 축원점이 머리 중심 바로 아래라 구성상 수직이다.
        // ★★<b>2026-09-07 — 기둥을 앞으로 뺀다(사용자 지시).</b>
        //   "강체 위치가 엄지 기준보다 앞에 있어야 한다. <b>엄지는 뒤쪽에서 잡히잖아.</b>"
        //   엄지 중점(headCenter)은 실제 머리 중심보다 <b>뒤</b>에 있다 — 그대로 세우면 기둥이 뒤로 밀린다.
        //   ★<b>기둥(보이는 것)만</b> 옮긴다. 회전축·축원점(axisOrigin)은 각도 계산에 들어가므로 안 건드린다.
        //   ★부호는 추론하지 않는다(규칙 9) — 값을 음수로 넣으면 반대로 간다. Play에서 보고 정한다.
        Vector3 fwdShift = refFwd.sqrMagnitude > 1e-8f
                           ? refFwd.normalized * rigidForwardOffset : Vector3.zero;
        rigidBase0 = axisOrigin + fwdShift;
        rigidTop0 = headCenter + Vector3.up * rigidTopRise + fwdShift;
        rigidRadius = Mathf.Max(0.02f, span * 0.5f * Mathf.Max(0.1f, rigidWidthScale));
        rigidReady = true;

        Mark($"머리 위치 고정 - 측두 간격 {span * 100f:F0}cm · 회전 중심은 {neckDropFromHead * 100f:F0}cm 아래. " +
             "이제 시상면 파지로 가세요.");
        ChunaLogger.Log($"<color=cyan>[실측] 머리 위치 — 중심 {headCenter} · 축원점 {axisOrigin} " +
                        $"· 기둥 높이 {(rigidTop0.y - rigidBase0.y) * 100f:F0}cm</color>");
    }

    /// <summary>머리 위치가 잡혔는가. 브리지가 단계를 넘길 조건으로 읽는다.</summary>
    public bool HeadReady => headReady;

    /// <summary>
    /// 강체가 지금 돈 만큼의 회전. 축은 고정이고 강체만 이만큼 돌아 있다.
    /// ★기둥·코끝선·정보창이 <b>모두 이 하나를 쓴다.</b> 따로 계산하면 표시끼리 어긋난다.
    /// </summary>
    private Quaternion RigidTurn()
    {
        // ★단락 평가로 묶으면 out 변수가 '확실히 할당됨'을 못 넘긴다(CS0165) — 호출을 먼저 한다.
        Vector3 axis = AxisFor(direction);
        bool angleOk = TryGetAngle(out _, out _, out float signed);
        if (axis.sqrMagnitude < 1e-8f || !angleOk) return Quaternion.identity;
        return Quaternion.AngleAxis(signed, axis.normalized);
    }

    /// <summary>
    /// 강체에 <b>완전히 붙은</b> 자리를 준다 — 머리 중심에서 <paramref name="riseAboveHead"/>만큼 위,
    /// 그리고 강체가 돈 만큼 같이 돈 자리다. 머리가 숙으면 이 점도 같이 앞으로 넘어간다.
    ///
    /// ★2026-09-07 사용자 지시: "손에 따라오는 디스플레이가 환자 머리랑 강체를 기준으로
    ///   일정 위치에 고정 배치돼서 움직이는 거야." 손 떨림이 글씨 떨림이 되던 것을 여기서 끊는다.
    /// ★<b>강체를 아직 안 잡았으면 false다</b>(어깨선·머리위치 단계). 그때는 부르는 쪽이
    ///   종전대로 손을 따라간다 — 그 두 단계는 손을 보며 하는 작업이다(사용자 확정).
    /// </summary>
    public bool TryGetRigidAnchor(float riseAboveHead, out Vector3 world, out Vector3 up)
    {
        world = Vector3.zero;
        up = Vector3.up;
        if (!headReady) return false;
        Quaternion turn = RigidTurn();
        Vector3 local = headCenter + Vector3.up * riseAboveHead - rigidBase0;
        world = rigidBase0 + turn * local;

        // ★강체가 기운 만큼 글자도 기운다(2026-09-07 사용자 지시).
        //   중립에서 기둥은 수직이므로, 돈 뒤의 '위쪽'은 같은 회전을 먹인 수직이다.
        up = turn * Vector3.up;
        return true;
    }

    /// <summary>중립에서의 기둥 — 밑동(축원점)·꼭대기·반지름. 잡을 때 한 번 정한다.</summary>
    private Vector3 rigidBase0, rigidTop0;
    private float rigidRadius;
    private bool rigidReady;

    private Transform rigidBody;      // 기둥 (DontSave)
    private LineRenderer noseLine;

    // ── 측정 확정 알림 (2026-09-04) ───────────────────────────────────────
    // 사용자: "능동이든 수동이든 측정이 되면 딩동이나 뭐 완료됐다는 소리 좀 내줄래?
    //          게이지만 계속 도니까 이게 측정이 된 건지 아직 안 된 건지 모르겠네.
    //          측정됐으면 텀이라도 있든가."
    //
    // ★소리와 <b>텀</b>을 같이 준다. 소리만 있으면 놓치고, 텀만 있으면 왜 멈췄는지 모른다.
    //   텀 동안에는 홀드 게이지가 안 돈다 — "지금은 다음 걸 재는 중이 아니다"가 눈으로 보여야 한다.
    // ★클립은 이미 있는 것을 쓴다(Assets/Resources/Audio/RomStepDone.wav).
    //   교육 브리지가 단계 완료음으로 쓰던 그 소리다 — 새로 만들지 않는다.

    [Tooltip("측정(능동·수동)이 확정될 때 낼 소리. 비우면 Resources/Audio/RomStepDone을 쓴다.")]
    [SerializeField] private AudioClip measureDoneClip;

    [Range(0f, 1f)][SerializeField] private float measureDoneVolume = 0.9f;

    [Tooltip("확정 뒤 이만큼은 <b>다음 측정을 안 센다</b>(초). 0이면 텀 없이 곧장 이어진다.\n" +
             "★게이지가 계속 도는 것과 확정된 것을 구분하려고 둔다.")]
    [SerializeField] private float measureDonePause = 1.0f;

    private AudioSource cueSource;
    private float donePauseLeft;

    /// <summary>확정 직후의 텀. 이 동안에는 홀드를 안 센다.</summary>
    private bool InDonePause => donePauseLeft > 0f;

    /// <summary>측정 확정을 알린다 — 소리 + 텀. 능동·수동 <b>둘 다</b> 여기를 탄다.</summary>
    private void PlayMeasureDone(string what)
    {
        donePauseLeft = Mathf.Max(0f, measureDonePause);

        if (cueSource == null)
        {
            cueSource = GetComponent<AudioSource>();
            if (cueSource == null)
            {
                cueSource = gameObject.AddComponent<AudioSource>();
                cueSource.playOnAwake = false;
                cueSource.spatialBlend = 0f;   // 2D — 어디를 보고 있든 들려야 한다
            }
        }

        AudioClip clip = measureDoneClip;
        if (clip == null)
        {
            if (fallbackDoneClip == null) fallbackDoneClip = Resources.Load<AudioClip>("Audio/RomStepDone");
            clip = fallbackDoneClip;
        }
        if (clip == null)
        {
            // ★조용히 넘어가지 않는다 — 소리가 안 나는 이유를 알 수 있어야 한다.
            if (!warnedNoDoneClip)
            {
                warnedNoDoneClip = true;
                ChunaLogger.LogWarning("[실측] 확정음 클립이 없습니다 — Resources/Audio/RomStepDone 확인.");
            }
            return;
        }
        cueSource.PlayOneShot(clip, measureDoneVolume);
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[실측] {what} 확정 — 알림음 + {donePauseLeft:F1}초 텀</color>");
    }

    private AudioClip fallbackDoneClip;
    private bool warnedNoDoneClip;

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
    private Transform infoBackdrop;   // 글자 뒤 반투명 판 (2026-09-08)
    private LineRenderer lineRight, lineUp, lineFwd, lineNeutral, lineNow;
    private LineRenderer lineMidline, lineShoulder;
    private TextMeshPro labelRightPos, labelRightNeg, labelFwdPos, labelFwdNeg, labelUpPos;
    private LineRenderer needle, activeMark, passiveMark;

    /// <summary>
    /// 테스트용 두 번째 지침 — <b>T중심-S중심 연결선</b>의 각을 가리킨다(2026-09-04 요청).
    /// ★본 지침(needle)은 T라인 회전각이다. 둘을 나란히 놓고 눈으로 고르려는 것이다.
    ///   showNeckLineCompare를 끄면 사라진다. 측정·채점에는 안 들어간다.
    /// </summary>
    private LineRenderer testNeedle;
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
    private int shownCompare = int.MinValue;   // 테스트용 T-S선 각(2026-09-04)
    private bool shownDonePause;               // 확정 텀 표시(2026-09-04)

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

        // ★튐 이력도 방향과 함께 버린다(2026-09-08). 이전 방향에서 튄 것을
        //   다음 방향까지 들고 가면, 멀쩡한 중립을 한 번 더 기다리게 만든다.
        neutralPrevAngle = -1f;
        neutralQuietSince = -1f;
        neutralJumped = false;

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
    /// <summary>
    /// 중립으로 돌아왔는가. ★<b>값이 튀면 인정하지 않고 다시 잡으라고 안내한다</b>(2026-09-08 지시:
    /// "중립 위치해서 다음 방향 하려는데 갑자기 확 튀거나 값이 뛰면 경고하면서 중립 다시 잡게 안내해 줘").
    ///
    /// ★<b>왜 여기인가</b>: 각을 들고 있는 것이 이 클래스다. 브리지에서 재면 같은 값을 두 번 계산하게 되고,
    ///   두 계산이 조금만 달라도 "화면은 중립인데 안 넘어간다"가 된다(08-24에 겪은 형태).
    /// ★<b>튄 적이 없으면 종전 그대로 즉시 참</b>이다 — 평소 흐름을 느리게 만들지 않는다.
    ///   튄 뒤에만 <c>neutralResettleSeconds</c>만큼 조용하기를 요구한다.
    /// ★튐은 <b>프레임 간 변화량</b>으로 본다. 손을 놓치거나 파지가 미끄러지면 각이 계단처럼 뛴다.
    /// </summary>
    public bool IsBackToNeutral(float toleranceDeg)
    {
        if (!TryGetAngle(out float deg, out _, out _))
        {
            // 못 재는 상태 — 이력을 버린다. 다시 잡히면 처음부터 본다.
            neutralPrevAngle = -1f;
            neutralQuietSince = -1f;
            return false;
        }

        // ★★<b>2026-09-08 되돌렸다</b> — 사용자: "측굴·회전 튀는 거 잡으랬더니 오히려
        //   처음 측정한 것에서 진행이 안 되고 틀어져 버린다. 튀는 거 잡는 필터 다시 되돌려 줘."
        //   ★<b>내 회귀였다.</b> 각이 흔들리는 구간에서는 튐 판정이 <b>계속 다시 걸려</b>
        //     `neutralJumped`가 안 풀리고, 그러면 중립을 영영 안 받아 그 방향에서 못 나간다.
        //     앞서 보고받은 "못 빠져나가고 제자리걸음"도 이것이었다.
        //   ★<c>neutralJumpDeg</c>가 <b>0 이하면 통째로 끈다</b> — 그러면 아래 흐름이
        //     종전 한 줄짜리(`deg <= toleranceDeg`)와 <b>정확히 같아진다</b>.
        //     코드는 남긴다(미사용 코드 방침). 임계를 정할 자료가 생기면 값만 넣으면 된다.
        if (neutralJumpDeg <= 0f)
        {
            neutralPrevAngle = deg;
            neutralJumped = false;
            neutralQuietSince = -1f;
            return deg <= toleranceDeg;
        }

        // ── 튐 감지 ──────────────────────────────────────────────────────
        if (neutralPrevAngle >= 0f)
        {
            float jump = Mathf.Abs(deg - neutralPrevAngle);
            if (jump > neutralJumpDeg)
            {
                neutralJumped = true;
                neutralQuietSince = -1f;

                // ★매 프레임 경고하면 로그가 쏟아져 진짜 신호를 묻는다. 텀을 둔다.
                if (Time.time - neutralJumpWarnAt > neutralJumpWarnCooldown)
                {
                    neutralJumpWarnAt = Time.time;
                    Warn($"값이 {jump:F0}도 튀었습니다 — 중립을 다시 잡아 주세요");
                }
            }
        }
        neutralPrevAngle = deg;

        if (deg > toleranceDeg)
        {
            neutralQuietSince = -1f;
            return false;
        }

        // 중립 범위 안. 튄 적이 없으면 곧장 인정한다.
        if (!neutralJumped) return true;

        // 튄 뒤라면 잠깐 조용해야 인정한다 — 그래야 "다시 잡았다"가 된다.
        if (neutralQuietSince < 0f) neutralQuietSince = Time.time;
        if (Time.time - neutralQuietSince < neutralResettleSeconds) return false;

        neutralJumped = false;
        neutralQuietSince = -1f;
        return true;
    }

    private float neutralPrevAngle = -1f;
    private float neutralQuietSince = -1f;
    private bool neutralJumped;
    private float neutralJumpWarnAt = -99f;
    private const float neutralJumpWarnCooldown = 1.5f;

    /// <summary>지금 읽히는 각(도). 크기다. 못 재는 상태면 false.</summary>
    public bool TryGetAngle(out float degrees, out float perpRatio, out float signed)
    {
        degrees = 0f; perpRatio = 0f; signed = 0f;
        if (!neutralReady) return false;

        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return false;

        // ════════════════════════════════════════════════════════════════
        // ★★<b>강체 방식</b>(2026-09-04 설계 변경). 손 사이 벡터를 안 쓴다.
        //
        //   종전: 두 손을 잇는 벡터 하나가 얼마나 돌았나 → 한 손이 떨리면 그 선이 통째로 흔들린다.
        //         사용자: "노이즈 빈도도 높고 기술 한계로 정밀한 유지가 안 되는 경우가 많았다."
        //
        //   새 방식: 축을 어깨선에서 <b>고정</b>하고, 각 손의 <b>반경벡터</b>가 그 축 둘레에서
        //           중립으로부터 몇 도 돌았는지를 잰다. 손마다 각이 따로 나오고 평균한다.
        //
        //   ★<b>사영이 노이즈를 버린다</b>. ProjectOnPlane으로 축 방향 성분이 사라지므로
        //     그쪽으로 떠는 손 떨림은 각에 <b>들어오지 못한다</b>. 1자유도로 눌러 담는 것이다.
        //     정밀도는 떨어진다 — 실제 미세 움직임도 같이 버려진다. 그 교환을 택했다.
        //   ★<b>한 손만 남아도 성립한다</b>. 두 손이 같은 축 둘레를 같은 각만큼 돌기 때문이다.
        //     종전에는 이게 4초짜리 예외 경로였는데, 이제 <b>본류</b>다.
        //   ★부호 규약은 AxisFor 그대로다 — 09-01~09-02에 검증한 것을 안 건드린다.
        // ════════════════════════════════════════════════════════════════
        if (useRigidBodyMeasure)
        {
            // ★값은 <b>프레임당 한 번</b> 계산해 캐시한다(UpdateAngleCache).
            //   여기서 스무딩을 걸면 한 프레임에 여러 번 불려(안내문·각도기·기둥) 중복으로 먹는다.
            if (!cachedValid) return false;
            signed = cachedSigned;
            perpRatio = cachedPerp;
            degrees = Mathf.Abs(signed);
            return true;
        }

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

    // ════════════════════════════════════════════════════════════════════
    // ★비교용 각도 — <b>T-S 중심 연결선</b> (2026-09-04 테스트 요청)
    //
    //   지금 재는 각(TryGetAngle)은 <b>T라인의 회전각</b>이다 — 중립 때의 T라인과 지금 T라인을
    //   측정 평면에 투영해 잰다. S라인은 기준틀을 세우는 데만 쓰이고 각에는 안 들어간다.
    //   사용자: "T라인의 회전각만 쓰는 거면 테스트용으로 T-S라인의 중심축 연결선 각도도 표시해줘."
    //
    //   여기서 재는 것: (T중심 − S중심) 벡터가 중립에서 얼마나 돌았나.
    //   ★위와 <b>같은 축·같은 투영·같은 부호 규약</b>을 쓴다. 안 그러면 비교가 성립하지 않는다.
    //
    // ★예상되는 결과를 미리 적어 둔다(계산이지 실측이 아니다):
    //   굴곡·신전·측굴 → 목이 기우니 두 값이 <b>비슷하게</b> 나올 것이다.
    //   좌·우회전     → 목축이 선 채로 머리만 도니 <b>이쪽만 0 근처</b>로 나올 것이다.
    //   그 차이를 눈으로 보자는 것이 이 표시의 목적이다.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>중립을 잡은 순간의 (T중심 − S중심). 비교용 각의 0점이다.</summary>
    private Vector3 neckVec0;
    private bool neckVec0Valid;

    /// <summary>
    /// 비교용 각 — T중심과 S중심을 잇는 선이 중립에서 돈 각(도).
    /// ★측정·채점에는 <b>안 쓴다.</b> 화면에만 띄운다.
    /// </summary>
    public bool TryGetNeckLineAngle(out float degrees, out float signed)
    {
        degrees = 0f; signed = 0f;
        if (!neutralReady || !neckVec0Valid || !refReady || !handMidValid) return false;

        // ★★<b>회전에서는 안 낸다</b>(2026-09-04). T중심-S중심 선은 회전축(수직)과 거의 나란하다 —
        //   머리 중심이 어깨 중심 바로 위이기 때문이다. 수평 투영이 몇 cm뿐이라
        //   손이 1cm만 흔들려도 각이 수십 도씩 튄다. 비교가 아니라 <b>잡음</b>이다.
        //   09-04에 이 값이 "회전이 반대로 나온다"로 읽혔다.
        if (IsRotationDir(direction)) return false;

        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return false;

        Vector3 a = Vector3.ProjectOnPlane(neckVec0, axis);
        Vector3 b = Vector3.ProjectOnPlane(handMid - shoulderMid, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        signed = Vector3.SignedAngle(a, b, axis);
        degrees = Mathf.Abs(signed);
        return true;
    }

    /// <summary>
    /// 손 하나의 회전각. 축 둘레에서 <b>중립 반경벡터 → 지금 반경벡터</b>가 돈 각이다.
    /// ★두 손이 같은 축 둘레를 <b>같은 각만큼</b> 돌기 때문에 손 하나로도 성립한다.
    /// </summary>
    /// <param name="perp">반경벡터 중 축에 수직인 성분의 비율. 낮으면 이 파지로는 못 잰다.</param>
    private bool TryHandAngle(Vector3 rad0, Vector3 handNow, Vector3 axis,
                              out float signed, out float perp)
    {
        signed = 0f; perp = 0f;

        Vector3 a = Vector3.ProjectOnPlane(rad0, axis);
        Vector3 b = Vector3.ProjectOnPlane(handNow - AnglePivotNow, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        perp = a.magnitude / Mathf.Max(1e-6f, rad0.magnitude);
        signed = Vector3.SignedAngle(a, b, axis);
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    // ★★각 캐시 — 프레임당 한 번 계산하고, 스무딩과 '믿을 수 없음'을 여기서 정한다.
    //
    //   2026-09-04 지적: "오히려 더 확 튀어서 중립 지점으로 왔다갔다 난리였어."
    //   원인 셋을 여기서 한꺼번에 잡는다.
    //     ①<b>스무딩이 빠져 있었다</b>. 종전 경로는 vNow를 Lerp로 부드럽게 했는데
    //       반경 방식은 원값을 그대로 썼다 — 노이즈를 줄이려다 필터를 떼어 놓은 셈이었다.
    //     ②<b>딱딱한 탈락</b>이 계단을 만들었다. 반경 25%를 넘는 순간 그 손의 몫이
    //       0.25 → 0으로 뚝 떨어져 값이 뛰었다. → 오차에 따라 <b>서서히</b> 줄인다.
    //     ③<b>양손이 다 탈락하면 각을 버렸다</b>. 그러면 바늘이 중심으로 접혀
    //       <b>중립으로 보인다</b> — 그게 "중립을 왔다갔다"의 정체다.
    //       → 직전 값을 유지하고 '믿을 수 없음'만 표시한다. 단 <b>확정은 막는다.</b>
    // ════════════════════════════════════════════════════════════════════

    private float cachedSigned, cachedPerp;
    private bool cachedValid;

    /// <summary>
    /// 지금 단계에서 <b>무엇을 잡을 수 있는가</b>(2026-09-04). 브리지가 매 프레임 정한다.
    /// ★이걸 안 두면 정지가 이어지는 동안 어깨선 → 머리가 연달아 잡힌다.
    /// </summary>
    public enum CaptureTarget { Auto, Shoulders, Head, Neutral }

    private CaptureTarget captureTarget = CaptureTarget.Auto;

    // ── 중립 복귀 리프레시 · 최소 반경 (2026-09-04) ───────────────────────
    [Tooltip("중립으로 돌아오면 0점을 조용히 다시 잡는다. 그 허용 각(도). 0이면 안 한다. "
           + "★안전장치다 — 이 각을 넘으면 안 잡는다. 돌아가 있는 자세를 0으로 굳히면 "
           + "머리는 돌았는데 화면이 0도라고 말하게 된다(09-04에 실제로 밟았다).")]
    [SerializeField] private float neutralRefreshTolerance = 5f;

    [Tooltip("측정 평면에서의 반경이 이보다 짧으면 0점을 안 잡는다(m). "
           + "★잴 수 없는 자리에서 0점이 굳는 것을 막는다 — 09-04 로그의 "
           + "'파지폭 5cm · 면 성분 0.00'이 그 예다. 회전은 반경의 수평 성분만 남아 특히 짧다.")]
    [SerializeField] private float minMeasureRadius = 0.045f;

    private float refreshCooldown;

    /// <summary>브리지가 단계에 맞춰 알려 준다. 런타임 전용 — 직렬화를 안 타므로 씬 값에 안 진다.</summary>
    public void SetCaptureTarget(CaptureTarget t) => captureTarget = t;

    /// <summary>
    /// 지금 단계에서 <b>더 잡을 게 없는가</b>. 이때는 홀드 게이지를 안 돌린다 —
    /// 잡을 게 없는데 게이지가 차면 "다음 걸 재고 있다"로 보인다(2026-09-04 지적).
    /// </summary>
    private bool NothingToCaptureNow()
    {
        switch (captureTarget)
        {
            case CaptureTarget.Shoulders: return refReady;
            case CaptureTarget.Head:      return headReady;
            default:                      return false;   // 파지 0점은 언제든 다시 잡을 수 있다
        }
    }

    /// <summary>홀드 게이지를 화면에 그릴 상황인가. 잡을 게 없으면 게이지 자체를 숨긴다.</summary>
    public bool HoldGaugeActive => !(stage == Stage.AwaitNeutral && NothingToCaptureNow());

    /// <summary>재는 중에 파지가 풀렸는가. 0점은 유지하되 다시 잡을지는 사람이 정한다.</summary>
    private bool gripLost;

    /// <summary>지금 각이 <b>직전 값을 붙들고 있는</b> 상태인가. 이때는 확정하지 않는다.</summary>
    public bool AngleStale { get; private set; }

    /// <summary>진단용 — 이번 프레임에 각 손이 실제로 가진 몫(0~1).</summary>
    private float shareL, shareR;

    private void UpdateAngleCache(float dt)
    {
        if (!useRigidBodyMeasure) { cachedValid = false; AngleStale = false; return; }

        shareL = shareR = 0f;

        Vector3 axis = neutralReady ? AxisFor(direction) : Vector3.zero;
        if (axis.sqrMagnitude < 1e-8f) { cachedValid = false; AngleStale = false; return; }

        // ════════════════════════════════════════════════════════════
        // ★★<b>회전은 T라인 방향으로 잰다</b>(2026-09-04).
        //
        //   각 오차 = 손 떨림 ÷ 반경이다. 그런데 면마다 유효 반경이 다르다 —
        //   회전축은 <b>수직</b>이라 반경의 수평 성분만 남고, 그게 측두 간격의 절반(약 7.5cm)뿐이다.
        //   굴곡·측굴은 축원점에서 위로 12cm 이상이 그대로 반경이 된다.
        //   → 같은 1cm 떨림이 굴곡 4.8도 / 회전 7.6도로 번역된다. 회전만 나쁜 이유가 이것이다.
        //   ★파지폭이 5cm로 좁혀지면 수평 반경 2.5cm → 1cm 떨림이 <b>22도</b>가 된다.
        //     09-04 로그의 '면 성분 0.00'이 그 상태다.
        //
        //   T라인(두 엄지를 잇는 선)은 <b>축과 완전히 수직</b>이고 길이가 양손 간격 전체(15cm)다.
        //   회전에서는 반경 방식보다 2배 이상 유리하다.
        //   ★한 손만 남으면 T라인이 없다 → 반경 방식으로 떨어진다(그래야 한 손 측정이 산다).
        // ════════════════════════════════════════════════════════════
        if (IsRotationDir(direction) && leftOk && rightOk && len0 > 1e-4f)
        {
            Vector3 ta = Vector3.ProjectOnPlane(v0, axis);
            Vector3 tb = Vector3.ProjectOnPlane(acceptedRight - acceptedLeft, axis);
            if (ta.sqrMagnitude > 1e-8f && tb.sqrMagnitude > 1e-8f)
            {
                float rawT = Vector3.SignedAngle(ta, tb, axis);
                float perpT = ta.magnitude / Mathf.Max(1e-6f, v0.magnitude);

                shareL = shareR = 0.5f;   // T라인은 양손이 함께 만든다 — 몫을 나눌 수 없다
                if (!cachedValid || smoothing <= 0f) cachedSigned = rawT;
                else cachedSigned = Mathf.Lerp(cachedSigned, rawT,
                                               1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, smoothing)));
                cachedPerp = perpT;
                cachedValid = true;
                AngleStale = false;
                return;
            }
        }

        float aL = 0f, pL = 0f, aR = 0f, pR = 0f;
        float tL = 0f, tR = 0f;                       // 반경 신뢰도 0~1

        if (leftOk && TryHandAngle(radL0, acceptedLeft, axis, out aL, out pL))
            tL = RadiusTrust(acceptedLeft, radLen0L);
        if (rightOk && TryHandAngle(radR0, acceptedRight, axis, out aR, out pR))
            tR = RadiusTrust(acceptedRight, radLen0R);

        // ★방향 가중치 × 반경 신뢰도. 한쪽이 0이면 남은 손이 자동으로 100%가 된다 —
        //   따로 분기하지 않는다. 분기가 곧 계단이었다.
        float wL = HandWeight(true) * tL;
        float wR = HandWeight(false) * tR;
        float sum = wL + wR;

        if (sum < 1e-4f)
        {
            // ★믿을 손이 없다. 그래도 <b>각을 버리지 않는다</b> — 버리면 중립으로 보인다.
            AngleStale = cachedValid;
            return;
        }

        wL /= sum; wR /= sum;
        shareL = wL; shareR = wR;

        float raw = aL * wL + aR * wR;
        float perp = pL * wL + pR * wR;

        // ★스무딩 복원. 각은 1차원이라 축끼리 안 섞인다 — 벡터를 부드럽게 하는 것보다 안전하다.
        if (!cachedValid || smoothing <= 0f) cachedSigned = raw;
        else cachedSigned = Mathf.Lerp(cachedSigned, raw, 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, smoothing)));

        cachedPerp = perp;
        cachedValid = true;
        AngleStale = false;
    }

    /// <summary>
    /// 중립으로 돌아왔으면 0점을 <b>조용히</b> 다시 잡는다(2026-09-04 요청).
    ///
    /// 사용자: "중립으로 돌아왔을 때 게이지 안 기다리고 이전 중립점으로 돌아오면
    ///          초기화해서 0으로 깔끔하게 다시 스타트 할 수 있나?"
    ///
    /// ★★<b>각이 이미 0 근처일 때만</b> 한다. 그게 이 기능과 09-04에 밟은 사고를 가르는 선이다 —
    ///   돌아가 있는 자세에서 다시 잡으면 그 자세가 0도가 되어 <b>측정이 통째로 망가진다.</b>
    /// ★<b>능동을 이미 잡은 방향에서는 안 한다.</b> 기록된 값의 기준이 바뀌면 안 된다.
    /// ★게이지를 안 기다린다 — 각이 0 근처라 표시가 안 튄다. 쌓인 드리프트만 사라진다.
    /// </summary>
    private void TickNeutralRefresh(float dt, bool has, Vector3 l, Vector3 r)
    {
        if (neutralRefreshTolerance <= 0f || !useRigidBodyMeasure) return;
        if (!neutralReady || frozen || AngleStale || !has) return;

        int i = (int)direction;
        if (i <= 0 || i >= results.Length || results[i].hasActive) return;   // 재는 중이면 손대지 않는다

        refreshCooldown -= dt;
        if (refreshCooldown > 0f) return;

        if (Mathf.Abs(cachedSigned) > neutralRefreshTolerance) return;
        if (RadiusTrust(l, radLen0L) <= 0f || RadiusTrust(r, radLen0R) <= 0f) return;
        if (!IsGripPlausible(l, r, 0f, out _)) return;

        refreshCooldown = 0.5f;

        radL0 = l - AnglePivotNow;
        radR0 = r - AnglePivotNow;
        radLen0L = radL0.magnitude;
        radLen0R = radR0.magnitude;
        v0 = r - l; len0 = v0.magnitude;
        cachedSigned = 0f;
    }

    /// <summary>
    /// 반경이 얼마나 믿을 만한가(1 = 그대로, 0 = 버림). ★딱딱한 on/off가 아니라 <b>기울기</b>다.
    /// 허용치의 절반까지는 온전히 믿고, 거기서 허용치까지 선형으로 0이 된다.
    /// </summary>
    private float RadiusTrust(Vector3 handNow, float radLen0)
    {
        if (radiusDriftTolerance <= 0f || radLen0 < 1e-4f) return 1f;

        float err = Mathf.Abs((handNow - AnglePivotNow).magnitude - radLen0) / radLen0;
        float soft = radiusDriftTolerance * 0.5f;
        if (err <= soft) return 1f;
        if (err >= radiusDriftTolerance) return 0f;
        return 1f - (err - soft) / (radiusDriftTolerance - soft);
    }

    /// <summary>
    /// 이 손이 <b>강체 구속</b>을 지키고 있는가. 축원점에서의 거리가 중립과 크게 달라지면 틀린 값이다.
    /// ★속도 필터가 못 잡는 <b>느린 드리프트</b>를 여기서 잡는다(2026-09-04).
    /// </summary>
    private bool RadiusHolds(Vector3 handNow, float radLen0)
    {
        if (radiusDriftTolerance <= 0f || radLen0 < 1e-4f) return true;
        float now = (handNow - AnglePivotNow).magnitude;
        return Mathf.Abs(now - radLen0) / radLen0 <= radiusDriftTolerance;
    }

    /// <summary>
    /// 왼손의 몫(0~1). 오른손은 1에서 뺀 값이다.
    /// ★굴곡=뒤통수 손 · 신전=이마 손 · 우회전=오른손 · 좌회전=왼손 (2026-09-04 지시).
    ///   측굴은 좌우가 대칭이라 가중치를 안 준다.
    /// </summary>
    private float HandWeight(bool forLeft)
    {
        float w = Mathf.Clamp(primaryHandWeight, 0.5f, 1f);
        bool leftIsPrimary;

        switch (direction)
        {
            // 시상면 — 어느 손이 이마/뒤통수인지는 중립에서 판정해 뒀다.
            case CervicalRomDriver.Direction.Flexion:   leftIsPrimary = faceSideL < faceSideR; break;  // 뒤통수 손
            case CervicalRomDriver.Direction.Extension: leftIsPrimary = faceSideL > faceSideR; break;  // 이마 손
            case CervicalRomDriver.Direction.RotationRight: leftIsPrimary = false; break;              // 오른손
            case CervicalRomDriver.Direction.RotationLeft:  leftIsPrimary = true;  break;              // 왼손
            default: return 0.5f;                                                                      // 측굴 — 대칭
        }
        float leftShare = leftIsPrimary ? w : 1f - w;
        return forLeft ? leftShare : 1f - leftShare;
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
        headReady = false;   // ★머리도 세션마다 다시 잡는다 — 환자가 바뀌면 머리도 바뀐다
        acceptedValidL = acceptedValidR = false; rejectSecondsL = rejectSecondsR = 0f; lostSeconds = 0f;
        holdResets = 0; rejectedFrames = 0; trackingRelocks = 0; lostTotal = 0f;
        gripAnchorValid = false; angleExcursionSeconds = 0f;
        fixedGaugeAnchorValid = false;
        neckVec0Valid = false;
        rigidReady = false;
        gripLost = false;

        // ★★<b>방향도 놓는다</b>(2026-09-04 지적 — "절차 시작할 때 왜 좌회전 상태 대기인 거야").
        //   direction은 씬에 <b>6(좌회전)</b>이 직렬화돼 있다(TrainingScene:202495).
        //   코드 기본값은 Flexion이지만 씬 값이 이기고(규칙 7), ResetAll이 여기를 안 건드려서
        //   실측에 들어오는 순간 <b>늘 좌회전 대기</b>로 시작했다. 방향 목록의 '좌회전'이
        //   처음부터 주황(▶)으로 켜져 있던 이유가 이것이다.
        //   ★런타임 대입은 직렬화를 안 타므로 씬 값에 안 진다 — 인스펙터를 고칠 필요가 없다.
        //   ★첫 파지 단계에서 GripStepDirection이 제 방향을 넣어 준다(2026-09-02 신설).
        direction = CervicalRomDriver.Direction.None;

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

        // ★★<b>간격만 보지 않는다</b>(2026-09-04). 엄지 단독이라 간격 대역이 5~35cm로 넓은데도
        //   한 손이 순간 튀면 그 대역을 벗어나 "손을 뗐다"로 읽혔다.
        //   → <b>반경 구속</b>을 같이 본다. 축원점에서 두 손까지의 거리가 유지되고 있으면
        //     간격이 잠깐 벌어져도 <b>여전히 잡고 있는 것</b>이다. 튐과 진짜 놓음을 이걸로 가른다.
        bool spanOk = has && IsGripPlausible(l, r, releaseHysteresis, out _);
        bool radiiOk = useRigidBodyMeasure && has
                       && RadiusTrust(l, radLen0L) > 0f && RadiusTrust(r, radLen0R) > 0f;

        bool held = spanOk || radiiOk;
        releaseTimer = held ? 0f : releaseTimer + Time.deltaTime;
        if (releaseTimer < releaseGraceSeconds) return;

        releaseTimer = 0f;

        // ★★<b>재는 중인 방향에서는 0점을 안 버린다</b>(2026-09-04 지적).
        //   버리면 AwaitNeutral로 떨어지고, 잠깐 정지하는 순간 CaptureNeutral이 다시 돌아
        //   <b>지금 자세가 새 0점</b>이 된다 — 머리는 돌아가 있는데 화면은 0도라고 말한다.
        //   09-04 로그가 그 증거다: 우회전 능동 71.4도 직후 "파지가 풀렸습니다" → "0점 고정" → 압박 생략.
        //   사용자: "중립 지점으로 왔다갔다 확 튀니까 뭘 기준으로 잡은 건지 모르겠다."
        //   → 이미 능동을 잡은 방향이면 <b>0점을 유지</b>하고, 다시 잡을지는 사람이 [다시 측정]으로 정한다.
        int di = (int)direction;
        bool measuring = di > 0 && di < results.Length && results[di].hasActive;

        if (measuring)
        {
            gripLost = true;
            if (di > 0 && di < results.Length) results[di].gripReleases++;
            Mark("파지가 풀렸습니다 — 다시 잡으세요. <b>0점은 그대로 둡니다.</b> " +
                 "0점을 다시 잡으려면 [다시 측정]을 누르세요.");
            return;
        }

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
    // ★간결 표시에서는 A_2(실습 각도기를 실측으로 옮긴 것)도 끈다(2026-09-04 회의).
    //   usePracticeGauge는 씬에 1로 굳어 있어 코드 기본값으로는 못 끈다(규칙 7) → 여기서 덮는다.
    //   ★브리지가 이 값을 보고 각도기를 끼울지 말지 정하므로, 여기 하나만 막으면 A_2가 안 뜬다.
    public bool UsePracticeGauge => usePracticeGauge && !minimalDisplay;

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
        => minimalDisplay
            // ★간결 표시에서는 <b>S라인 중심</b>(양 어깨 중점)이 원점이다 — 십자선과 같은 자리다.
            //   2026-09-04 회의: 니들도 십자선도 어깨 중심 하나에서 나가야 한다.
            //   ★어깨를 안 잡았으면 니들 자체를 안 그린다(UpdateGauge의 refReady 조건) —
            //     파지 자리로 떨어뜨리면 "십자선이 파지 위치에 나온다"가 된다(09-04 지적).
            ? shoulderMid
            : (gripAnchorValid ? gripAnchor : pivot) - Vector3.up * realityGaugeDrop;

    /// <summary>간결 표시에서 니들 길이. 종전 모드에서는 각도기 반지름을 쓴다.</summary>
    private float NeedleLengthNow => minimalDisplay ? minimalNeedleLength : GaugeRadiusNow;

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
        // ★★<b>어깨 축 라인으로 되돌린다</b>(2026-09-04 지시).
        //   09-03에 A_2를 파지 지점으로 옮기고 B와 겹치지 않게 practiceGaugeRise(0.42m)만큼 올렸는데,
        //   그 0.42m이 <b>횡단면 원반을 한참 위로 띄웠다</b>. 횡단면은 수평 원반이라 위로 뜨면
        //   어깨선과 나란한 군더더기 선이 생긴다 — 09-02에 이미 지적받아 gaugeDrawRise=0으로 잡았던 병이
        //   0.42m으로 되살아난 것이다.
        //   사용자: "A_2가 내가 말한 어깨 축 라인과 동일한 위치로 안 나와. 횡단면이 한참 위에서 나와."
        //   → 어깨 축(shoulderMid)에 그대로 앉힌다. practiceGaugeRise는 <b>타지 않는다</b>.
        //   ★겹침 걱정은 없다 — B는 파지 지점에서 realityGaugeDrop만큼 내려가 있고, 여기는 어깨선이다.
        Vector3 anchor;
        if (practiceGaugeFixed && fixedGaugeAnchorValid) anchor = fixedGaugeAnchor;
        else if (gaugeAtGrip && gripAnchorValid) anchor = gripAnchor;
        else anchor = refReady ? shoulderMid : pivot;

        // ★A_2를 어깨 축 라인에 앉히려면 <b>인스펙터에서 practiceGaugeRise를 0</b>으로 둔다.
        //   (0.42는 B와 겹치지 말라고 넣은 값인데, 그게 횡단면 원반을 한참 위로 띄웠다.)
        float rise = gaugeDrawRise + (useRealityGauge ? practiceGaugeRise : 0f);

        // ★축(proxyTorso)은 세 각도기가 같다 — refFwd·refUp을 그대로 쓴다.
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
        // ★<b>손바닥</b>으로 잡는다(2026-09-04 회의). 자동 경로(UpdateHold)도 이 함수를 타므로
        //   여기 한 곳만 바꾸면 둘 다 바뀐다. 손바닥을 못 찾으면 종전 파지점으로 떨어진다.
        if (!TryGetShoulderHands(out Vector3 l, out Vector3 r)) { Warn("손을 못 찾았습니다."); return; }
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
        // ★★<b>어깨선은 바닥과 수평이다</b>(2026-09-04 지시).
        //   사용자: "어깨선 생성할 때 양손의 기울기 반영하지 말고, 이건 그냥 환자의 위치를
        //            나타내는 거니까 양손 높이 중앙값 위치에 바닥이랑 수평하게 선을 그어 줘."
        //   → 좌우축에서 <b>수직 성분을 뺀다</b>. 양손을 조금 높낮이 다르게 짚어도 축이 안 기운다.
        //   ★이건 그리기만의 문제가 아니다 — refRight가 기준틀의 좌우축이라
        //     여기를 수평으로 만들면 refFwd(= Cross(refRight, up))도 같이 안정된다.
        Vector3 span3 = operatorBehindPatient ? (r - l) : (l - r);
        Vector3 flatSpan = Vector3.ProjectOnPlane(span3, Vector3.up);
        refRight = (flatSpan.sqrMagnitude > 1e-6f ? flatSpan : span3).normalized;

        // 높이는 양손의 중앙값 — 위에서 (l+r)*0.5로 이미 그렇게 잡혀 있다.
        shoulderMid.y = (l.y + r.y) * 0.5f;

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

        // ★★<b>좌우축이 실제로 어디를 가리키는지 잰다</b>(2026-09-04 신설).
        //
        //   09-04에 회전만 반대로 나오는 것을 쫓다가 구조를 알았다 —
        //     시상면 축 = ±refRight        (좌우축에 <b>딸린다</b>)
        //     관상면 축 = ±Cross(refRight) (좌우축에 <b>딸린다</b>)
        //     횡단면 축 = ±월드 수직        (좌우축과 <b>무관하다</b>)
        //   그래서 좌우축이 뒤집힌 상태면 <b>시상·관상만</b> 뒤집히고 횡단면은 멀쩡하다.
        //   flip 셋을 다 켜면 횡단면 하나만 <b>과교정</b>되어 회전이 반대로 간다.
        //
        //   ★그러니 flip으로 메우기 전에 <b>좌우축이 맞는지</b>를 먼저 재야 한다.
        //     시술자가 환자 뒤에 서면 환자 오른쪽 ≈ 시술자(헤드셋) 오른쪽이다.
        //     마주 서면 반대다. 그 둘을 비교하면 지금 설정이 실제와 맞는지 바로 나온다.
        Camera scam = Camera.main;
        if (scam != null)
        {
            float dot = Vector3.Dot(refRight, scam.transform.right);
            bool matchesBehind = dot > 0f;
            bool expected = operatorBehindPatient;
            ChunaLogger.Log($"<color=cyan>[실측] 좌우축 점검 — refRight·헤드셋오른쪽 = {dot:F2} " +
                            $"({(matchesBehind ? "시술자가 뒤" : "마주 봄")}로 읽힘 / " +
                            $"설정은 {(expected ? "뒤" : "마주")})</color>");
            if (matchesBehind != expected)
                ChunaLogger.LogWarning("[실측] ★좌우축이 operatorBehindPatient 설정과 <b>반대</b>입니다 — " +
                                       "시상·관상면이 뒤집히고 횡단면만 멀쩡해집니다. " +
                                       "flip으로 메우지 말고 이 설정을 먼저 맞추세요.");
        }

        Mark($"어깨 기준 고정 - 어깨폭 {span * 100f:F0}cm. 이제 머리를 파지하세요.");
    }

    /// <summary>어깨 기준을 놓는다. 술기를 벗어나거나 다시 잡을 때.</summary>
    public void ClearReference() => refReady = false;

    /// <summary>
    /// <b>[다시 측정]</b> — 지금 단계에서 잡은 것을 놓고 다시 잡게 한다(2026-09-04 지시).
    ///
    /// ★어깨선·머리위치 <b>둘에만</b> 해당한다. 사용자: "이건 어깨선이랑 강체 위치 지정에만
    ///   해당하는 거야." 파지 0점은 종전대로 언제든 다시 잡힌다.
    /// ★<b>뒤에서부터</b> 놓는다 — 머리를 잡았으면 머리만, 아직이면 어깨선을.
    ///   그래야 "방금 잘못 잡은 것"이 풀린다. 둘 다 날리면 처음부터 다시 해야 한다.
    /// </summary>
    public void RetryCapture()
    {
        // ★재는 중에 파지가 풀렸으면 <b>0점부터</b> 놓아 준다(2026-09-04).
        //   이때는 어깨선·머리를 다시 잡을 게 아니라 파지 0점을 다시 잡는 상황이다.
        if (gripLost)
        {
            gripLost = false;
            neutralReady = false;
            frameReady = false;
            stage = Stage.AwaitNeutral;
            holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;
            frameStamp++;
            Mark("0점을 다시 잡습니다 - 중립에서 머리를 파지하고 정지하세요.");
            ChunaLogger.Log("<color=cyan>[실측] 다시 측정 — 0점을 놓았다.</color>");
            return;
        }

        if (headReady)
        {
            headReady = false;
            rigidReady = false;
            holdTimer = 0f;
            Mark("머리 위치를 다시 잡습니다 - 양손으로 머리 양 측면을 대고 정지하세요.");
            ChunaLogger.Log("<color=cyan>[실측] 다시 측정 — 머리 위치를 놓았다.</color>");
            return;
        }

        if (refReady)
        {
            refReady = false;
            holdTimer = 0f;
            Mark("어깨 기준선을 다시 잡습니다 - 양손을 양어깨에 올리고 정지하세요.");
            ChunaLogger.Log("<color=cyan>[실측] 다시 측정 — 어깨 기준선을 놓았다.</color>");
        }
    }

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
        // ★머리를 잡았으면 그 축원점을 쓴다(2026-09-04). 안 잡았으면 종전 추정으로 떨어진다.
        anglePivot = refReady ? shoulderMid + refUp * gaugePivotRise : (l + r) * 0.5f;
        radL0 = l - AnglePivotNow;
        radR0 = r - AnglePivotNow;
        radLen0L = radL0.magnitude;
        radLen0R = radR0.magnitude;

        // ★손이 이마 쪽인지 뒤통수 쪽인지 <b>중립에서</b> 판정한다. 시상면 가중치가 이걸 쓴다.
        //   기준점은 머리 중심(없으면 축원점). refFwd가 환자 앞이므로 양수면 이마다.
        Vector3 headRef = headReady ? headCenter : AnglePivotNow;
        faceSideL = Vector3.Dot(l - headRef, refFwd);
        faceSideR = Vector3.Dot(r - headRef, refFwd);
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
        gripLost = false;
        stage = Stage.Active;
        holdTimer = 0f; peakAngle = 0f; passiveBaseAngle = 0f;
        frameStamp++;

        // ★비교용 각의 0점 — 중립에서의 (T중심 − S중심). 어깨를 안 잡았으면 못 잰다.
        neckVec0 = gripAnchor - shoulderMid;
        neckVec0Valid = refReady && neckVec0.sqrMagnitude > 1e-6f;

        // ★★강체 기둥을 여기서 확정한다(2026-09-04). 밑동은 어깨선, 꼭대기는 파지점보다 살짝 위,
        //   굵기는 지금 파지 간격 — 전부 <b>잡는 순간</b>의 값으로 못 박는다.
        //   이후 기둥은 축 둘레로 돌기만 하고 크기는 안 변한다. 그래야 기준이 된다.
        // ★기둥은 <b>머리 위치 단계</b>에서 수직으로 세워 뒀다(CaptureHead). 여기서 다시 만들지 않는다 —
        //   파지 중점으로 만들면 머리가 어깨 앞에 있어 <b>중립부터 기울어진</b> 기둥이 나온다.
        //   사용자: "강체는 최초에는 수직으로 뻗어야 해."
        //   ★머리 단계를 안 쓰는 설정이면 종전대로 여기서 만든다(되돌릴 자리를 남긴다).
        if (!requireHeadCapture && refReady)
        {
            rigidBase0 = shoulderMid;
            rigidTop0 = shoulderMid + Vector3.up * Mathf.Max(0.05f,
                            (gripAnchor.y + rigidTopRise) - shoulderMid.y);   // ★수직으로 세운다
            rigidRadius = Mathf.Max(0.02f, len0 * 0.5f * Mathf.Max(0.1f, rigidWidthScale));
            rigidReady = true;
        }

        // ★★<b>잴 수 없는 자리에서는 0점을 안 굳힌다</b>(2026-09-04).
        //   09-04 로그: '파지폭 5cm · 면 성분 0.00'으로 0점이 잡히고 그 방향이 통째로 죽었다.
        //   각 오차 = 손 떨림 ÷ 반경이라, 측정 평면에서의 반경이 짧으면 1cm 떨림이 20도가 넘는다.
        //   ★회전이 특히 위험하다 — 축이 수직이라 반경의 <b>수평 성분만</b> 남는다.
        //   → 반경이 최소치에 못 미치면 <b>0점을 세우지 않고</b> 다시 잡게 한다.
        Vector3 nAxis = AxisFor(direction);
        if (minMeasureRadius > 0f && nAxis.sqrMagnitude > 1e-8f)
        {
            float rL = Vector3.ProjectOnPlane(radL0, nAxis).magnitude;
            float rR = Vector3.ProjectOnPlane(radR0, nAxis).magnitude;
            float rBest = Mathf.Max(rL, rR);
            if (rBest < minMeasureRadius)
            {
                neutralReady = false;
                frameReady = false;
                stage = Stage.AwaitNeutral;
                holdTimer = 0f;
                Warn($"이 자리로는 {Label(direction)}을(를) 못 잽니다 — 측정 반경 {rBest * 100f:F1}cm " +
                     $"(최소 {minMeasureRadius * 100f:F1}cm). 양손을 머리 양옆으로 더 벌려 잡으세요.");
                return;
            }
        }

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
        PlayMeasureDone("능동");

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
        PlayMeasureDone("수동");

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

        // ★★<b>안내문이 따라올 자리</b>를 매 프레임 잡아 둔다(2026-09-04 지시).
        //   종전에는 gripAnchor를 썼는데, 그건 <b>0점을 잡은 순간에 얼어붙는다</b>
        //   (아래 `if (has && !neutralReady)` 안에서만 갱신된다).
        //   그래서 신전처럼 손이 크게 움직이는 방향에서는 안내문이 중립 자리에 남아 시야 밖으로 나갔다.
        //   사용자: "신전할 때 아예 안 보여."
        //   ★양손을 잡고 있으면 <b>두 손을 잇는 선의 가운데</b>, 한 손만 읽히면 그 손을 쓴다.
        //   사용자: "중립 파지 안 할 때는 오른손 위에 두더라도, 양손 잡을 땐 직선의 가운데 기준으로 살짝 위."
        if (has) { handMid = (l + r) * 0.5f; handMidValid = true; }
        else if (rightOk) { handMid = r; handMidValid = true; }
        else if (leftOk) { handMid = l; handMidValid = true; }
        else handMidValid = false;

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
        // ★★강체 방식에서 <b>한 손은 예외가 아니라 정식 경로</b>다(2026-09-04).
        //   09-04 로그: 왼손이 튀는 동안 우회전이 기록이 안 됐다. 각 계산은 한 손을 받는데
        //   <b>기록 게이트만 4초로 끊고</b> 있었다 — 반쪽만 바꿔 놓은 상태였다.
        //   두 손이 같은 축 둘레를 같은 각만큼 도니, 하나만 살아도 값이 성립한다.
        bool singleOk = singleHandFallback && !has && anyHand && neutralReady
                        && (useRigidBodyMeasure || singleHandSeconds <= singleHandMaxSeconds);
        bool usable = has || singleOk;

        UpdateAngleCache(frameDt);   // ★각은 프레임당 한 번 계산한다(스무딩·신뢰도 포함)
        TickNeutralRefresh(frameDt, has, l, r);

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

        // ★★<b>확정 직후의 텀</b>(2026-09-04 요청 — "측정됐으면 텀이라도 있든가").
        //   이 동안에는 게이지를 안 센다. 소리가 난 뒤 게이지가 곧장 다시 차면
        //   방금 것이 잡힌 건지 다음 걸 재는 건지 구분이 안 된다.
        if (donePauseLeft > 0f)
        {
            donePauseLeft -= dt;
            holdTimer = 0f;
            return;
        }

        // ★★<b>이 단계에서 잡을 게 없으면 게이지를 안 돌린다</b>(2026-09-04 지적).
        //   사용자: "난 누르지도 않았고 설정 끝나니까 바로 눈앞에서 머리 중립 게이지 올라가던데."
        //   어깨선이 잡혀도 stage는 AwaitNeutral 그대로라 홀드 타이머가 계속 돌았다.
        //   잡는 것은 captureTarget이 막고 있었지만 <b>게이지는 그대로 차올랐고</b>,
        //   그게 "머리를 재고 있다"로 보였다. 게다가 단계가 넘어가는 순간
        //   이미 가득 찬 타이머로 <b>즉시</b> 잡혔을 것이다.
        if (stage == Stage.AwaitNeutral && NothingToCaptureNow())
        {
            holdTimer = 0f;
            return;
        }

        // ★★<b>직전 값을 붙들고 있는 동안에는 확정하지 않는다</b>(2026-09-04).
        //   각을 안 버리는 것은 <b>바늘이 중립으로 접히지 않게</b> 하려는 것이지,
        //   그 값으로 기록하려는 게 아니다. 0으로 죽이지 않고 깎기만 한다.
        if (AngleStale)
        {
            holdTimer = Mathf.Max(0f, holdTimer - dt * holdDecayRate);
            return;
        }

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
                // ★같은 '정지'가 순서대로 세 가지를 잡는다(2026-09-04 순서 변경).
                //     ①어깨선 → ②머리 위치 → ③파지 0점
                //   순서가 하나뿐이라 헷갈릴 여지가 없다.
                // ★★<b>단계마다 잡을 수 있는 것은 하나뿐이다</b>(2026-09-04 재수정).
                //
                //   09-04 1차 수정에서 <b>단계 진행</b>만 막고 <b>잡는 것</b>은 안 막았다.
                //   그러면 정지가 이어지는 동안 같은 자세에서 어깨선 → 머리가 <b>연달아</b> 잡힌다 —
                //   사용자: "다음 버튼으로 안내만 넘어가면 뭐해."
                //   → 브리지가 지금 단계에서 <b>무엇을 잡을 수 있는지</b> 알려 주고, 그것만 잡는다.
                //     [다음]을 눌러 단계가 바뀌어야 다음 것을 잡을 수 있다.
                switch (captureTarget)
                {
                    case CaptureTarget.Shoulders:
                        if (requireReference && !refReady) CaptureShoulders();
                        break;
                    case CaptureTarget.Head:
                        if (requireHeadCapture && !headReady) CaptureHead();
                        break;
                    case CaptureTarget.Neutral:
                        CaptureNeutral();
                        break;
                    default:
                        // Auto — 브리지가 안 알려 준 경우(에디터 단독 테스트 등)는 종전 순서대로.
                        if (requireReference && !refReady) CaptureShoulders();
                        else if (requireHeadCapture && !headReady) CaptureHead();
                        else CaptureNeutral();
                        break;
                }
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

    /// <summary>
    /// 이 측정기가 <b>지금 화면을 그리고 있는가</b>. 실습 표시(<see cref="CervicalRomPracticeReadout"/>)가
    /// 겹치지 않으려고 묻는다.
    ///
    /// ★★<b>2026-09-07 — 이 창을 낸 이유.</b> 실습 표시가 모드를 <c>enabled</c>로 <b>간접 추론</b>했다가
    ///   틀렸다. 이 컴포넌트는 <b>씬에 m_Enabled: 1로 굳어 있어</b>(TrainingScene) 실습에서도 켜져 있다.
    ///   브리지의 <c>enabled = false</c>는 <b>런타임에 새로 붙일 때만</b> 도는 코드라 한 번도 안 돈다.
    ///   → 그리는지 마는지는 <b>그리는 쪽이 대답한다.</b> 따로 계산하면 또 어긋난다
    ///     (규칙 9의 "미리보기는 실제와 같은 함수를 타야 한다"와 같은 형태다).
    ///   ★<c>Update</c>의 두 갈래(미리보기 · 실측모드)와 <b>같은 조건</b>을 쓴다. 여기만 고치면 안 된다 —
    ///     저 두 갈래가 바뀌면 이것도 같이 바꾼다.
    /// </summary>
    public bool IsDrawing => PreviewActive || IsMeasurementMode();

    /// <summary>
    /// ★<b>어깨를 짚을 때 쓸 손 위치</b>(2026-09-04 회의 지시).
    ///
    /// 어깨 짚기는 <b>손바닥을 얹는</b> 동작인데 종전에는 엄지·검지 파지점을 썼다.
    /// 엄지 끝은 손바닥에서 8~10cm 떨어져 있어 S라인(어깨선)이 그만큼 어긋난다.
    /// ★파지·각도 측정은 <b>안 바꾼다</b> — 그쪽은 엄지가 맞다(2026-09-02 확정).
    ///   여기만 손바닥으로 가른다.
    /// ★손바닥을 못 찾으면 <b>종전대로</b> 파지점으로 떨어진다. 조용히 죽지 않게 한다.
    /// </summary>
    private bool TryGetShoulderHands(out Vector3 left, out Vector3 right)
    {
        left = Vector3.zero; right = Vector3.zero;

        if (leftHandOverride != null && rightHandOverride != null)
        {
            left = leftHandOverride.position;
            right = rightHandOverride.position;
            return true;
        }

        if (usesPalmForShoulders && gripJudge != null
            && gripJudge.TryGetPalmPoint(GripFingerTip.Side.Left, out Vector3 pl)
            && gripJudge.TryGetPalmPoint(GripFingerTip.Side.Right, out Vector3 pr))
        {
            left = pl; right = pr;
            return true;
        }

        return TryGetHands(out left, out right);
    }

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

        // ★★<b>어깨선·머리 위치를 잡는 즉시 3축선을 띄운다</b>(2026-09-08 지시:
        //   "강체 위치는 사실상 안 중요하고 어깨선 기준으로 머리와의 3축선이 중요하잖아.
        //    그거만 어깨선·머리위치 잡았을 때 바로 나오게").
        //   ★<c>frameReady</c>는 <b>양손 파지 틀</b>이라(`:1612`·`:1641`) 그것만 보면 파지를 기다린다 —
        //     강체 기둥에서 이미 같은 실수를 했다. 여기서는 처음부터 <c>refReady</c>를 같이 본다.
        //   ★<see cref="UpdateAxisLines"/>는 이미 <c>refReady ? shoulderMid : pivot</c>으로
        //     그릴 자리를 스스로 고르고, 어깨선이 없으면 <c>minimalDisplay</c>에서 아무것도 안 그린다.
        //     즉 <b>일찍 불러도 안전하다</b>.
        // ★★<b>어깨를 잡는 순간부터 그린다</b>(2026-09-08 최종).
        //   ★<b>왜 그게 맞나</b>(사용자 판단, 동의함): 3축선은 <b>잡는 것을 검사하는 자</b>다.
        //     "3축선을 그리면 내가 제대로 목이 중앙에 오게 잡았는지 어느 한쪽으로 기울었는지
        //      눈으로 판단하고 다시 측정한다. 머리 위치 잡을 때도 목 수직선에 머리를 정렬시키고
        //      잡은 건지 그냥 기울었는데 잡은 건지 판단할 수 있다."
        //   ★근거가 코드에도 있다 — 축 <b>방향</b>(refRight·refUp·refFwd)은 CaptureShoulders에서
        //     전부 정해진다(`:2185~2214`). 머리는 <b>원점만</b> 정한다. 즉 어깨를 잡은 순간
        //     방향 정보는 이미 다 있고, <b>확정된 뒤에 보여 주면 고칠 기회가 없다</b>.
        //   ※한때 AND(머리까지)로 넣었다가 되돌렸다. 이유가 바뀐 게 아니라 용도가 갈렸다 —
        //     3축선은 '확정 결과'가 아니라 '잡는 중에 보는 눈금'이다.
        if (showAxes && refReady) UpdateAxisLines();
        // ★중심선은 0점(frameReady)과 무관하다 — 어깨를 짚은 순간부터 측정 내내 떠 있다.
        UpdateMidline();
        UpdateRigidBody();      // ★강체 기둥 + 코끝선(2026-09-04)
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

        // ★비교용 각(T-S 중심 연결선). 테스트 표시라 dedup 서명에도 넣어야 갱신된다 —
        //   안 넣으면 본 각이 안 바뀌는 동안 비교값이 얼어붙어 "안 움직인다"로 오해한다.
        // ★단락 평가로 묶으면 out 변수가 '확실히 할당됨'을 못 넘긴다(CS0165) — 호출을 먼저 한다.
        bool compareOk = TryGetNeckLineAngle(out float compareDeg, out float compareSigned);
        bool hasCompare = showNeckLineCompare && compareOk;
        int compareShown = hasCompare
            ? Mathf.RoundToInt(requireForwardDirection ? compareSigned : compareDeg)
            : int.MinValue + 1;

        int warnSig = HasFreshWarn ? (lastWarn != null ? lastWarn.GetHashCode() : 1) : 0;
        if (stage == shownStage && shown == shownAngle && warn == shownWarn
            && holdStep == shownHold && mask == shownMask && gripSig == shownGrip
            && warnSig == shownWarnSig && compareShown == shownCompare
            && InDonePause == shownDonePause) return;
        shownDonePause = InDonePause;
        shownWarnSig = warnSig;
        shownStage = stage; shownAngle = shown; shownWarn = warn;
        shownHold = holdStep; shownMask = mask; shownGrip = gripSig;
        shownCompare = compareShown;

        sb.Clear();

        // ★평가에서는 <b>절차 사슬(중립 ▶ 능동 ▶ 압박)을 숨긴다</b>(2026-09-03 사용자 지시).
        //   다음에 뭘 해야 하는지를 그대로 알려 주는 줄이라, 평가에서는 힌트다.
        //   ★홀드 게이지·방향 목록은 남긴다 — 그건 절차가 아니라 <b>지금 먹히고 있나</b>를 보는 것이다.
        //     정지로만 넘어가는 구조라 게이지가 없으면 왜 안 넘어가는지 알 수가 없다.
        if (showProgress && !HideHints && showSecondaryInfo)
        {
            sb.Append("<size=70%>");
            AppendStageChain();
            sb.Append("</size>\n");
        }

        switch (stage)
        {
            case Stage.AwaitNeutral:
                // ★★안내문은 <b>지금 단계에서 잡을 수 있는 것</b>을 말한다(2026-09-04 재수정).
                //   종전에는 '무엇이 잡혔나'를 보고 있어서, 어깨선이 잡히는 순간 글자가
                //   머리 단계 문구로 바뀌었다 — 단계는 그대로인데 <b>넘어간 것처럼 보였다.</b>
                //   사용자: "아직도 어깨선 설정하고 바로 머리 중립 체크로 넘어가네."
                switch (captureTarget)
                {
                    case CaptureTarget.Shoulders:
                        sb.Append(refReady
                            ? "어깨 기준선 완료 - [다음]을 누르세요"
                            : "양손을 환자 양어깨에 - 정지");
                        break;
                    case CaptureTarget.Head:
                        sb.Append(headReady
                            ? "머리 위치 완료 - [다음]을 누르세요"
                            : "양손으로 머리 양 측면을 - 정지");
                        break;
                    default:
                        sb.Append(requireReference && !refReady
                            ? "양손을 환자 양어깨에 - 정지"
                            : requireHeadCapture && !headReady
                                ? "양손으로 머리 양 측면을 - 정지"
                                : "중립에서 머리를 파지 - 정지");
                        break;
                }
                // ★실측치를 같이 띄운다. 임계를 맞추려면 실제 숫자를 봐야 한다(2026-08-31).
                //   ★2026-09-08부터 기본으로 접는다 — 정보창은 각도와 유지 게이지만 남긴다.
                //     ★단계 안내문(바로 위)은 <b>접지 않는다.</b> 그건 각도가 아니라 지금 할 일이고,
                //       같이 접으면 준비 단계에서 화면이 통째로 빈다(09-07에 실제로 겪었다).
                if (showSecondaryInfo) AppendGripNumbers();
                break;
            default:
                sb.Append(Label(direction));
                sb.Append("  ");
                if (measurable) { sb.Append(shown); sb.Append('도'); } else sb.Append("--");

                // ★확정 직후에는 그렇게 적는다. 게이지가 멈춘 이유가 보여야 한다(2026-09-04).
                if (InDonePause) sb.Append("  <color=#7ad67a>✔ 기록됨</color>");

                // ★★<b>테스트용 비교값</b>(2026-09-04 요청). 위는 T라인 회전각,
                //   아래는 T중심-S중심 연결선의 각이다. 둘을 나란히 놓고 눈으로 고른다.
                //   ★회전 두 방향에서 아래가 0 근처면 "연결선으로는 회전을 못 잰다"가 확인된 것이다.
                //   ★비교값은 측정·채점·CSV에 <b>안 들어간다.</b> 화면에만 뜬다.
                // ★<b>곁가지 스위치에 묶는다</b>(2026-09-08 지적: "내가 분명 테스트 관련 정보 끄라고 했고").
                //   ★씬에 <c>showNeckLineCompare: 1</c>이 굳어 있어(TrainingScene:183057)
                //     그 필드만으로는 코드에서 못 끈다(규칙 7). 곁가지 스위치를 <b>같이</b> 봐야 꺼진다.
                if (showNeckLineCompare && showSecondaryInfo)
                {
                    // ★★좌우 손이 <b>따로</b> 낸 각을 나란히 띄운다(2026-09-04).
                    //   둘이 크게 다르면 강체 전제나 축원점이 틀린 것이고,
                    //   거의 같으면 중심 높이만 맞추면 되는 문제다. 그걸 가르는 유일한 자다.
                    sb.Append("\n<size=60%><color=#8fb8ff>[테스트] ");
                    Vector3 dbgAxis = AxisFor(direction);
                    if (dbgAxis.sqrMagnitude > 1e-8f)
                    {
                        if (leftOk && TryHandAngle(radL0, acceptedLeft, dbgAxis, out float dL, out _))
                            sb.Append($"L {dL:F0}");
                        else sb.Append("L --");
                        sb.Append(" / ");
                        if (rightOk && TryHandAngle(radR0, acceptedRight, dbgAxis, out float dR, out _))
                            sb.Append($"R {dR:F0}");
                        else sb.Append("R --");
                        sb.Append("  ");
                    }
                    // ★지금 <b>어느 손을 얼마나</b> 쓰고 있는지. 09-04 지적:
                    //   "왔다갔다 확 튀니까 이게 오른손을 기준으로 잡은 건지 뭔지 모르겠다."
                    sb.Append($"몫 {shareL * 100f:F0}:{shareR * 100f:F0}  T-S선 ");
                    if (hasCompare) { sb.Append(compareShown); sb.Append('도'); }
                    else sb.Append("--");
                    if (AngleStale) sb.Append(" <color=#ff8a65>믿을 수 없음</color>");
                    sb.Append("</color></size>");
                }
                break;
        }

        // ★평가에서는 능동·수동 숫자를 <b>숨긴다</b>(2026-09-03 사용자 지시 — 힌트 최소화).
        //   ★삭제가 아니다. results에는 그대로 쌓이고 _rom.csv·결과지에도 그대로 나간다.
        //     화면에서만 안 보이게 하는 것이다.
        if (neutralReady && !HideHints)
        {
            Result res = results[(int)direction];

            // ★능동·수동·차이 줄은 2026-09-08부터 기본으로 접는다(사용자 지시:
            //   "정보는 현재각도 유지 게이지만 남기고 비활성화").
            //   ★삭제가 아니다 — results·_rom.csv·결과지에는 그대로 간다. 화면에서만 안 보인다.
            bool wroteNumbers = false;
            if (showSecondaryInfo)
            {
                sb.Append('\n');
                sb.Append(res.hasActive ? $"능동 {Mathf.Abs(res.active):F0}도" : "능동 -");
                sb.Append(res.hasPassive ? $"   수동 {Mathf.Abs(res.passive):F0}도" : "   수동 -");
                if (res.hasActive && res.hasPassive)
                    sb.Append($"   차이 {Mathf.Abs(res.passive) - Mathf.Abs(res.active):F0}도");
                wroteNumbers = true;
            }

            // ★압박이 아직 게인을 못 채웠으면 얼마나 더 가야 하는지 알려준다.
            //   안 그러면 "멈췄는데 왜 안 넘어가지"로 보인다 — 정지로만 진행하는 구조라 치명적이다.
            //   ★<b>이 줄은 접지 않는다</b>(2026-09-08 "경고는 남긴다"). 위 숫자를 접었으면
            //     붙일 자리가 없으므로 <b>줄을 바꿔서</b> 쓴다 — 안 그러면 각도 뒤에 들러붙는다.
            if (stage == Stage.Passive && !res.hasPassive)
            {
                float need = passiveBaseAngle + minPassiveGain - peakAngle;
                if (need > 0.5f)
                    sb.Append(wroteNumbers
                        ? $"   <color=#ffcc55>{need:F0}도 더</color>"
                        : $"\n<color=#ffcc55>{need:F0}도 더</color>");
            }
        }

        if (warn == 2) sb.Append($"\n<color=#ff6b6b>파지 미끄러짐 {(slipRatio - 1f) * 100f:+0;-0}%</color>");
        else if (warn == 1) sb.Append($"\n<color=#ffcc55>이 파지로는 못 잽니다 (면 {perp:F2})</color>");

        // ★확정에 실패한 이유를 그대로 띄운다. 몇 초 뒤 사라진다.
        if (HasFreshWarn) sb.Append($"\n<size=70%><color=#ff8a65>{lastWarn}</color></size>");

        // ★잡을 게 없으면 게이지를 아예 안 그린다(2026-09-04). 빈 게이지가 남아 있어도
        //   "무언가 재는 중"으로 보인다 — 타이머만 멈추는 것으로는 부족하다.
        if (showProgress && HoldGaugeActive)
        {
            sb.Append("\n<size=70%>");
            AppendHoldBar(holdStep);
            // ★방향 목록(●굴곡 ▶신전 ○…)은 2026-09-08부터 기본으로 접는다.
            //   ★유지 게이지는 남는다 — 그게 "지금 먹히고 있나"를 보는 유일한 자다(09-03 판단 그대로).
            if (showSecondaryInfo)
            {
                sb.Append('\n');
                AppendDirectionRow();
            }
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
        // ★★<b>다 재고 나면 정보창도 접는다</b>(2026-09-08 지시: "과정 다 끝나면 정보 표시 끄고").
        //   각도기·강체는 이미 <c>gaugeForceHidden</c>으로 접혔는데 <b>글자만 남아 있었다</b> —
        //   결과를 읽는 자리에 재는 화면이 겹쳐 보였다. 같은 스위치에 묶는다.
        if (!showReadout || gaugeForceHidden)
        {
            if (readout.gameObject.activeSelf) readout.gameObject.SetActive(false);
            return;
        }
        if (!readout.gameObject.activeSelf) readout.gameObject.SetActive(true);

        readout.text = ReadoutText;
        FitInfoBackdrop();   // 글이 바뀌었으니 판도 다시 맞춘다 (2026-09-08)

        // ★★<b>손 옆에 띄운다</b>(2026-09-03). 종전에는 pivot을 썼는데, 어깨를 짚고 나면
        //   pivot이 shoulderMid로 옮겨간다(CaptureShoulders). 그래서 안내문이 <b>어깨 높이로 내려가</b>
        //   "손 쪽에 따라오던 정보가 안 보인다"가 됐다. 각도기가 겪던 것과 같은 병이다.
        //   → 각도기와 같은 앵커(파지 지점)를 쓴다. 0점 전에는 손을 따라오고, 0점 뒤에는 고정된다.
        //   ★2026-09-04 정정: gripAnchor는 <b>0점에서 얼어붙는다</b>. 그 바람에 신전에서 안내문이
        //     중립 자리에 남아 안 보였다. → 손을 따라오는 handMid를 먼저 쓴다(양손이면 가운데).
        //
        // ★★2026-09-07 사용자 지시 — <b>강체가 잡히면 강체에 붙는다.</b>
        //   손을 따라오게 해 뒀더니 핸드트래킹 떨림이 그대로 글씨 떨림이 됐다.
        //   강체는 어깨선·머리위치로 <b>사람이 직접 잡은</b> 기준이라 흔들리지 않는다.
        //   ★자리는 강체에 완전 종속이고(머리가 숙으면 글자도 같이 넘어간다),
        //     <b>글자가 보는 방향만</b> 시술자를 따라 돈다(아래 FaceCamera). 안 그러면
        //     회전 90도에서 글이 뒤집혀 못 읽는다.
        //   ★강체 전(어깨선·머리위치 단계)에는 종전대로 손을 따라간다 — 손을 보며 하는 작업이다.
        //   ★새 스위치를 만들지 않았다. 켜고 끄는 값을 하나 더 만들면 씬에 굳는다(09-03 사고).
        //     되돌릴 일이 생기면 이 분기를 지운다.
        if (TryGetRigidAnchor(ReadoutRiseNow, out Vector3 rigidReadout, out Vector3 rigidUp))
        {
            // ★★★2026-09-07 사용자 지시 — 강체에 붙은 자리는 <b>사용자와의 거리를 재지 않는다.</b>
            //   "그냥 축에 따라가게 하고 방향만 사용자가 볼 수 있게 회전시켜."
            //   그래서 당김·최소거리 밀어내기를 <b>안 태운다</b>(clampToViewer: false).
            PlaceReadout(rigidReadout, rigidUp, clampToViewer: false);
            return;
        }

        Vector3 readoutBase = readoutFollowHands && handMidValid
            ? handMid
            : (gripAnchorValid ? gripAnchor : pivot);

        // ★손 바로 위라 눈에서 40cm쯤 떨어지는데, VR에서 그 거리는 초점이 안 맞아 흐리다.
        //   최소 거리를 두고 밀어낸다(2026-08-31 사용자: '가까워서 흐린가 글씨가 안 보였다').
        //
        // ★★2026-09-04 지적: "난 분명 손을 바라보는데 진행정보텍스트는 저 위쪽으로 가 있어서 안 보인다."
        //   ★손잡이는 <b>둘 다 이미 있다</b> — 오버라이드 필드를 새로 파지 않고 인스펙터에서 맞춘다.
        //     readoutRise : 올리는 높이(m).
        //     readoutMinDistance  : 이보다 가까우면 시선 방향으로 밀어낸다(m). 0.55는 멀다.
        // 강체 전(어깨선·머리위치)에는 기울일 강체가 없다 — 월드 수직으로 세운다.
        // ★이 경로는 손을 따라가던 <b>종전 방식 그대로</b>다. 거기서는 최소거리가 여전히 쓸모 있다.
        PlaceReadout(readoutBase + Vector3.up * ReadoutRiseNow, Vector3.up, clampToViewer: true);
    }

    /// <summary>
    /// 안내문을 그 자리에 놓고 시술자를 보게 돌린다.
    /// ★강체에 붙일 때와 손을 따라갈 때가 <b>같은 함수를 타야 한다</b> — 최소 거리 밀어내기와
    ///   글자 방향이 두 경로에서 달라지면 "왜 여기선 안 밀려나지"가 된다(규칙 9).
    /// </summary>
    private void PlaceReadout(Vector3 readoutPos, Vector3 up, bool clampToViewer)
    {
        Camera rcam = Camera.main;
        if (rcam != null && clampToViewer)
        {
            // ★★환자 머리에 가려서 시술자 쪽으로 당겨 온다(2026-09-07 지적).
            //   ★<b>수평으로만</b> 당긴다. 시선 방향으로 당기면 카메라가 위에 있어
            //     당길수록 아래로 내려와, 올린 만큼이 도로 깎인다.
            // ★★<b>2026-09-08 — 어깨·머리 잡을 때 글자가 홱 날아가던 것</b>.
            //   이 두 보정은 <b>수평 방향</b>을 쓰는데, 손을 얼굴 가까이 가져가면
            //   그 수평 성분이 몇 cm로 줄고 <b>방향이 프레임마다 뒤집힌다</b>.
            //   1mm 가드(1e-3)로는 못 막는다 — 3cm에서도 홱홱 돈다.
            //   → 수평거리가 <c>minFlatForDirection</c>보다 짧으면 <b>카메라의 수평 전방</b>을 쓴다.
            //     그건 손 위치와 무관하게 안정적이라, 글자가 튀지 않는다.
            Vector3 camFlatFwd = rcam.transform.forward;
            camFlatFwd.y = 0f;
            camFlatFwd = camFlatFwd.sqrMagnitude > 1e-6f ? camFlatFwd.normalized : Vector3.forward;

            if (readoutPullToViewer > 0f)
            {
                Vector3 flat = rcam.transform.position - readoutPos;
                flat.y = 0f;
                Vector3 dir = flat.magnitude >= minFlatForDirection ? flat.normalized : -camFlatFwd;
                readoutPos += dir * readoutPullToViewer;
            }

            // 너무 가까우면 초점이 안 맞아 흐리다(08-31) → 밀어낸다.
            // ★★<b>2026-09-07 — 여기가 "고개를 들면 정보가 내려간다"의 범인이었다.</b>
            //   종전 <c>cam.position + 방향 * 최소거리</c>는 글자를 <b>카메라 기준 구면</b>에 붙인다.
            //   그 거리 안으로 들어오면 위치가 통째로 사용자 머리를 따라간다.
            //   → <b>수평으로만</b> 민다. 높이는 강체가 준 값 그대로 둔다.
            Vector3 away = readoutPos - rcam.transform.position;
            away.y = 0f;
            float flatDist = away.magnitude;
            if (flatDist < readoutMinDistance)
            {
                // ★방향이 못 미더우면(너무 가까움) 카메라 전방으로 민다. 뒤로 밀지 않는다.
                Vector3 dir = flatDist >= minFlatForDirection ? away / flatDist : camFlatFwd;
                readoutPos += dir * (readoutMinDistance - flatDist);
            }
        }
        readout.transform.position = readoutPos;

        // ★★좌우는 시술자를 향하고, 기울기는 강체를 따른다(2026-09-07 사용자 지시).
        //   ★시선과 up이 거의 나란해지면 LookRotation이 무너진다 — 그때만 월드 수직으로 돌아간다.
        if (rcam != null)
        {
            Vector3 fwd = readout.transform.position - rcam.transform.position;

            // ★★겨눔도 같은 병을 앓는다 — 글자가 눈앞에 오면 <c>fwd</c>가 짧아져
            //   <c>LookRotation</c>이 프레임마다 홱 돈다("엉뚱한 방향으로 회전").
            //   ★1e-8 가드는 <b>0인 경우</b>만 막는다. 짧지만 0이 아닌 경우가 문제였다.
            if (fwd.magnitude < minFlatForDirection)
            {
                Vector3 f = rcam.transform.forward; f.y = 0f;
                fwd = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            }

            if (fwd.sqrMagnitude > 1e-8f)
            {
                if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(fwd.normalized, up.normalized)) > 0.99f) up = Vector3.up;
                readout.transform.rotation = Quaternion.LookRotation(fwd, up);
            }
        }
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

            // ★주황 ▶는 "<b>지금 재는 중</b>"이라는 뜻이다. 다 재고 나면(결과 단계 = frozen)
            //   재는 방향이 없으므로 켜 두면 안 된다 —
            //   2026-09-04 지적: "끝나고도 좌회전 텍스트 주황색으로 활성화 되어 있어."
            //   마지막으로 잰 방향이 direction에 그대로 남아 ▶ 분기를 계속 타고 있었다.
            //   frozen이면 완료(●)/미완(○)만 보여 준다.
            if (d == direction && !frozen) sb.Append("<color=#ffcc55>▶");
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
        // ★간결 표시에서는 <b>니들만</b> 산다(2026-09-04 회의). 반원 메시·눈금 라벨·기록 마크는
        //   전부 "표시되는 정보가 너무 많다"에 해당한다. useRealityGauge는 씬에 1로 굳어 있어
        //   코드로 못 끄므로 여기서 덮는다(규칙 7).
        // ★간결 표시의 니들은 <b>어깨 기준선 위</b>에만 선다 — refReady가 없으면 안 그린다(09-04).
        bool needleOn = showGauge && !gaugeForceHidden && neutralReady && frameReady
                        && (minimalDisplay ? refReady : useRealityGauge);
        bool arcOn = needleOn && !minimalDisplay;

        if (gaugeFilter != null && gaugeFilter.gameObject.activeSelf != arcOn)
            gaugeFilter.gameObject.SetActive(arcOn);
        if (needle != null && needle.enabled != needleOn) needle.enabled = needleOn;
        if (activeMark != null) activeMark.enabled = arcOn;
        if (passiveMark != null) passiveMark.enabled = arcOn;

        if (!arcOn)
        {
            for (int i = 0; i < gaugeLabels.Count; i++)
                if (gaugeLabels[i] != null) gaugeLabels[i].gameObject.SetActive(false);
        }

        if (!needleOn)
        {
            // ★테스트 지침도 같이 접는다. 안 접으면 직전 프레임 자세로 남아 떠 있다.
            if (testNeedle != null && testNeedle.enabled) testNeedle.enabled = false;
            return;
        }

        // ★니들 굵기는 간결 표시에서만 덮는다. 종전 모드는 BuildVisuals에서 정한 값 그대로 둔다.
        if (minimalDisplay && needle != null
            && !Mathf.Approximately(needle.widthMultiplier, minimalNeedleWidth))
            needle.widthMultiplier = minimalNeedleWidth;

        if (arcOn && (direction != builtDirection || frameStamp != builtStamp)) RebuildGauge();

        if (TryGetAngle(out _, out _, out float signed))
            SetLine(needle, GaugeCenter, GaugeCenter + GaugeDir(signed) * NeedleLengthNow);
        else
            SetLine(needle, GaugeCenter, GaugeCenter);

        // ★★테스트용 두 번째 지침 — <b>T중심-S중심 연결선</b>의 각(2026-09-04 요청).
        //   같은 원점·같은 축·같은 부호 규약으로 그린다. 안 그러면 비교가 성립하지 않는다.
        //   ★조금 짧게 그린다 — 두 지침이 겹칠 때 어느 쪽이 어느 쪽인지 보이게.
        //   ★회전 두 방향에서 이 지침이 <b>0도에 붙어 안 움직이면</b>
        //     "연결선으로는 회전을 못 잰다"가 눈으로 확인된 것이다.
        if (testNeedle != null)
        {
            // ★단락 평가로 묶으면 out 변수가 '확실히 할당됨'을 못 넘긴다(CS0165) — 호출을 먼저 한다.
            bool testOk = TryGetNeckLineAngle(out _, out float testSigned);
            // ★<b>테스트용 지침(T-S선)도 곁가지에 묶는다</b>(2026-09-08 지시:
            //   "내가 테스트용으로 넣은 엄지기준 각도기 바늘 있을 건데 그것도 빼 줘").
            //   ★<c>showNeckLineCompare</c>는 씬에 1로 굳어 있어 그 필드만으로는 못 끈다(규칙 7).
            //     같은 날 테스트 <b>글줄</b>을 끈 것과 같은 방식이다 — 스위치 하나로 둘 다 꺼진다.
            bool showTest = showNeckLineCompare && showSecondaryInfo && testOk;
            if (testNeedle.enabled != showTest) testNeedle.enabled = showTest;
            if (showTest)
            {
                if (minimalDisplay && !Mathf.Approximately(testNeedle.widthMultiplier, minimalNeedleWidth * 0.7f))
                    testNeedle.widthMultiplier = minimalNeedleWidth * 0.7f;
                SetLine(testNeedle, GaugeCenter,
                        GaugeCenter + GaugeDir(testSigned) * (NeedleLengthNow * 0.78f));
            }
        }

        if (!arcOn) return;

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

        float tw = tickWidth;

        // 미세 → 보조 → 주 순으로 쌓는다. 굵은 눈금이 나중에 와야 위에 보인다.
        if (microStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += microStep)
            {
                if (OnStep(a, minorStep) || OnStep(a, majorStep)) continue;
                AddTickQuad(a, microTickLength, tw * 0.7f, microColor, axis);
            }
        }
        if (minorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += minorStep)
            {
                if (OnStep(a, majorStep)) continue;
                AddTickQuad(a, minorTickLength, tw, tickColor, axis);
            }
        }
        if (majorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += majorStep)
                AddTickQuad(a, majorTickLength, tw * 1.4f, tickColor, axis);
        }

        // 0도 기준선 — 중심에서 눈금까지 통짜로 긋는다. 어디가 중립인지가 제일 중요하다.
        AddTickQuad(0f, GaugeRadiusNow, tw * 1.6f, zeroLineColor, axis);

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

    /// <summary>
    /// 강체 기둥과 코끝선을 그린다(2026-09-04).
    ///
    /// ★기둥은 <b>중립 자세를 축 둘레로 돌린 것</b>이다. 손을 그대로 따라가지 않는다 —
    ///   그게 이 방식의 핵심이다. 축에서 벗어나는 손 떨림은 여기에 안 실린다.
    /// ★코끝선은 회전을 보여준다. 회전은 머리 중심이 제자리라 기둥의 기울기로는 안 보인다.
    /// </summary>
    private void UpdateRigidBody()
    {
        // ★★<b>머리 위치를 잡은 순간부터 보여 준다</b>(2026-09-08 지시:
        //   "강체 위치는 파지 말고 머리 위치 설정 끝나면 그때부터 그냥 고정적으로 보여주는 걸로.
        //    그래야 내가 강체 위치가 제대로 세팅됐는지 확인하고 다시 하든 말든 할 거 아니야").
        //   ★그릴 재료는 그 시점에 이미 다 있다 — <c>CaptureHead()</c>가 rigidBase0·rigidTop0·
        //     rigidRadius를 정하고 <c>rigidReady</c>를 세운다(:743).
        //   ★중립 전에는 <c>RigidTurn()</c>이 <b>identity</b>를 돌려주므로(각이 없다)
        //     기둥은 <b>돌지 않고 그 자리에 고정</b>돼 보인다 — 그게 요구한 "고정적으로"다.
        //   ★★<b>`frameReady`를 뺐다</b>(2026-09-08 재지적: "강체 생성하면 강체 기둥 보이게 하라고 했는데
        //     왜 굴곡 파지 해야 그때부터 보이냐"). `frameReady`는 <b>양손 파지 틀</b>이 잡혀야 참이라
        //     (`:1612`·`:1641`) 넣어 두면 결국 파지를 기다린다 — 내가 덜 푼 것이다.
        //     기둥은 파지 틀을 <b>안 쓴다</b>. 쓰는 건 각도기·바늘 쪽이다(`:3635`).
        bool on = showRigidBody && rigidReady && !gaugeForceHidden;

        if (!on)
        {
            if (rigidBody != null && rigidBody.gameObject.activeSelf) rigidBody.gameObject.SetActive(false);
            if (noseLine != null && noseLine.enabled) noseLine.enabled = false;
            return;
        }

        EnsureRigidBody();
        if (rigidBody == null) return;
        if (!rigidBody.gameObject.activeSelf) rigidBody.gameObject.SetActive(true);

        Quaternion turn = RigidTurn();

        // 밑동은 축원점에 붙어 있고, 꼭대기만 돈다 — 그게 "축은 고정, 강체만 움직임"이다.
        Vector3 baseP = rigidBase0;
        Vector3 topP = baseP + turn * (rigidTop0 - rigidBase0);

        Vector3 mid = (baseP + topP) * 0.5f;
        float height = Vector3.Distance(baseP, topP);
        rigidBody.SetPositionAndRotation(mid, Quaternion.FromToRotation(Vector3.up, (topP - baseP).normalized));
        // Unity 기본 실린더는 높이 2·지름 1이다.
        rigidBody.localScale = new Vector3(rigidRadius * 2f, height * 0.5f, rigidRadius * 2f);

        if (noseLine == null || noseLineLength <= 0f) return;
        if (!noseLine.enabled) noseLine.enabled = true;

        // ★코끝은 중립에서 환자 정면을 본다. 같은 회전을 먹여 돌린다.
        Vector3 nose = turn * refFwd;
        SetLine(noseLine, topP, topP + nose.normalized * noseLineLength);
    }

    private void EnsureRigidBody()
    {
        if (rigidBody != null) return;
        if (root == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "강체기둥";
        go.hideFlags = HideFlags.DontSave;
        // ★콜라이더는 지운다 — 손과 부딪히면 파지·이동이 엉킨다. 이건 표시물이다.
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");
        mr.material = new Material(sh) { color = rigidBodyColor, renderQueue = 3000 };

        go.transform.SetParent(root, false);
        rigidBody = go.transform;

        noseLine = CreateLine("코끝선", noseLineColor);
        noseLine.widthMultiplier = 0.006f;
    }

    private void UpdateAxisLines()
    {
        // ★축선 3개는 <b>어깨 중점</b>에 고정한다(2026-09-01 사용자 지시).
        //   손을 따라다니면 기준이 될 수가 없다 — 환자가 몸을 틀었는지 보려면
        //   기준이 몸에 붙어 가만히 있어야 한다. 어깨를 안 잡았으면 종전대로 손 기준이다.
        //   ★그리는 자리만 바뀐다. 각을 재는 축(axRight/axUp/axFwd)은 파지선에서 세운 그대로다.
        Vector3 axisAt = refReady ? shoulderMid : pivot;

        // ★★<b>2026-09-08 — 게이트만 풀고 그릴 값을 안 챙겼다.</b>
        //   <c>axRight/axUp/axFwd</c>는 <b>파지 틀</b>이 설 때만 채워진다(`:1688`·`:2355`).
        //   그래서 어깨선·머리위치 단계에서는 셋 다 <b>영벡터</b>였고, 게이트를 풀어도
        //   길이 0짜리 선을 그려 <b>아무것도 안 보였다</b>(사용자: "3축선이 나와야 하는데").
        //   → 파지 틀 전에는 어깨선이 준 <c>refRight/refUp/refFwd</c>를 쓴다. 같은 틀이다
        //     (파지 틀도 `:2355`에서 ref*를 그대로 복사한다).
        //   ★<b>그리는 값만</b> 바꾼다. 각을 재는 축(ax*)은 한 글자도 안 건드린다.
        bool haveAx = axRight.sqrMagnitude > 1e-8f && axUp.sqrMagnitude > 1e-8f
                      && axFwd.sqrMagnitude > 1e-8f;
        Vector3 dRight = haveAx ? axRight : refRight;
        Vector3 dUp    = haveAx ? axUp    : refUp;
        Vector3 dFwd   = haveAx ? axFwd   : refFwd;
        if (dRight.sqrMagnitude < 1e-8f || dUp.sqrMagnitude < 1e-8f || dFwd.sqrMagnitude < 1e-8f)
        {
            // 아직 어깨선도 없다 — 그릴 틀이 없으므로 접는다(아래 refReady 가드와 같은 뜻).
            if (lineRight != null) lineRight.enabled = false;
            if (lineUp != null) lineUp.enabled = false;
            if (lineFwd != null) lineFwd.enabled = false;
            HideAxisLabels();
            return;
        }

        if (minimalDisplay)
        {
            // ★★<b>어깨 기준선이 없으면 아무것도 안 그린다</b>(2026-09-04 Play 지적).
            //   "십자선을 파지한 위치에 나오게 하라는 게 아니라 어깨 기준선 위에서 니들로 표시하라고."
            //   종전에는 refReady가 아니면 pivot(=파지 지점)으로 <b>조용히 떨어졌다</b> —
            //   기준틀을 파지 자리에 그리는 건 뜻이 없는 그림이라, 차라리 안 그리는 게 맞다.
            //   ★안 그리는 이유는 안내문이 말해 준다("양손을 환자 양어깨에 - 정지").
            if (!refReady)
            {
                if (lineRight != null) lineRight.enabled = false;
                if (lineUp != null) lineUp.enabled = false;
                if (lineFwd != null) lineFwd.enabled = false;
                HideAxisLabels();
                return;
            }

            // ★★간결 표시 십자선(2026-09-04 회의 — A안).
            //   니들이 도는 <b>평면을 펴는 두 축</b>만 그린다. 나머지 한 축은 니들의 회전축이라
            //   화면에서는 점으로 보이고, 정보만 늘린다.
            //     굴곡·신전 (축 = 좌우) → 수직 + 전후
            //     좌·우측굴 (축 = 전후) → 수직 + 좌우
            //     좌·우회전 (축 = 수직) → 전후 + 좌우
            //   ★이 규칙 하나로 세 면이 다 맞는다 — 회전축과 나란한 축을 뺀다.
            //   ★B안(3축 항상)은 minimalCrossBothAxes를 켜면 된다. 지우는 게 아니라 더하는 것이다.
            Vector3 spin = AxisFor(direction);
            bool haveSpin = spin.sqrMagnitude > 1e-8f && !minimalCrossBothAxes;

            // ★★<b>수직 팔은 어떤 방향에서도 안 지운다</b>(2026-09-08 지시:
            //   "회전할 때 3축선 중 수직선이 지워졌단 말이야. 그건 지우면 안 되지").
            //   ★09-04의 A안은 "회전축과 나란한 팔은 점으로 보이니 뺀다"였는데,
            //     그때는 3축선을 <b>재는 눈금</b>으로만 봤다. 지금은 용도가 하나 더 있다 —
            //     <b>목이 중앙에 왔는지, 한쪽으로 기울었는지 보는 기준선</b>이고 그게 수직선이다.
            //     회전은 회전축이 수직이라 하필 그 기준선이 지워졌다.
            //   ★좌우·전후 팔은 종전 규칙 그대로다. 바뀌는 것은 <b>회전 단계의 수직 팔</b> 하나뿐이다.
            SetCrossArm(lineRight, axisAt, dRight, spin, haveSpin);
            SetCrossArm(lineUp, axisAt, dUp, spin, dropSpinAxis: false);
            SetCrossArm(lineFwd, axisAt, dFwd, spin, haveSpin);

            // ★축 이름표는 뺀다 — 회의 요지가 "정보를 줄이자"다.
            //   showAxisLabels는 씬에 1로 굳어 있어 코드로 못 끈다(규칙 7) → 여기서 덮는다.
            HideAxisLabels();
            return;
        }

        SetLine(lineRight, axisAt, axisAt + dRight * axisLength);
        SetLine(lineUp, axisAt, axisAt + dUp * axisLength);
        SetLine(lineFwd, axisAt, axisAt + dFwd * axisLength);

        UpdateAxisLabels(axisAt);
    }

    /// <summary>
    /// 십자선 팔 하나를 원점 <b>양쪽으로</b> 긋는다. 회전축과 나란한 팔은 접는다(A안).
    /// </summary>
    private void SetCrossArm(LineRenderer lr, Vector3 at, Vector3 dir, Vector3 spin, bool dropSpinAxis)
    {
        if (lr == null) return;

        // 회전축과 나란하면(|cos| ≈ 1) 그 팔은 안 그린다.
        if (dropSpinAxis && Mathf.Abs(Vector3.Dot(dir.normalized, spin.normalized)) > 0.9f)
        {
            lr.enabled = false;
            return;
        }

        if (!lr.enabled) lr.enabled = true;
        if (!Mathf.Approximately(lr.widthMultiplier, minimalCrossWidth))
            lr.widthMultiplier = minimalCrossWidth;

        SetLine(lr, at - dir * minimalCrossLength, at + dir * minimalCrossLength);
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
        // ★간결 표시에서는 이 넷을 전부 접는다(2026-09-04 회의 — "정보가 너무 많다").
        //   중심선·어깨선은 십자선의 수직 팔·좌우 팔과 <b>같은 선</b>이라 중복이고,
        //   중립파지·현재파지(T라인 두 개)는 회의가 요구한 표시물이 아니다.
        //   ★midlineAlwaysOn·showShoulderLine은 씬에 1로 굳어 있어 코드로 못 끈다(규칙 7).
        if (minimalDisplay)
        {
            // ★★<b>어깨 기준선은 남긴다</b>(2026-09-04 Play 지적 — "어깨 파지선이 안 뜸").
            //   09-04에 "십자선의 좌우 팔과 중복"이라고 판단해 접었는데, 그게 틀렸다.
            //   십자선은 <b>측정 기준틀</b>이고 어깨선은 <b>어디를 짚었나</b>를 보여 주는 것이다 —
            //   길이도 역할도 다르다. 어깨선이 있어야 십자선이 어디 얹혔는지가 읽힌다.
            // ★★<b>머리까지 잡으면 어깨선을 접는다</b>(2026-09-08 지시:
            //   "어깨선도 초록선이랑 3축선 빨강이랑 겹쳐서, 어깨선은 최초에 측정할 때만 표시하면 좋겠어.
            //    이후에 머리까지 측정해서 3축선 위치가 확정되면 그때부턴 겹치니까 안 보였으면").
            //   ★어깨선은 <b>어디를 짚었나</b>를 보여 주는 것이라 짚는 동안에만 쓸모가 있다.
            //     3축선이 서면 같은 자리에 겹쳐 두 겹으로 보인다.
            if (refReady && showShoulderLine && !(hideShoulderLineAfterHead && headReady))
            {
                Vector3 halfW = shoulderLineLength > 0f
                    ? refRight * (shoulderLineLength * 0.5f)
                    : (shoulderR - shoulderL) * 0.5f;
                SetLine(lineShoulder, shoulderMid - halfW, shoulderMid + halfW);
            }
            else SetLine(lineShoulder, shoulderMid, shoulderMid);

            // 세로 중심선(0.8m)과 파지선 둘은 계속 접는다 — 십자선의 수직 팔과 겹친다.
            SetLine(lineMidline, shoulderMid, shoulderMid);
            SetLine(lineNeutral, pivot, pivot);
            SetLine(lineNow, pivot, pivot);
            return;
        }

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
        // ★테스트 지침은 <b>다른 색·더 얇게</b>. 본 지침과 헷갈리면 비교가 안 된다.
        testNeedle = CreateLine("지침_테스트(T-S선)", new Color(0.56f, 0.72f, 1f, 0.9f));
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

        if (showInfoBackdrop) BuildInfoBackdrop();
    }

    /// <summary>
    /// 글자 뒤에 반투명 판을 깐다 (2026-09-08 지시: "평가모드 강체 위 텍스트도 실습처럼 바탕").
    /// ★실습 정보창(<see cref="CervicalRomPracticeReadout"/>)과 <b>같은 방식</b>을 그대로 쓴다 —
    ///   따로 만들면 두 모드의 바탕이 미묘하게 달라지고, 그건 사용자가 바로 알아챈다.
    /// ★재질은 <c>Sprites/Default</c>(없으면 <c>Standard</c>). 검증된 경로다 —
    ///   빌드에서 커스텀 셰이더가 스트립돼 xray가 죽은 전례가 있는 자리다.
    /// </summary>
    private void BuildInfoBackdrop()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "정보창배경";
        go.hideFlags = HideFlags.DontSave;

        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        infoBackdrop = go.transform;
        infoBackdrop.SetParent(readout.transform, false);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(sh) { color = infoBackdropColor, renderQueue = 2990 };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>글이 바뀔 때마다 판을 글자 상자에 맞춘다.</summary>
    private void FitInfoBackdrop()
    {
        if (infoBackdrop == null || readout == null) return;

        readout.ForceMeshUpdate();
        Bounds b = readout.textBounds;
        if (b.size.x < 1e-4f || b.size.y < 1e-4f)
        {
            // ★글이 비면 판도 접는다. 안 그러면 빈 판이 떠 있어 <b>무언가 뜬 것처럼</b> 보인다.
            if (infoBackdrop.gameObject.activeSelf) infoBackdrop.gameObject.SetActive(false);
            return;
        }
        if (!infoBackdrop.gameObject.activeSelf) infoBackdrop.gameObject.SetActive(true);

        infoBackdrop.localScale = new Vector3(b.size.x + infoBackdropPadX * 2f,
                                                 b.size.y + infoBackdropPadY * 2f, 1f);
        // ★글자보다 <b>뒤</b>로 살짝 물린다. 같은 평면에 두면 z-파이팅으로 지글거린다.
        infoBackdrop.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);
        infoBackdrop.localRotation = Quaternion.identity;
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
        infoBackdrop = null;   // readout의 자식이라 root와 함께 파괴된다
        lineRight = lineUp = lineFwd = lineNeutral = lineNow = null;
        lineMidline = lineShoulder = null;
        labelRightPos = labelRightNeg = labelFwdPos = labelFwdNeg = labelUpPos = null;
        needle = activeMark = passiveMark = testNeedle = null;
        rigidBody = null; noseLine = null;   // root와 함께 파괴된다
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
