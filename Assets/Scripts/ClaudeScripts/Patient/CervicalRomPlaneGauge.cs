using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 경추 ROM 각도기. <b>해부학적 면을 반투명 판으로 머리에 관통시키고</b>, 그 면 위에
/// 경추부 축을 중심으로 눈금과 지침을 그린다.
///
///   굴곡·신전 → 시상면(청록)   좌우측굴 → 관상면(주황)   좌우회전 → 횡단면(초록)
///
/// ★움직임 평면이 곧 해부학적 면이다. <see cref="CervicalRomDriver.CurrentWorldAxis"/>에
///   수직인 평면이 그 면이고, 압박 각도를 재는 평면과 정확히 같다. 그래서 재는 것과
///   보여 주는 것이 어긋날 수 없다.
///
/// 눈금은 0°(중립)에서 최대각까지 그린다. 채움은 세 구간이다 —
///   0 → 능동 한계      환자가 스스로 간 데까지
///   능동 → 압박 한계    시술자가 밀어서 더 간 데까지
///   압박 → 최대각      부족각. 이게 기록할 값이다.
///
/// 렌더링은 이 프로젝트 관례를 따른다 — <b>Sprites/Default</b>. 알파 블렌드가 셰이더에
/// 고정돼 있어 맞출 상태가 없고, UI가 늘 쓰므로 빌드에서 스트립되지 않는다
/// (커스텀 셰이더가 빌드에서 죽은 xray 전례, Standard Fade가 조용히 불투명해진 전례를 피한다).
/// </summary>
/// <remarks>
/// ★<see cref="ExecuteAlways"/> — Play 없이 씬 뷰에서도 그린다. 값을 만지고 결과를
///   바로 보려면 이게 있어야 한다. 대신 생성물에는 전부 <c>HideFlags.DontSave</c>를
///   걸어 <b>씬에 절대 직렬화되지 않게</b> 한다. 안 걸면 각도기 조각 수백 개가
///   씬 파일에 굳어 버린다(이 프로젝트는 그런 사고 이력이 있다).
/// </remarks>
[ExecuteAlways]
[RequireComponent(typeof(Transform))]
public class CervicalRomPlaneGauge : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomDriver driver;

    [Tooltip("눈금 숫자 폰트. Assets/_NJS/Noto_Sans_KR/NotoSansKR-Bold 를 넣는다.\n" +
             "★Resources 밖에 있어 코드가 런타임에 못 찾는다 — 인스펙터에서 직접 할당해야 한다.\n" +
             "비워 두면 TMP 기본 폰트(LiberationSans)가 쓰여 한글이 깨진다.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("=== 면 판 ===")]
    [Tooltip("해부학적 면을 반투명 판으로 그린다. 끄면 눈금만 남는다.")]
    [SerializeField] private bool showPlane = true;

    [Tooltip("판 크기를 눈금·숫자에 맞춰 자동으로 잡는다.\n" +
             "★끄고 손으로 잡으면 숫자가 판 밖으로 삐져나간다 — 숫자는 반지름 + 오프셋 자리에 놓이므로\n" +
             "  판이 그보다 커야 한다. 켜 두면 반지름을 바꿔도 판이 따라 커진다.")]
    [SerializeField] private bool autoFitPlane = true;

    [Tooltip("자동 맞춤일 때 눈금 바깥으로 더 둘 여백 (m)")]
    [SerializeField] private float planeMargin = 0.10f;

    [Tooltip("판의 크기 (m). x=면 안 가로, y=면 안 세로. autoFitPlane이 꺼져 있을 때만 쓴다.")]
    [SerializeField] private Vector2 planeSize = new Vector2(0.95f, 1.05f);

    [Tooltip("판을 회전 중심에서 위로 얼마나 올릴지 (m). 경추 밑동이 아니라 머리에 걸치게 한다.\n" +
             "★면 '안'에서 미는 값이다. 머리 밖으로 빼는 건 아래 면수직 오프셋이다.")]
    [SerializeField] private float planeLift = 0.18f;

    [Header("=== 면수직 오프셋 (머리 밖으로 빼기) ===")]
    // ★각도기 전체를 면에 <b>수직으로</b> 밀어낸다. 회전축과 나란한 평행이동이라
    //   어떤 각도도 바뀌지 않는다 — 지침이 가리키는 값은 그대로다.
    //
    // ★회전축 부호를 기준으로 밀면 안 된다. AxisOf가 방향마다 부호를 뒤집기 때문에
    //   (굴곡 −X / 신전 +X, 우측굴 −Z / 좌측굴 +Z) 같은 값을 넣어도 굴곡은 오른쪽,
    //   신전은 왼쪽으로 간다. 그래서 몸통의 해부학 축을 기준으로 잡는다.
    //
    //   시상면(굴곡·신전)  torso.right    — 시술자가 서는 환자 우측으로 뺀다
    //   관상면(좌우측굴)   torso.forward  — 환자 앞쪽으로 뺀다
    //   횡단면(좌우회전)   torso.up       — 위아래로 뺀다

    [Tooltip("시상면(굴곡·신전)을 환자 좌우로 미는 거리 (m).\n" +
             "절차상 시술자가 환자 우측 측면에 서므로 양수면 그쪽으로 나온다.\n" +
             "★어느 쪽이 우측인지는 실측하지 않았다 — 반대로 나오면 부호를 뒤집으면 된다.")]
    [SerializeField] private float sagittalNormalOffset = 0.30f;

    [Tooltip("관상면(좌우측굴)을 환자 앞뒤로 미는 거리 (m). 양수면 앞쪽이다.\n" +
             "★앞뒤 부호도 미실측이다. 반대면 뒤집는다.")]
    [SerializeField] private float coronalNormalOffset = 0.32f;

    [Tooltip("횡단면(좌우회전)을 위아래로 미는 거리 (m).\n" +
             "목에 걸친 지금 모습이 보기 좋다고 하셔서 기본 0이다.")]
    [SerializeField] private float transverseNormalOffset = 0f;

    [Tooltip("시술자가 선 쪽의 <b>반대편</b>에 면을 놓는다. 위 오프셋의 부호를 자동으로 정한다.\n" +
             "★그 면 단계에 들어갈 때 한 번 정하고 고정한다 — 매 프레임 보면 시술자가\n" +
             "  정중선 근처에서 움직일 때 면이 좌우로 깜빡인다.\n" +
             "끄면 위에 적은 부호를 그대로 쓴다.")]
    [SerializeField] private bool autoSideByViewer = true;

    [Tooltip("정중선에 이만큼 가까우면 좌우를 판단하지 않고 수동 부호를 쓴다 (m).\n" +
             "딱 가운데에 서 있을 때 아무 쪽이나 뽑히는 걸 막는다.")]
    [SerializeField] private float sideDeadZone = 0.08f;

    [Tooltip("판의 불투명도. 이미지처럼 옅게 깔린다.")]
    [Range(0f, 1f)] [SerializeField] private float planeAlpha = 0.18f;

    [Header("=== 색 (해부학 도해 관례) ===")]
    [Tooltip("시상면 — 굴곡·신전")]
    [SerializeField] private Color sagittalColor = new Color(0.31f, 0.78f, 0.80f);
    [Tooltip("관상면 — 좌·우 측굴")]
    [SerializeField] private Color coronalColor = new Color(0.96f, 0.63f, 0.36f);
    [Tooltip("횡단면 — 좌·우 회전")]
    [SerializeField] private Color transverseColor = new Color(0.48f, 0.78f, 0.48f);

    [Header("=== 눈금 ===")]
    [Tooltip("눈금 호의 반지름 (m).\n" +
             "★손끝 중점이 회전 중심에서 약 0.21m다(실측). 그보다 밖에 둬야 손에 안 가린다.")]
    [SerializeField] private float scaleRadius = 0.34f;

    [Tooltip("★<b>횡단면(좌·우 회전)에서만</b> 눈금 반지름에 곱하는 배수. 1이면 위 값 그대로.\n" +
             "횡단면은 회전축이 세로라 판이 <b>머리를 가로질러 눕는다</b> — 뒤에서 내려다보면\n" +
             "머리가 눈금을 덮는다(2026-08-27 사용자 지적). 시상면·관상면은 판이 세로로 서서\n" +
             "옆에서 보이므로 이 문제가 없어 손대지 않는다.\n" +
             "기본 1.5 = 반지름 0.34m → 0.51m(계산값). 채움 부채꼴도 같은 배수로 커져 비율이 유지된다.\n" +
             "판·눈금숫자·현재각은 반지름에서 파생되므로 자동으로 따라 커진다.\n" +
             "※반지름 대신 판을 머리 위로 <b>띄우고</b> 싶으면 위쪽 transverseNormalOffset을 쓴다.")]
    [SerializeField] private float transverseRadiusScale = 1.5f;

    [Tooltip("눈금을 그릴 범위 (도). 방향의 최대각과 무관하게 이만큼 그린다.\n" +
             "★굴곡은 최대각이 45°지만 눈금은 90°까지 깔고 45°에 굵은 마지노선을 긋는다 —\n" +
             "  각도기처럼 눈금이 먼저 있고 그 위에 기준선이 표시되는 게 읽기 쉽다.")]
    [SerializeField] private float scaleSpan = 90f;

    [Header("=== 십자선 ===")]
    [Tooltip("회전 중심을 지나는 십자선을 긋는다. 0°/90°/180°/270° 네 방향.")]
    [SerializeField] private bool showCrosshair = true;

    [Tooltip("십자선 팔 길이 (m). 0이면 눈금 반지름을 쓴다.")]
    [SerializeField] private float crosshairLength = 0f;

    [Tooltip("십자선 굵기 (m)")]
    [SerializeField] private float crosshairWidth = 0.0030f;

    [Tooltip("십자선 불투명도. 눈금보다 옅게 깔아야 방해가 안 된다.")]
    [Range(0f, 1f)] [SerializeField] private float crosshairAlpha = 0.45f;

    [Tooltip("주눈금 간격 (도). 숫자가 붙는다.")]
    [SerializeField] private float majorStep = 10f;

    [Tooltip("보조눈금 간격 (도). 숫자는 안 붙는다.")]
    [SerializeField] private float minorStep = 5f;

    [Tooltip("미세눈금 간격 (도). 0이면 안 그린다.\n" +
             "반지름 0.34m에서 1° 간격이면 눈금 사이가 약 6mm라 촘촘하되 뭉개지지는 않는다(계산값).")]
    [SerializeField] private float microStep = 1f;

    [Tooltip("주눈금 길이 (m)")] [SerializeField] private float majorTickLength = 0.036f;
    [Tooltip("보조눈금 길이 (m)")] [SerializeField] private float minorTickLength = 0.022f;
    [Tooltip("미세눈금 길이 (m)")] [SerializeField] private float microTickLength = 0.011f;
    [Tooltip("눈금 굵기 (m)")] [SerializeField] private float tickWidth = 0.0032f;

    [Header("=== 채움 ===")]
    [Tooltip("능동 구간 — 환자가 스스로 간 데까지")]
    [SerializeField] private Color activeFillColor = new Color(0.29f, 0.56f, 0.89f, 0.55f);
    [Tooltip("압박 구간 — 시술자가 밀어서 더 간 데까지")]
    [SerializeField] private Color pressFillColor = new Color(0.95f, 0.55f, 0.18f, 0.60f);
    [Tooltip("부족각 — 최대각까지 남은 만큼. 결과로 읽을 값이다.")]
    [SerializeField] private Color deficitFillColor = new Color(0.55f, 0.55f, 0.58f, 0.30f);

    [Tooltip("채움 부채꼴의 바깥 반지름 (m). 눈금 안쪽에 깔린다.")]
    [SerializeField] private float fillRadius = 0.28f;

    [Tooltip("부족각 구간을 처음부터 보여줄지. 끄면 압박이 끝난 뒤에 드러난다.")]
    [SerializeField] private bool showDeficitFromStart = true;

    [Header("=== 지침 ===")]
    [Tooltip("현재 각도를 가리키는 바늘 색")]
    [SerializeField] private Color needleColor = new Color(0.10f, 0.10f, 0.12f, 0.95f);
    [Tooltip("지침 굵기 (m)")] [SerializeField] private float needleWidth = 0.0055f;

    [Tooltip("지침 색을 면 색에서 뽑는다. 면마다 같은 계열의 <b>진한 원색</b>이 된다.\n" +
             "면 색의 색상(H)만 가져오고 채도·명도는 아래 값으로 올린다.\n" +
             "끄면 위의 needleColor를 그대로 쓴다.")]
    [SerializeField] private bool needleFromPlaneColor = true;

    [Tooltip("지침 색의 채도(x)·명도(y). 둘 다 1.0이면 가장 선명한 원색이 된다.\n" +
             "★명도를 낮추면 진해지는 게 아니라 <b>탁해진다</b>(검정이 섞인다).\n" +
             "  0.60으로 내렸다가 되돌렸다 — 선명함은 채도 1.0 · 명도 1.0에서 나온다.")]
    [SerializeField] private Vector2 needleSaturationValue = new Vector2(1f, 1f);

    [Header("=== 압박 방향 화살표 ===")]
    [Tooltip("★압박 유지 단계에서 <b>어느 쪽으로 더 돌려야 하는지</b>를 호 화살표로 보여준다.\n" +
             "굴곡·신전·측굴·회전 여섯 방향 전부에 자동으로 붙는다 — 눈금·지침과 같은 면 기저를\n" +
             "쓰므로 방향이 어긋날 수가 없다(씬에 화살표를 따로 배치하지 않는 이유다).\n" +
             "★능동 구간에는 안 뜬다. 능동은 환자가 스스로 가는 구간이라 시술자에게 줄 지시가 없다.")]
    [SerializeField] private bool showPressArrow = true;

    [Tooltip("호의 길이 (도). 지침에서 진행 방향으로 이만큼 뻗는다.")]
    [SerializeField] private float pressArrowSpanDeg = 24f;

    [Tooltip("호를 그릴 반지름 (눈금 반지름 대비 배수).\n" +
             "★채움 부채꼴과 눈금 사이의 <b>빈 띠</b>에 앉힌다 — 겹치면 둘 다 읽기 어렵다.\n" +
             "씬 값 기준 계산: 채움이 0.28/0.36 = 0.78배까지, 주눈금 안쪽 끝이\n" +
             "1 − 0.036/0.36 = 0.90배부터다. 그 사이 한가운데가 0.84.\n" +
             "★눈금 길이나 채움 반지름을 바꿨으면 이 값도 같이 봐야 한다.")]
    [Range(0.2f, 1.3f)] [SerializeField] private float pressArrowRadiusScale = 0.84f;

    [Tooltip("호의 굵기 (m)")] [SerializeField] private float pressArrowWidth = 0.010f;
    [Tooltip("화살촉 길이 (도)")] [SerializeField] private float pressArrowHeadDeg = 9f;
    [Tooltip("화살촉 폭 (m). 호보다 확실히 넓어야 촉으로 읽힌다.")]
    [SerializeField] private float pressArrowHeadWidth = 0.032f;

    [Tooltip("호 색을 면 색에서 뽑는다(지침과 같은 규칙). 끄면 아래 색을 쓴다.")]
    [SerializeField] private bool pressArrowFromPlaneColor = true;
    [SerializeField] private Color pressArrowColor = new Color(0.95f, 0.35f, 0.15f, 0.95f);

    [Tooltip("한 방향으로 쓸어가는 폭 (도). 0이면 제자리에 선다.\n" +
             "★왕복시키지 않는다 — 왔다갔다 하면 '반대로도 민다'로 읽혀 방향이 흐려진다\n" +
             "  (2026-08-17에 힘의 방향 화살표에서 같은 지적을 받았다).")]
    [SerializeField] private float pressArrowSweepDeg = 10f;

    [Tooltip("초당 쓸어가는 횟수")] [SerializeField] private float pressArrowSweepPerSecond = 0.8f;

    [Tooltip("쓸어간 뒤 시작으로 되돌아가는 순간을 감추려고 양 끝에서 밝기를 죽인다.\n" +
             "그 구간의 비율(0~0.5). 0이면 밝기 변화 없이 그냥 되돌아간다.")]
    [Range(0f, 0.5f)] [SerializeField] private float pressArrowFadeEnds = 0.18f;

    [Range(0f, 1f)] [SerializeField] private float pressArrowMinAlpha = 0.25f;

    [Header("=== 숫자 ===")]
    [Tooltip("눈금 숫자 크기")] [SerializeField] private float tickLabelSize = 0.030f;
    [Tooltip("현재 각도 숫자 크기")] [SerializeField] private float readoutSize = 0.055f;
    [Tooltip("숫자를 눈금에서 얼마나 바깥에 둘지 (m). 작을수록 눈금에 붙는다.")]
    [SerializeField] private float labelOffset = 0.016f;

    [Tooltip("★이 체크는 <b>끄는 것이 기본</b>이다.\n" +
             "\n" +
             "【끔(권장)】 숫자가 면 위에 누운 채로 <b>Y축 180°만</b> 돌아 읽는 방향을 맞춘다.\n" +
             "  기울지 않으므로 눈금과 나란한 상태가 유지된다. 어느 쪽으로 돌릴지는\n" +
             "  아래 autoFlipLabels가 정한다.\n" +
             "\n" +
             "【켬】 숫자가 헤드셋을 <b>매 프레임 계속 따라 돈다</b>(X·Z도 같이 기운다).\n" +
             "  읽기는 쉬운데 머리를 조금만 움직여도 숫자가 꿈틀거린다.\n" +
             "\n" +
             "★이름이 '사용자를 향한다'로 읽히지만 실제 뜻은 '계속 따라 돈다'이다 —\n" +
             "  요청받은 'Y축 180°만'은 <b>끈 상태</b>의 동작이다(2026-08-26).")]
    [SerializeField] private bool labelsFaceViewer = false;

    [Tooltip("숫자를 고정할 때 <b>Y축으로만</b> 180° 돌려 보는 사람 쪽을 향하게 한다.\n" +
             "★면의 뒷면에서 보면 글씨가 좌우로 뒤집혀 읽힌다. X·Z는 건드리지 않으므로\n" +
             "  눈금과 나란한 상태는 그대로 유지된다(2026-08-26 사용자 지시).\n" +
             "면에 들어갈 때 한 번 정하고 고정한다 — 매 프레임 보면 글씨가 깜빡 뒤집힌다.")]
    [SerializeField] private bool autoFlipLabels = true;

    [Tooltip("숫자에 더할 Y 회전(도). 자동 판정이 반대로 나오면 180을 넣는다.")]
    [SerializeField] private float labelYawOffset = 0f;

    [Tooltip("글씨 뒤집기가 깜빡이지 않게 두는 여유 (m).\n" +
             "면에 딱 붙어 설 때만 의미가 있다. 크게 주면 면을 넘어가도 늦게 뒤집힌다.")]
    [SerializeField] private float flipHysteresis = 0.05f;

    [Tooltip("★횡단면(좌우 회전)에서만 숫자를 <b>호의 접선 방향</b>으로 돌린다.\n" +
             "면 위에 눕는 것은 그대로다 — 세우지 않는다.\n" +
             "각 숫자의 글씨 방향이 <b>중심에서 그 숫자로 가는 선과 수직</b>이 되게 놓는다.\n" +
             "각도기 눈금 숫자가 호를 따라 도는 그 모양이다.\n" +
             "시상면·관상면은 전부 같은 방향으로 눕는다(종전대로).")]
    [SerializeField] private bool transverseLabelsAlongArc = true;

    [Tooltip("최대각 마지노선의 색. ★면이 바뀌어도 <b>늘 같은 색</b>이다 —\n" +
             "  '여기가 최대'는 면과 무관한 기준선이라, 면 색을 따라가면 기준선으로 안 읽힌다\n" +
             "  (2026-08-26 사용자 지시). 지침처럼 진한 원색으로 둔다.")]
    [SerializeField] private Color maxAngleMarkColor = new Color(0.10f, 0.80f, 0.25f, 1f);

    [Tooltip("최대각 마지노선 굵기 (m). ★지침과 같은 꼴로 <b>중심에서 눈금까지</b> 긋는다 —\n" +
             "  짧은 눈금으로는 '여기가 최대'라는 기준선으로 안 읽힌다(2026-08-26 사용자 지시).")]
    [SerializeField] private float maxAngleMarkWidth = 0.0045f;

    [Tooltip("눈금 위 최대각 자리에 숫자를 붙일지. 끄면 굵은 눈금만 남는다.\n" +
             "★켜 둔다 — 굴곡 45°는 10° 간격 눈금 사이에 떨어져서, 끄면 최대각 숫자가\n" +
             "  화면 어디에도 안 나온다. 지침 위 부제를 뺀 자리를 이게 대신한다.")]
    [SerializeField] private bool showMaxAngleLabel = true;

    [Tooltip("현재각 숫자를 눈금에서 얼마나 바깥에 둘지 (m).\n" +
             "★눈금 숫자(labelOffset)보다 확실히 커야 겹치지 않는다.")]
    [SerializeField] private float readoutOffset = 0.085f;

    [Tooltip("현재각 글씨 색")]
    [SerializeField] private Color readoutColor = Color.white;

    [Tooltip("현재각 숫자 뒤에 깔 원형 배경. 0이면 안 그린다.\n" +
             "흰 글씨가 밝은 배경 위에서 안 읽히는 걸 막는다. 반지름(m).")]
    [SerializeField] private float readoutBackdropRadius = 0.055f;

    [Tooltip("원형 배경 색")]
    [SerializeField] private Color readoutBackdropColor = new Color(0.09f, 0.10f, 0.13f, 0.78f);

    [Header("=== 에디터 프리뷰 (Play 없이 보기) ===")]
    [Tooltip("켜면 Play하지 않아도 씬 뷰에 각도기가 그려진다. 값을 만지면 바로 반영된다.\n" +
             "★Play 중에는 이 설정과 무관하게 항상 실제 드라이버 값을 그린다.")]
    [SerializeField] private bool previewInEditor = true;

    [Tooltip("프리뷰로 띄울 방향. 면·색·좌우 배치가 여기 따라 바뀐다.")]
    [SerializeField] private CervicalRomDriver.Direction previewDirection = CervicalRomDriver.Direction.Flexion;

    [Tooltip("프리뷰 지침이 가리킬 각(도). 최대각을 넘으면 최대각으로 잘린다.\n" +
             "능동 한계·압박 한계는 드라이버 인스펙터의 기본값(추첨 전)으로 그린다.")]
    [SerializeField] private float previewAngle = 30f;

    [Tooltip("프리뷰에서 압박 방향 화살표를 같이 띄운다. Play 없이 호 크기·색을 맞추려고 둔 것이다.\n" +
             "★Play 중에는 이 값과 무관하다 — 브리지가 압박 유지 substep에서만 켠다.")]
    [SerializeField] private bool previewPressArrow = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // ── 겹침 순서. 같은 투명 큐라 sortingOrder가 유일하게 확실한 수단이다. ──
    private const int OrderPlane = 0;      // 면 판 · 눈금 · 채움 · 지침
    private const int OrderBackdrop = 1;   // 현재각 숫자 뒤 원판
    private const int OrderLabel = 2;      // 눈금 숫자 · 현재각 숫자

    // ── 생성물 ────────────────────────────────────────────────────────────
    private Transform root;              // 면과 함께 도는 부모. 회전 중심에 붙는다.
    private MeshFilter staticFilter;     // 판 + 눈금 (방향이 바뀔 때만 다시 만든다)
    private MeshFilter dynamicFilter;    // 채움 + 지침 (각도가 변하면 다시 만든다)
    private Mesh staticMesh;
    private Mesh dynamicMesh;
    private Material sharedMaterial;
    private readonly List<TextMeshPro> tickLabels = new List<TextMeshPro>();
    private TextMeshPro readout;
    private Transform readoutBackdrop;
    private Mesh backdropMesh;
    private Material backdropMaterial;

    // ── 메시 버퍼. 매 프레임 새로 만들지 않는다(VR 프레임 예산). ──────────
    private readonly List<Vector3> verts = new List<Vector3>(512);
    private readonly List<Color> colors = new List<Color>(512);
    private readonly List<int> tris = new List<int>(1024);

    // ── 마지막으로 그린 상태. 바뀌었을 때만 다시 만든다. ─────────────────
    private CervicalRomDriver.Direction builtDirection = CervicalRomDriver.Direction.None;
    private float builtMax = -1f;
    private float lastDrawnAngle = float.NaN;
    private float lastDrawnActive = float.NaN;
    private float lastDrawnPassive = float.NaN;
    private int lastReadoutDegrees = int.MinValue;

    // ── 좌우 자동 배치. 그 면에 들어갈 때 한 번 정하고 고정한다. ─────────
    private bool labelFlip;              // 글씨를 반대편으로 돌려야 하는가
    private int sagittalSide;            // −1 / +1, 0 = 아직 안 정함
    private int coronalSide;
    private PlaneGroup lastGroup;
    private bool hasLastGroup;

    /// <summary>
    /// 에디터에서 프리뷰로 그리는 중인가. Play 중에는 언제나 false다 —
    /// 실제 측정값을 프리뷰 값으로 덮어쓰면 재는 것과 보여 주는 것이 어긋난다.
    /// </summary>
    private bool UsePreview => !Application.isPlaying && previewInEditor;

    private void Awake()
    {
        EnsureDriver();
    }

    /// <summary>
    /// 드라이버를 잡는다. ★에디터에서는 <b>컴포넌트를 끄지 않는다</b> —
    /// 한 번 꺼지면 사람이 다시 켜기 전까지 프리뷰가 영영 안 뜬다.
    /// </summary>
    private bool EnsureDriver()
    {
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (driver == null)
        {
            if (Application.isPlaying)
            {
                ChunaLogger.LogWarning("[ROM 각도기] CervicalRomDriver를 찾지 못했습니다. 각도기를 끕니다.");
                enabled = false;
            }
            return false;
        }
        if (Application.isPlaying && font == null)
        {
            ChunaLogger.LogWarning("[ROM 각도기] 폰트가 비어 있습니다 — TMP 기본 폰트라 한글이 깨집니다. " +
                                   "인스펙터의 font에 Assets/_NJS/Noto_Sans_KR/NotoSansKR-Bold 를 넣으세요.");
        }
        return true;
    }

    /// <summary>
    /// 생성물이 없으면 만든다. ★Awake가 아니라 여기서 만드는 이유 —
    /// 에디터는 스크립트를 다시 컴파일할 때마다 도메인을 갈아엎는다.
    /// 그때 생성물은 DontSave라 사라지는데 Awake는 다시 불리지 않는다.
    /// </summary>
    private void EnsureBuilt()
    {
        if (root == null) BuildHierarchy();
    }

    private void OnDestroy() => Teardown();

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.SceneView.duringSceneGui -= OnSceneGui;
#endif
        // ★에디터에서 컴포넌트를 끄면 생성물도 같이 치운다. 안 그러면
        //   씬에 주인 없는 각도기가 남아 사람이 지울 수도 없다(DontSave라 저장은 안 된다).
        if (!Application.isPlaying) Teardown();
    }

    private void Teardown()
    {
        if (root != null) SafeDestroy(root.gameObject);
        root = null;
        staticFilter = null;
        dynamicFilter = null;
        readout = null;
        readoutBackdrop = null;
        tickLabels.Clear();

        SafeDestroy(backdropMesh);
        SafeDestroy(backdropMaterial);
        SafeDestroy(staticMesh);
        SafeDestroy(dynamicMesh);
        SafeDestroy(sharedMaterial);
        backdropMesh = null; backdropMaterial = null;
        staticMesh = null; dynamicMesh = null; sharedMaterial = null;

        builtDirection = CervicalRomDriver.Direction.None;
        builtMax = -1f;
        lastDrawnAngle = float.NaN;
        lastDrawnActive = float.NaN;
        lastDrawnPassive = float.NaN;
        lastReadoutDegrees = int.MinValue;
        hasLastGroup = false;
    }

    /// <summary>에디터에서는 Destroy가 다음 프레임까지 안 죽는다. 즉시 지운다.</summary>
    private static void SafeDestroy(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 인스펙터 값이 바뀌면 다시 그린다. ★에디터는 스스로 프레임을 돌리지 않으므로
    /// 플레이어 루프를 한 번 태워 줘야 LateUpdate가 불린다.
    /// </summary>
    private void OnValidate()
    {
        builtDirection = CervicalRomDriver.Direction.None;   // 판·눈금을 다시 만들게 한다
        lastDrawnAngle = float.NaN;
        lastReadoutDegrees = int.MinValue;
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }
#endif

    /// <summary>
    /// ★LateUpdate에서 그린다. 드라이버가 목뼈에 각도를 얹은 <b>뒤</b>라야
    ///   지침과 실제 머리가 같은 프레임에서 맞는다.
    /// </summary>
    private void LateUpdate()
    {
        if (!EnsureDriver()) return;
        EnsureBuilt();

        bool preview = UsePreview;
        CervicalRomDriver.Direction dir = preview ? previewDirection : driver.CurrentDirection;
        if (dir == CervicalRomDriver.Direction.None || driver.Pivot == null || driver.Torso == null)
        {
            SetVisible(false);
            return;
        }

        Vector3 axis = preview ? driver.WorldAxisFor(dir) : driver.CurrentWorldAxis;
        if (axis.sqrMagnitude < 1e-6f) { SetVisible(false); return; }
        axis.Normalize();

        // 0°가 어디를 가리키는가 — 머리에 고정된 기준 방향이다.
        //   굴곡·신전·측굴은 머리 꼭대기(위)가 기울고, 회전은 코끝(앞)이 돈다.
        Vector3 zeroDir = IsRotation(dir) ? driver.Torso.forward : driver.Torso.up;
        zeroDir = Vector3.ProjectOnPlane(zeroDir, axis);
        if (zeroDir.sqrMagnitude < 1e-6f) { SetVisible(false); return; }
        zeroDir.Normalize();

        // ★면이 바뀌면 좌우를 다시 정한다. 같은 면 안에서는(굴곡→굴곡압박→신전…)
        //   처음 정한 쪽을 끝까지 지킨다 — 중간에 면이 반대로 넘어가면 더 헷갈린다.
        PlaneGroup group = PlaneGroupOf(dir);
        if (!hasLastGroup || group != lastGroup)
        {
            hasLastGroup = true;
            lastGroup = group;
            sagittalSide = 0;
            coronalSide = 0;
        }

        // 면 안의 두 기저. root를 이 자세로 두면 이하 계산이 전부 로컬로 끝난다.
        // ★면수직 오프셋은 회전축과 나란한 평행이동이라 각도에 영향이 없다.
        //   각도기를 머리 밖으로 빼도 지침이 가리키는 값은 그대로다.
        root.SetPositionAndRotation(driver.Pivot.position + NormalOffsetOf(dir),
                                    Quaternion.LookRotation(axis, zeroDir));
        SetVisible(true);
        UpdateLabelYaw();

        float maxAngle = preview ? driver.MaxAngleFor(dir) : driver.MaxAngle;
        if (dir != builtDirection || !Mathf.Approximately(maxAngle, builtMax))
        {
            BuildStatic(dir, maxAngle);
            builtDirection = dir;
            builtMax = maxAngle;
        }

        // ★프리뷰의 능동·압박 한계는 인스펙터 기본값으로 그린다. 추첨값을 쓰면
        //   에디터에서 볼 때마다 칸이 달라져 눈금을 맞출 수가 없다.
        float angle, activeLimit, passiveLimit;
        if (preview)
        {
            angle = Mathf.Clamp(previewAngle, 0f, maxAngle);
            activeLimit = Mathf.Max(0f, maxAngle - driver.NominalDysfunction);
            passiveLimit = Mathf.Min(maxAngle, activeLimit + driver.NominalPassiveGain);
        }
        else
        {
            angle = driver.CurrentAngle;
            activeLimit = driver.ActiveTargetAngle;
            passiveLimit = driver.PassiveLimitAngle;
        }

        // ★프리뷰에서는 인스펙터 체크로 화살표를 켠다. Play에서는 브리지가 켠다.
        if (preview) pressGuideOn = previewPressArrow;

        // 0.25° 미만 변화는 무시한다. 눈에 안 보이는데 메시만 다시 만든다.
        // ★단, 압박 화살표가 켜져 있으면 매 프레임 다시 만든다 — 호가 쓸려 가야 하기 때문이다.
        //   메시는 리스트를 재사용해 다시 채우므로 프레임마다 새로 할당하지 않는다.
        if (PressArrowAnimating
            || Changed(angle, lastDrawnAngle) || Changed(activeLimit, lastDrawnActive)
                                              || Changed(passiveLimit, lastDrawnPassive))
        {
            BuildDynamic(angle, activeLimit, passiveLimit, maxAngle);
            lastDrawnAngle = angle;
            lastDrawnActive = activeLimit;
            lastDrawnPassive = passiveLimit;
        }

        UpdateReadout(angle);
    }

    private static bool Changed(float now, float before)
        => float.IsNaN(before) || Mathf.Abs(now - before) >= 0.25f;

    /// <summary>이 각도가 그 간격의 눈금 자리인가. 눈금이 겹쳐 그려지는 걸 막는다.</summary>
    private static bool OnStep(float degrees, float step)
        => step > 0f && Mathf.Abs(degrees % step) < 0.001f;

    private static bool IsRotation(CervicalRomDriver.Direction d)
        => d == CervicalRomDriver.Direction.RotationLeft || d == CervicalRomDriver.Direction.RotationRight;

    /// <summary>
    /// 각도기를 면에 수직으로 얼마나 밀어낼지(월드).
    /// ★몸통의 해부학 축을 쓴다 — 회전축은 방향마다 부호가 뒤집혀 기준으로 못 쓴다.
    /// </summary>
    private Vector3 NormalOffsetOf(CervicalRomDriver.Direction d)
    {
        Transform t = driver.Torso;
        if (t == null) return Vector3.zero;

        switch (PlaneGroupOf(d))
        {
            case PlaneGroup.Sagittal:   // 환자 좌우. 시술자가 선 쪽의 반대편으로 뺀다.
                return t.right * SideSigned(t.right, sagittalNormalOffset, ref sagittalSide);
            case PlaneGroup.Coronal:    // 환자 앞뒤. 후면에 서면 앞쪽으로 나간다.
                return t.forward * SideSigned(t.forward, coronalNormalOffset, ref coronalSide);
            default:
                return t.up * transverseNormalOffset;        // 위아래는 자동 판단 대상이 아니다
        }
    }

    /// <summary>
    /// 시술자가 선 쪽의 <b>반대편</b> 부호를 붙인다.
    /// ★한 번 정하면 그 면을 벗어날 때까지 고정한다. 매 프레임 보면 정중선 근처에서 깜빡인다.
    /// </summary>
    private float SideSigned(Vector3 anatomicalAxis, float magnitude, ref int latched)
    {
        if (!autoSideByViewer) return magnitude;

        if (latched == 0)
        {
            int viewer = ViewerSide(anatomicalAxis);
            if (viewer == 0) return magnitude;   // 정중선 근처 — 판단 보류, 수동 부호를 쓴다
            latched = -viewer;                   // 반대편
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=cyan>[ROM 각도기] 시술자가 {(viewer > 0 ? "+" : "−")}쪽에 있어 " +
                                $"면을 {(latched > 0 ? "+" : "−")}쪽에 놓는다</color>");
            }
        }
        return Mathf.Abs(magnitude) * latched;
    }

    /// <summary>보는 사람이 그 축의 어느 쪽에 있는가. 정중선 근처면 0(판단 보류).</summary>
    private int ViewerSide(Vector3 anatomicalAxis)
    {
        Camera cam = Camera.main;
        if (cam == null || driver.Torso == null) return 0;

        float d = Vector3.Dot(cam.transform.position - driver.Torso.position, anatomicalAxis);
        if (Mathf.Abs(d) < sideDeadZone) return 0;
        return d > 0f ? 1 : -1;
    }

    private enum PlaneGroup { Sagittal, Coronal, Transverse }

    /// <summary>
    /// 지금 그려진 면의 반지름 배수. <see cref="BuildStatic"/>에서 한 번 정하고 그 뒤로는 읽기만 한다.
    /// ★<see cref="builtDirection"/>을 직접 보면 안 된다 — 그건 BuildStatic이 <b>끝난 뒤</b>에 대입된다.
    /// 기본 1이라 아직 한 번도 안 그린 상태에서도 예전과 똑같이 나온다.
    /// </summary>
    private float curRadiusFactor = 1f;

    /// <summary>지금 면에 적용된 눈금 반지름. 판·눈금숫자·현재각 위치가 전부 여기서 파생된다.</summary>
    private float CurScaleRadius => scaleRadius * curRadiusFactor;

    /// <summary>지금 면에 적용된 채움 부채꼴 반지름. 눈금과 같은 배수로 커져 비율이 유지된다.</summary>
    private float CurFillRadius => fillRadius * curRadiusFactor;

    /// <summary>
    /// 지금이 압박 유지 구간인가. <see cref="CervicalRomScenarioBridge"/>가 켜고 끈다.
    /// ★각도기가 스스로 알아낼 방법이 없다 — 드라이버는 능동이든 압박이든 그냥 각도만 들고 있다.
    /// </summary>
    private bool pressGuideOn;

    /// <summary>호가 지금 쓸려 가고 있는가. 켜져 있으면 메시를 매 프레임 다시 만들어야 한다.</summary>
    private bool PressArrowAnimating => showPressArrow && pressGuideOn
                                        && pressArrowSweepDeg > 0.01f && pressArrowSweepPerSecond > 0.01f;

    /// <summary>
    /// 압박 방향 화살표를 켜고 끈다. 압박 유지 substep에 들어갈 때 켜고, 복귀·다른 단계에서 끈다.
    /// ★능동 구간에서 켜면 안 된다 — 환자가 스스로 가는 구간이라 시술자에게 줄 지시가 없다.
    /// </summary>
    public void SetPressGuide(bool on)
    {
        if (pressGuideOn == on) return;
        pressGuideOn = on;
        lastDrawnAngle = float.NaN;   // 켜지든 꺼지든 다음 프레임에 다시 그리게 한다
    }

    /// <summary>면별 반지름 배수. 횡단면만 따로 키운다(머리에 가려서).</summary>
    private float RadiusFactorOf(CervicalRomDriver.Direction d)
    {
        if (PlaneGroupOf(d) != PlaneGroup.Transverse) return 1f;
        return Mathf.Max(0.01f, transverseRadiusScale);
    }

    private static PlaneGroup PlaneGroupOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return PlaneGroup.Sagittal;
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return PlaneGroup.Coronal;
            default:
                return PlaneGroup.Transverse;
        }
    }

    /// <summary>
    /// 지침 색. 면 색과 같은 계열의 진한 원색으로 뽑는다 —
    /// 면 색의 색상(H)만 가져오고 채도·명도를 올려 판 위에서 또렷하게 만든다.
    /// (판은 알파 0.18로 옅게 깔리므로 같은 색이라도 겹쳐 보이지 않는다)
    /// </summary>
    private Color NeedleColorOf(CervicalRomDriver.Direction d)
    {
        if (!needleFromPlaneColor) return needleColor;

        Color.RGBToHSV(PlaneColorOf(d), out float h, out _, out _);
        Color c = Color.HSVToRGB(h,
                                 Mathf.Clamp01(needleSaturationValue.x),
                                 Mathf.Clamp01(needleSaturationValue.y));
        c.a = needleColor.a;
        return c;
    }

    private Color PlaneColorOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return sagittalColor;
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return coronalColor;
            default:
                return transverseColor;
        }
    }

    private string PlaneNameOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return "시상면";
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return "관상면";
            default:
                return "횡단면";
        }
    }

    // ── 면 안의 좌표 ──────────────────────────────────────────────────────
    // root의 로컬에서 axis = +z, zeroDir = +y다(LookRotation(axis, zeroDir)).
    // 그래서 각도 θ의 방향은 z축 둘레로 y를 돌린 것이고, 면은 xy 평면이 된다.

    private static Vector3 Dir(float degrees)
    {
        // ★x가 음수다. root가 LookRotation(axis, zeroDir)라 로컬 +z=axis · +y=zeroDir이고,
        //   머리는 Quaternion.AngleAxis(θ, axis)로 돌아간다. 그 회전을 로컬로 쓰면
        //   (0,1,0) → (−sinθ, cosθ, 0)이다. 예전엔 +sin을 써서 눈금과 지침이
        //   머리와 반대로 돌았다 — 사용자가 "좌우 앞뒤가 반대"로 본 것이 이것이다.
        float r = degrees * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f);
    }

    /// <summary>판·눈금. 방향이나 최대각이 바뀔 때만 다시 만든다.</summary>
    private void BuildStatic(CervicalRomDriver.Direction dir, float maxAngle)
    {
        verts.Clear(); colors.Clear(); tris.Clear();

        // ★제일 먼저 정한다. 아래 전부가 CurScaleRadius·CurFillRadius를 읽는다.
        curRadiusFactor = RadiusFactorOf(dir);

        Color plane = PlaneColorOf(dir);

        if (showPlane)
        {
            Color fill = plane; fill.a = planeAlpha;
            // ★자동 맞춤이면 숫자 자리(반지름 + 오프셋)보다 바깥까지 판을 넓힌다.
            //   손으로 잡으면 숫자가 판을 넘어간다 — 사용자가 본 그 현상이다.
            float hx, hy;
            if (autoFitPlane)
            {
                hx = hy = CurScaleRadius + labelOffset + planeMargin;
            }
            else
            {
                // ★손으로 잡은 크기도 면 배수를 따라간다. 안 그러면 횡단면에서 눈금만 커지고
                //   판은 그대로라 숫자와 현재각이 판 밖으로 삐져나간다
                //   (씬은 autoFitPlane이 꺼져 있고 planeSize가 1.1m — 반지름을 1.5배 하면 넘친다).
                hx = planeSize.x * 0.5f * curRadiusFactor;
                hy = planeSize.y * 0.5f * curRadiusFactor;
            }
            // 세로 치우침도 같은 배수로. 판 안에서 눈금이 앉는 자리가 그대로 유지된다.
            float lift = planeLift * curRadiusFactor;
            AddQuad(new Vector3(-hx, lift - hy, 0f), new Vector3(hx, lift - hy, 0f),
                    new Vector3(hx, lift + hy, 0f), new Vector3(-hx, lift + hy, 0f), fill);
        }

        Color tick = plane; tick.a = 0.95f;
        Color micro = plane; micro.a = 0.55f;   // 미세눈금은 옅게 — 촘촘해서 진하면 띠로 뭉친다
        Color maxMark = maxAngleMarkColor;

        // ★십자선을 제일 밑에 깐다. 회전 중심이 어디인지가 한눈에 보여야 각도가 읽힌다.
        if (showCrosshair)
        {
            Color cross = plane; cross.a = crosshairAlpha;
            float arm = crosshairLength > 0f ? crosshairLength : CurScaleRadius;
            for (int q = 0; q < 4; q++) AddArm(q * 90f, arm, crosshairWidth, cross);
        }

        // ★눈금은 방향의 최대각과 무관하게 scaleSpan(기본 90°)까지 깐다.
        //   각도기처럼 눈금이 먼저 있고 그 위에 마지노선이 그어지는 게 읽기 쉽다.
        float span = Mathf.Max(maxAngle, scaleSpan);

        // 미세 → 보조 → 주 순으로 겹쳐 그린다. 굵은 눈금이 위에 온다.
        if (microStep > 0f)
        {
            for (float a = 0f; a <= span + 0.001f; a += microStep)
            {
                if (OnStep(a, minorStep) || OnStep(a, majorStep)) continue;
                AddTick(a, microTickLength, tickWidth * 0.7f, micro);
            }
        }
        for (float a = 0f; a <= span + 0.001f; a += minorStep)
        {
            if (OnStep(a, majorStep)) continue;
            AddTick(a, minorTickLength, tickWidth, tick);
        }
        for (float a = 0f; a <= span + 0.001f; a += majorStep)
        {
            AddTick(a, majorTickLength, tickWidth * 1.35f, tick);
        }
        // 최대각(마지노선)은 눈금 사이에 안 떨어질 수 있다(예: 45°와 10° 간격). 따로 긋는다.
        // ★지침과 같은 꼴 — 중심에서 눈금 반지름까지 통짜 직선이다(fromCenter).
        AddTick(maxAngle, CurScaleRadius, maxAngleMarkWidth, maxMark, fromCenter: true);

        Upload(staticMesh, staticFilter);
        BuildTickLabels(span, maxAngle, plane);

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[ROM 각도기] {PlaneNameOf(dir)} — {dir} · 0~{maxAngle:F0}° · " +
                            $"주눈금 {majorStep:F0}° · 반지름 {CurScaleRadius:F2}m</color>");
        }
    }

    /// <summary>채움 세 구간 + 지침. 각도가 변하면 다시 만든다.</summary>
    private void BuildDynamic(float angle, float activeLimit, float passiveLimit, float maxAngle)
    {
        verts.Clear(); colors.Clear(); tris.Clear();

        // 채움은 겹치지 않게 구간을 나눠 그린다. 안쪽부터 바깥으로 읽힌다.
        AddSector(0f, Mathf.Min(angle, activeLimit), CurFillRadius, activeFillColor);

        if (angle > activeLimit)
        {
            AddSector(activeLimit, Mathf.Min(angle, passiveLimit), CurFillRadius, pressFillColor);
        }

        // 부족각 — 압박 한계에서 최대각까지. 이게 결과로 읽을 값이다.
        bool revealDeficit = showDeficitFromStart || angle >= passiveLimit - 0.5f;
        if (revealDeficit && maxAngle > passiveLimit + 0.05f)
        {
            AddSector(passiveLimit, maxAngle, CurFillRadius, deficitFillColor);
        }

        AddTick(angle, CurScaleRadius, needleWidth, NeedleColorOf(builtDirection), fromCenter: true);

        // 압박 방향 화살표는 지침 위에 얹는다 — 지침이 '지금 어디'고 화살표가 '어느 쪽으로 더'다.
        AddPressArrow(angle, maxAngle);

        Upload(dynamicMesh, dynamicFilter);
    }

    /// <summary>
    /// 압박 방향 화살표 — 지침에서 <b>진행 방향으로</b> 뻗는 호 + 화살촉.
    ///
    /// ★목표 지점을 가리키지 않는다. 지침을 따라다니며 "이 방향으로 더"만 말한다.
    ///   압박 한계(<c>passiveLimit</c>)를 가리키면 <b>끝느낌의 정답을 미리 알려주는 셈</b>이라
    ///   측정이 무의미해진다 — 끝느낌은 손으로 찾아야 하는 것이다.
    ///   눈금이 깔린 범위 밖으로는 안 나간다.
    ///
    /// ★한 방향으로만 쓸어간다. 왕복하면 '반대로도 민다'로 읽혀 방향이 흐려진다.
    ///   되돌아가는 순간은 양 끝 밝기를 죽여 감춘다(힘의 방향 화살표에서 쓰던 것과 같은 수법).
    /// </summary>
    private void AddPressArrow(float angle, float maxAngle)
    {
        if (!showPressArrow || !pressGuideOn) return;
        if (pressArrowSpanDeg <= 0.1f) return;

        float limit = Mathf.Max(maxAngle, scaleSpan);          // 눈금이 깔린 데까지만
        float radius = CurScaleRadius * pressArrowRadiusScale;

        // 한 방향 쓸기: 0 → 1을 반복하며 그만큼 앞으로 밀어 놓는다.
        float phase = 0f, fade = 1f;
        if (pressArrowSweepDeg > 0.01f && pressArrowSweepPerSecond > 0.01f)
        {
            phase = Mathf.Repeat(Time.time * pressArrowSweepPerSecond, 1f);
            if (pressArrowFadeEnds > 0.001f)
            {
                // 시작·끝 구간에서만 밝기를 올렸다 내린다. 가운데는 1로 평평하다.
                float e = pressArrowFadeEnds;
                fade = Mathf.Clamp01(Mathf.Min(phase, 1f - phase) / e);
            }
        }

        float start = angle + phase * pressArrowSweepDeg;
        float end = Mathf.Min(start + pressArrowSpanDeg, limit);
        if (end - start < pressArrowHeadDeg + 0.5f) return;    // 자리가 없으면 아예 안 그린다

        Color c = pressArrowFromPlaneColor ? NeedleColorOf(builtDirection) : pressArrowColor;
        c.a *= Mathf.Lerp(pressArrowMinAlpha, 1f, fade);

        float headBase = end - pressArrowHeadDeg;
        AddArcBand(start, headBase, radius, pressArrowWidth, c);
        AddArcHead(headBase, end, radius, pressArrowHeadWidth, c);
    }

    /// <summary>호를 따라가는 띠. 1~2도마다 한 조각씩 이어 붙인다.</summary>
    private void AddArcBand(float fromDeg, float toDeg, float radius, float width, Color color)
    {
        float sweep = toDeg - fromDeg;
        if (sweep < 0.05f || width <= 0f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(sweep * 0.5f));
        float step = sweep / steps;
        float rIn = Mathf.Max(0f, radius - width * 0.5f);
        float rOut = radius + width * 0.5f;

        for (int i = 0; i < steps; i++)
        {
            Vector3 d0 = Dir(fromDeg + step * i);
            Vector3 d1 = Dir(fromDeg + step * (i + 1));
            AddQuad(d0 * rIn, d1 * rIn, d1 * rOut, d0 * rOut, color);
        }
    }

    /// <summary>화살촉. 밑변은 호를 가로지르고 꼭짓점은 진행 방향 끝에 놓인다.</summary>
    private void AddArcHead(float baseDeg, float tipDeg, float radius, float width, Color color)
    {
        Vector3 baseDir = Dir(baseDeg);
        Vector3 tip = Dir(tipDeg) * radius;
        Vector3 left = baseDir * (radius - width * 0.5f);
        Vector3 right = baseDir * (radius + width * 0.5f);

        // 삼각형 하나. AddQuad에 꼭짓점을 겹쳐 넣으면 퇴화 삼각형이 하나 붙지만
        // Sprites/Default는 Cull Off라 문제되지 않고, 전용 AddTri를 새로 만들 이유가 없다.
        AddQuad(left, tip, tip, right, color);
    }

    /// <summary>지침 끝의 현재 각도 숫자. 정수가 바뀔 때만 문자열을 새로 만든다.</summary>
    private void UpdateReadout(float angle)
    {
        if (readout == null) return;

        // ★유지 남은 초는 여기 안 쓴다 — ProgressCircle이 그린다(2026-08-26 사용자 지시).
        //   같은 값을 두 군데 띄우면 시선만 갈린다.
        int degrees = Mathf.RoundToInt(angle);
        if (degrees != lastReadoutDegrees)
        {
            lastReadoutDegrees = degrees;
            // ★문자열 생성은 정수가 바뀔 때만. 매 프레임 만들면 VR에서 GC가 튄다.
            // 최대각은 눈금 위에 이미 있으므로 여기엔 현재각만 쓴다.
            readout.text = $"{degrees}°";
        }

        // ★눈금 숫자보다 확실히 바깥에 둔다. 가까우면 눈금 라벨과 겹친다.
        Vector3 local = Dir(angle) * (CurScaleRadius + readoutOffset);
        readout.transform.localPosition = local;
        FaceCamera(readout.transform);

        // 원형 배경은 글씨와 같은 자세로, 카메라에서 조금 더 먼 쪽에 둔다.
        if (readoutBackdrop != null)
        {
            bool show = readoutBackdropRadius > 0f;
            if (readoutBackdrop.gameObject.activeSelf != show) readoutBackdrop.gameObject.SetActive(show);
            if (show)
            {
                readoutBackdrop.rotation = readout.transform.rotation;
                // readout의 로컬 +z가 카메라 반대쪽이다(FaceCamera가 그렇게 잡는다).
                // ★밀지 않는다. 카메라가 반대편에 있으면 4mm 민 배경이 글씨를 덮는다.
                readoutBackdrop.localPosition = local;
                readoutBackdrop.localScale = Vector3.one * readoutBackdropRadius;
            }
        }
    }

    // ── 메시 조립 ─────────────────────────────────────────────────────────

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    /// <summary>눈금 하나. fromCenter면 중심에서 뻗는 지침이 된다.</summary>
    private void AddTick(float degrees, float length, float width, Color color, bool fromCenter = false)
    {
        Vector3 dir = Dir(degrees);
        Vector3 side = new Vector3(dir.y, -dir.x, 0f) * (width * 0.5f);
        float r = CurScaleRadius;
        Vector3 outer = dir * r;
        Vector3 inner = fromCenter ? Vector3.zero : dir * (r - length);
        AddQuad(inner - side, outer - side, outer + side, inner + side, color);
    }

    /// <summary>회전 중심에서 뻗는 팔. 십자선을 그리는 데 쓴다.</summary>
    private void AddArm(float degrees, float length, float width, Color color)
    {
        Vector3 dir = Dir(degrees);
        Vector3 side = new Vector3(dir.y, -dir.x, 0f) * (width * 0.5f);
        Vector3 tip = dir * length;
        AddQuad(-side, tip - side, tip + side, side, color);
    }

    /// <summary>부채꼴. 1도마다 한 조각씩 이어 붙인다.</summary>
    private void AddSector(float fromDeg, float toDeg, float radius, Color color)
    {
        if (toDeg - fromDeg < 0.05f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(toDeg - fromDeg));
        float span = (toDeg - fromDeg) / steps;

        int center = verts.Count;
        verts.Add(Vector3.zero);
        colors.Add(color);

        for (int i = 0; i <= steps; i++)
        {
            verts.Add(Dir(fromDeg + span * i) * radius);
            colors.Add(color);
        }
        for (int i = 0; i < steps; i++)
        {
            tris.Add(center);
            tris.Add(center + 1 + i);
            tris.Add(center + 2 + i);
        }
    }

    private void Upload(Mesh mesh, MeshFilter filter)
    {
        mesh.Clear();
        if (verts.Count == 0) { filter.sharedMesh = mesh; return; }
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0, false);
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    // ── 하위 오브젝트 ─────────────────────────────────────────────────────

    private void BuildHierarchy()
    {
        sharedMaterial = CreateMaterial();

        root = new GameObject("경추ROM_각도기").transform;
        root.SetParent(transform, false);
        Ephemeral(root.gameObject);

        staticMesh = new Mesh { name = "각도기_판눈금" };
        staticMesh.MarkDynamic();
        Ephemeral(staticMesh);
        staticFilter = CreateLayer("판·눈금", root);

        dynamicMesh = new Mesh { name = "각도기_채움지침" };
        dynamicMesh.MarkDynamic();
        Ephemeral(dynamicMesh);
        dynamicFilter = CreateLayer("채움·지침", root);

        // ★배경을 먼저 만든다. 같은 렌더 큐면 나중에 그려진 쪽이 위로 오므로,
        //   글씨보다 앞서 만들어 두고 큐도 한 단계 낮춘다.
        readoutBackdrop = CreateReadoutBackdrop(root);
        readout = CreateLabel("현재각도", root, readoutSize);
        readout.color = readoutColor;
    }

    /// <summary>
    /// 이 오브젝트·에셋은 <b>씬에 저장되지 않는다</b>. 각도기 생성물은 매번 다시 만들면
    /// 되는 것들이라, 씬에 굳으면 이득 없이 파일만 오염된다.
    /// </summary>
    private static void Ephemeral(Object target)
    {
        if (target != null) target.hideFlags = HideFlags.DontSave;
    }

    private MeshFilter CreateLayer(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Ephemeral(go);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = sharedMaterial;
        renderer.sortingOrder = OrderPlane;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return filter;
    }

    /// <summary>
    /// 현재각 숫자 뒤에 깔 원판. 반지름 1의 원을 만들어 두고 스케일로 크기를 준다.
    /// 글씨와 같은 자세로 돌리되 카메라에서 조금 더 먼 쪽에 놓는다.
    /// </summary>
    private Transform CreateReadoutBackdrop(Transform parent)
    {
        var go = new GameObject("현재각도_배경");
        go.transform.SetParent(parent, false);
        Ephemeral(go);

        const int segments = 32;
        var v = new List<Vector3>(segments + 2) { Vector3.zero };
        var c = new List<Color>(segments + 2) { readoutBackdropColor };
        var t = new List<int>(segments * 3);
        for (int i = 0; i <= segments; i++)
        {
            float r = i * Mathf.PI * 2f / segments;
            v.Add(new Vector3(Mathf.Cos(r), Mathf.Sin(r), 0f));
            c.Add(readoutBackdropColor);
        }
        for (int i = 1; i <= segments; i++) { t.Add(0); t.Add(i); t.Add(i + 1); }

        var mesh = new Mesh { name = "각도기_현재각배경" };
        mesh.SetVertices(v);
        mesh.SetColors(c);
        mesh.SetTriangles(t, 0, false);
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        // 글씨보다 먼저 그려지게 큐를 한 단계 낮춘다(같은 큐면 겹칠 때 순서가 안 정해진다).
        // ★큐를 낮춰 '먼저 그리기'로는 안 된다 — 그러면 같은 큐의 면 판이 나중에 그려져
        //   배경을 덮는다. 실제로 면마다 배경이 보였다 안 보였다 했다(2026-08-26 사용자 지적).
        //   순서는 sortingOrder로 못박는다: 판·눈금 < 배경 < 글씨.
        renderer.sharedMaterial = new Material(sharedMaterial) { name = "GaugeReadoutBackdropMat" };
        renderer.sortingOrder = OrderBackdrop;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        Ephemeral(mesh);
        Ephemeral(renderer.sharedMaterial);
        backdropMesh = mesh;
        backdropMaterial = renderer.sharedMaterial;
        return go.transform;
    }

    private TextMeshPro CreateLabel(string name, Transform parent, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Ephemeral(go);
        var text = go.AddComponent<TextMeshPro>();
        if (font != null) text.font = font;
        var tmpRenderer = go.GetComponent<MeshRenderer>();
        if (tmpRenderer != null) tmpRenderer.sortingOrder = OrderLabel;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size * 100f;   // TMP는 월드 단위가 아니라 폰트 크기로 잡는다
        text.transform.localScale = Vector3.one * 0.01f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// 주눈금 숫자. 눈금 범위(span) 전체에 붙이고, 그 위에 최대각만 굵게 덧붙인다.
    /// span과 최대각이 둘 다 바뀔 수 있어 둘 다 받는다(굴곡 45° vs 회전 90°).
    /// </summary>
    /// <summary>최대각 숫자 색. 마지노선과 같은 색으로 묶어 읽는다.</summary>
    private Color MaxLabelColor
    {
        get { Color c = maxAngleMarkColor; c.a = 1f; return c; }
    }

    private void BuildTickLabels(float span, float maxAngle, Color color)
    {
        int needed = Mathf.FloorToInt(span / majorStep) + 1;
        bool maxOnMajor = Mathf.Abs(maxAngle % majorStep) < 0.001f;
        bool addMaxLabel = showMaxAngleLabel && !maxOnMajor;
        if (addMaxLabel) needed++;   // 최대각 숫자를 따로 붙인다

        while (tickLabels.Count < needed)
        {
            tickLabels.Add(CreateLabel($"눈금{tickLabels.Count}", root, tickLabelSize));
        }

        Color labelColor = color; labelColor.a = 1f;
        int index = 0;
        for (float a = 0f; a <= span + 0.001f; a += majorStep, index++)
        {
            // 최대각과 겹치는 눈금만 굵게. 표시를 끄면 전부 보통 숫자다.
            bool isMax = showMaxAngleLabel && Mathf.Abs(a - maxAngle) < 0.001f;
            PlaceTickLabel(index, a, isMax ? MaxLabelColor : labelColor, bold: isMax);
        }
        if (addMaxLabel)
        {
            PlaceTickLabel(index, maxAngle, MaxLabelColor, bold: true);
            index++;
        }
        for (int i = index; i < tickLabels.Count; i++) tickLabels[i].gameObject.SetActive(false);
    }

    private void PlaceTickLabel(int index, float degrees, Color color, bool bold)
    {
        if (index >= tickLabels.Count) return;
        TextMeshPro label = tickLabels[index];
        label.gameObject.SetActive(true);
        label.text = bold ? $"<b>{degrees:F0}°</b>" : $"{degrees:F0}";
        label.color = color;
        label.transform.localPosition = Dir(degrees) * (CurScaleRadius + labelOffset);
    }

    /// <summary>숫자는 늘 보는 사람 쪽을 향한다. 면에 눕히면 옆에서 읽을 수 없다.</summary>
    /// <summary>
    /// 글씨를 Y축으로만 뒤집을지 정한다. ★면에 들어갈 때 한 번 정하고 고정한다 —
    /// 매 프레임 보면 시술자가 면 근처를 오갈 때 글씨가 깜빡 뒤집힌다.
    /// </summary>
    private void UpdateLabelYaw()
    {
        if (!autoFlipLabels) { labelFlip = false; return; }

        Camera cam = ViewerCamera();
        if (cam == null) return;   // 못 정하면 직전 값을 그대로 쓴다

        // ★매 프레임 다시 본다. 면 단위로 고정하면 안 된다 —
        //   굴곡과 신전은 같은 시상면인데 회전축 부호가 반대라(AxisOf: 굴곡 −X / 신전 +X)
        //   root.forward가 정반대를 가리킨다. 한 번 정해 물려주면 둘 중 하나는
        //   반드시 뒤집힌다(2026-08-26 사용자 지적 — 180을 넣어도 같이 뒤집힐 뿐이었다).
        float d = Vector3.Dot(root.forward, cam.transform.position - root.position);

        // 다만 면에 딱 붙어 설 때 깜빡이지 않게 히스테리시스를 준다.
        // 지금 상태를 뒤집으려면 반대쪽으로 이만큼은 넘어가야 한다.
        if (labelFlip) { if (d > flipHysteresis) labelFlip = false; }
        else           { if (d < -flipHysteresis) labelFlip = true; }
    }

    private void FaceCamera(Transform t)
    {
        if (!labelsFaceViewer)
        {
            // ★횡단면만 따로 — 숫자를 호를 따라 돌린다(면 위에 눕는 건 그대로).
            //   글씨의 위쪽을 <b>중심에서 바깥으로 뻗는 반지름 방향</b>으로 잡으면,
            //   글씨가 읽히는 방향(오른쪽)이 자동으로 그 반지름과 수직 = 호의 접선이 된다.
            //   시상면·관상면은 전부 같은 방향으로 눕는 게 읽기 좋아 손대지 않는다.
            if (transverseLabelsAlongArc && PlaneGroupOf(builtDirection) == PlaneGroup.Transverse)
            {
                Vector3 radial = Vector3.ProjectOnPlane(t.position - root.position, root.forward);
                if (radial.sqrMagnitude > 1e-8f)
                {
                    Vector3 arcFacing = labelFlip ? -root.forward : root.forward;
                    t.rotation = Quaternion.LookRotation(arcFacing, radial.normalized);
                    if (labelYawOffset != 0f) t.rotation *= Quaternion.Euler(0f, labelYawOffset, 0f);
                    return;
                }
            }

            // ★월드에서 직접 자세를 만든다. localRotation으로 Y를 돌리면 <b>부모(root)의 Y축</b>으로
            //   도는데, root의 Y는 면 안쪽 0° 방향이지 글씨의 위쪽이 아니다 —
            //   그래서 엉뚱한 축으로 돌아 뒤집힘이 안 풀렸다(2026-08-26, 세 번 헛돌았다).
            //
            //   위쪽(up)은 면 안쪽 방향으로 <b>그대로 두고</b>, 앞쪽(글씨가 읽히는 쪽)만
            //   보는 사람 편으로 잡는다. up이 안 바뀌므로 결과가 정확히 'Y축 180°만 돌린 것'이 된다.
            Vector3 up = root.up;                                   // 면 안쪽 위 — 기울지 않게 고정
            Vector3 facing = labelFlip ? -root.forward : root.forward;
            t.rotation = Quaternion.LookRotation(facing, up);
            if (labelYawOffset != 0f) t.rotation *= Quaternion.Euler(0f, labelYawOffset, 0f);
            return;
        }

        Camera cam = ViewerCamera();
        if (cam == null) return;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, cam.transform.up);
    }

    /// <summary>
    /// 글씨가 향할 카메라. ★에디터에서는 <b>씬 뷰 카메라</b>다 —
    /// Camera.main을 쓰면 사람이 보는 각도와 어긋나 숫자가 옆을 본다.
    /// </summary>
    private static Camera ViewerCamera()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.SceneView sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera;
        }
