using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM 실측 기록 프로그램 — 본체(2026-09-18 신설, 실측 전용 씬 <c>RomMarkerScene</c>에서 돈다).
///
/// 설계(2026-09-18 사용자와 확정 — 메모리 rom-marker-lab)
///   · <b>패스스루 전용</b>. 실제 사람 대상. 센서가 재는 도구가 아니라 <b>진단 기록 보조 + 3축 시각화</b>.
///   · 시작하면 지면 연직·수평 3축이 뜬다 → Y 회전·높이 조절 또는 대추 지정으로 자리를 잡고 → <b>환자를 축에 정렬</b>시킨다.
///   · 대추·미간(중립) 지정. 기준축은 목 둘레 중앙으로 앞뒤 보정할 수 있다(미간은 그대로).
///   · 4단계: 굴곡·신전(1회째 능동, 2회째 수동), 측굴·회전(4회, 좌우 자동, 홀수 능동·짝수 수동).
///   · ★09-28 바늘 개편: 바늘은 <b>정상가동범위 최대값</b>(굴곡 45·신전 90·측굴 45·회전 90)에서 시작하고,
///     <b>굴곡→신전→우측굴→좌측굴→우회전→좌회전</b> 순서로 방향마다 능동·압박을 맞춘다(탭 3개 유지).
///     놓으면 저절로 잠기고(오토 홀드) 구체를 검지로 치면 풀린다. 잠긴 뒤엔 바늘 위 판에서 ±0.1°로 다듬는다.
///     각도기는 눈금 0을 가운데 둔 반원 180°(위쪽 / 회전은 얼굴 쪽)만 그리고 숫자는 10°마다다.
///   · 값 = 중립선에서 움직인 양(양수). 각도기 눈금 0은 단면 연직(회전은 정면).
///   · 마커: 핀치를 오므리면 생기고 펴면 고정. 동작 마커는 1° 단위 수정, 대추·미간은 mm 단위.
///   · ★09-22: 기준점은 대추·외이도·미간 셋(목중앙 보정 폐지). 굴곡·신전·회전은 <b>외이도(정중면으로 옮긴 점)</b>,
///     측굴은 대추를 축으로 잰다(교과서 각도계 축과 같다 — Norkin & White: 굴곡·신전 외이도, 측굴 C7).
///     대추 기준 각도 <b>같이</b> 기록한다 — 축에 따라 값이 얼마나 흔들리는지 나중에 비교한다.
///
/// ★기존 실측(경추ROM실측 시나리오)·실측랩과 <b>서로 참조하지 않는다</b>. 이 씬 전용이다.
/// ★기록 줄은 <c>Debug.Log</c>로 남긴다 — <c>ChunaLogger.Log</c>는 릴리스 빌드에서 잘린다(Conditional).
/// </summary>
public class RomRecordSession : MonoBehaviour
{
    [Header("=== 시작 자리 ===")]
    [Tooltip("시작할 때 3축을 눈앞 이만큼(m) 앞에 둔다.")]
    [SerializeField] private float startDistance = 0.7f;
    [Tooltip("시작할 때 3축을 눈높이보다 이만큼(m) 낮게 둔다.")]
    [SerializeField] private float startBelowEye = 0.3f;
    [Tooltip("3축 선 길이(m, 중심에서 한쪽).")]
    [SerializeField] private float axisLength = 0.25f;

    [Header("=== 크기(글자는 TMP 폰트 크기, 스케일 1) ===")]
    [Tooltip("축 끝 글자·마커 각도 글자·눈금 숫자(0.8배). ★기존 각도기 판독 0.05(기기 확인값).\n" +
             "09-18: 1.1(너무 큼) → 0.03(\"각도기도 텍스트도 너무 줄였다\") → 0.05.\n" +
             "09-22: 0.08(사용자: 작은 글씨가 잘 안 보인다 — 씬 값을 브리지로 바꿨다).")]
    [SerializeField] private float textSize = 0.08f;
    [Tooltip("★09-22부터 안 쓴다 — 떠 있던 안내판을 없애고 측정값을 조작 판으로 옮겼다. 옛 안내판 글자 크기.")]
    [SerializeField] private float panelTextSize = 0.045f;
    [Tooltip("손목 판의 버튼 글자·머리줄 글자.")]
    [SerializeField] private float buttonLabelSize = 0.03f;

    [Header("=== 손목 판 — Meta UI Set(2026-09-21) ===")]
    // ★09-21 기기 판정: Meta UI 판은 <b>퇴화</b>였다(사용자) — 버튼 크기가 들쭉날쭉하고, 배치가 헐겁고,
    //   무엇보다 <b>누르는 손에 판이 가려</b> 안 보였다. 그래서 기본을 끔으로 내린다.
    //   지우지는 않는다 — 크기·배치 문제를 풀면 글자 크기 이점(Meta 표준 7mm)이 여전히 크다.
    [Tooltip("Meta Interaction SDK의 UI Set 프리팹으로 판을 만든다. ★09-21 기기 확인에서 퇴화로 판정돼 기본은 끔이다.")]
    [SerializeField] private bool useMetaUI = false;
    [Tooltip("Meta 판의 칸 너비(캔버스 px). 캔버스 스케일이 0.0005라 1px = 0.5mm다 — 84px ≈ 4.2cm.")]
    [SerializeField] private float metaCellPx = 84f;
    [Tooltip("Meta 판의 줄 높이(px).")]
    [SerializeField] private float metaRowPx = 68f;
    [Tooltip("Meta 판의 버튼 높이(px).")]
    [SerializeField] private float metaButtonPx = 56f;
    [Tooltip("Meta 판의 글자 크기(pt). ★Meta 표준은 14 — 캔버스 스케일을 곱하면 7mm다.")]
    [SerializeField] private float metaLabelPt = 15f;

    [Header("=== 각도기·바늘 모양(2026-09-21) ===")]
    // ★사용자 지시 09-21: "각도기도 하얀색 말고 회색 계열로 조금 시선 분산 덜 되게 · 바늘 선 얇게 ·
    //   각도 텍스트는 직선이나 원이 안 겹치게 · 회전 각도는 원통형으로 머리 둘레에 벽면처럼."
    // ★여기 값은 전부 <b>눈으로 맞출 것</b>이라 인스펙터로 뺀다(기기 미검증).
    [Tooltip("눈금판 원·원통 테두리 색.")]
    [SerializeField] private Color dialColor = new Color(0.60f, 0.62f, 0.65f, 0.55f);
    [Tooltip("눈금 색.")]
    [SerializeField] private Color dialTickColor = new Color(0.64f, 0.66f, 0.69f, 0.60f);
    [Tooltip("눈금 0 색. ★기준선이라 다른 눈금보다 또렷하게 둔다.")]
    [SerializeField] private Color dialZeroColor = new Color(0.88f, 0.90f, 0.93f, 0.95f);
    [Tooltip("눈금 숫자 색.")]
    [SerializeField] private Color dialLabelColor = new Color(0.80f, 0.82f, 0.85f, 0.95f);
    [Tooltip("바늘 선 굵기(m). 09-21에 0.007에서 줄였다.")]
    [SerializeField] private float needleWidth = 0.003f;
    [Tooltip("눈금 숫자를 원 밖으로 미는 배수(반지름 기준).")]
    [SerializeField] private float tickLabelOut = 1.20f;
    [Tooltip("회전 단계 원통 벽의 높이(m, 위아래 합). ★추정값이다 — 머리에 맞는지 눈으로 볼 것.")]
    [SerializeField] private float cylinderHeight = 0.30f;
    [Tooltip("원통 벽의 반지름 배수. 1이면 각도기 반지름(회전중심→미간)과 같아 머리에 바짝 붙는다.\n" +
             "★09-21 사용자 지시로 키웠다. 눈으로 맞출 값이다.")]
    [SerializeField] private float cylinderRadiusScale = 2.2f;

    [Header("=== 판 모양(2026-09-21) ===")]
    // ★판 디자인은 진행Root·설정 팝업을 재서 그대로 옮겼다(RomRecordWristMenu 주석에 실측값이 있다).
    //   그 값들은 코드에 두고, <b>눈으로 자주 만질 둘만</b> 여기로 뺀다.
    [Tooltip("판 배경의 불투명도. ★실측(저쪽 UI)은 1이지만 패스스루에서 답답하면 0.8쯤으로 내린다.")]
    [Range(0.2f, 1f)] [SerializeField] private float menuPlateAlpha = 1f;
    [Tooltip("버튼에 항목 색(tint)을 섞는 정도. 0이면 저쪽 UI처럼 완전 무채색이 된다.")]
    [Range(0f, 1f)] [SerializeField] private float menuTintStrength = 0.5f;

    [Header("=== 판 자리(2026-09-21) ===")]
    // ★사용자 지시 09-21: "UI 동작할 때 간섭이 너무 많다 — 진행ROOT 패널처럼 따로 분리해야겠다."
    //   손목을 따라다니면 <b>기록하는 손과 판이 같은 자리</b>에 있어 서로 막는다.
    //   09-21 로그 실측에서 핀치를 막은 주범이 판 자신이었다(판 근처 3,084 · 다가옴 3,708 프레임).
    [Tooltip("판을 손목이 아니라 <b>허공에 고정</b>한다(진행Root 방식). 끄면 종전처럼 양 손목을 따라다닌다.")]
    [SerializeField] private bool menuFixedInSpace = true;
    [Tooltip("고정 판을 처음 놓을 자리 — 눈앞 거리(m).")]
    [SerializeField] private float menuFixedDistance = 0.55f;
    [Tooltip("고정 판을 처음 놓을 자리 — 눈높이보다 아래로(m). ★09-21: 0.3은 너무 낮아 조작이 힘들었다.")]
    [SerializeField] private float menuFixedBelowEye = 0.14f;
    [Tooltip("고정 판을 처음 놓을 자리 — 정면에서 옆으로(m). 양수면 오른쪽. ★3축과 겹치지 않게 비켜 둔다.")]
    // ★판 기본 자리는 <b>우측 대각선</b>이다(2026-09-23 사용자). 양수 = 보는 방향의 오른쪽.
    //   ★씬에 값이 굳어 있어 코드 기본값은 새 씬에만 먹는다 — 지금 씬은 인스펙터 값을 직접 바꿨다(규칙 7).
    [SerializeField] private float menuFixedSide = 0.38f;
    // ★09-22 로그 실측: 판을 옮기려고 가장자리를 집었더니 <b>기준점이 찍혔다</b>(대추·미간이 판 중심에서 18~23cm).
    //   판 폭이 약 33cm라 중심 반경(0.16)으로는 가장자리가 안 잡히고, 못 잡은 핀치가 «찍기»로 빠졌다.
    //   «잡았다 — 조작 판» 로그는 한 번도 없었다. → 판 사각형 전체를 이 여유만큼 넓혀 잡는다.
    // ★같은 날 반대 지적: "놓은 줄 알았는데 꽤 멀리서 잡힌다"(사용자 확인 — 잡는 범위가 넓다는 뜻).
    //   옛 중심 반경 0.16m(menuGrabRadius)이 판 <b>앞 16cm</b>까지 구로 튀어나와 있었다 → 반경을 없애고 여유를 0.08→0.04로.
    [Tooltip("고정 판을 잡는 여유(m). 판 사각형 가장자리에서 이만큼 밖·앞뒤로 이만큼 안에서 오므리면 판이 잡힌다.")]
    [SerializeField] private float menuGrabMargin = 0.04f;

    [Header("=== 손목 판(종전 Quad 판) ===")]
    [Tooltip("판의 칸 너비(m). 판은 6칸 너비다.")]
    [SerializeField] private float menuCellWidth = 0.042f;
    [Tooltip("판의 줄 높이(m).")]
    [SerializeField] private float menuRowHeight = 0.034f;
    [Tooltip("버튼 높이(m).")]
    [SerializeField] private float menuButtonHeight = 0.026f;
    [Tooltip("손목 메뉴를 손목에서 위로 띄우는 높이(m).")]
    [SerializeField] private float menuLift = 0.06f;
    [Tooltip("누르는 손 검지가 판에서 이 거리(m) 안으로 오면 판이 손목을 따라가지 않고 멈춘다(가림으로 튀는 것 방지).")]
    [SerializeField] private float menuHoldDistance = 0.15f;
    [Tooltip("버튼·핀치 소리 크기(0이면 무음).")]
    [Range(0f, 1f)] [SerializeField] private float soundVolume = 0.6f;
    [Tooltip("기준점 안내 음성 크기(0이면 무음). 음성은 Resources/RomRecordUI/Voice — .claude/tools/rom_record_voice.py로 만든다.")]
    [Range(0f, 1f)] [SerializeField] private float voiceVolume = 1f;

    [Header("=== 조정 단위 ===")]
    [SerializeField] private float yawStepDeg = 1f;
    [SerializeField] private float heightStepMm = 5f;
    [Tooltip("대추·미간을 버튼으로 옮기는 단위(mm).")]
    [SerializeField] private float nudgeStepMm = 1f;
    [SerializeField] private float adjustStepDeg = 1f;

    [Header("=== 잡아 끌기(2026-09-21 신설) ===")]
    // ★09-21 로그 실측: 기준선 세팅에서 핀치가 17번 거부됐다("기준선 세팅 중에는 찍지 않는다").
    //   사용자는 3축을 손으로 잡아 옮기려 한 것이었다 — 5mm 버튼으로만 올리게 해 둬 한참 걸렸다.
    [Tooltip("3축(기준선)을 잡을 수 있는 반경(m). 이 안에서 핀치를 오므리면 3축이 손을 따라온다.")]
    [SerializeField] private float axesGrabRadius = 0.25f;
    [Tooltip("대추·미간 점을 잡을 수 있는 반경(m).")]
    [SerializeField] private float dotGrabRadius = 0.08f;

    [Header("=== 바늘 각도기(2026-09-21 신설) ===")]
    // ★사용자 지시 09-21: "미간 찍는 게 빡세면 각도기에 니들을 하나 그어서 그 끝을 잡고
    //   그 면에서만 회전하도록". ★점찍기를 <b>대체하지 않는다</b> — 둘 다 두고 따로 테스트한다.
    [Tooltip("동작 단계에서 바늘을 띄운다. 손목 판의 [바늘] 버튼으로도 껐다 켤 수 있다.")]
    [SerializeField] private bool needleEnabled = true;
    [Tooltip("바늘 손잡이를 잡을 수 있는 반경(m).")]
    [SerializeField] private float needleGrabRadius = 0.1f;
    [Tooltip("손잡이를 눈금판보다 이만큼(m) 더 밖에 둔다. ★0이면 손잡이가 머리 표면에 놓여 사람 머리에 묻힌다(09-21 사용자 지적).")]
    [SerializeField] private float needleHandleOut = 0.16f;
    [Tooltip("미간을 아직 안 찍었을 때 쓸 각도기 반지름(m). 미간이 있으면 중심에서 미간까지를 쓴다.")]
    [SerializeField] private float needleFallbackRadius = 0.18f;
    // ★바늘 면의 크기(2026-09-23 사용자: "너무 크지 않게 환자 머리 크기 정도만"). 사람 머리 높이 어림이다.
    //   ★추정값이다 — 기기에서 보고 맞춘다. 회전은 이만큼 <b>아래로</b>, 나머지는 바늘을 가운데 두고 위아래로 벌어진다.
    [SerializeField] private float needleFinSize = 0.2f;

    [Header("=== 바늘 시작 자리·잠금(2026-09-28) ===")]
    // ★★바늘은 <b>정상가동범위 최대값</b>에서 시작한다(2026-09-28 사용자: "굴곡 45 신전 90 측굴 45 회전 90").
    //   종전(09-21~09-23)엔 전부 0°에 포개 세웠다. 한계에서 출발해 환자가 실제로 간 자리까지 <b>되돌려</b> 맞춘다.
    //   ★아직 안 끌어낸 바늘은 값이 아니다(탭 아래 «—») — 한계 자리는 출발점일 뿐이다.
    [Tooltip("굴곡 바늘이 처음 서는 각(°). 정상가동범위 최대값.")]
    [SerializeField] private float flexionLimitDeg = 45f;
    [Tooltip("신전 바늘이 처음 서는 각(°). 정상가동범위 최대값.")]
    [SerializeField] private float extensionLimitDeg = 90f;
    [Tooltip("측굴(좌우 같음) 바늘이 처음 서는 각(°). 정상가동범위 최대값.")]
    [SerializeField] private float lateralLimitDeg = 45f;
    [Tooltip("회전(좌우 같음) 바늘이 처음 서는 각(°). 정상가동범위 최대값.")]
    [SerializeField] private float rotationLimitDeg = 90f;
    // ★오토 홀드(09-28 사용자 메모 «바늘 한번 옮기면 오토 홀드»): 놓는 순간 잠긴다. [홀드] 버튼은 없앴다.
    //   09-23 계수에서 바늘 기록 20회 중 4회(20%)가 «놓자마자 다시 고침»이었다 — 놓을 때의 흔들림이 값이 됐다.
    [Tooltip("잠긴 바늘의 구체를 검지끝으로 이 거리(m) 안까지 치면 잠금이 풀린다. 구체 지름은 1.8cm다.")]
    [SerializeField] private float needleTouchRadius = 0.025f;
    [Tooltip("바늘 위 미세 조정 버튼 한 번에 움직이는 각(°).")]
    [SerializeField] private float fineStepDeg = 0.1f;
    [Tooltip("미세 조정 판을 바늘 구체보다 이만큼(m) 위에 띄운다(손과 환자 머리가 한 시선에 들게).")]
    [SerializeField] private float finePanelRise = 0.08f;

    [Header("=== 기준점 다시 찍기(2026-09-28) ===")]
    [Tooltip("이미 찍은 기준점 버튼을 이만큼(초) 누르고 있으면 검지끝 자리로 다시 찍힌다. 짧게 누르면 이동 대상만 바뀐다.\n" +
             "★판의 쿨다운(0.5초)보다 길어야 한다 — 짧으면 길게 누르기가 쿨다운에 먹힌다.")]
    [SerializeField] private float landmarkRecaptureSeconds = 1f;

    [Header("=== 나가기 ===")]
    [Tooltip("[나가기]를 이만큼(초) 누르고 있어야 나간다. ★09-21: 지탱하던 손이 스쳐 나가기가 눌렸다.")]
    [SerializeField] private float exitHoldSeconds = 1f;