#endif
        return Camera.main;
    }

    private void SetVisible(bool visible)
    {
        if (root == null || root.gameObject.activeSelf == visible) return;
        root.gameObject.SetActive(visible);
    }

    /// <summary>
    /// ★프로젝트 관례 — Sprites/Default. 알파 블렌드가 셰이더에 고정돼 있어
    ///   맞출 상태가 없고, UI가 늘 쓰므로 빌드에서 스트립되지 않는다.
    ///   정점 색을 그대로 곱하므로 머티리얼 하나로 판·눈금·채움을 다 그릴 수 있다.
    /// </summary>
    private Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            ChunaLogger.LogWarning("[ROM 각도기] Sprites/Default를 찾지 못했습니다. 각도기가 안 보일 수 있습니다.");
            shader = Shader.Find("Standard");
        }
        var mat = new Material(shader) { name = "CervicalRomGaugeMat", renderQueue = 3000 };
        Ephemeral(mat);
        return mat;
    }

    private void LateUpdateLabels()
    {
        for (int i = 0; i < tickLabels.Count; i++)
        {
            if (tickLabels[i].gameObject.activeSelf) FaceCamera(tickLabels[i].transform);
        }
    }

    private void OnEnable()
    {
        lastReadoutDegrees = int.MinValue;
#if UNITY_EDITOR
        // ★씬 뷰가 다시 그려질 때마다 플레이어 루프를 한 번 태운다. 이게 없으면
        //   에디터에서 시점을 돌려도 LateUpdate가 안 불려 숫자가 옆을 본 채 굳는다.
        UnityEditor.SceneView.duringSceneGui -= OnSceneGui;
        UnityEditor.SceneView.duringSceneGui += OnSceneGui;
#endif
    }

#if UNITY_EDITOR
    private void OnSceneGui(UnityEditor.SceneView view)
    {
        if (!Application.isPlaying && previewInEditor && isActiveAndEnabled)
        {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
    }
#endif

    private void Update()
    {
        // 숫자만 매 프레임 보는 사람 쪽으로 돌린다. 메시는 건드리지 않는다.
        if (root != null && root.gameObject.activeSelf) LateUpdateLabels();
    }
}