    // ★목중앙 보정(neckOffsetMm)은 09-22에 없앴다(사용자 제안 «목중앙을 없애고 대추·풍부·미간»).
    //   대추에서 앞으로 몇 mm라는 값은 사람마다 정할 근거가 없었고, 09-21 로그에서 35mm가 회전 각을 30° 바꿨다.
    //   대신 손으로 짚어 찍는 점을 축으로 쓴다 — 처음엔 풍부(GV16)였고 같은 날 <b>외이도</b>로 바꿨다(사용자).
    //   씬에 남은 옛 값은 읽히지 않는다.

    [Header("=== 좌우 ===")]
    [Tooltip("측굴·회전의 좌우가 반대로 기록되면 켠다. ★Play에서 환자 오른쪽으로 기울여 '우'가 찍히는지 먼저 본다.")]
    [SerializeField] private bool flipSides = false;

    [Header("=== 단면 표시(2026-09-21 신설) ===")]
    [Tooltip("3축이 이루는 단면을 사람 크기만큼 사각형으로 보여 준다.")]
    [SerializeField] private bool showSectionPlane = true;
    [Tooltip("찍은 점에서 단면까지 수직선을 그어 삼각면으로 보여 준다.\n" +
             "★각은 단면에 투영해 재므로, 벗어난 만큼이 «각에 못 쓴 성분»이다. 그걸 눈에 보이게 한다.")]
    [SerializeField] private bool showOffPlaneTriangle = true;

    [Header("=== 현실에 가려지기(2026-09-21 신설) ===")]
    // ★사용자 09-21: "패스스루에서 오브젝트가 현실 위에 붕 떠 있다 — 사람 위에 십자선을 그어도
    //   몸통을 관통하는 십자선이 아니라 그냥 위에 올려진 십자선 같다."
    //   정체는 <b>폐색이 없는 것</b>이다. 가상 물체가 현실 물체 뒤로 안 들어가고 늘 덧그려지니
    //   뇌가 "몸 안"이 아니라 "몸 앞 유리창의 그림"으로 읽는다.
    // ★Meta Depth API로 가린다. 다만 <b>완전히 지우지 않고 흐리게</b> 남긴다 — 기준축·십자선은
    //   몸을 관통해 보여야 하는 것이라 사라지면 안 된다(사용자: "아예 약간 흐린색으로").
    // ★Quest 3/3S 전용이다. 안 되는 기기에서는 키워드가 안 켜져 <b>종전과 똑같이</b> 보인다.
    [Tooltip("표시물이 현실 물체에 가려지게 한다(Quest 3/3S). 끄면 종전처럼 늘 덧그려진다.")]
    [SerializeField] private bool useDepthOcclusion = true;
    [Tooltip("가려진 부분에 남길 진하기. 0이면 완전히 사라지고, 1이면 폐색이 없는 것과 같다.\n" +
             "★0.15~0.25가 «몸 안을 지나간다»로 읽히는 구간이다(추정 — 눈으로 맞출 값).")]
    [Range(0f, 1f)] [SerializeField] private float occludedAlpha = 0.18f;

    [Header("=== 씬 ===")]
    [Tooltip("시작할 때 패스스루를 켠다. 이 씬은 패스스루 전용이다.")]
    [SerializeField] private bool enablePassthroughOnStart = true;
    [SerializeField] private string lobbySceneName = "lobby";   // ★실제 종료 팝업(ExitPopupController)이 쓰는 이름과 같게

    // ── 상태 ─────────────────────────────────────────────────────────
    // ★대추부터 시작한다(2026-09-21 사용자 제안). 대추를 찍으면 3축이 그 자리로 가고,
    //   정면은 미간을 찍을 때 대추→미간으로 정해진다 — 기준선 세팅을 먼저 할 이유가 없다.
    //   [이전]으로 기준선 세팅에 갈 수는 있다(3축을 손으로 다듬고 싶을 때).
    private RomRecordStep step = RomRecordStep.Landmarks;
    private float yaw;
    private Vector3 frameOrigin;           // 대추를 찍기 전 3축 자리
    private bool hasC7, hasGlab, hasEar;
    private Vector3 c7, glab, ear;
    private int landmarkTarget;            // 0 대추 · 1 미간 · 2 외이도(09-22 — 목중앙 자리를 이어받았다)
    // ★★기준점은 <b>버튼을 누르는 순간</b> 찍힌다(2026-09-23 사용자: "핀치는 너무 흔들린다").
    //   손가락을 목표 자리에 대고 반대 손으로 [대추]·[외이도]·[미간]을 누른다.
    //   09-22의 «찍기 대기(landmarkArmed)»는 없앴다 — 핀치로 찍지 않으니 덮어쓸 일 자체가 없어졌다.
    //   (그 대기는 «판을 잡으러 가다 일찍 오므린 핀치가 기준점을 덮어쓰는» 것을 막으려던 문이었다.)
    private bool lastPressWasRight = true;   // 방금 버튼을 누른 손 — 반대 손 검지끝이 목표점이다
    private readonly List<RomRecordMark>[] marks =
    {
        new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(),
    };

    private readonly RomRecordHands hands = new RomRecordHands();
    // ★판 구현이 둘이다(09-21) — Meta UI Set(기본)과 종전 월드 Quad 판. useMetaUI로 고른다.
    private IRomRecordMenu leftMenu, rightMenu;
    private readonly RomRecordVisual view = new RomRecordVisual();
    private Transform eye;
    private bool placed;
    private bool dirty = true;
    private bool livePinchLeft;            // 지금 오므리고 있는 손(한 번에 하나만 받는다)
    private bool liveActive;
    // ★바늘(09-21) — 단계마다 <b>넷</b>이다: 한 탭에 방향 둘 × (능동·압박).
    // ★★09-28부터 바늘은 <b>방향에 묶인다</b>(사용자: "굴곡 신전 우측굴 좌측굴 우회전 좌회전 순서, 탭은 3개 유지").
    //   k = 0 첫 방향 능동 · 1 첫 방향 압박 · 2 둘째 방향 능동 · 3 둘째 방향 압박.
    //   첫 방향 = 굴곡 / 우측굴 / 우회전, 둘째 방향 = 신전 / 좌측굴 / 좌회전(<see cref="DirName"/>).
    //   ★둘째 방향 바늘은 첫 방향 둘을 다 맞춘 뒤에 나타난다 — 순서대로 한 방향씩 잰다.
    //   (종전엔 넷이 0°에 포개져 있고 바늘이 가리키는 쪽으로 좌우가 저절로 갈렸다.)
    // ★★<b>바늘이 곧 값이다</b>(09-21 "손이 너무 많이 가면 안 돼") — 따로 [기록]을 누르지 않는다.
    //   한계 자리에 세워 둔 바늘을 끌어내면 그게 그 항목의 기록이고, 다시 끌면 고쳐진다.
    private const int NeedlePerStep = 4;
    private readonly Vector3[] needleDir = new Vector3[4 * NeedlePerStep];
    private readonly bool[] needleMoved = new bool[4 * NeedlePerStep];   // 한계 자리에서 끌어냈나 = 기록됐나
    // ★잠금 — ★09-28부터 <b>놓는 순간 저절로</b> 잠긴다(오토 홀드). 잠긴 바늘은 FindDragTarget이 건너뛰고,
    //   <b>구체를 검지로 치면</b> 풀린다. [홀드] 버튼(09-23)은 없앴다(사용자: "UI에 홀드버튼을 놓지 말고").
    //   ★값(needleMoved)은 안 지운다 — 잠금은 «안 움직임»이지 «안 셈»이 아니다.
    private readonly bool[] needleLocked = new bool[4 * NeedlePerStep];
    // ★미세 조정 판이 다루는 바늘(k) — 마지막으로 놓았거나 구체를 쳐서 푼 바늘. -1이면 판을 숨긴다.
    private int fineTarget = -1;
    private IRomRecordMenu fineMenu;
    private float nextFineLog;
    // ★구체 터치는 <b>밖에서 안으로 들어오는 순간</b>만 센다(손별로 지금 안에 있는 바늘 k, 없으면 -1).
    //   놓는 순간 그 손가락은 이미 구체 안에 있다 — 들어옴만 세면 놓자마자 도로 풀리는 일이 없다.
    private readonly int[] touchInside = { -1, -1 };
    private bool needleOn = true;
    // ★잡아 끌기(09-21) — 0 3축 · 1 대추 · 2 미간 · 3~6 바늘 · 8 외이도 · 9 조작 판 · -1 아무것도 안 잡음(«찍기»로 간다)
    private int dragTarget = -1;
    private Vector3 dragGrabbedAt, dragStartValue;
    // ★진단 로그 자기 침묵 — <b>손별로</b> 따로 센다(09-21 수정).
    //   처음엔 하나로 뒀는데 왼손·오른손을 번갈아 처리하니 «마지막 이유»가 매번 바뀌어
    //   억제가 통째로 죽었다 — 한 판에 11,000줄이 쏟아졌다. 로그가 로그를 못 보게 만든 꼴이다.
    private readonly float[] nextPinchLog = new float[2];
    private readonly Dictionary<string, int> pinchBlockCount = new Dictionary<string, int>();
    private readonly StringBuilder sb = new StringBuilder(512);
    private float lastPressTime = -99f;
    private bool menuHoldL, menuHoldR;
    private AudioSource audioSrc;
    // ★기준점 안내 음성(2026-09-22 사용자 "다음 기준점은 뭘 설정해야 할지 나레이션으로"). 차례로 들려준다 —
    //   새 안내가 오면 하던 말을 끊는다(빨리 누르는 사람에게 지난 안내가 늦게 나오지 않게).
    private AudioSource voiceSrc;
    private readonly Queue<AudioClip> voiceQueue = new Queue<AudioClip>(4);
    private readonly Dictionary<string, AudioClip> voices = new Dictionary<string, AudioClip>(16);
    private static readonly string[] VoiceNames =
    {
        "arm_c7", "arm_ear", "arm_glab", "done_c7", "done_ear", "done_glab",
        "next_c7", "next_ear", "next_glab", "all_done", "locked",
    };
    private float nextLockedVoice;
    private AudioClip sndPress, sndRepeat, sndPinch, sndUndo, sndDeny;

    // ★Flexion은 09-22부터 «굴곡·신전» 한 단계다. Extension(신전)은 더 안 쓰지만 자리는 남긴다(enum 값과 짝).
    private static readonly string[] StepTitle = { "기준선 세팅", "기준점 설정", "굴곡·신전", "신전", "측굴", "회전", "완료" };

    private void Start()
    {
        if (enablePassthroughOnStart) EnablePassthrough();

        TMP_FontAsset font = KoreanFontResolver.Resolve();
        Material mat = BuildDisplayMaterial();

        needleOn = needleEnabled;   // ★런타임 토글([바늘] 버튼)이 이 값을 이어받는다
        view.textSize = textSize;
        view.panelSize = panelTextSize;
        // ★색·굵기는 매 SetDial/SetNeedle에서 다시 입혀지므로 여기서 한 번 넘기면 된다(09-21).
        view.dialColor = dialColor;
        view.dialTickColor = dialTickColor;
        view.dialZeroColor = dialZeroColor;
        view.dialLabelColor = dialLabelColor;
        view.needleWidth = needleWidth;
        view.tickLabelOut = tickLabelOut;
        view.cylinderHeight = cylinderHeight;
        view.cylinderRadiusScale = cylinderRadiusScale;
        view.Build(transform, font, mat);
        leftMenu = NewMenu();
        rightMenu = NewMenu();
        leftMenu.FixedInSpace = menuFixedInSpace;
        leftMenu.Build(transform, menuFixedInSpace ? "조작 판" : "왼손목 메뉴", MenuCapacity, font, mat);
        // ★고정 모드면 판은 <b>하나</b>다(둘을 허공에 띄우면 서로 가린다). 그때는 오른쪽 자리에
        //   같은 판을 다시 가리켜 둔다 — SetLayout·Near 같은 곳에서 널을 만지지 않게.
        if (menuFixedInSpace)
        {
            rightMenu = leftMenu;
        }
        else
        {
            rightMenu.FixedInSpace = false;
            rightMenu.Build(transform, "오른손목 메뉴", MenuCapacity, font, mat);
        }
        Debug.Log($"[실측기록] 판 — {(useMetaUI ? "Meta UI Set" : "Quad")} · " +
                  $"{(menuFixedInSpace ? "허공 고정(진행Root 방식)" : "양 손목 추종")}");

        // ★바늘 위 미세 조정 판(2026-09-28 사용자: "0.1도 간격으로 버튼 UI가 바늘 위 손과 환자 머리가
        //   한 시선 안에 들어오는 위치에"). 조작 판과 같은 종류의 판을 하나 더 띄운다 — 누름 판정이 같다.
        //   바늘을 놓거나 구체를 쳐서 풀면 그 바늘 위에 서고, 고른 바늘이 없으면 숨는다.
        fineMenu = NewMenu();
        fineMenu.FixedInSpace = true;
        fineMenu.Build(transform, "바늘 미세 조정", 4, font, mat);
        fineMenu.SetLayout("", new[]
        {
            // ★글자는 ASCII '-'다 — 앱 폰트(NotoSansKR-Bold)에 없는 글리프는 네모로 나온다(09-22 ↺↻ 전례).
            RomMenuItem.Button("fine-", "-" + fineStepDeg.ToString("0.0#") + "°", 0f, 0f, 1.4f, AdjTint, repeat: true),
            new RomMenuItem { id = null, key = FineValueKey, label = "", col = 1.4f, row = 0f, span = 1.4f },
            RomMenuItem.Button("fine+", "+" + fineStepDeg.ToString("0.0#") + "°", 2.8f, 0f, 1.4f, AdjTint, repeat: true),
        });
        fineMenu.Hide();
        LoadTabIcons();
        ApplyStepButtons();
        BuildSounds();
        Say(ArmVoice(landmarkTarget));   // ★처음엔 대추가 찍기 대기다

        Debug.Log("[실측기록] 시작 — 기준점 설정부터. 좌우 뒤집기 " + (flipSides ? "켬" : "끔") + " · " +
                  $"글자 {textSize}/{panelTextSize}/버튼 {buttonLabelSize} · 칸 {menuCellWidth * 100f:F1}×{menuRowHeight * 100f:F1}cm · 소리 {soundVolume:F1}");
    }

    /// <summary>
    /// 판 하나를 만든다(Build 전 설정까지). ★조작 판·미세 조정 판이 <b>같은 종류</b>를 쓰게 한 곳에 모았다(09-28).
    /// </summary>
    private IRomRecordMenu NewMenu()
    {
        if (useMetaUI)
        {
            return new RomRecordMenuUI
            {
                cellPx = metaCellPx,
                rowPx = metaRowPx,
                buttonPx = metaButtonPx,
                labelPt = metaLabelPt,
                headerPt = metaLabelPt,
                lift = menuLift,
            };
        }
        var menu = new RomRecordWristMenu
        {
            labelSize = buttonLabelSize,
            headerSize = buttonLabelSize,
            cellW = menuCellWidth,
            rowH = menuRowHeight,
            buttonH = menuButtonHeight,
            lift = menuLift,
            buttonTintStrength = menuTintStrength,
        };
        // ★판 색은 실측값을 쓰되 불투명도만 여기서 조절한다(패스스루에서 답답할 수 있어서).
        Color top = menu.plateTopColor, bot = menu.plateBottomColor;
        top.a = bot.a = menuPlateAlpha;
        menu.plateTopColor = top;
        menu.plateBottomColor = bot;
        return menu;
    }

    /// <summary>
    /// 표시물이 쓸 머티리얼을 만든다(2026-09-21). 폐색을 켜면 «현실에 가려지면 흐려지는» 셰이더를 쓰고,
    /// 안 되면 종전 <c>Sprites/Default</c>로 떨어진다.
    /// ★<b>조용히 실패하지 않게</b> 어느 쪽으로 갔는지 반드시 로그에 남긴다 —
    ///   폐색이 안 보일 때 "셰이더를 못 찾은 것"인지 "기기가 지원을 안 하는 것"인지 갈려야 한다.
    /// </summary>
    private Material BuildDisplayMaterial()
    {
        if (useDepthOcclusion)
        {
            var src = Resources.Load<Material>("RomRecordUI/RomRecordOccluded");
            if (src != null)
            {
                var m = new Material(src);   // ★에셋 원본을 건드리지 않게 복제한다
                m.SetFloat("_OccludedAlpha", occludedAlpha);
                bool supported = EnableDepthOcclusion();
                Debug.Log($"[실측기록] 표시 머티리얼 — 폐색 셰이더 · 가려진 곳 {occludedAlpha:F2} · " +
                          $"기기 지원 {(supported ? "O" : "★없음(종전처럼 늘 덧그려진다)")}");
                return m;
            }
            ChunaLogger.LogWarning("[실측기록] ★폐색 머티리얼을 못 찾았다(Resources/RomRecordUI/RomRecordOccluded) — 종전 셰이더로 간다.");
        }

        // ★Sprites/Default는 Always Included에 들어 있다(GraphicsSettings 10753) — 빌드에서도 null이 아니다
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null)
        {
            ChunaLogger.LogWarning("[실측기록] Sprites/Default 셰이더를 못 찾았다 — 선이 분홍으로 보이면 이것이다.");
            return null;
        }
        Debug.Log("[실측기록] 표시 머티리얼 — 종전 Sprites/Default(폐색 없음)");
        return new Material(sh);
    }

    /// <summary>
    /// 깊이(폐색)를 켠다. 켜졌으면 true. ★지원하지 않는 기기에서 켜면 에러를 뱉으므로 먼저 묻는다.
    /// </summary>
    private bool EnableDepthOcclusion()
    {
        if (!Meta.XR.EnvironmentDepth.EnvironmentDepthManager.IsSupported)
        {
            Debug.Log("[실측기록] 이 기기는 환경 깊이를 지원하지 않는다(Quest 3/3S 전용) — 폐색 없이 간다.");
            return false;
        }
        var mgr = FindAnyObjectByType<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>();
        if (mgr == null) mgr = gameObject.AddComponent<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>();
        mgr.enabled = true;
        // 부드러운 폐색이 기본이다 — 경계가 딱딱하면 오려 붙인 것처럼 보인다.
        mgr.OcclusionShadersMode = Meta.XR.EnvironmentDepth.OcclusionShadersMode.SoftOcclusion;
        // ★손은 깊이에서 빼지 않는다. 시술자 손이 표시물을 가리는 것도 «앞에 있다»는 단서라 그대로 둔다.
        return true;
    }

    // ── 소리 ─────────────────────────────────────────────────────────
    // ★09-18 사용자: "누른 건지 구분이 안 간다. 소리도 애니메이션도 없다."
    //   음원 파일 없이 짧은 음을 코드로 만든다(에셋 배선이 필요 없고 빌드에서 빠질 일이 없다).
    private void BuildSounds()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;            // 2D — 어디서 눌러도 같은 크기로 들린다
        sndPress = Tone("누름", 1250f, 1250f, 0.045f);
        sndRepeat = Tone("반복", 1600f, 1600f, 0.025f);
        sndPinch = Tone("찍음", 880f, 1320f, 0.11f);      // 올라가는 두 음 — 기록됐다
        sndUndo = Tone("취소", 700f, 440f, 0.09f);        // 내려가는 음 — 지웠다
        sndDeny = Tone("거부", 300f, 300f, 0.12f);        // 낮은 음 — 받지 않았다

        voiceSrc = gameObject.AddComponent<AudioSource>();
        voiceSrc.playOnAwake = false;
        voiceSrc.spatialBlend = 0f;
        int missing = 0;
        foreach (string n in VoiceNames)
        {
            var c = Resources.Load<AudioClip>("RomRecordUI/Voice/" + n);
            if (c != null) voices[n] = c;
            else missing++;
        }
        if (missing > 0)
            Debug.LogWarning($"[실측기록] ★안내 음성 {missing}개를 못 찾았다(Resources/RomRecordUI/Voice) — 그 안내는 무음이다. " +
                             ".claude/tools/rom_record_voice.py로 만든다.");
    }

    /// <summary>안내 음성을 차례로 말한다. ★하던 말은 끊는다.</summary>
    private void Say(string a, string b = null)
    {
        if (voiceSrc == null) return;
        voiceQueue.Clear();
        voiceSrc.Stop();
        if (voices.TryGetValue(a, out AudioClip ca)) voiceQueue.Enqueue(ca);
        if (b != null && voices.TryGetValue(b, out AudioClip cb)) voiceQueue.Enqueue(cb);
    }

    /// <summary>매 프레임 — 말이 끝났으면 다음 말을 꺼낸다. 할당 없음.</summary>
    private void UpdateVoice()
    {
        if (voiceSrc == null || voiceSrc.isPlaying || voiceQueue.Count == 0) return;
        voiceSrc.clip = voiceQueue.Dequeue();
        voiceSrc.volume = voiceVolume;
        voiceSrc.Play();
    }

    private static AudioClip Tone(string name, float f0, float f1, float seconds)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);   // 짧게 올라갔다 빠르게 사라진다(딸깍)
            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        var clip = AudioClip.Create("[실측기록] " + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip c)
    {
        if (audioSrc != null && c != null && soundVolume > 0f) audioSrc.PlayOneShot(c, soundVolume);
    }

    private void EnablePassthrough()
    {
        // ★이 씬은 패스스루 전용이라 우리가 켜고 씬이 내려갈 때 같이 사라진다(다른 씬과 공유하는 상태가 아니다).
        var layer = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        if (layer != null)
        {
            layer.enabled = true;
            layer.hidden = false;
        }
        if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // ★알파 0이어야 뒤의 패스스루가 보인다(Underlay)
        }
        Debug.Log($"[실측기록] 패스스루 — 레이어 {(layer != null ? "O" : "★없음")} · OVRManager {(OVRManager.instance != null ? "O" : "★없음")} · 카메라 {(cam != null ? "O" : "★없음")}");
    }

    private void Update()
    {
        if (eye == null && Camera.main != null) eye = Camera.main.transform;
        if (eye == null) return;

        // ★★09-21 실측: 첫 프레임에 자리를 잡으면 <b>헤드셋 추적이 아직 안 붙어</b> 눈이 원점에 있다.
        //   그러면 3축도 조작 판도 발밑 엉뚱한 곳에 선다 — 로그에 판이 (-0.380,-0.300,0.550)로 찍혔는데
        //   그건 눈이 (0,0,0)일 때 나오는 값이었고, 그 판에서 "기준선을 못 잡았다"가 8번 찍혔다.
        //   사람 눈은 바닥에서 최소 30cm 위다 — 그 전까지는 아무것도 놓지 않는다.
        if (!EyeReady)
        {
            // ★★09-21: XR 초기화가 실패하면(로그에 OVRManager 없음·OpenXR 예외) 눈이 영영 원점이다.
            //   그때 그냥 기다리기만 하면 <b>화면이 통째로 비어</b> 무슨 일인지 알 수가 없다 —
            //   실제로 그 판은 2초 만에 꺼졌다. 그래서 얼마간 기다린 뒤에는 그냥 놓고 경고를 남긴다.
            if (trackingWaitStart < 0f) trackingWaitStart = Time.unscaledTime;
            float waited = Time.unscaledTime - trackingWaitStart;
            ReportNotReady(waited);
            if (waited < trackingWaitSeconds) return;
        }
        else
        {
            trackingWaitStart = -1f;
            // ★★09-21 실측: Start에서 물으면 <b>XR이 아직 안 붙어</b> 깊이 서브시스템이 없다
            //   (로그 순서로 확인 — 표시 머티리얼 줄이 시작 줄보다 앞이고, 그 직후 눈 높이가 0.00m다).
            //   그래서 추적이 붙은 뒤에 <b>한 번 더</b> 켜 본다.
            if (useDepthOcclusion && !depthRetried)
            {
                depthRetried = true;
                if (EnableDepthOcclusion())
                    Debug.Log("[실측기록] ★추적이 붙은 뒤 폐색을 켰다 — 이제 현실에 가려진다.");
                else
                    Debug.Log("[실측기록] 추적이 붙은 뒤에도 환경 깊이가 없다 — 이 실행에서는 폐색 없이 간다.");
            }
        }

        if (!placed)
        {
            // 처음 한 번 — 눈앞에, 눈높이보다 조금 아래에 3축을 둔다. 정면은 사용자가 보는 수평 방향.
            Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            frameOrigin = eye.position + f * startDistance - Vector3.up * startBelowEye;
            yaw = Quaternion.LookRotation(f, Vector3.up).eulerAngles.y;
            placed = true;
            dirty = true;
        }

        hands.Resolve();
        HandleMenus();
        HandlePinch(true);
        HandlePinch(false);
        UpdateNeedleTouch(true);
        UpdateNeedleTouch(false);
        UpdateMenuHighlight();
        UpdateVoice();

        if (dirty)
        {
            dirty = false;
            Redraw();
        }
        view.FaceCamera(eye);
    }

    /// <summary>
    /// 헤드셋 추적이 붙었나(2026-09-21 신설). ★붙기 전에는 눈이 원점이라 무엇을 놓든 발밑에 놓인다.
    /// 사람 눈은 바닥에서 최소 30cm 위다 — 그보다 낮으면 아직 추적 전이다.
    /// </summary>
    private bool EyeReady => eye != null && eye.position.y > 0.3f;

    private float nextNotReadyLog;
    private float trackingWaitStart = -1f;
    private bool trackingGaveUp;
    private bool depthRetried;   // 추적이 붙은 뒤 폐색을 한 번 더 켜 봤나
    // ★이만큼 기다려도 추적이 안 붙으면 그냥 놓는다. 화면이 계속 비어 있는 것보다 낫다(09-21).
    private const float trackingWaitSeconds = 8f;

    private void ReportNotReady(float waited)
    {
        if (Time.unscaledTime < nextNotReadyLog) return;
        nextNotReadyLog = Time.unscaledTime + 2f;
        float y = eye != null ? eye.position.y : -1f;
        if (waited < trackingWaitSeconds)
        {
            Debug.Log($"[실측기록] 헤드셋 추적을 기다린다 — 눈 높이 {y:F2}m · {waited:F0}/{trackingWaitSeconds:F0}초 " +
                      "(0.30m를 넘어야 3축과 조작 판을 놓는다)");
            return;
        }
        if (trackingGaveUp) return;
        trackingGaveUp = true;
        // ★한 번만 크게 알린다 — 이 상태면 XR이 안 붙은 것이라 자리가 엉뚱해도 어쩔 수 없다.
        ChunaLogger.LogWarning($"<color=orange>[실측기록] ★추적이 {trackingWaitSeconds:F0}초 동안 안 붙었다" +
                               $"(눈 높이 {y:F2}m) — 그냥 놓는다. XR 초기화 실패일 수 있다" +
                               "(패스스루 로그의 OVRManager 항목을 본다).</color>");
        Debug.Log("[실측기록] ★추적 없이 놓는다 — 자리가 엉뚱하면 조작 판과 3축을 손으로 잡아 끈다.");
    }

    // ── 계산 ─────────────────────────────────────────────────────────
    // ★축은 단계마다 다르다(09-22 사용자 결정): 굴곡·신전·회전 = 외이도, 측굴 = 대추.
    //   대추를 축으로 신전을 재면 90°를 젖혀도 절반쯤으로 나왔다(사용자 지적) — 머리는 대추보다 훨씬 위를 중심으로 돈다.
    //   처음엔 풍부였고 같은 날 외이도로 바꿨다(사용자). 교과서 각도계도 굴곡·신전 축을 외이도에 둔다.
    //   ★외이도는 머리 <b>옆면</b> 점이다. 굴곡·신전(시상면)은 투영하면 좌우 성분이 사라져 상관없지만,
    //   회전(횡단면)은 축이 한쪽 귀에 있으면 좌·우 회전이 비대칭으로 틀어진다(계산).
    //   → 찍은 외이도를 <b>정중면</b>(대추를 지나고 법선이 환자 오른쪽인 연직면)으로 옮긴 점을 축으로 쓴다.
    //   귀의 높이·앞뒤는 그대로, 좌우만 가운데로 — 왼귀·오른귀 어느 쪽을 찍어도 같은 축이 된다.
    //   외이도를 아직 안 찍었으면 대추로 대신 잰다(안내 줄이 말한다). 대추 기준 각은 로그에 늘 같이 남긴다.
    private Vector3 Pivot => PivotFor(step);

    private Vector3 PivotFor(RomRecordStep s) =>
        !hasC7 ? frameOrigin : hasEar && UsesEar(s) ? EarAxis : c7;

    /// <summary>외이도를 정중면으로 옮긴 점. ★정면(yaw)이 바뀌면 같이 바뀐다 — 대추→미간으로 정면을 잡은 뒤가 맞다.</summary>
    private Vector3 EarAxis
    {
        get
        {
            Vector3 r = RomRecordGeometry.Right(yaw);
            return ear - r * Vector3.Dot(ear - c7, r);   // 지움: 좌우 성분(정중면에서 벗어난 양)만
        }
    }

    // ★2026-09-23 사용자 지시로 <b>회전은 다시 대추 축</b>이다. 외이도 축은 굴곡·신전에만 쓴다.
    //   (09-22엔 회전도 외이도를 정중면으로 옮긴 점을 축으로 썼다 — 두 축의 차이는 녹화에서 8~9°였다.)
    private static bool UsesEar(RomRecordStep s) =>
        s == RomRecordStep.Flexion || s == RomRecordStep.Extension;

    private string PivotName(RomRecordStep s) => hasC7 && hasEar && UsesEar(s) ? "외이도" : "대추";

    /// <summary>
    /// 각도기·바늘이 함께 쓰는 기하(2026-09-21). ★표시·드래그·기록이 <b>같은 함수</b>를 탄다(규칙 9 —
    /// 따로 계산하면 미리보기가 거짓말을 한다).
    /// ★미간이 없어도 뜬다 — 바늘은 미간 찍기를 대신하려고 만든 것이다. 그때는 반지름이 기본값이고
    ///   중립선 기준 각은 못 낸다(눈금 기준 각만 남는다).
    /// </summary>
    private bool DialFrame(out Vector3 center, out Vector3 normal, out Vector3 zero, out float radius)
    {
        center = default;
        normal = Vector3.up;
        zero = Vector3.forward;
        radius = needleFallbackRadius;
        if (!IsMotion(step) || !hasC7) return false;

        Vector3 pivot = Pivot;
        normal = RomRecordGeometry.PlaneNormal(step, yaw);
        zero = RomRecordGeometry.ScaleZero(step, yaw);
        // 회전 눈금판은 미간 높이의 수평면에 둔다 — 회전 중심 높이에 두면 머리에 가려 안 보인다.
        center = step == RomRecordStep.Rotation && hasGlab ? new Vector3(pivot.x, glab.y, pivot.z) : pivot;
        radius = hasGlab
            ? Mathf.Max(0.08f, Vector3.ProjectOnPlane(glab - center, normal).magnitude)
            : needleFallbackRadius;
        return true;
    }

    private static int MotionIndex(RomRecordStep s) => (int)s - (int)RomRecordStep.Flexion;
    private static bool IsMotion(RomRecordStep s) => s >= RomRecordStep.Flexion && s <= RomRecordStep.Rotation;
    private static int Capacity(RomRecordStep s) => HasSides(s) ? 4 : 2;
    // ★양쪽을 가르는 단계 — 측굴·회전은 좌우, 굴곡·신전(09-22 합침)은 앞뒤다. 네 번씩 찍는다.
    private static bool HasSides(RomRecordStep s) =>
        s == RomRecordStep.Flexion || s == RomRecordStep.LateralFlexion || s == RomRecordStep.Rotation;

    /// <summary>그 마커의 각 — 넘겨준 축 기준, 수정 포함.</summary>
    private float AngleOf(RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        Vector3 n = RomRecordGeometry.PlaneNormal(s, yaw);
        return Mathf.Max(0f, RomRecordGeometry.PlaneAngle(pivot, glab, m.raw, n) + m.adjustDeg);
    }

    // ── 손목 메뉴 ────────────────────────────────────────────────────
    private void HandleMenus()
    {
        bool rTip = hands.TryJoint(false, HandJointId.HandIndexTip, out Vector3 rIdx);
        bool lTip = hands.TryJoint(true, HandJointId.HandIndexTip, out Vector3 lIdx);

        // ★허공 고정 모드(09-21) — 판 하나를 세워 두고 <b>양손 검지 어느 쪽으로든</b> 누른다.
        //   판이 손에서 떨어져 있으니 "판 고정·판 근처" 차단이 필요 없다 — 그게 간섭의 주범이었다.
        if (menuFixedInSpace)
        {
            if (!leftMenu.Placed && eye != null)
            {
                Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, f);   // f를 볼 때 오른쪽
                Vector3 pos = eye.position + f * menuFixedDistance
                            + side * menuFixedSide - Vector3.up * menuFixedBelowEye;
                leftMenu.PlaceAt(pos, eye);
                Debug.Log($"[실측기록] 조작 판을 세웠다 — {Fmt(pos)} (잡아 끌어 옮길 수 있다)");
            }
            leftMenu.Follow(false, Vector3.zero, eye, false);   // 고정 모드에서는 회전만 눈을 따라간다
            menuHoldL = menuHoldR = false;

            // ★★09-21 사용자: "눌렀다 뗐다 하는 것에 이중 삼중으로 다다다다 눌려 버려."
            //   종전엔 같은 판을 <b>양손으로 두 번</b> Poll했다. 그러면 한 손이 버튼 안에 있고 다른 손이 밖일 때
            //   «들어옴/나감» 상태가 매 프레임 뒤집혀, 쿨다운(0.35초)이 풀릴 때마다 한 번씩 계속 발사됐다.
            //   → 판에 <b>더 가까운 손 하나만</b> 본다. 두 손으로 번갈아 누를 일은 없다.
            bool useRight = rTip && (!lTip || leftMenu.Distance(rIdx) <= leftMenu.Distance(lIdx));
            Vector3 pokeTip = useRight ? rIdx : lIdx;
            bool pokeOk = useRight ? rTip : lTip;
            string fid = leftMenu.Poll(pokeTip, pokeOk);
            bool frep = leftMenu.LastRepeat;
            if (fid != null)
            {
                lastPressTime = Time.unscaledTime;
                // ★누른 손을 남긴다 — 기준점 버튼이 <b>반대 손</b> 검지끝을 목표점으로 쓴다(09-23).
                lastPressWasRight = useRight;
                Play(fid == "undo" ? sndUndo : frep ? sndRepeat : sndPress);
                OnButton(fid);
            }
            leftMenu.Tick();
            PollFineMenu(rTip, rIdx, lTip, lIdx);
            return;
        }

        bool lw = hands.TryWrist(true, out Pose lwp);
        bool rw = hands.TryWrist(false, out Pose rwp);

        // ★다가오면 제자리 고정(09-18 사용자 결정). 왼손목 판은 오른 검지가, 오른손목 판은 왼 검지가 누른다.
        //   누르는 손이 판 쪽 손목을 가리면 그 손목 추적이 끊겨 판이 숨었다 튀었다("UI가 도망간다").
        menuHoldL = rTip && leftMenu.Distance(rIdx) < menuHoldDistance;
        menuHoldR = lTip && rightMenu.Distance(lIdx) < menuHoldDistance;
        leftMenu.Follow(lw, lwp.position, eye, menuHoldL);
        rightMenu.Follow(rw, rwp.position, eye, menuHoldR);

        // ★왼손목 판은 오른 검지가, 오른손목 판은 왼 검지가 누른다 — 누른 손이 판으로 갈린다(09-23).
        string id = leftMenu.Poll(rIdx, rTip);
        bool repeat = leftMenu.LastRepeat;
        bool pressedRight = true;
        if (id == null) { id = rightMenu.Poll(lIdx, lTip); repeat = rightMenu.LastRepeat; pressedRight = false; }
        if (id != null)
        {
            lastPressWasRight = pressedRight;
            lastPressTime = Time.unscaledTime;
            Play(id == "undo" ? sndUndo : repeat ? sndRepeat : sndPress);
            OnButton(id);
        }
        leftMenu.Tick();
        rightMenu.Tick();
        PollFineMenu(rTip, rIdx, lTip, lIdx);
    }

    /// <summary>
    /// 바늘 위 미세 조정 판을 돌린다(2026-09-28). ★조작 판과 같은 얼개다 — 판에 더 가까운 손 하나만 본다.
    /// 자리는 <see cref="ShowFineMenu"/>가 한 번 정하고, 여기서는 눈 쪽으로 돌리기만 한다(누르는 중에 판이 움직이면 헛짚는다).
    /// </summary>
    private void PollFineMenu(bool rTip, Vector3 rIdx, bool lTip, Vector3 lIdx)
    {
        if (fineMenu == null || !fineMenu.Visible) return;
        fineMenu.Follow(false, Vector3.zero, eye, false);
        bool useRight = rTip && (!lTip || fineMenu.Distance(rIdx) <= fineMenu.Distance(lIdx));
        string id = fineMenu.Poll(useRight ? rIdx : lIdx, useRight ? rTip : lTip);
        if (id != null)
        {
            lastPressTime = Time.unscaledTime;   // ★누른 직후 핀치를 막는다 — 조작 판과 같다
            Play(fineMenu.LastRepeat ? sndRepeat : sndPress);
            OnButton(id);
        }
        fineMenu.Tick();
    }

    // ★09-21 사용자: "UI 너무 조잡해 · 버튼 디자인도 투박하고."
    //   녹화 프레임에서 보니 채도 높은 초록·노랑·파랑이 나란히 있어 눈이 아팠다.
    //   → <b>어둡고 채도 낮은 한 계열</b>로 낮추고, 고른 상태만 밝아지게 둔다(SetLayout이 흰색과 섞는다).
    //   글자는 흰색이라 어두운 바탕에서 잘 읽힌다.
    private static readonly Color NavTint = new Color(0.24f, 0.29f, 0.38f);
    private static readonly Color ExitTint = new Color(0.40f, 0.20f, 0.22f);
    private static readonly Color AdjTint = new Color(0.24f, 0.26f, 0.30f);
    private static readonly Color TargetTint = new Color(0.30f, 0.27f, 0.20f);
    private static readonly Color UndoTint = new Color(0.33f, 0.25f, 0.20f);
    private static readonly Color ActiveTint = new Color(0.34f, 0.26f, 0.16f);   // 능동 — 마커 주황의 어두운 쪽
    private static readonly Color PressTint = new Color(0.32f, 0.20f, 0.32f);    // 압박 — 마커 자홍의 어두운 쪽
    private static readonly Color NeedleTint = new Color(0.18f, 0.29f, 0.30f);   // 바늘 — 표시물 청록의 어두운 쪽

    // ★위쪽 탭(2026-09-22) — <b>측정값이 있는 것만</b> 셋. 기준점 설정(대추·미간)은 아래 설정 칸으로 내렸다
    //   (09-22 3차 — 교수님 지시 "위쪽 탭에는 측정값 있는 애들만, 기준점 설정은 설정 칸 쪽에").
    //   ★굴곡·신전은 한 탭·한 단계다(09-22 2차 "굴곡 신전 한 단위로 묶기"):
    //   둘은 같은 시상면(법선 Right·눈금 0 연직)이라 각도기 하나에 바늘 넷을 두고 앞=굴곡·뒤=신전으로 저절로 가른다 —
    //   측굴이 좌우를 가르는 것과 같은 얼개다. 단계 값은 Flexion을 쓰고 Extension은 더 안 쓴다.
    //   기준선 세팅(Setup)은 기준점 설정에 합쳤고(정면 ◀▶), 완료(Done)는 탭이 없다 — 요약은 [나가기]가 남긴다.
    private static readonly RomRecordStep[] TabSteps =
    {
        RomRecordStep.Flexion, RomRecordStep.LateralFlexion, RomRecordStep.Rotation,
    };
    private static readonly string[] TabIds = { "tab2", "tab3", "tab4" };
    private static readonly string[] TabValueKeys = { "v2", "v3", "v4" };
    private static readonly string[] TabIconNames = { "flexext", "lateral", "rotation" };
    private readonly Texture2D[] tabIcons = new Texture2D[3];
    private Texture2D refIcon;              // 기준점 설정 타일 그림
    private const string RefId = "tab1", RefStatusKey = "v1";
    private const string FineValueKey = "finev";   // 미세 조정 판 가운데 값 칸
    private int layoutSig = int.MinValue;   // 배치를 다시 짜야 하는 상태의 지문 — 같으면 글자만 바꾼다

    // 탭 아래 값 색 — 바늘·마커와 같은 색(RomRecordVisual.ActiveColor·PassiveColor). 대추 하늘 · 미간 흰색.
    private const string HexActive = "#FFD199", HexPassive = "#FF6B05", HexC7 = "#4DE6FF", HexGlab = "#FFFFFF";
    private const string HexEar = "#73FF99";   // RomRecordVisual.EarColor와 같다

    // 칸 치수(판 칸 단위). ★Meta 판은 칸 4.9cm·줄 4.1cm(씬 값)라 판 폭 6.5칸 ≈ 33cm다 — 설정 팝업(33cm)과 같다.
    private const float BoardSpan = 6.5f, TabRows = 1.8f, ValueBand = 0.7f, ValueScale = 0.95f;
    private const float CtlSpan = 1.55f, CtlGap = 0.1f, RefRows = 1.8f, RefStatusRows = 0.5f;
    private static float TabSpan => BoardSpan / TabSteps.Length;
    // ★판이 미리 만들어 두는 칸 수. 글자 칸(탭 아래 값·상태)도 한 칸씩 먹는다.
    //   09-22에 14로 두었다가 기준점 단계(최대 18칸)에서 [정면 ▶]·[정면 다시]가 <b>조용히 잘렸다</b>(사용자 발견).
    //   넘치면 판이 경고를 남긴다.
    // ★24 → 32(09-23): 기준점 단계에 위치 이동 버튼 여섯이 늘어 칸이 모자라면 <b>조용히 잘린다</b>(09-22 전례).
    private const int MenuCapacity = 32;

    private void LoadTabIcons()
    {
        for (int i = 0; i < tabIcons.Length; i++)
            tabIcons[i] = LoadIcon(TabIconNames[i]);
        refIcon = LoadIcon("landmarks");
    }

    private static Texture2D LoadIcon(string name)
    {
        var t = Resources.Load<Texture2D>("RomRecordUI/Icons/" + name);
        if (t == null)
            Debug.LogWarning($"[실측기록] ★탭 그림을 못 찾았다(Resources/RomRecordUI/Icons/{name}) — 그 칸은 글자만 나온다.");
        return t;
    }

    /// <summary>
    /// 판 배치. 위에서부터 ① 측정 탭 셋과 그 아래 값 ② 안내 한 줄 + [나가기] ③ 설정 칸 —
    /// 왼쪽에 [기준점 설정] 타일(그림 + 대추·미간 ●●), 오른쪽에 지금 단계의 조작 버튼 세 줄.
    ///
    /// ★사용자 09-22 1차: 다음·이전이 번거롭다 · 글자뿐이라 직관적이지 않다 · 떠 있던 측정 정보가 안 보인다.
    ///   → 탭으로 바로 간다(자동 넘김 없음). 탭은 그림+글자. 떠 있던 안내판은 없앴다.
    /// ★09-22 2차: 굴곡·신전 한 단위 · 정보를 탭 아래에 · 각도 색은 밝기·채도로.
    /// ★09-22 3차(교수님): 대추·미간 → «기준점 설정»으로 이름을 바꾸고 위쪽 탭에서 빼 설정 칸에 둔다.
    /// ★동작 탭은 <b>대추</b>를 찍어야 풀린다 — GoTo와 같은 조건이다(바늘은 미간 없이도 잰다).
    /// ★배치는 <see cref="LayoutSignature"/>가 바뀔 때만 다시 짠다. 값만 바뀌면 SetText로 끝낸다 —
    ///   SetLayout은 쿨다운을 다시 걸어 누르고 있던 반복 버튼(±1°·정면 ◀▶)을 끊기 때문이다.
    /// </summary>
    private void ApplyStepButtons()
    {
        var items = new List<RomMenuItem>(24);
        float tabSpan = TabSpan;

        // ── ① 측정 탭과 그 아래 값 ──
        for (int i = 0; i < TabSteps.Length; i++)
        {
            RomRecordStep s = TabSteps[i];
            items.Add(new RomMenuItem
            {
                id = TabIds[i], label = StepTitle[(int)s],
                col = i * tabSpan, row = 0f, span = tabSpan, rows = TabRows,
                tint = NavTint, selected = step == s,
                locked = !hasC7,
                icon = tabIcons[i],
            });
            items.Add(new RomMenuItem
            {
                id = null, key = TabValueKeys[i], label = TabValues(s),
                col = i * tabSpan, row = TabRows, span = tabSpan, rows = ValueBand, textScale = ValueScale,
            });
        }

        // ── ② 안내 한 줄 + [나가기] ──
        float r = TabRows + ValueBand;
        items.Add(new RomMenuItem { id = null, key = "guide", label = GuideText(), col = 0f, row = r,
                                    span = BoardSpan - CtlSpan, alignLeft = true });
        items.Add(RomMenuItem.Button("exit", "나가기", BoardSpan - CtlSpan, r, CtlSpan, ExitTint, holdSeconds: exitHoldSeconds));
        r += 1f;

        // ── ③ 설정 칸: 왼쪽 [기준점 설정] 타일 ──
        items.Add(new RomMenuItem
        {
            id = RefId, label = StepTitle[(int)RomRecordStep.Landmarks],
            col = 0f, row = r, span = CtlSpan, rows = RefRows,
            tint = TargetTint, selected = step == RomRecordStep.Landmarks,
            icon = refIcon,
        });
        items.Add(new RomMenuItem
        {
            id = null, key = RefStatusKey, label = TabValues(RomRecordStep.Landmarks),
            col = 0f, row = r + RefRows, span = CtlSpan, rows = RefStatusRows, textScale = ValueScale,
        });

        // ── ③ 설정 칸: 오른쪽 조작 버튼(한 줄에 셋) ──
        int n = 0;
        void Ctl(RomMenuItem it)
        {
            it.col = CtlSpan + CtlGap + (n % 3) * (CtlSpan + CtlGap);
            it.row = r + n / 3;
            it.span = CtlSpan;
            items.Add(it);
            n++;
        }
        if (step == RomRecordStep.Landmarks)
        {
            // ★밝은 버튼 = 지금 <b>다듬을 대상</b>인 점(아래 이동 버튼이 이 점을 옮긴다).
            //   찍혔는지 여부는 기준점 타일의 ●●●가 말한다 — 두 정보를 한 버튼에 겹치지 않는다.
            // ★★이미 찍은 점은 <b>누르면 대상만 고르고, 길게 누르면 다시 찍는다</b>(2026-09-28 사용자 확정 ⓑ).
            //   09-23에 버튼을 누르는 순간 찍히게 바꾸면서, 이동 버튼의 대상을 바꾸려고 누르면 점이 검지끝으로
            //   <b>다시 찍혀 버렸다</b> — 대상만 바꾸는 길이 없었다. 아직 안 찍은 점은 종전처럼 누르면 찍힌다.
            Ctl(LandmarkButton("t0", "대추", 0, hasC7));
            Ctl(LandmarkButton("t2", "외이도", 2, hasEar));
            Ctl(LandmarkButton("t1", "미간", 1, hasGlab));
            // ★기준선 세팅에서 옮겨 왔다(09-22). ↺↻는 앱 폰트(NotoSansKR-Bold)에 없어 ◀▶로 쓴다(글리프 표 실측).
            Ctl(RomMenuItem.Button("yaw-", "정면 ◀", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("yaw+", "정면 ▶", 0, 0, 0, AdjTint, repeat: true));
            // ★정면을 다시 잡는 버튼(09-21) — 자동은 처음 한 번뿐이라 여기서 고쳐 잡는다.
            if (hasC7 && hasGlab) Ctl(RomMenuItem.Button("aim", "정면 다시", 0, 0, 0, NavTint));
            else n++;   // 자리를 비워 둔다 — 미간을 찍는 순간 아랫줄 버튼이 밀리지 않게
            // ★기준점을 1mm씩 <b>옮기는</b> 버튼(2026-09-23 사용자: "좌우 회전 버튼 말고 위치 이동이 필요해").
            //   ★Nudge 기능은 09-21부터 코드에 있었는데 판에 버튼이 없어 쓸 수가 없었다 — 버튼만 얹는다.
            //   옮기는 대상은 <b>마지막으로 누른 기준점</b>이다(밝은 버튼이 그것이다).
            Ctl(RomMenuItem.Button("up", "위", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("down", "아래", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("fwd", "앞", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("back", "뒤", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("left", "왼쪽", 0, 0, 0, AdjTint, repeat: true));
            Ctl(RomMenuItem.Button("right", "오른쪽", 0, 0, 0, AdjTint, repeat: true));
        }
        else if (IsMotion(step))
        {
            // ★★바늘을 쓰면 버튼이 거의 필요 없다(09-21 "손이 너무 많이 가면 안 돼").
            //   끌어낸 자리가 곧 기록이라 [기록]이 없고, ±1°도 바늘을 다시 끌면 된다.
            Ctl(RomMenuItem.Button("needle", "바늘", 0, 0, 0, NeedleTint, selected: needleOn));
            if (needleOn)
            {
                // ★[홀드](09-23)는 없앴다(09-28) — 놓으면 저절로 잠기고, 구체를 치면 풀린다.
                Ctl(RomMenuItem.Button("nreset", "다시", 0, 0, 0, UndoTint));
            }
            else
            {
                // 점찍기로 쓸 때만 다듬기 버튼을 낸다.
                Ctl(RomMenuItem.Button("undo", "취소", 0, 0, 0, UndoTint));
                Ctl(RomMenuItem.Button("adj-", "-1°", 0, 0, 0, AdjTint, repeat: true));
                Ctl(RomMenuItem.Button("adj+", "+1°", 0, 0, 0, AdjTint, repeat: true));
            }
        }

        var arr = items.ToArray();
        leftMenu.SetLayout("", arr);
        // ★고정 모드에서는 둘이 같은 판이다 — 두 번 짜지 않는다.
        if (!ReferenceEquals(rightMenu, leftMenu)) rightMenu.SetLayout("", arr);
        layoutSig = LayoutSignature();
    }

    /// <summary>기준점 버튼 하나. 찍힌 점이면 «짧게 = 고르기 · 길게 = 다시 찍기» 버튼이 된다(길게 누르면 막대가 찬다).</summary>
    private RomMenuItem LandmarkButton(string id, string label, int t, bool placed)
    {
        var it = RomMenuItem.Button(id, label, 0, 0, 0, TargetTint, selected: landmarkTarget == t);
        if (placed)
        {
            it.holdSeconds = landmarkRecaptureSeconds;
            it.tapThenHold = true;
        }
        return it;
    }

    /// <summary>값 글자 칸(안내 + 탭 아래 셋 + 기준점 상태)만 새로 쓴다. 배치는 그대로다.</summary>
    private void RefreshMenuTexts()
    {
        SetMenuText("guide", GuideText());
        SetMenuText(RefStatusKey, TabValues(RomRecordStep.Landmarks));
        for (int i = 0; i < TabSteps.Length; i++)
            SetMenuText(TabValueKeys[i], TabValues(TabSteps[i]));
    }

    private void SetMenuText(string key, string text)
    {
        leftMenu.SetText(key, text);
        if (!ReferenceEquals(rightMenu, leftMenu)) rightMenu.SetText(key, text);
    }

    /// <summary>
    /// 배치를 다시 짜야 하는 상태의 지문 — 단계·대상·바늘·대추·미간(탭 잠금·고름·조작 버튼이 여기서 갈린다).
    /// ★각도 값·찍은 개수는 넣지 않는다 — 탭 아래 값은 SetText로 바꾼다.
    /// </summary>
    private int LayoutSignature()
    {
        unchecked
        {
            int h = (int)step;
            h = h * 31 + landmarkTarget;
            h = h * 31 + (needleOn ? 1 : 0) + (hasC7 ? 2 : 0) + (hasGlab ? 4 : 0) + (hasEar ? 8 : 0);
            return h;
        }
    }

    /// <summary>
    /// 탭 아래 값(2026-09-22). 대추·미간은 찍었나(●)·아직(○)만, 동작은 두 줄이다.
    ///   굴곡·신전 = 윗줄 «굴»(앞) · 아랫줄 «신»(뒤) / 측굴·회전 = 윗줄 «좌» · 아랫줄 «우».
    /// 각도는 숫자와 °만 쓰고 능동·압박은 <b>색의 밝기</b>로 가른다(능동 연하게·압박 진하게 — 사용자).
    /// ★바늘은 눈금 각, 점은 중립선 기준 각이다 — 종전 요약과 같은 수를 쓴다.
    /// </summary>
    private string TabValues(RomRecordStep s)
    {
        sb.Clear();
        if (s == RomRecordStep.Landmarks)
        {
            sb.Append("<color=").Append(HexC7).Append(hasC7 ? ">●" : "70>○").Append("</color> ");
            sb.Append("<color=").Append(HexEar).Append(hasEar ? ">●" : "70>○").Append("</color> ");
            sb.Append("<color=").Append(HexGlab).Append(hasGlab ? ">●" : "70>○").Append("</color>");
            return sb.ToString();
        }
        // ★윗줄이 재는 순서의 첫 방향이다(09-28) — 굴곡(앞) / 우측굴 / 우회전. 종전엔 측굴·회전이 좌가 윗줄이었다.
        const int first = 1;
        int mi = MotionIndex(s);
        Vector3 pivot = PivotFor(s);   // ★그 단계의 축 — 지금 단계의 축이 아니다
        for (int line = 0; line < 2; line++)
        {
            int side = line == 0 ? first : -first;
            if (line > 0) sb.Append('\n');
            sb.Append("<color=#FFFFFF80>").Append(SideShort(s, side)).Append("</color>");
            bool any = false;
            if (needleOn)
            {
                Vector3 z = RomRecordGeometry.ScaleZero(s, yaw);
                int count = NeedleCountOf(s);
                for (int k = 0; k < count; k++)
                {
                    int nIdx = mi * NeedlePerStep + k;
                    if (!needleMoved[nIdx] || NeedleSideOf(k) != side) continue;
                    AppendValue(sb, k % 2 == 1, Vector3.Angle(z, needleDir[nIdx]));
                    any = true;
                }
            }
            else
            {
                var list = marks[mi];
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m.side != side) continue;
                    if (m.byNeedle) AppendValue(sb, m.passive, Mathf.RoundToInt(m.dialDeg));
                    else if (hasGlab) AppendValue(sb, m.passive, Mathf.RoundToInt(AngleOf(s, m, pivot)));
                    else sb.Append(" <color=#FFFFFF60>—</color>");
                    any = true;
                }
            }
            if (!any) sb.Append(" <color=#FFFFFF40>—</color>");
        }
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder b, bool passive, int deg) =>
        b.Append(" <color=").Append(passive ? HexPassive : HexActive).Append('>').Append(deg).Append("°</color>");

    /// <summary>바늘 값 — ★0.1° 미세 조정이 생겨(09-28) 소수 한 자리로 적는다.</summary>
    private static void AppendValue(StringBuilder b, bool passive, float deg) =>
        b.Append(" <color=").Append(passive ? HexPassive : HexActive).Append('>').Append(deg.ToString("F1")).Append("°</color>");

    /// <summary>탭 아래 줄 머리 — 한 글자. 판 안에서는 한글을 써도 된다(빼는 것은 바늘·마커 위 글자뿐 — 사용자 09-22).</summary>
    private static string SideShort(RomRecordStep s, int side) =>
        s == RomRecordStep.Flexion ? (side > 0 ? "굴" : "신") : (side > 0 ? "우" : "좌");

    /// <summary>로그에 쓰는 방향 이름.</summary>
    private static string SideName(RomRecordStep s, int side) =>
        s == RomRecordStep.Flexion ? (side > 0 ? "굴곡" : "신전") : (side > 0 ? "우" : "좌");

    /// <summary>
    /// 바늘이 어느 쪽인가 — 굴곡·신전은 앞(+1)/뒤(-1), 측굴·회전은 환자 오른쪽(+1)/왼쪽(-1).
    /// ★미리보기(탭 아래 값)와 로그가 같은 함수를 탄다(규칙 9).
    /// </summary>
    private int NeedleSide(RomRecordStep s, Vector3 dir) =>
        s == RomRecordStep.Flexion ? RomRecordGeometry.FrontBackOfDirection(dir, yaw)
                                   : RomRecordGeometry.SideOfDirection(dir, yaw, flipSides);

    /// <summary>점찍기 마커가 어느 쪽인가 — <see cref="NeedleSide"/>와 같은 부호 약속이다.</summary>
    private int MarkSide(RomRecordStep s, Vector3 p) =>
        s == RomRecordStep.Flexion ? RomRecordGeometry.FrontBack(glab, p, yaw)
                                   : RomRecordGeometry.Side(glab, p, yaw, flipSides);

    /// <summary>안내 한 줄. ★판 폭(약 24cm)을 넘지 않게 짧게 쓴다 — 넘치면 [나가기]를 덮는다.</summary>
    private string GuideText()
    {
        if (step == RomRecordStep.Landmarks)
        {
            // ★손가락을 대고 버튼을 누른다(09-23) — 핀치로는 안 찍힌다. 자동으로 넘어가지 않는다(09-22 사용자).
            int next = NextUnsetLandmark();
            string moving = $" · 이동 버튼은 [{LandmarkName(landmarkTarget)}]를 1mm씩 옮깁니다";
            return next >= 0 ? $"{LandmarkName(next)}에 손가락을 대고 [{LandmarkName(next)}]를 누르세요"
                             : "기준점 완료 — 다시 찍으려면 대고 길게 누르세요" + moving;
        }
        if (!IsMotion(step)) return "";
        int mi = MotionIndex(step);
        if (needleOn)
        {
            // ★순서대로 다음에 맞출 바늘을 말한다(09-28) — 굴곡→신전 / 우측굴→좌측굴 / 우회전→좌회전, 방향마다 능동→압박.
            int cnt = NeedleCountOf(step), done = 0, next = -1;
            for (int k = 0; k < cnt; k++)
            {
                if (needleMoved[mi * NeedlePerStep + k]) done++;
                else if (next < 0) next = k;
            }
            return next >= 0
                ? $"{DirName(step, next / 2)} {(next % 2 == 0 ? "능동" : "압박")} — 바늘을 끌어 맞추세요 ({done}/{cnt}){NoEarNote(step)}"
                : $"다 맞췄습니다 · 구체를 치면 잠금이 풀립니다{NoEarNote(step)}";
        }
        // ★점찍기 각은 중립선(대추→미간) 기준이라 미간이 없으면 안 나온다.
        if (!hasGlab) return "점찍기는 미간이 있어야 각이 나옵니다";
        var list = marks[mi];
        if (list.Count >= Capacity(step)) return "다 찍었습니다" + NoEarNote(step);
        return $"{(list.Count % 2 == 0 ? "능동" : "압박")} {list.Count + 1}/{Capacity(step)} — 미간에 핀치하세요{NoEarNote(step)}";
    }

    /// <summary>외이도를 축으로 써야 하는 단계인데 외이도가 없으면 짧게 알린다(대추로 대신 잰다).</summary>
    private string NoEarNote(RomRecordStep s) => UsesEar(s) && !hasEar ? " · 대추 축" : "";

    /// <summary>
    /// 요약 줄에 쓸 각 하나. ★바늘은 눈금 각, 점은 중립선 기준 각이다 — <b>다른 수</b>라서 섞어 적으면 안 된다.
    /// 미간이 없으면 점은 각이 없다(0°가 아니라 «—»다).
    /// </summary>
    /// <summary>
    /// 그 단계의 <b>바늘 값</b>을 붙인다(09-21). ★끌어낸 바늘만 값이다 — 0°에 남은 것은 아직 안 잰 것이다.
    /// ★눈금 0은 단계마다 다르므로(연직 / 회전은 정면) 현재 단계가 아니어도 s로 직접 구한다.
    /// </summary>
    private void AppendNeedleValues(StringBuilder b, RomRecordStep s)
    {
        Vector3 z = RomRecordGeometry.ScaleZero(s, yaw);
        int mi = MotionIndex(s);
        int count = NeedleCountOf(s);
        bool any = false;
        for (int k = 0; k < count; k++)
        {
            int n = mi * NeedlePerStep + k;
            if (!needleMoved[n]) continue;
            b.Append(any ? " · " : "");
            b.Append(DirName(s, k / 2)).Append(' ');
            b.Append(NeedleName(k)).Append(' ')
             .Append(Vector3.Angle(z, needleDir[n]).ToString("F1")).Append('°');
            any = true;
        }
        if (!any) b.Append('—');
    }

    private void AppendDeg(StringBuilder b, RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        if (m.byNeedle) b.Append(m.dialDeg.ToString("F0")).Append('°');
        else if (hasGlab) b.Append(AngleOf(s, m, pivot).ToString("F0")).Append('°');
        else b.Append('—');
    }

    private void OnButton(string id)
    {
        // ★어느 버튼이 눌렸는지 남긴다(09-21). 종전엔 "버튼 누른 직후"만 있고 <b>무엇을 눌렀는지가 없어</b>
        //   5분짜리 판에서 왜 동작 단계로 못 갔는지를 로그로 가를 수가 없었다.
        if (id != "yaw-" && id != "yaw+" && id != "h+" && id != "h-" &&
            id != "fwd" && id != "back" && id != "left" && id != "right" && id != "up" && id != "down" &&
            id != "adj+" && id != "adj-" && id != "fine+" && id != "fine-")
            Debug.Log($"[실측기록] 버튼 [{id}] — 단계 {StepTitle[(int)step]}");

        Vector3 f = RomRecordGeometry.Forward(yaw), r = RomRecordGeometry.Right(yaw), u = Vector3.up;
        float mm = nudgeStepMm * 0.001f;
        switch (id)
        {
            case "yaw-": yaw -= yawStepDeg; break;
            case "yaw+": yaw += yawStepDeg; break;
            case "h+": if (!hasC7) frameOrigin += u * (heightStepMm * 0.001f); break;
            case "h-": if (!hasC7) frameOrigin -= u * (heightStepMm * 0.001f); break;
            // ★찍힌 점을 짧게 누르면 고르기만, 길게 누르면(«!hold») 다시 찍는다. 안 찍힌 점은 누르면 찍힌다(09-28).
            case "t0": if (hasC7) SelectLandmark(0); else SetTarget(0); break;
            case "t1": if (hasGlab) SelectLandmark(1); else SetTarget(1); break;
            case "t2": if (hasEar) SelectLandmark(2); else SetTarget(2); break;
            case "t0" + RomMenuItem.HoldSuffix: SetTarget(0); break;
            case "t1" + RomMenuItem.HoldSuffix: SetTarget(1); break;
            case "t2" + RomMenuItem.HoldSuffix: SetTarget(2); break;
            case "aim":
                frontAimed = false;      // 다시 잡게 풀어 준다
                AimFrontFromLandmarks();
                break;
            case "up": Nudge(u * mm); break;
            case "down": Nudge(-u * mm); break;
            case "fwd": Nudge(f * mm); break;
            case "back": Nudge(-f * mm); break;
            case "left": Nudge(-r * mm); break;
            case "right": Nudge(r * mm); break;
            case "adj+": AdjustLast(+adjustStepDeg); break;
            case "adj-": AdjustLast(-adjustStepDeg); break;
            case "undo": UndoLast(); break;
            case "needle":
                needleOn = !needleOn;
                for (int k = 0; k < NeedlePerStep; k++) lastNeedleDegShown[k] = -999;
                if (!needleOn) HideFineMenu();
                Debug.Log("[실측기록] 바늘 " + (needleOn ? "켬" : "끔"));
                ApplyStepButtons();
                break;
            case "nreset": ResetNeedles(); break;
            case "fine+": FineAdjust(+fineStepDeg); break;
            case "fine-": FineAdjust(-fineStepDeg); break;
            // ★탭(09-22) — [이전]/[다음] 대신 바로 간다. 잠긴 탭은 판이 id를 비워 여기까지 안 온다.
            case "tab1": GoTo(RomRecordStep.Landmarks); break;         // 기준점 설정 타일(설정 칸)
            case "tab2": GoTo(RomRecordStep.Flexion); break;          // 굴곡·신전
            case "tab3": GoTo(RomRecordStep.LateralFlexion); break;
            case "tab4": GoTo(RomRecordStep.Rotation); break;
            case "exit": Exit(); break;
        }
        dirty = true;
    }

    /// <summary>
    /// 환자 정면을 <b>대추 → 미간</b>으로 잡는다(2026-09-21 사용자 제안:
    /// "처음에 그냥 생성 바로 대추혈 찍으면 되는 거 아닌가?").
    ///
    /// ★대추는 목 뒤, 미간은 이마 앞이다. 그 사이 벡터의 <b>수평 성분</b>이 곧 환자가 보는 쪽이다.
    ///   그래서 기준선 세팅 단계에서 정면을 손으로 맞출 필요가 없다 — 두 점을 찍으면 저절로 정해진다.
    /// ★<b>지움</b>: 위아래 성분을 버린다(ProjectOnPlane). 미간이 대추보다 얼마나 높든 정면 방향은
    ///   수평이어야 하므로 의도한 것이다. 대신 <b>환자가 고개를 숙이거나 든 채로 찍으면</b>
    ///   수평 성분이 짧아져 정면이 흔들린다 — 그래서 중립에서 찍으라 하고, ↺↻로 다듬게 남긴다.
    /// ★부호를 추론으로 정하지 않았다(규칙 9). 두 점의 차라 뒤집힐 여지가 없다.
    ///   그래도 <b>Play에서 '환자 앞' 글자가 실제 환자 앞을 가리키는지 눈으로 봐야 한다.</b>
    /// </summary>
    private bool frontAimed;   // 정면을 이미 한 번 잡았나

    private void AimFrontFromLandmarks()
    {
        if (!hasC7 || !hasGlab) return;
        // ★★09-21 실측: 미간을 찍을 때마다 다시 잡았더니 정면이 347→1→97→172→121→258→156→316→133°로
        //   판마다 튀었다. 사용자가 ↺↻로 맞춰 놔도 다시 찍으면 날아가니 악순환이었다.
        //   → <b>처음 한 번만</b> 잡는다. 다시 잡고 싶으면 [정면] 버튼을 누른다.
        if (frontAimed)
        {
            Debug.Log("[실측기록] 정면은 이미 잡혀 있다 — 다시 잡으려면 [정면]을 누른다.");
            return;
        }
        Vector3 flat = Vector3.ProjectOnPlane(glab - c7, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f)
        {
            Debug.Log("[실측기록] ★대추와 미간이 거의 수직으로 겹쳐 정면을 못 잡았다 — ↺↻로 맞춘다.");
            return;
        }
        float before = yaw;
        yaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
        frontAimed = true;
        // ★미간이 대추보다 낮으면 잘못 찍었을 가능성이 크다 — 중립에서는 이마가 목 뒤보다 위다.
        string warn = glab.y < c7.y ? " ★미간이 대추보다 낮다 — 잘못 찍었는지 본다" : "";
        Debug.Log($"[실측기록] 환자 정면을 대추→미간으로 잡았다 — {before:F0}° → {yaw:F0}° " +
                  $"(수평 거리 {flat.magnitude * 100f:F0}cm · ↺↻로 다듬는다){warn}");
    }

    /// <summary>
    /// 기준점 버튼 — ★<b>누르는 순간</b> 그 자리에 찍는다(2026-09-23 사용자: "핀치는 너무 흔들린다").
    /// 자리는 <b>버튼을 누른 손의 반대 손</b> 검지끝이다. 오른손으로 누르면 왼손 검지끝이 목표점이다.
    /// ★반대 손을 쓰는 이유: 누르는 손 자신을 목표점으로 삼으면 판 앞에 점이 찍힌다.
    /// </summary>
    private void SetTarget(int t)
    {
        bool pointerLeft = lastPressWasRight;   // 오른손으로 눌렀으면 왼손이 가리키는 손이다
        if (!hands.TryJoint(pointerLeft, HandJointId.HandIndexTip, out Vector3 p))
        {
            Play(sndDeny);
            Debug.Log($"[실측기록] {LandmarkName(t)}를 못 찍는다 — " +
                      $"{(pointerLeft ? "왼손" : "오른손")} 검지가 안 잡힌다(그 손을 목표 자리에 두고 누른다).");
            return;
        }
        landmarkTarget = t;   // ±1mm 다듬기(Nudge)가 이 점을 본다
        PlaceLandmark(t, p);
    }

    /// <summary>이동 버튼이 옮길 기준점만 고른다 — 점은 그대로다(09-28).</summary>
    private void SelectLandmark(int t)
    {
        if (landmarkTarget == t) return;
        landmarkTarget = t;
        Debug.Log($"[실측기록] 이동 대상 → {LandmarkName(t)}(점은 그대로 · 다시 찍으려면 길게 누른다)");
        ApplyStepButtons();
    }

    /// <summary>
    /// 기준점 하나를 그 자리에 세운다. ★찍는 <b>수단</b>(버튼·핀치)과 <b>결과</b>를 갈라 둔다 —
    /// 2026-09-23에 핀치에서 버튼으로 옮기면서, 로그·음성·정면 재계산이 딸려 오도록 한 곳에 모았다.
    /// </summary>
    private void PlaceLandmark(int t, Vector3 p)
    {
        if (t == 0)
        {
            c7 = p; hasC7 = true;
            Play(sndPinch);
            Debug.Log($"[실측기록] 대추 {Fmt(c7)}");
        }
        else if (t == 2)
        {
            ear = p; hasEar = true;
            Play(sndPinch);
            // ★외이도는 대추보다 위다 — 낮으면 잘못 찍었을 가능성이 크다.
            //   정중면에서 옆으로 얼마나 떨어졌나도 남긴다(한쪽 귀라 6~8cm쯤이어야 한다 — 추정. 정면을 잡은 뒤라야 뜻이 있다).
            string warn = hasC7 && ear.y < c7.y ? " ★대추보다 낮다 — 잘못 찍었는지 본다" : "";
            Debug.Log($"[실측기록] 외이도 {Fmt(ear)}" +
                      (hasC7 ? $" · 대추에서 위로 {(ear.y - c7.y) * 100f:F1}cm" +
                               $" · 정중면에서 옆으로 {Mathf.Abs(Vector3.Dot(ear - c7, RomRecordGeometry.Right(yaw))) * 100f:F1}cm" +
                               $" · 축 {Fmt(EarAxis)}" : "") + warn);
        }
        else
        {
            glab = p; hasGlab = true;
            Play(sndPinch);
            Debug.Log($"[실측기록] 미간(중립) {Fmt(glab)}");
            AimFrontFromLandmarks();
        }
        landmarkPlacedTime[t == 0 ? 0 : t == 1 ? 1 : 2] = Time.unscaledTime;   // 즉시 고침을 재는 기준
        // ★다음은 자동으로 넘기지 않고 말로 알린다(09-22 사용자) — 사용자가 버튼을 누른다.
        int nextT = NextUnsetLandmark();
        Say(DoneVoice(t), nextT >= 0 ? NextVoice(nextT) : "all_done");
        ApplyStepButtons();
        dirty = true;
    }

    private static string LandmarkName(int t) => t == 0 ? "대추" : t == 2 ? "외이도" : "미간";
    private static string ArmVoice(int t) => t == 0 ? "arm_c7" : t == 2 ? "arm_ear" : "arm_glab";
    private static string DoneVoice(int t) => t == 0 ? "done_c7" : t == 2 ? "done_ear" : "done_glab";
    private static string NextVoice(int t) => t == 0 ? "next_c7" : t == 2 ? "next_ear" : "next_glab";

    /// <summary>아직 안 찍은 기준점 중 첫째(대추 → 외이도 → 미간). 다 찍었으면 -1.</summary>
    private int NextUnsetLandmark() => !hasC7 ? 0 : !hasEar ? 2 : !hasGlab ? 1 : -1;

    private void Nudge(Vector3 d)
    {
        if (landmarkTarget == 0 && hasC7) c7 += d;
        else if (landmarkTarget == 1 && hasGlab) glab += d;
        else if (landmarkTarget == 2 && hasEar) ear += d;
    }

    private void GoTo(RomRecordStep s)
    {
        if (s < RomRecordStep.Setup || s > RomRecordStep.Done) return;
        if (s == step) return;   // ★지금 탭을 다시 누른 것 — 배치를 다시 짜면 쿨다운만 걸린다
        // ★대추만 필수로 바꿨다(09-21). 바늘은 미간 없이도 재는 수단이라 미간으로 막으면 그 길이 닫힌다.
        //   미간이 없으면 점찍기 각(중립선 기준)은 못 내고 바늘 눈금 각만 남는다 — 안내판이 그것을 말한다.
        if (IsMotion(s) && !hasC7)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 대추를 먼저 찍는다 — 동작 단계로 못 넘어간다.");
            return;
        }
        if (IsMotion(s) && !hasGlab)
            Debug.Log("[실측기록] ★미간이 없다 — 점찍기는 각이 안 나온다. 바늘로 눈금 각만 기록된다.");
        // ★단계를 넘길 때마다 오조작을 쏟는다 — 어느 단계에서 얼마나 헛손질했는지 갈리게(09-23).
        DumpMisses($"{StepTitle[(int)step]} 떠남");
        step = s;
        HideFineMenu();   // ★미세 조정 판이 <b>다른 단계</b>의 바늘을 움직이지 않게 한다
        ApplyStepButtons();
        Debug.Log($"[실측기록] 단계 → {StepTitle[(int)step]}");
        if (step == RomRecordStep.Done) LogSummary();
    }

    private void Exit()
    {
        LogSummary();
        Debug.Log("[실측기록] 나가기 → " + lobbySceneName);
        DumpMisses("나가기");
        SceneLoader.LoadScene(lobbySceneName);
    }

    // ── 핀치 ─────────────────────────────────────────────────────────
    private void HandlePinch(bool isLeft)
    {
        // ★한 번에 한 손만. 다른 손이 오므리고 있으면 이 손은 새로 시작하지 않는다.
        bool otherBusy = liveActive && livePinchLeft != isLeft;

        // ★버튼을 누르는 동안과 누른 직후에는 핀치를 받지 않는다(09-18 로그: 의도하지 않은 핀치 30여 번).
        //   판을 누르러 다가오는 중(고정 상태)이면 양손 모두 막는다 — 가려진 손이 핀치로 읽히는 것을 막는다.
        // ★고정 모드에서는 «판 근처»로 막지 않는다(09-21) — 판이 손에서 떨어져 있어 우연히 겹칠 일이 없고,
        //   오히려 판을 <b>잡아 끌려면</b> 판 근처에서 핀치가 돼야 한다. 그 차단이 간섭의 주범이었다.
        bool nearMenu = !menuFixedInSpace
                        && hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 tip)
                        && (leftMenu.Near(tip, 0.06f) || rightMenu.Near(tip, 0.06f));

        // ★막은 이유를 문구로 넘긴다(09-21) — 막는 자리가 넷인데 셋이 조용해 원인을 못 갈랐다.
        string blockedBy = otherBusy ? "다른 손이 잡는 중"
                         : menuHoldL || menuHoldR ? "판에 손이 다가옴(판 고정 중)"
                         : Time.unscaledTime - lastPressTime < 0.4f ? "버튼 누른 직후"
                         : nearMenu ? "판 근처" : null;

        int ev = hands.UpdatePinch(isLeft, blockedBy, out Vector3 fixedPos);
        var st = isLeft ? hands.LeftPinch : hands.RightPinch;
        ReportPinch(isLeft, st);

        if (ev == 1)
        {
            liveActive = true;
            livePinchLeft = isLeft;
            // ★잡을 것이 있으면 «찍기»가 아니라 «잡아 끌기»다. 없으면 종전대로 찍는다.
            dragTarget = FindDragTarget(st.current, out dragStartValue);
            if (dragTarget >= 0)
            {
                dragGrabbedAt = st.current;
                dragStartTime = Time.unscaledTime;
                Play(sndPress);
                Debug.Log($"[실측기록] 잡았다 — {DragName(dragTarget)}");
                // ★바늘을 끄는 동안 미세 조정 판은 숨긴다 — 놓으면 새 자리 위에 다시 선다.
                if (dragTarget >= 3 && dragTarget <= 6) HideFineMenu();
                // ★놓은 지 얼마 안 된 것을 다시 잡았다 = 직전에 놓은 자리가 틀렸다는 신호다.
                if (dragTarget >= 3 && dragTarget <= 6
                    && Time.unscaledTime - needleEndTime[dragTarget - 3] < 3f)
                    CountMiss(Miss.QuickRedoNeedle, $"{NeedleName(dragTarget - 3)}를 놓자마자 다시 잡았다");
                int lm = dragTarget == 1 ? 0 : dragTarget == 2 ? 1 : dragTarget == 8 ? 2 : -1;
                if (lm >= 0 && Time.unscaledTime - landmarkPlacedTime[lm] < 5f)
                    CountMiss(Miss.QuickFixLandmark, $"{LandmarkName(lm == 1 ? 1 : lm == 2 ? 2 : 0)}를 찍자마자 옮긴다");
            }
            else
            {
                // ★잡을 것이 없었다 — <b>왜</b> 없었는지를 센다(핀치는 여기서 «찍기»로 넘어간다).
                CountNearMiss(st.current);
            }
        }
        if (liveActive && livePinchLeft == isLeft)
        {
            if (st.closed)
            {
                if (dragTarget >= 0) ApplyDrag(st.current);
                // ★바늘 모드에서는 «찍는 중» 표시를 아예 안 낸다(2026-09-23 사용자: "바늘 모드일 때
                //   핀치로 마커 포인트 생기는 거 끄고"). 점은 종전에도 안 찍혔지만 노란 구가 떠서
                //   <b>찍히는 것처럼 보였다</b> — 핀치는 잡아 끌기 전용이다.
                else if (!needleOn) view.SetLive(true, st.current);
            }
            if (ev == 2)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                if (dragTarget >= 0) EndDrag();
                else OnPinchFixed(fixedPos);
            }
            else if (ev == 3 || ev == 4)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                // ★놓친 드래그는 되돌리지 않는다 — 옮긴 자리를 그대로 둔다(되돌리면 더 놀란다).
                if (dragTarget >= 0) EndDrag();
                else if (ev == 3) Debug.Log("[실측기록] 핀치 취소 — 손을 놓쳤거나 시스템 제스처가 시작됐다.");
            }
        }
    }

    /// <summary>
    /// ★막힌 이유를 2초에 한 줄만 말한다(자기 침묵 — 잘 잡히면 한 줄도 안 남는다).
    /// "위에서 잡으면 안 잡힌다"(09-21)를 가르려고 <b>손 높이·판까지 거리·신뢰도</b>를 같이 찍는다.
    /// </summary>
    private void ReportPinch(bool isLeft, RomRecordHands.PinchState st)
    {
        string r = st.blockReason;
        if (r == null) return;
        // ★세는 것은 매번, 말하는 것은 손별로 2초에 한 번. 요약에서 <b>종류별 횟수</b>로 본다 —
        //   한 줄씩 쏟아 놓고 눈으로 세면 오판한다(09-21에 11,000줄을 쏟아 놓고 그랬다).
        pinchBlockCount.TryGetValue(r, out int n);
        pinchBlockCount[r] = n + 1;

        int k = isLeft ? 0 : 1;
        if (Time.unscaledTime < nextPinchLog[k]) return;
        nextPinchLog[k] = Time.unscaledTime + 2f;

        float eyeDy = eye != null ? (st.tip.y - eye.position.y) * 100f : 0f;
        float toMenu = Mathf.Min(leftMenu.Distance(st.tip), rightMenu.Distance(st.tip));
        Debug.Log($"[실측기록·핀치] 안 받는다 — {(isLeft ? "왼" : "오른")}손 «{r}» · " +
                  $"간격 {(st.gap >= 0f ? (st.gap * 100f).ToString("F1") + "cm" : "—")} · " +
                  $"신뢰 {(st.highConfidence ? "O" : "★낮음")} · 눈높이 대비 {eyeDy:+0;-0}cm · " +
                  $"판까지 {(toMenu < 9f ? (toMenu * 100f).ToString("F0") + "cm" : "—")}");
    }

    // ── 잡아 끌기(09-21 신설) ─────────────────────────────────────────
    private static string DragName(int t) =>
        t == 0 ? "기준선(3축)" : t == 1 ? "대추" : t == 2 ? "미간" : t == 8 ? "외이도" : t == 9 ? "조작 판" : "바늘";

    /// <summary>
    /// 이 자리에서 오므리면 조작 판이 잡히나. ★잡기(<see cref="FindDragTarget"/>)와 하이라이트가 <b>같은 함수</b>를 탄다 —
    /// 따로 계산하면 «테두리는 켜졌는데 안 잡힌다»가 생긴다(규칙 9: 미리보기는 실제와 같은 함수를 탄다).
    /// </summary>
    private bool InMenuGrabZone(Vector3 p) =>
        menuFixedInSpace && leftMenu != null && leftMenu.Placed
        && leftMenu.Near(p, menuGrabMargin);

    /// <summary>
    /// 잡을 것이 없이 오므렸을 때, <b>거의 잡힐 뻔한 것</b>을 센다(2026-09-23).
    /// ★여기서만 센다 — <see cref="FindDragTarget"/> 안에서 세면 하이라이트 때문에 매 프레임 불려 수가 터진다.
    /// </summary>
    private void CountNearMiss(Vector3 p)
    {
        // ① 잠근 바늘을 잡으려 했나 = [홀드]가 실제로 막아 준 횟수다.
        if (needleOn && NeedleVisible(out Vector3 nc, out _, out _, out _, out float nh, out int mi))
        {
            bool hang = NeedleFinHangs(step);
            int count = NeedleCountOf(step);
            for (int k = 0; k < count; k++)
            {
                int n = mi * NeedlePerStep + k;
                if (!needleLocked[n] || !NeedleInPlay(mi, k)) continue;
                if (NeedleDistance(p, nc, needleDir[n], nh, NeedleFin(step, needleDir[n]), hang) <= needleGrabRadius)
                {
                    CountMiss(Miss.LockedNeedle, $"{NeedleName(k)}는 잠겨 있다(구체를 치면 풀린다)");
                    return;
                }
            }
        }
        // ② 판 <b>언저리</b>에서 오므렸나. 09-22의 진범이 이 형태였다(판 앞 10~26cm의 조기 핀치).
        if (menuFixedInSpace && leftMenu != null && leftMenu.Placed
            && leftMenu.Near(p, menuGrabMargin + 0.12f))
        {
            CountMiss(Miss.NearPlate, "판을 잡으려다 조금 일찍 오므렸다(테두리가 켜진 뒤에 오므린다)");
        }
    }

    // ★판 잡기 하이라이트(09-22 사용자: "잡을 수 있는 상태에 들어왔다는 하이라이트가 있으면").
    //   09-22 로그: 판을 잡으러 가다 조금 일찍(판 앞 10~26cm) 오므린 핀치가 대추·미간을 덮어썼다.
    //   테두리가 켜진 뒤에 오므리면 된다는 것을 눈으로 알게 한다.
    private int menuHighlight = -1;
    private int menuHoverCount;   // 잡기 준비에 들어간 횟수 — 요약에 남긴다

    /// <summary>
    /// ★★"모든 오브젝트는 그랩되기 전에 «지금 잡으면 잡힌다»는 하이라이트가 필수다"(2026-09-23 사용자).
    /// ★판정은 <b>실제로 잡을 때 쓰는 <see cref="FindDragTarget"/> 그대로</b>를 태운다 —
    ///   따로 계산하면 하이라이트가 거짓말을 한다(규칙 9. 09-01에 실제로 밟았다).
    /// 잡는 자리는 엄지·검지 사이(오므리면 핀치가 생기는 자리)다 — 핀치 판정과 같은 점이다.
    /// </summary>
    private int HoverTargetNow()
    {
        if (dragTarget >= 0) return dragTarget;      // 잡고 있는 동안은 그것이 계속 밝다
        int t = HoverOfHand(false);                  // 오른손 먼저
        return t >= 0 ? t : HoverOfHand(true);
    }

    private int HoverOfHand(bool left)
    {
        if (!hands.TryJoint(left, HandJointId.HandThumbTip, out Vector3 th)
            || !hands.TryJoint(left, HandJointId.HandIndexTip, out Vector3 ix)) return -1;
        return FindDragTarget((th + ix) * 0.5f, out _);
    }

    private void UpdateMenuHighlight()
    {
        view.SetGrabHover(HoverTargetNow());

        int level = dragTarget == 9 ? 2
                  : !liveActive && (HandInMenuGrabZone(true) || HandInMenuGrabZone(false)) ? 1
                  : 0;
        if (level == menuHighlight) return;
        if (level == 1 && menuHighlight == 0) menuHoverCount++;
        menuHighlight = level;
        leftMenu.SetHighlight(level);
        if (!ReferenceEquals(rightMenu, leftMenu)) rightMenu.SetHighlight(level);
    }

    /// <summary>그 손의 엄지·검지 사이(오므리면 핀치가 생기는 자리)가 판 잡는 범위 안인가.</summary>
    private bool HandInMenuGrabZone(bool left) =>
        hands.TryJoint(left, HandJointId.HandThumbTip, out Vector3 t)
        && hands.TryJoint(left, HandJointId.HandIndexTip, out Vector3 i)
        && InMenuGrabZone((t + i) * 0.5f);

    /// <summary>핀치를 오므린 자리에서 잡을 수 있는 것을 고른다. 없으면 -1(그러면 «찍기»로 간다).</summary>
    private int FindDragTarget(Vector3 p, out Vector3 startValue)
    {
        startValue = default;
        // ★바늘 손잡이가 먼저다(09-21) — 동작 단계에서 바늘 끝을 잡으면 마커를 찍지 않는다.
        //   바늘이 둘이라 <b>가까운 쪽</b>을 잡는다.
        if (NeedleVisible(out Vector3 nc, out _, out _, out _, out float nh, out int mi))
        {
            int count = NeedleCountOf(step);
            bool hang = NeedleFinHangs(step);
            // ★이미 끌어낸 바늘부터 본다 — 손잡이가 흩어져 있어 어느 것을 집는지 분명하다.
            int best = -1;
            float bestD = needleGrabRadius;
            for (int k = 0; k < count; k++)
            {
                int n = mi * NeedlePerStep + k;
                // ★잠근 바늘은 안 잡힌다(놓으면 저절로 잠긴다 — 구체를 쳐서 푼다). 아직 차례가 아닌 방향도 안 잡힌다.
                if (!needleMoved[n] || needleLocked[n] || !NeedleInPlay(mi, k)) continue;
                float d = NeedleDistance(p, nc, needleDir[n], nh, NeedleFin(step, needleDir[n]), hang);
                if (d <= bestD) { bestD = d; best = k; }
            }
            // ★아직 안 끌어낸 것들은 한계 자리에 능동·압박이 포개져 있다 — 그중 <b>첫째</b>(능동)를 집는다.
            //   그래서 능동 → 압박 순서로 하나씩 꺼내진다.
            if (best < 0)
            {
                for (int k = 0; k < count; k++)
                {
                    int n = mi * NeedlePerStep + k;
                    if (needleMoved[n] || needleLocked[n] || !NeedleInPlay(mi, k)) continue;
                    if (NeedleDistance(p, nc, needleDir[n], nh, NeedleFin(step, needleDir[n]), hang) <= needleGrabRadius)
                    { best = k; break; }
                }
            }
            if (best >= 0) return 3 + best;   // 3·4·5·6 = 바늘 0·1·2·3
        }
        // ★조작 판도 잡아 끈다(09-21 고정 모드) — 자리가 마음에 안 들면 옮긴다.
        if (InMenuGrabZone(p))
        {
            startValue = leftMenu.Position;
            return 9;   // ★바늘이 3~6을 쓴다
        }
        if (step == RomRecordStep.Setup)
        {
            if (Vector3.Distance(p, frameOrigin) > axesGrabRadius) return -1;
            startValue = frameOrigin;
            return 0;
        }
        if (step != RomRecordStep.Landmarks) return -1;

        // 찍혀 있는 점만 잡는다. 아직 안 찍은 것은 [대추]·[외이도]·[미간] 버튼으로 찍는다(09-23).
        // ★가장 가까운 점을 잡는다(대추·외이도·미간 — 09-22 추가).
        float dC7 = hasC7 ? Vector3.Distance(p, c7) : float.MaxValue;
        float dGl = hasGlab ? Vector3.Distance(p, glab) : float.MaxValue;
        float dFf = hasEar ? Vector3.Distance(p, ear) : float.MaxValue;
        float nearest = Mathf.Min(dC7, Mathf.Min(dGl, dFf));
        if (nearest > dotGrabRadius) return -1;
        if (nearest == dC7) { startValue = c7; return 1; }
        if (nearest == dFf) { startValue = ear; return 8; }
        startValue = glab;
        return 2;
    }

    /// <summary>
    /// 바늘이 지금 보이나. 보이면 기하와 <b>손잡이 반지름</b>을 준다. 바늘 둘이 같은 기하를 쓴다.
    /// ★손잡이 반지름은 눈금 반지름보다 <see cref="needleHandleOut"/>만큼 <b>더 밖</b>이다 —
    ///   눈금 반지름은 회전중심에서 미간까지라 그 자리에 손잡이를 두면 <b>사람 머리 안에 묻힌다</b>(09-21).
    /// </summary>
    private bool NeedleVisible(out Vector3 center, out Vector3 normal, out Vector3 zero,
                               out float radius, out float handleRadius, out int mi)
    {
        mi = 0;
        handleRadius = needleFallbackRadius + needleHandleOut;
        if (!needleOn || !DialFrame(out center, out normal, out zero, out radius))
        {
            center = default; normal = Vector3.up; zero = Vector3.forward; radius = needleFallbackRadius;
            return false;
        }
        handleRadius = radius + needleHandleOut;
        // ★회전 바늘은 원통 <b>윗 테두리</b> 높이에 건다(2026-09-23 사용자) — 눈금판 높이에 두면 머리에 가린다.
        //   ★각은 안 바뀐다: 회전은 단면 법선이 연직이라 ProjectOnPlane이 높이 성분을 지운다(실측 아님·계산).
        //   ★여기서 한 번만 올린다 — 잡기(FindDragTarget)·끌기(ApplyDrag)·그리기(DrawNeedle)가 다 이 center를 쓴다.
        center += Vector3.up * NeedleRise(step);
        mi = MotionIndex(step);
        for (int k = 0; k < NeedlePerStep; k++)
        {
            int n = mi * NeedlePerStep + k;
            if (needleMoved[n]) continue;
            // ★아직 안 끌어낸 바늘은 <b>그 방향의 한계 각도</b>에 세운다(2026-09-28 사용자).
            //   ★매번 다시 세운다 — 정면(yaw)을 다듬으면 한계 자리도 따라가야 한다. 값이 아니라 출발점이라 괜찮다.
            //   (09-21~09-23엔 전부 눈금 0에 포개 세웠다.)
            needleDir[n] = LimitDir(step, k);
        }
        return true;
    }

    /// <summary>
    /// 그 바늘이 서는 한계 자리(단면 안의 단위 방향). 눈금 0에서 그 방향 쪽으로 한계 각도만큼 돈 자리다.
    /// ★부호를 각으로 정하지 않는다(규칙 9) — «앞»(대추→미간으로 잡은 정면)·«환자 오른쪽» 벡터를 그대로 섞는다.
    ///   그래서 <see cref="NeedleSide"/>(같은 두 벡터에 내적)와 늘 같은 쪽을 가리킨다.
    /// </summary>
    private Vector3 LimitDir(RomRecordStep s, int k)
    {
        Vector3 z = RomRecordGeometry.ScaleZero(s, yaw);
        int sign = NeedleSideOf(k);
        Vector3 toward = s == RomRecordStep.Flexion
            ? RomRecordGeometry.Forward(yaw) * sign
            : RomRecordGeometry.Right(yaw) * (flipSides ? -sign : sign);   // ★Side()의 뒤집기와 같은 약속
        float deg = LimitDeg(s, k);
        float a = deg * Mathf.Deg2Rad;
        return (z * Mathf.Cos(a) + toward * Mathf.Sin(a)).normalized;
    }

    private float LimitDeg(RomRecordStep s, int k) =>
        s == RomRecordStep.Flexion ? (k / 2 == 0 ? flexionLimitDeg : extensionLimitDeg)
        : s == RomRecordStep.LateralFlexion ? lateralLimitDeg
        : rotationLimitDeg;

    /// <summary>
    /// 바늘 k가 묶인 쪽. ★k/2 = 0이 첫 방향(+1: 굴곡=앞 · 우측굴 · 우회전), 1이 둘째 방향(-1: 신전 · 좌측굴 · 좌회전).
    /// 부호 약속은 <see cref="NeedleSide"/>·<see cref="MarkSide"/>와 같다.
    /// </summary>
    private static int NeedleSideOf(int k) => k / 2 == 0 ? 1 : -1;

    /// <summary>방향 이름 — 재는 순서 그대로다(09-28 사용자: 굴곡 → 신전 → 우측굴 → 좌측굴 → 우회전 → 좌회전).</summary>
    private static string DirName(RomRecordStep s, int dirIdx) =>
        s == RomRecordStep.Flexion ? (dirIdx == 0 ? "굴곡" : "신전")
        : s == RomRecordStep.LateralFlexion ? (dirIdx == 0 ? "우측굴" : "좌측굴")
        : (dirIdx == 0 ? "우회전" : "좌회전");

    /// <summary>
    /// 바늘 k가 지금 차례에 들어와 있나(보이고·잡힌다). ★첫 방향 둘(능동·압박)을 다 맞춰야 둘째 방향이 나온다 —
    /// 순서대로 한 방향씩 잰다(09-28). 둘째 방향에 이미 값이 있으면(돌아와 고칠 때) 그대로 보인다.
    /// </summary>
    private bool NeedleInPlay(int mi, int k)
    {
        if (k / 2 == 0) return true;
        int b = mi * NeedlePerStep;
        return (needleMoved[b] && needleMoved[b + 1]) || needleMoved[b + 2] || needleMoved[b + 3];
    }

    /// <summary>그 단계에서 쓰는 바늘 수 — 굴곡·신전은 둘, 측굴·회전은 넷(좌우가 있어서).</summary>
    private int NeedleCountOf(RomRecordStep s) => HasSides(s) ? 4 : 2;

    // ★이름을 짧게 쓴다(09-21 녹화 실측) — "능동2/압박2"는 네 글자가 서로 겹쳐 읽을 수가 없었다.
    private static string NeedleName(int k) => k == 0 ? "능1" : k == 1 ? "압1" : k == 2 ? "능2" : "압2";

    /// <summary>회전 바늘을 원통 윗 테두리까지 올리는 높이. 다른 단면은 0이다(평면 눈금판이라 가릴 것이 없다).</summary>
    private float NeedleRise(RomRecordStep s) => s == RomRecordStep.Rotation ? cylinderHeight * 0.5f : 0f;

    /// <summary>
    /// 바늘 면이 <b>뻗는 방향 × 크기</b>. 0이면 종전처럼 가는 선이다.
    /// ★★<b>면은 그 단계의 각도기와 수직이다</b>(2026-09-23 사용자 정정):
    ///   "측정하는 축이랑 같은 단면 말고 수직인 면 … 회전할 때 회전 각도기랑 수직인 단면이잖아, 그거랑 같은 구조."
    ///   → 면이 뻗는 방향 = 그 단면의 <b>법선</b>이다. 회전은 연직, 굴곡·신전은 환자 좌우, 측굴은 환자 앞뒤.
    ///   ★한 번 «단면 안에 눕게» 만들었다가 정정받았다 — 그러면 바늘과 같은 면이라 각도기에 묻힌다.
    /// </summary>
    private Vector3 NeedleFin(RomRecordStep s, Vector3 dir)
        => IsMotion(s) ? RomRecordGeometry.PlaneNormal(s, yaw).normalized * needleFinSize : Vector3.zero;

    /// <summary>회전만 면이 한쪽으로 쏠린다 — 원통 윗 테두리에 걸고 아래로 늘어뜨린다.</summary>
    private static bool NeedleFinHangs(RomRecordStep s) => s == RomRecordStep.Rotation;

    /// <summary>바늘 선의 <b>어디서부터</b> 잡히나(손잡이 반지름 배수). 0.35 = 안쪽 1/3은 안 잡힌다.</summary>
    private const float NeedleGrabFrom = 0.35f;

    /// <summary>
    /// 그 손이 바늘에서 얼마나 떨어져 있나. ★꼭지 끝만이 아니라 <b>선 중간 어디를 잡아도</b> 끌린다
    /// (2026-09-23 사용자: "니들 원형 꼭지 끝 말고 중간을 잡아도"). 안쪽 <see cref="NeedleGrabFrom"/>까지는
    /// 빼 둔다 — 중심 근처는 머리 속이라 잡을 일이 없고, 거기까지 열면 마커 찍기를 가로챈다.
    /// 회전은 바늘이 <b>면</b>이라 그 높이 안이면 세로 어디를 잡아도 같게 친다.
    /// </summary>
    private float NeedleDistance(Vector3 p, Vector3 center, Vector3 dir, float handleRadius, Vector3 fin, bool hang)
    {
        float finLen = fin.magnitude;
        if (finLen > 1e-4f)
        {
            // 지움: 면 <b>안</b>의 면방향 차이만. 면 밖으로 벗어난 거리는 그대로 남는다.
            //   회전은 한쪽(아래)으로만 뻗고, 나머지는 바늘을 가운데 두고 양쪽으로 벌어진다.
            Vector3 u = fin / finLen;
            float along = Vector3.Dot(p - center, u);
            float cl = hang ? Mathf.Clamp(along, -finLen, 0f) : Mathf.Clamp(along, -finLen * 0.5f, finLen * 0.5f);
            p -= u * (along - cl);
        }
        Vector3 a = center + dir * (handleRadius * NeedleGrabFrom);
        Vector3 ab = center + dir * handleRadius - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    private void ApplyDrag(Vector3 p)
    {
        // ★바늘은 델타가 아니라 <b>손이 있는 쪽</b>을 향한다. 단면에 투영해 그 면에서만 돌게 한다.
        if (dragTarget >= 3 && dragTarget <= 6)
        {
            if (NeedleVisible(out Vector3 nc, out Vector3 nn, out Vector3 nz, out _, out _, out int mi))
            {
                int k = dragTarget - 3;
                int n = mi * NeedlePerStep + k;
                Vector3 d = RomRecordGeometry.OnPlane(p - nc, nn, needleDir[n]);
                // ★바늘은 제 방향 쪽에만 있다(09-28) — 굴곡 바늘을 뒤로 넘기면 눈금 0에서 멈춘다.
                //   넘어가게 두면 «굴곡 바늘에 신전 값»이 적혀 탭 아래 값이 엉뚱한 줄로 간다.
                if (NeedleSide(step, d) != NeedleSideOf(k)) d = nz;
                needleDir[n] = d;
                needleMoved[n] = true;   // ★끌어낸 순간부터 값이다 — 따로 [기록]을 누르지 않는다
                DrawNeedle();
            }
            return;
        }

        Vector3 v = dragStartValue + (p - dragGrabbedAt);
        switch (dragTarget)
        {
            case 0: frameOrigin = v; break;
            case 1: c7 = v; break;
            // ★미간을 대추보다 낮게 끌 수 없다(2026-09-22 사용자 지시, 마진 없음) — 마커 생성과 같은 규칙이다.
            //   대추(dragTarget==1) 자신은 기준점이라 이 게이트를 안 건다.
            case 2: glab = hasC7 ? new Vector3(v.x, Mathf.Max(v.y, c7.y), v.z) : v; break;
            case 8: ear = v; break;
            case 9: leftMenu.MoveTo(v); return;   // 판은 표시물과 무관하다 — 다시 그릴 것이 없다
        }
        // ★끄는 동안은 가벼운 갱신만 한다 — 안내판·머리줄 문자열을 매 프레임 새로 만들지 않는다(VR 프레임 예산).
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasEar, ear, hasGlab, glab, pivot);
    }

    private void EndDrag()
    {
        // ★잡자마자 놓았다 = 잡을 뜻이 없었을 가능성이 크다(2026-09-23 계수).
        if (Time.unscaledTime - dragStartTime < 0.25f)
            CountMiss(Miss.ShortGrab, $"{DragName(dragTarget)}를 {(Time.unscaledTime - dragStartTime) * 1000f:F0}ms 만에 놓았다");
        if (dragTarget >= 3 && dragTarget <= 6) needleEndTime[dragTarget - 3] = Time.unscaledTime;

        int endedNeedle = -1;
        if (dragTarget >= 3 && dragTarget <= 6 && NeedleVisible(out _, out _, out Vector3 z, out _, out _, out int mi))
        {
            int k = dragTarget - 3;
            int n = mi * NeedlePerStep + k;
            // ★놓은 자리가 곧 기록이다 — 따로 누를 것이 없다. 다시 끌면 고쳐진다(구체를 쳐서 푼 뒤에).
            // ★★오토 홀드(09-28) — 놓는 순간 잠근다. 놓을 때 흔들린 것은 미세 조정 판(±0.1°)으로 고친다.
            bool wasInPlay1 = NeedleInPlay(mi, 2);
            needleLocked[n] = true;
            endedNeedle = k;
            Debug.Log($"[실측기록] 기록 {StepTitle[(int)step]} {DirName(step, k / 2)} {NeedleName(k)}(바늘) " +
                      $"눈금 {Vector3.Angle(z, needleDir[n]):F1}° · " +
                      $"{(hasGlab ? "중립 기준 " + NeedleNeutralDeg(n, mi).ToString("F1") + "°" : "미간 없음")} · 잠금");
            if (!wasInPlay1 && NeedleInPlay(mi, 2))
                Debug.Log($"[실측기록] {DirName(step, 0)} 끝 — 다음은 {DirName(step, 1)}(한계 {LimitDeg(step, 2):F0}°에 바늘이 선다)");
        }
        else
        {
            Vector3 at = dragTarget == 0 ? frameOrigin
                       : dragTarget == 1 ? c7
                       : dragTarget == 8 ? ear
                       : dragTarget == 9 ? leftMenu.Position : glab;
            Debug.Log($"[실측기록] 놓았다 — {DragName(dragTarget)} {Fmt(at)}");
        }
        dragTarget = -1;
        Play(sndPinch);
        dirty = true;
        if (endedNeedle >= 0)
        {
            DrawNeedle();                 // ★잠금 모양(작은 구체)을 먼저 그려야 판 자리가 새 구체 위로 간다
            ShowFineMenu(endedNeedle);
        }
    }

    // ── 바늘(09-21 신설) ─────────────────────────────────────────────
    private readonly int[] lastNeedleDegShown = { -999, -999, -999, -999 };

    /// <summary>그 바늘의 중립선 기준 각(미간이 있을 때만 뜻이 있다).</summary>
    private float NeedleNeutralDeg(int n, int mi)
    {
        if (!hasGlab || !DialFrame(out Vector3 c, out Vector3 nrm, out _, out float r)) return 0f;
        return RomRecordGeometry.PlaneAngle(Pivot, glab, c + needleDir[n] * r, nrm);
    }

    /// <summary>바늘 넷을 그린다. ★끄는 동안 매 프레임 불린다 — 각이 1° 넘게 바뀔 때만 글자를 새로 만든다.</summary>
    private void DrawNeedle()
    {
        if (!NeedleVisible(out Vector3 c, out _, out Vector3 z, out float r, out float h, out int mi))
        {
            for (int k = 0; k < NeedlePerStep; k++)
            {
                view.SetNeedle(k, false, Vector3.zero, Vector3.up, 0.1f, 0.2f, null, false);
                lastNeedleDegShown[k] = -999;
            }
            return;
        }
        int count = NeedleCountOf(step);
        for (int k = 0; k < NeedlePerStep; k++)
        {
            // ★아직 차례가 아닌 방향의 바늘은 안 보인다(09-28 — 순서대로 한 방향씩).
            if (k >= count || !NeedleInPlay(mi, k))
            {
                view.SetNeedle(k, false, Vector3.zero, Vector3.up, 0.1f, 0.2f, null, false);
                lastNeedleDegShown[k] = -999;
                continue;
            }
            int nk = mi * NeedlePerStep + k;
            Vector3 dir = needleDir[nk];
            float deg = Vector3.Angle(z, dir);
            int tenths = Mathf.RoundToInt(deg * 10f);   // ★0.1° 미세 조정이 있어 소수 한 자리로 본다(09-28)
            string label = null;
            if (tenths != lastNeedleDegShown[k])
            {
                lastNeedleDegShown[k] = tenths;
                // ★바늘 위에는 각도만 쓴다(09-22 사용자). 아직 한계 자리에 있는 바늘은 글자를 비운다 —
                //   값이 아니라 출발점이라서다. 능동·압박은 바늘 색의 밝기로 가른다.
                label = needleMoved[nk] ? (tenths * 0.1f).ToString("F1") + "°" : "";
            }
            view.SetNeedle(k, true, c, dir, r, h, label, dragTarget == 3 + k,
                           NeedleFin(step, dir), NeedleFinHangs(step), needleLocked[nk]);
        }
    }

    // ── 구체 터치 · 미세 조정(2026-09-28 신설) ────────────────────────
    // ★[홀드] 버튼(09-23)을 대신한다. 사용자 메모: "바늘 한번 옮기면 오토 홀드 — 이후 버튼 조정, 구체 터치로 홀드 해제"
    //   + "UI에 홀드 버튼을 놓지 말고 바늘 구체를 터치하는 걸로" + "0.1도 간격 버튼을 바늘 위, 손과 환자 머리가 한 시선에".

    /// <summary>
    /// 검지끝이 잠긴 바늘 구체에 <b>들어오는 순간</b> 잠금을 푼다(손별로 매 프레임).
    /// ★터치와 핀치는 이렇게 가른다: <b>잠긴 바늘은 터치만, 풀린 바늘은 핀치만</b> 받는다.
    ///   풀린 바늘은 터치로 다시 잠그지 않는다 — 잡으러 가는 손가락이 구체에 먼저 닿아 잠겨 버린다.
    /// ★들어옴만 센다 — 놓는 순간 그 손가락은 이미 구체 안이라, 놓자마자 도로 풀리지 않는다.
    ///   그래서 안에 있는지는 <b>끄는 중에도</b> 계속 잰다(동작만 안 한다).
    /// ★구체 자리는 <see cref="RomRecordVisual.TryNeedleGrip"/> — <b>그려진 자리</b>다(규칙 9).
    /// </summary>
    private void UpdateNeedleTouch(bool left)
    {
        int h = left ? 0 : 1;
        int inside = -1;
        if (needleOn && IsMotion(step) && hands.TryJoint(left, HandJointId.HandIndexTip, out Vector3 tip))
        {
            int mi = MotionIndex(step);
            int prev = touchInside[h];
            float best = float.MaxValue;
            for (int k = 0; k < NeedleCountOf(step); k++)
            {
                if (!NeedleInPlay(mi, k) || !view.TryNeedleGrip(k, out Vector3 g)) continue;
                float d = Vector3.Distance(tip, g);
                // ★나갈 때는 1.5배까지 봐준다 — 경계에서 떨면 들어옴이 되풀이된다.
                float r = k == prev ? needleTouchRadius * 1.5f : needleTouchRadius;
                if (d <= r && d < best) { best = d; inside = k; }
            }
        }
        int was = touchInside[h];
        touchInside[h] = inside;
        if (inside < 0 || inside == was) return;
        if (dragTarget >= 0 || liveActive) return;   // 무엇이든 끄는 중이면 안 받는다(안에 있다는 것만 기억)

        int n = MotionIndex(step) * NeedlePerStep + inside;
        if (!needleLocked[n]) return;                 // 풀린 바늘은 핀치 몫이다
        needleLocked[n] = false;
        Play(sndUndo);
        Debug.Log($"[실측기록] 구체 터치 — {StepTitle[(int)step]} {DirName(step, inside / 2)} {NeedleName(inside)} 잠금 풀림" +
                  $"({(left ? "왼" : "오른")}손 검지 · 다시 끌어 맞추면 또 잠긴다)");
        DrawNeedle();
        ShowFineMenu(inside);
        dirty = true;
    }

    /// <summary>미세 조정 판을 그 바늘 구체 위에 세운다. ★자리는 이때 한 번만 — 누르는 중에 판이 움직이면 헛짚는다.</summary>
    private void ShowFineMenu(int k)
    {
        if (fineMenu == null || eye == null || !view.TryNeedleGrip(k, out Vector3 g)) return;
        fineTarget = k;
        fineMenu.PlaceAt(g + Vector3.up * finePanelRise, eye);
        UpdateFineValue();
    }

    private void HideFineMenu()
    {
        fineTarget = -1;
        if (fineMenu != null) fineMenu.Hide();
    }

    private void UpdateFineValue()
    {
        if (fineMenu == null || fineTarget < 0 || !IsMotion(step)) return;
        int n = MotionIndex(step) * NeedlePerStep + fineTarget;
        float deg = Vector3.Angle(RomRecordGeometry.ScaleZero(step, yaw), needleDir[n]);
        // ★가운데 칸 = 어느 바늘인지 + 지금 값. 바늘 색과 같은 색으로 쓴다(능동 밝게 · 압박 진하게).
        fineMenu.SetText(FineValueKey,
            $"<color={(fineTarget % 2 == 1 ? HexPassive : HexActive)}>{deg:F1}°</color>");
    }

    /// <summary>
    /// 고른 바늘을 ±0.1°씩 옮긴다. + = 눈금 0에서 <b>멀어지는</b> 쪽(각이 커진다).
    /// ★잠긴 채로 움직인다 — 잠금은 «손으로 끌리지 않음»이지 «못 고침»이 아니다.
    /// ★바늘 방향을 각으로 바꿔 들지 않는다(규칙 9) — 그 바늘의 한계 자리 쪽으로 도는 회전을 그대로 쓴다.
    /// </summary>
    private void FineAdjust(float d)
    {
        if (fineTarget < 0 || !IsMotion(step) || !needleOn) { Play(sndDeny); return; }
        int k = fineTarget;
        int n = MotionIndex(step) * NeedlePerStep + k;
        Vector3 z = RomRecordGeometry.ScaleZero(step, yaw);
        Vector3 nrm = RomRecordGeometry.PlaneNormal(step, yaw);
        // 멀어지는 쪽 = 눈금 0에서 그 바늘의 한계 자리로 도는 쪽. 한계 자리가 제 방향이라 부호가 늘 맞다.
        float away = Vector3.SignedAngle(z, LimitDir(step, k), nrm) >= 0f ? 1f : -1f;
        float now = Vector3.Angle(z, needleDir[n]);
        float next = Mathf.Clamp(Mathf.Round((now + d) * 10f) * 0.1f, 0f, 180f);   // ★0.1° 눈에 맞춘다(누적 오차 없이)
        needleDir[n] = Quaternion.AngleAxis(away * next, nrm) * z;
        needleMoved[n] = true;   // ★버튼으로만 맞춰도 기록이다 — 한계 자리에서 벗어났으니
        UpdateFineValue();
        DrawNeedle();
        dirty = true;
        // ★반복 버튼이라 누르고 있으면 초당 8번 불린다 — 로그는 0.5초에 한 줄만(09-21 로그 폭주 전례).
        if (Time.unscaledTime >= nextFineLog)
        {
            nextFineLog = Time.unscaledTime + 0.5f;
            Debug.Log($"[실측기록] 미세 조정 {StepTitle[(int)step]} {DirName(step, k / 2)} {NeedleName(k)} → 눈금 {next:F1}°");
        }
    }

    /// <summary>
    /// 이 단계의 바늘을 전부 한계 자리로 되돌린다([다시]). ★끌어낸 자리가 곧 기록이라 «지우기»가 이것이다.
    /// 둘째 방향은 다시 숨는다 — 순서가 처음부터다.
    /// </summary>
    private void ResetNeedles()
    {
        if (!NeedleVisible(out _, out _, out _, out _, out _, out int mi))
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘이 없다 — 대추를 먼저 찍고 동작 단계로 온다.");
            return;
        }
        for (int k = 0; k < NeedlePerStep; k++)
        {
            int n = mi * NeedlePerStep + k;
            needleDir[n] = LimitDir(step, k);
            needleMoved[n] = false;
            needleLocked[n] = false;   // ★[다시]는 잠금도 푼다 — 안 그러면 되돌려 놓고 못 만진다
            lastNeedleDegShown[k] = -999;
        }
        HideFineMenu();
        Play(sndUndo);
        Debug.Log($"[실측기록] {StepTitle[(int)step]} 바늘을 모두 한계 자리로 되돌렸다.");
        dirty = true;
    }

    private void OnPinchFixed(Vector3 p)
    {
        switch (step)
        {
            case RomRecordStep.Setup:
                Play(sndDeny);
                // ★잡을 만큼 가까이 가지 않았다는 뜻이다(09-21). 3축 근처에서 오므리면 잡혀서 따라온다.
                Debug.Log($"[실측기록] 기준선을 못 잡았다 — 3축 중심에서 {axesGrabRadius * 100f:F0}cm 안에서 오므려야 잡힌다.");
                return;

            case RomRecordStep.Landmarks:
                // ★핀치로는 더 이상 찍지 않는다(2026-09-23 사용자: "핀치는 너무 흔들린다").
                //   기준점은 버튼으로 찍는다 — 핀치는 <b>이미 찍은 점을 잡아 끄는</b> 데만 쓴다
                //   (끌기는 FindDragTarget이 먼저 가로채므로 여기까지 오지 않는다).
                Play(sndDeny);
                if (Time.unscaledTime >= nextLockedVoice)
                {
                    nextLockedVoice = Time.unscaledTime + 8f;   // ★8초에 한 번만 말한다 — 스친 손마다 말하면 시끄럽다
                    Say("locked");
                }
                CountMiss(Miss.PinchIgnored, "기준점은 버튼으로 찍는다(핀치로는 안 찍힌다)");
                Debug.Log($"[실측기록] 기준점 핀치 무시 — 손가락을 대고 버튼을 누른다 · 자리 {Fmt(p)}");
                return;

            case RomRecordStep.Done:
                return;
        }

        // ★바늘 모드에서는 점을 찍지 않는다(09-21 실측). 바늘을 쓰는 판에서 손잡이 반경 밖을 오므리면
        //   점이 찍혀 "굴곡은 2개까지다"까지 떴다 — 사용자는 바늘을 맞추려던 참이었다. 둘이 섞이면 안 된다.
        if (needleOn)
        {
            // ★거부음도 내지 않는다(09-23) — 바늘 모드에서 핀치는 «잡아 끌기»일 뿐이라,
            //   아무것도 안 잡힌 핀치는 <b>실수가 아니라 그냥 빈손</b>이다. 수는 계속 센다.
            CountMiss(Miss.MarkWhileNeedle, "바늘 모드에서 빈 핀치(점은 안 찍힌다)");
            return;
        }

        // ★대추보다 낮은 자리는 마커로 찍지 않는다(2026-09-22 사용자 지시). 목이 그 위치로 꺾이면
        //   해부학적으로 불가능하다 — 의식하지 않은 손동작이 아래에서 핀치로 오인되던 것을 막는다.
        //   마진 없음(사용자 확정): 대추보다 조금이라도 낮으면 컷.
        if (hasC7 && p.y < c7.y)
        {
            Play(sndDeny);
            CountMiss(Miss.BelowC7, "대추보다 낮은 자리라 안 찍는다");
            Debug.Log($"[실측기록] 대추보다 낮은 자리 {Fmt(p)}(대추 {Fmt(c7)}) — 마커로 찍지 않는다.");
            return;
        }

        var list = marks[MotionIndex(step)];
        if (list.Count >= Capacity(step))
        {
            Play(sndDeny);
            Debug.Log($"[실측기록] {StepTitle[(int)step]}은 {Capacity(step)}개까지다 — [취소]로 지우고 다시 찍는다.");
            return;
        }

        var m = new RomRecordMark
        {
            raw = p,
            passive = list.Count % 2 == 1,               // 홀수번째(1,3) 능동 · 짝수번째(2,4) 수동
            side = HasSides(step) ? MarkSide(step, p) : 0,   // ★굴곡·신전은 앞뒤, 측굴·회전은 좌우(09-22)
        };
        list.Add(m);
        Play(sndPinch);
        LogMark(step, m);
        dirty = true;
    }

    private void AdjustLast(float d)
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        // ★바늘로 맞춘 것은 ±1°로 고치지 않는다 — 바늘을 다시 잡아 맞추는 것이 그 방식의 수정이다.
        if (m.byNeedle)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘로 기록한 것은 ±1°로 못 고친다 — [취소]하고 바늘을 다시 맞춘다.");
            return;
        }
        if (!hasGlab)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 미간이 없어 각이 없다 — ±1°로 고칠 것이 없다.");
            return;
        }
        float before = AngleOf(step, m, Pivot);
        m.adjustDeg += d;
        // ★0° 아래로는 안 간다 — 중립을 넘어 반대쪽으로 도는 것은 수정이 아니다.
        float raw = RomRecordGeometry.PlaneAngle(Pivot, glab, m.raw, RomRecordGeometry.PlaneNormal(step, yaw));
        if (raw + m.adjustDeg < 0f) m.adjustDeg = -raw;
        Debug.Log($"[실측기록] 수정 {StepTitle[(int)step]} {Kind(step, m)} {before:F1}° → {AngleOf(step, m, Pivot):F1}° (누적 {m.adjustDeg:+0;-0;0}°)");
    }

    private void UndoLast()
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        Debug.Log($"[실측기록] 취소 {StepTitle[(int)step]} {Kind(step, m)}");
    }

    // ── 기록 ─────────────────────────────────────────────────────────
    private static string Kind(RomRecordStep s, RomRecordMark m)
    {
        // ★쪽 이름은 단계가 정한다 — 굴곡·신전은 굴곡/신전, 측굴·회전은 우/좌(09-22).
        string side = m.side != 0 ? SideName(s, m.side) + " " : "";
        // ★바늘로 맞춘 것은 표시에서도 구분한다 — 점찍기와 섞이면 비교를 못 한다.
        return side + (m.passive ? "압박" : "능동") + (m.byNeedle ? "·바늘" : "");
    }

    private void LogMark(RomRecordStep s, RomRecordMark m)
    {
        string how = m.byNeedle ? "바늘" : "점";
        if (!hasGlab)
        {
            // ★미간이 없으면 중립선이 없어 중립 기준 각을 못 낸다 — 0을 각인 것처럼 적지 않는다.
            Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(s, m)} ({how}) 눈금 {m.dialDeg:F1}° · " +
                      $"★미간이 없어 중립 기준 각은 못 낸다 · 자리 {Fmt(m.raw)}");
            return;
        }
        float atPivot = AngleOf(s, m, PivotFor(s));
        float atC7 = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, m.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + m.adjustDeg);
        // ★두 기준을 같이 남긴다 — 축(외이도/대추)과 대추 기준을 비교한다(09-18 방침 · 09-22 목중앙→외이도).
        Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(s, m)} ({how}) {atPivot:F1}° ({PivotName(s)} 축) · " +
                  $"대추 기준 {atC7:F1}°{(m.byNeedle ? $" · 눈금 {m.dialDeg:F1}°" : "")} · " +
                  $"자리 {Fmt(m.raw)} · 수정 {m.adjustDeg:+0;-0;0}°");
    }

    // ── 오조작 계수(2026-09-23 신설) ──────────────────────────────────
    // ★★왜 세는가: 09-22에 «어느 오조작이 진범인지»를 셋 다 의심하다가, 로그를 <b>종류별로 세고</b>
    //   나서야 «판 앞 10~26cm의 조기 핀치»라는 답이 나왔다. 지금 입력 구조를 고치자는 이야기가 있는데,
    //   무엇이 실제로 많은지는 <b>아직 안 세어 봤다</b>. 고치기 전에 한 판 세어 본다.
    // ★자기 침묵이다(log-first §②ⓑ) — 잘 돌면 한 줄도 안 남기고, 막힐 때만 종류별로 2초에 한 번 말한다.
    //   ★대신 <b>세는 것은 매번</b> 센다. 말하지 않은 것도 수에는 들어간다.
    private enum Miss
    {
        PinchIgnored = 0,   // 기준점 단계에서 핀치했는데 아무것도 안 잡혔다(찍으려던 손으로 본다)
        NearPlate,          // 판 잡기 범위 <b>언저리</b>에서 오므렸는데 판이 안 잡혔다
        LockedNeedle,       // 잠근 바늘(홀드)을 잡으려 했다 = 홀드가 실제로 막아 준 횟수
        ShortGrab,          // 잡자마자(0.25초 안에) 놓았다 — 의도한 잡기가 아니었을 가능성
        MarkWhileNeedle,    // 바늘 모드인데 마커를 찍으려 했다(모드 경합)
        BelowC7,            // 대추보다 낮은 자리를 찍으려 했다
        QuickRedoNeedle,    // 바늘을 놓고 3초 안에 같은 바늘을 다시 끌었다 = 놓은 자리가 틀렸다
        QuickFixLandmark,   // 기준점을 찍고 5초 안에 그 점을 잡아 끌었다 = 찍힌 자리가 틀렸다
        Count,
    }

    private static readonly string[] MissName =
    {
        "기준점 핀치 무시", "판 언저리 헛핀치", "잠긴 바늘 건드림", "스친 잡기",
        "바늘 모드에서 찍기", "대추보다 낮은 자리", "바늘 즉시 고침", "기준점 즉시 고침",
    };

    private readonly int[] missCount = new int[(int)Miss.Count];
    private readonly float[] nextMissLog = new float[(int)Miss.Count];
    private float dragStartTime;                                   // 스친 잡기를 재는 시각
    private readonly float[] needleEndTime = new float[NeedlePerStep];      // 그 바늘을 놓은 시각
    private readonly float[] landmarkPlacedTime = new float[3];            // 0 대추 · 1 미간 · 2 외이도

    private void CountMiss(Miss m, string detail)
    {
        int i = (int)m;
        missCount[i]++;
        if (Time.unscaledTime < nextMissLog[i]) return;
        nextMissLog[i] = Time.unscaledTime + 2f;
        Debug.Log($"<color=orange>[실측기록·오조작] {MissName[i]} {missCount[i]}회 — {detail}</color>");
    }

    /// <summary>지금까지의 오조작을 한 줄로 쏟는다. ★0인 종류는 안 적는다 — 0이 줄줄이 있으면 안 읽힌다.</summary>
    private void DumpMisses(string when)
    {
        sb.Clear();
        int total = 0;
        for (int i = 0; i < (int)Miss.Count; i++)
        {
            if (missCount[i] == 0) continue;
            total += missCount[i];
            sb.Append(MissName[i]).Append(' ').Append(missCount[i]).Append("회 · ");
        }
        Debug.Log(total == 0
            ? $"[실측기록·오조작] {when} — 없음"
            : $"[실측기록·오조작] {when} 합 {total}회 — {sb}");
    }

    private void LogSummary()
    {
        sb.Clear();
        sb.Append("[실측기록] 요약 — 대추 ").Append(hasC7 ? Fmt(c7) : "없음")
          .Append(" · 외이도 ").Append(hasEar ? Fmt(ear) + " → 축 " + Fmt(EarAxis) : "없음(대추 축으로 잼)")
          .Append(" · 미간 ").Append(hasGlab ? Fmt(glab) : "없음").Append('\n');
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            if (s == RomRecordStep.Extension) continue;   // ★09-22부터 굴곡·신전 한 단계(Flexion)에 들어 있다
            sb.Append("  ").Append(StepTitle[(int)s]).Append(": ");
            // ★바늘 값을 먼저 적는다 — 끌어낸 바늘이 곧 기록이다(09-21).
            sb.Append("[바늘] ");
            AppendNeedleValues(sb, s);
            sb.Append("   [점] ");
            var list = marks[MotionIndex(s)];
            if (list.Count == 0) sb.Append("—");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                var mk = list[i];
                sb.Append(Kind(s, mk)).Append(' ');
                if (mk.byNeedle) sb.Append("눈금 ").Append(mk.dialDeg.ToString("F1")).Append('°');
                if (!hasGlab)
                {
                    if (!mk.byNeedle) sb.Append('—');
                    continue;
                }
                float c7a = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, mk.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + mk.adjustDeg);
                sb.Append(mk.byNeedle ? " / " : "").Append(AngleOf(s, mk, PivotFor(s)).ToString("F1"))
                  .Append("°(대추 ").Append(c7a.ToString("F1")).Append("°)");
            }
            sb.Append('\n');
        }
        // ★조용히 버려진 핀치를 여기서 한 번 센다(09-21) — 얼마나 걸러졌는지 판마다 남는다.
        sb.Append("  판 잡기 준비(테두리 켜짐) ").Append(menuHoverCount).Append("회\n");
        sb.Append("  무시된 핀치: 너무 짧음 ").Append(hands.IgnoredShort)
          .Append(" · 추적 신뢰 낮음 ").Append(hands.IgnoredLowConfidence).Append('\n');
        if (pinchBlockCount.Count > 0)
        {
            sb.Append("  핀치를 막은 것(프레임 수):");
            foreach (var kv in pinchBlockCount) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(" ·");
            sb.Append('\n');
        }
        Debug.Log(sb.ToString());
        DumpMisses("이번 판 전체");   // ★요약 끝에 오조작 집계를 붙인다(09-23)
    }

    private static string Fmt(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    // ── 그리기(값이 바뀔 때만) ────────────────────────────────────────
    private void Redraw()
    {
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasEar, ear, hasGlab, glab, pivot);
        // ★대추를 찍기 전에는 3축을 숨긴다(09-21) — 그때 3축은 눈앞 허공의 임시 자리일 뿐이라
        //   보여 봐야 시야만 가린다. 대추를 찍으면 그 자리로 와서 그때부터 뜻이 생긴다.
        view.SetAxesVisible(hasC7 || step == RomRecordStep.Setup);

        // ★판(09-22): 진행 점·잠금이 바뀌었으면 다시 짜고, 아니면 글자 칸 둘만 바꾼다.
        //   ★SetLayout은 쿨다운을 다시 걸어 누르고 있던 반복 버튼(±1°·정면 ◀▶)을 끊는다 — 매번 부르면 안 된다.
        if (LayoutSignature() != layoutSig) ApplyStepButtons();
        else RefreshMenuTexts();

        // ★각도기와 바늘은 같은 기하를 쓴다(DialFrame) — 따로 계산하면 미리보기가 거짓말을 한다(규칙 9).
        bool hasDial = DialFrame(out Vector3 dc, out Vector3 dn, out Vector3 dz, out float dr);
        if (hasDial) view.SetDial(true, dc, dn, dz, dr);
        else view.SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.1f);
        DrawNeedle();

        // ★단면 사각형(09-21) — 지금 재는 면이 어디인지 사람 크기만큼 네모로 보여 준다.
        //   회전은 눈금판이 미간 높이로 올라가 있으므로 사각형도 같은 중심을 쓴다.
        if (hasDial && showSectionPlane)
            view.SetSectionPlane(true, dc, dn, RomRecordGeometry.ScaleZero(step, yaw));
        else
            view.SetSectionPlane(false, Vector3.zero, Vector3.up, Vector3.up);

        var list = IsMotion(step) ? marks[MotionIndex(step)] : null;
        for (int i = 0; i < RomRecordVisual.MaxMarks; i++)
        {
            bool on = list != null && i < list.Count;
            if (!on) { view.SetMark(i, false, Vector3.zero, Vector3.zero, null, Color.white); continue; }
            var m = list[i];
            Vector3 n = RomRecordGeometry.PlaneNormal(step, yaw);
            // ★미간이 없으면 중립선이 없어 돌릴 기준도 없다 — 찍힌 자리를 그대로 둔다.
            Vector3 shown = hasGlab ? RomRecordGeometry.Adjusted(pivot, glab, m.raw, n, m.adjustDeg) : m.raw;
            // ★삼각면(09-21) — 점이 단면에서 벗어난 만큼이 «각에 못 쓴 성분»이다. 바늘은 면에 붙어 있어 뜻이 없다.
            Vector3 triC = !m.byNeedle && showOffPlaneTriangle && hasDial ? dc : Vector3.zero;
            Vector3 triN = !m.byNeedle && showOffPlaneTriangle && hasDial ? n : Vector3.zero;
            // ★바늘은 <b>눈금 각</b>을 보여 준다 — 사용자가 눈으로 읽어 맞춘 값이 그것이다.
            string deg = m.byNeedle ? m.dialDeg.ToString("F0") + "°"
                       : hasGlab ? AngleOf(step, m, pivot).ToString("F0") + "°"
                       : "—";
            // ★마커 위에는 각도만 쓴다(09-22 사용자: "바늘이랑 마커 위에 한글을 빼고 각도만").
            //   능동·압박은 색의 밝기로, 좌우·앞뒤는 마커 자리 자체로 읽힌다.
            string label = deg;
            view.SetMark(i, true, shown, pivot, label,
                         m.passive ? RomRecordVisual.PassiveColor : RomRecordVisual.ActiveColor, triC, triN);
        }

        // ★떠 있던 안내판(대추 위 43cm)은 없앴다(09-22 사용자: "있는지도 몰랐다 · 고개를 드니까 작게 보였다").
        //   측정값은 조작 판의 각 탭 아래(TabValues)로 옮겼다. 안내판 글자 객체는 빈 채로 남는다.
    }

}
