using Oculus.Interaction.Input;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

public enum RomLinePoint
{
    TrRight = 0,
    TrLeft = 1,
    Gl = 2,
    Gn = 3,
}

/// <summary>
/// ★경추 ROM «선 실측» — 본체(2026-10-01 신설, 전용 씬 <c>RomLineScene</c>에서 돈다).
///
/// 9/30 교수님 피드백으로 바뀐 방식이다. 기존 실측(<c>RomRecordSession</c> · <c>RomMarkerScene</c>)은 <b>그대로 두고</b> 따로 만든다.
///   · 기준: 양쪽 Tr 두 점(건너뛸 수 있다) → 환자 기준 방향
///   · 동작마다 <b>직선 셋</b>: 중립 2점 → 능동 2점 → 압박 2점. 여섯 동작이라 직선은 18개다
///   · 값 둘을 나란히 낸다: «직접» = 3차원에서 두 선의 각을 그대로(기준 없이도 나온다) /
///     «면» = 환자 기준 면 위로 옮겨 잰 각(기준 Tr을 찍었을 때만 — ★회전은 예외다. 회전의 면은 바닥이라
///     중력만으로 정해지므로 기준을 건너뛰어도 «면» 값이 나온다, 10-07)
///   · 순서 고정: 기준 → 굴곡 → 신전 → 우측굴 → 좌측굴 → 우회전 → 좌회전(각각 중립 → 능동 → 압박)
///   ★10-01 첫 판은 «중립 네 점을 한 번 찍어 모든 동작의 중립선으로 돌려쓰는» 구조였다(28점). 사용자 지시로
///     «기준은 양쪽 Tr만 · 중립선은 동작마다 찍는다(18직선)»로 바꿨다(38점, 기준을 건너뛰면 36점).
///
/// 조작은 <b>컨트롤러 하나</b>다(주로 오른손, 바꿔 쥐면 그쪽이 그대로 동작). 반대 손은 환자를 잡는 데 쓴다.
///   ★지금의 버튼은 아래 «10-12 저녁» 문단이 정본이다. 그 앞 문단들의 버튼 · 판 설명은 그때의 것이다.
///
/// ★임시로 정해 둔 것(사용자 답 대기, 10-01): 각이 작아 교점이 멀면 각도기를 중립선 첫 점에 세운다 ·
///   압박은 값만 더하고 각도기는 능동 때 자리에 둔다 · 면 밖으로 벗어난 양은 로그에만 남긴다.
///
/// ★비교 시험(10-02, <c>compareGlGn</c>): 굴곡·신전에서 한 자세마다 Gn을 하나 더 찍어(오른쪽 Tr → Gl → Gn)
///   Tr ~ Gl 선과 Gl ~ Gn 선의 값을 나란히 본다. Tr ~ Gl 선은 옆면에서 비스듬해 «직접»이 작게 나오고
///   Gl ~ Gn 선은 정중선 위라 그대로 나온다는 계산을 실제로 확인하려는 것이다. 끄면 종전 흐름(2점) 그대로다.
///
/// ★★10-08 사용자 지시(같은 날 두 번 바뀜 — 아래가 지금 구조다):
///   판 맨 위에 <b>탭 다섯</b> — 굴곡(3직선) · 신전(3) · 측굴(우 3 + 좌 3) · 회전(우 3 + 좌 3) · 기타.
///   앞의 넷은 위의 «18직선» 흐름을 <b>순서대로</b> 잰다(중립 → 능동 → 압박, 다 찍으면 다음 동작으로 저절로 넘어간다).
///   단계가 바뀔 때마다 <b>안내 음성</b>이 한 문장 나온다(Resources/RomLine/Narration).
///   <b>기준(양쪽 Tr)은 찍지 않는다</b> — 면은 고정 3축으로 세운다(frame = axes). 굴곡 · 신전의 선도 정중선(Gl ~ Gn)이다.
///   «기타» = 아래의 자유 측정. 3축 끄기 · 모드 버튼은 뺐다.
///
/// ★★10-12 사용자 지시 — 판은 <b>결과</b>를 보여 주고, 단계 · 진행은 <b>진행 정보</b>(팝업)로 옮겼다:
///   판(순서 탭) = 한 줄에 동작 하나: 측정 명칭 · 능동 · 압박 · 그 동작의 단면에서 본 각도기 그림. «직접» 값은 판에서 뺐다(로그 · CSV에는 남는다).
///   A/X = 진행 정보 여닫기(여섯 동작 × 중립 · 능동 · 압박의 진행). 띄운 동안에만 스틱 좌우로 단계를 옮긴다. 점을 찍으면 저절로 닫힌다.
///   B/Y = 한 점 취소(뗄 때 듣는다) · 1초 길게 = 이 동작 다시(«기타»에서는 이 동작 지우기). <b>건너뛰기는 없앴다</b>.
///   «기타»의 «기준선 바꾸기»는 판 · 모니터의 버튼으로만 한다(종전 B/Y).
///
/// ★★10-12 저녁 사용자 지시 — 화면과 조작을 다시 짰다(위 «10-12» 문단의 «진행 정보 · 스틱으로 단계 옮기기 · 이 동작 다시»를 대신한다):
///   <b>메인 화면</b>은 크고 넓게, 뒤쪽 멀리 세운다(결과표 — 굴곡 | 신전 / 우측굴 | 좌측굴 / 우회전 | 좌회전). 누르는 버튼은 화면에 없다.
///   <b>탭 창</b>은 메인 화면 아래에 따로 뜬다 — 탭 다섯: 굴곡 · 신전 / 측굴 / 회전 / 리셋 / 자유 측정.
///   A/X = 탭 창 여닫기. 띄운 채 스틱 좌우로 탭을 옮기면 <b>옮기는 대로</b> 그 탭이 된다. «리셋»만 A로 골라야 하고 확인 창이 한 번 더 뜬다(A : 지우기 · B : 취소).
///   ★탭을 <b>재는 중</b>(점이 하나라도 찍혔고 아직 다 재지 않았다)이면 스틱을 밀어도 탭이 바뀌지 않는다. 그만두려면 B/Y 길게로 그 탭을 지운다.
///   ★동작 하나(중립 · 능동 · 압박 세 직선)를 다 찍으면 <b>멈춘다</b>. A/X(확인)를 눌러야 다음 동작으로 넘어간다 — 신전을 확인하면 측굴이 저절로 시작된다.
///   지우기 셋: B/Y 짧게 = <b>직선 하나</b>(찍다 만 점이 있으면 그 점) · B/Y 1초 길게 = <b>지금 탭 전체</b> · 탭 창의 «리셋» = <b>전체 측정 결과</b>.
///   축 다시 잡기 = 스틱을 <b>눌러</b> 부르고, 스틱을 <b>밀어</b> 잡는다(두 단계 — 실수로 잡히지 않게). 방향은 <b>헤드셋이 보는 방향</b>이다
///     (10-08의 «컨트롤러가 가리키는 방향»을 되돌렸다 — 그때는 판의 버튼을 누르느라 헤드셋이 판을 봤다. 이제 판 버튼이 없다). ★10-13: 메인 화면은 고정 자리에 그대로 둔다(PlaceFixed) — 축만 돈다.
///   컨트롤러에 없는 일(끝점 보정 · 기준선 바꾸기 · 나가기)은 모니터(브라우저)의 버튼으로만 한다. 자유 측정의 동작 갈래는 탭 창을 닫은 채 스틱 좌우다.
///
/// ★★10-13 사용자 지시로 자유 측정을 다시 짰다 — 동작(굴곡·신전 / 측굴 / 회전)을 가르지 않고 <b>찍은 점들이 이루는 각 그대로</b>(3차원)를 낸다.
///   방식 셋: 3점 측정 · 2선 측정(10-14까지 «4점 측정») · 연속 측정(스틱 좌우로 바꾼다). 뜻은 아래 «자유 측정» 블록의 주석에 있다. 면에 내린 값 · 기준선 바꾸기 · «다른 동작으로 보임» 알림은 없앴다.
///   아래 10-07 ~ 10-08의 설명은 그 전 구조다(지금 코드와 다르다).
/// (옛 구조) 자유 측정(10-07): 기준점도 정해진 순서도 없다. 두 점마다 선 하나, 개수 제한 없이 찍는다.
///   동작마다 갈래(탭)를 나눈다 — 굴곡·신전 / 측굴 / 회전. 갈래마다 점을 따로 모으고, 처음 찍은 선이 <b>기준선</b>이다.
///   측정선마다 길이, 기준선과 벌어진 각(«직접»), 그리고 <b>그 동작의 면 하나</b>에 내린 각(«면» — 옆면·앞면·바닥면)을 낸다.
///   면은 공간에 고정된 세 축(정면·좌우·수직)으로 세운다 — 양쪽 Tr을 찍지 않는다. 회전의 바닥면은 중력만 쓴다.
///   세 축은 판을 세울 때 헤드셋이 보던 방향으로 잡는다(환자가 사용자를 마주 본다고 놓는다).
///   다시 잡을 때는 <b>컨트롤러가 가리키는 방향</b>이다(10-08) — 환자 정면에서 환자 쪽으로 겨누고 스틱을 위로 1초.
///   ★환자는 그 축에 정면만 맞춰 앉으면 된다(자리는 뜻이 없다). 어긋난 만큼 값이 달라진다.
///
/// ★끝점 보정(10-07, 스틱을 눌러 시작 · 다시 눌러 끝): 컨트롤러 끝을 움직이지 않는 한 점에 댄 채 여러 방향으로 돌리면,
///   그 자세들에서 «찍는 점»이 컨트롤러 좌표로 어디인지(<c>tipOffset</c>)를 계산으로 구한다. 눈으로 맞춘 값이 실제 닿는 자리와
///   어긋나 있으면, 찍을 때 컨트롤러를 쥔 각도에 따라 찍히는 자리가 달라진다(어긋남 × 2·sin(각/2)) — 민두 시험에서 드러났다.
///
/// ★모니터(10-07): 헤드셋을 쓰지 않고 정해 둔 자리에 놓은 채 컨트롤러만으로 잴 수 있게, 헤드셋 앱이 작은 웹 서버를 연다
///   (<see cref="RomLineMonitor"/>). 같은 Wi-Fi의 PC 브라우저가 그 주소를 열면 값·선·추적 상태가 보인다.
///   판을 누를 수 없으니 자유 측정의 조작을 전부 컨트롤러 버튼에 올렸다:
///   트리거 = 점 · A/X = 한 점 취소 · B/Y = 기준선 바꾸기 · 스틱 좌우 = 동작(갈래) 바꾸기 · 스틱 클릭 = 끝점 보정
///   · 그립 1.5초 = 이 동작 지우기 · 스틱 위로 1초 = 축 다시 잡기.
///   찍은 선은 CSV로 기기에 남고(persistentDataPath) 모니터에서도 받는다.
///
///   ★첫 판(10-07 낮)은 한 선에서 세 면의 값을 함께 보여 줬다. 민두 시험에서 다른 두 면의 값이 실제 움직임이 아닌
///     «겉보기 값»(순수 회전인데 옆면 120~150°)으로 나와 갈래를 나눴다 — 다른 면의 값은 로그에만 남긴다.
/// </summary>
public class RomLineSession : MonoBehaviour
{
    public const int GroupCount = 19;          // 기준 1 + 6동작 × (중립·능동·압박)
    public const int PointCount = 38;          // 묶음마다 2점

    public static readonly string[] MotionName = { "굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전" };
    // ★10-12 사용자 지시: 영어 약어(Tr · Gl · Gn)를 화면 · 음성 · 문서에 쓰지 않는다 — 받은 한글 명칭 그대로(이주점 · 미간점 · 턱끝점).
    //   판의 안내 글자 · 공간의 이름표 · 로그 · 안내 음성(romline_narration.py)이 전부 이 이름이다.
    public static readonly string[] PointName = { "오른쪽 이주점", "왼쪽 이주점", "미간점", "턱끝점" };
    // ★10-14 사용자 지시: 탭 = 홈 · 굴곡 · 신전 · 측굴 · 회전 · 자유 측정(지금 고른 방식의 이름 — FreeTopName). «리셋» 탭은 없앴다(홈에서 A).
    //   탭 번호(top): 0 홈 · 1~3 순서 측정(CurTab + 1) · 4 자유 측정.
    private static readonly string[] TopTabName = { "홈", "굴곡·신전", "측굴", "회전" };
    private static readonly string[] GuidedTabName = { "굴곡·신전", "측굴", "회전" };   // CurTab(0~2)으로 찾는다
    private static readonly string[] FreeTopName = { "3점", "2선", "연속" };   // ★10-14 사용자 지시: «측정»은 공통이라 뺀다
    private static readonly string[] PhaseHint = { "환자 정면을 바닥 화살표에 맞추세요 · 정면을 본 편안한 자세", "환자가 스스로 끝까지 움직인 자세", "측정자가 같은 방향으로 조금 더 눌러 준 자세" };
    private static readonly string[] PhaseName = { "중립", "능동", "압박" };

    [Header("=== 컨트롤러 ===")]
    [Tooltip("컨트롤러 기준 «찍는 점»의 자리(m, 오른손 기준 — 왼손은 x를 뒤집어 쓴다). 노란 점이 여기에 뜬다.\n" +
             "★0.033 = 컨트롤러 모형 경계 상자의 맨 앞(브리지 romline-ctrl로 잰 값, 10-01). 실제 컨트롤러 앞면과 어긋나면 인스펙터로 맞춘다.")]
    [SerializeField] private Vector3 tipOffset = new Vector3(0f, 0f, 0.033f);

    [Header("=== 비교 시험 ===")]
    [Tooltip("켜면 굴곡·신전에서 한 자세마다 세 점(오른쪽 이주점 → 미간점 → 턱끝점)을 찍어 이주점~미간점 선과 미간점~턱끝점 선의 값을 나란히 낸다.\n" +
             "미간점~턱끝점 값은 판의 결과표 아래 한 줄과 로그 «[선실측] 비교»에 나온다. 측굴·회전은 그대로다.")]
    [SerializeField] private bool compareGlGn = false;

    [Header("=== 손 ===")]
    [Tooltip("켜면 판의 버튼을 맨손 검지로도 누르고(닿고 머물기) 핀치로 판을 옮긴다. 컨트롤러 + 맨손 동시 추적을 요청한다.\n" +
             "끄면 컨트롤러만 쓴다(10-07 사용자 지시로 기본 끔). 점은 어느 쪽이든 컨트롤러로만 찍는다.\n" +
             "★10-12 저녁: 화면에서 누르는 버튼을 없애, 지금은 켜도 손으로 하는 일이 없다(추적 로그만 남는다).")]
    [SerializeField] private bool handInput = false;

    [Header("=== 모니터 ===")]
    [Tooltip("모니터 화면을 내보내는 포트. 같은 Wi-Fi의 브라우저에서 http://(헤드셋 IP):포트 로 연다 — 주소는 판 맨 아래와 로그에 나온다.\n" +
             "0이면 열지 않는다. ★인증이 없다(같은 망의 누구나 볼 수 있고 버튼도 누를 수 있다) — 시험용이다.")]
    [SerializeField] private int monitorPort = 8090;
    [Tooltip("관제 미러링(벽면UI · 현실 영상 이미지). 비어 있으면 실행할 때 기본값으로 붙인다(10-16).")]
    [SerializeField] private RomLineMirror mirror;

    [Header("=== 메인 화면(10-12 · 자리는 10-13부터 고정) ===")]
    [Tooltip("추적 원점(메타 퀘스트의 «위치 재설정»이 맞추는 자리)에서 메인 화면까지의 거리(m). 화면은 원점의 정면에 처음 한 번 서고 그 뒤로 옮기지 않는다.")]
    [SerializeField] private float screenDistance = 2.5f;
    [Tooltip("메인 화면의 폭(m). 높이는 폭의 0.475배다. 탭 창은 같은 폭으로 그 아래에 붙는다.")]
    [SerializeField] private float screenWidth = 2.5f;
    [Tooltip("메인 화면의 아랫변이 기준 눈높이(바닥에서 1.5m)보다 높은 만큼(m). 환자 머리를 가리지 않게 띄운다. 탭 창은 이 아래로 내려온다(띄웠을 때만 보인다).")]
    [SerializeField] private float screenRise = 0.30f;

    [Header("=== 안내(10-13) ===")]
    [Tooltip("헤드셋에서 안내 글까지의 거리(m). 지금 할 일을 한 문장으로 보여 준다 — 판 없이 반투명 테두리와 글만 있고, 헤드셋이 보는 쪽을 따라온다.")]
    [SerializeField] private float guideDistance = 0.60f;
    [Tooltip("안내 글을 시선의 가운데보다 아래로 내리는 만큼(m). 찍는 자리를 가리면 더 내린다.")]
    [SerializeField] private float guideDrop = 0.22f;
    [Tooltip("안내 글을 컨트롤러 끝에서 위로 띄우는 만큼(m) — 안내는 찍는 컨트롤러 바로 위에 붙어 다닌다(10-13). 컨트롤러가 안 잡힐 때만 위의 두 값(시선 앞)으로 돌아간다.")]
    [SerializeField] private float guideRise = 0.10f;

    [Header("=== 공간 각도기(11-16) ===")]
    [Tooltip("확인한 동작의 각도기를 환자 중심에서 사용자 반대편으로 띄우는 거리(m). 굴곡·신전 · 측굴은 면의 법선 방향, 회전은 수평으로 민다.")]
    [SerializeField] private float spaceGaugeDistance = 0.25f;
    [Tooltip("공간 각도기의 반지름(m).")]
    [SerializeField] private float spaceGaugeRadius = 0.15f;
    [Tooltip("회전(수평면) 각도기만 환자 머리 높이에서 위로 띄우는 만큼(m).")]
    [SerializeField] private float spaceGaugeRotationRise = 0.10f;

    [Header("=== 기타 ===")]
    [SerializeField] private float soundVolume = 1f;
    [SerializeField] private string lobbySceneName = "lobby";

    // 단계
    private readonly Vector3[] points = new Vector3[PointCount];
    private readonly bool[] has = new bool[PointCount];
    private readonly RomLineResult[] results = new RomLineResult[12];
    private readonly RomLineDirect[] direct = new RomLineDirect[12];   // 비교용 — 면으로 옮기지 않고 3차원에서 잰 값
    // 비교 시험 — 묶음마다 세 번째 점(Gn) 하나, 굴곡·신전의 Gl ~ Gn 선 값(동작 2 × 능동·압박)
    private readonly Vector3[] extra = new Vector3[GroupCount];
    private readonly bool[] hasExtra = new bool[GroupCount];
    private readonly RomLineResult[] cmpResults = new RomLineResult[4];
    private readonly RomLineDirect[] cmpDirect = new RomLineDirect[4];
    private RomLineFrame frame;

    // 자유 측정(10-07) — 두 점마다 선 하나, 개수 제한 없음
    private bool freeMode;
    private int freeTab;                // 자유 측정의 방식(10-13): 0 = 3점 측정 · 1 = 2선 측정(종전 «4점» — 10-14 개명) · 2 = 연속 측정 — 방식마다 점을 따로 모은다
    private readonly List<Vector3>[] freePts = { new List<Vector3>(64), new List<Vector3>(64), new List<Vector3>(64) };
    private readonly List<Vector3> freeAngleAt = new List<Vector3>(32);    // 공간에 띄울 각 글자의 자리와 글 — 상태가 바뀔 때만 다시 만든다
    private readonly List<string> freeAngleText = new List<string>(32);
    private readonly RomLineFreeView freeView = new RomLineFreeView();
    private readonly RomLineSpaceGauge spaceGauge = new RomLineSpaceGauge();
    private int lastConfirmedTab = -1;   // 공간 각도기 — 마지막으로 확인한 탭(다음 탭의 첫 점을 찍기 전까지 그 탭 것을 남긴다)
    private int spaceGaugeTab = -1;      // 지금 공간 각도기가 그리는 탭(-1 = 숨김) — 바뀔 때만 자리를 다시 정한다
    private Vector3 spaceGaugeCenter;
    private bool axesOn = true;
    private RomLineFrame axes;          // 공간에 고정된 세 축 — 양쪽 Tr이 하던 일을 대신한다
    private Vector3 axesOrigin;         // 바닥의 십자를 그리는 자리(방향만 쓰고 자리는 계산에 안 들어간다)
    private int group;      // 0 = 기준(양쪽 Tr), 1~18 = 동작 × (중립·능동·압박), 19 = 끝
    private int index;      // 그 묶음 안에서 다음에 찍을 점

    // 입력
    private Transform trackingSpace;
    private Transform eye;
    private OVRInput.Controller active = OVRInput.Controller.RTouch;
    private static readonly bool AllowControllerSwitch = false;   // ★11-17 사용자 결정: 오른쪽만(const로 두면 «접근할 수 없는 코드» 경고)
    private bool tipValid;              // 헤드셋 카메라가 컨트롤러를 보고 있다(자리가 «추적» 상태) — 이때만 점을 찍는다
    private bool tipShown;              // 자리가 있긴 하다(못 보는 동안은 기기가 센서로 추정한 값) — 끝 구슬을 빨갛게라도 보여 준다
    private int trackState = -1;        // 연결·자리 추적·자리 유효·방향 추적을 묶은 값 — 바뀔 때 로그를 남긴다
    private float trackSince, trackLogNext;
    private Vector3 tipPos;
    private float prevTrigger;
    private Vector3 armedPos;
    private float armedTime = -10f;
    private const float ConfirmGuard = 0.5f;          // 마지막 점을 찍은 뒤 이만큼은 확인(트리거)을 받지 않는다 — 한 번 당긴 것이 두 번으로 읽혀 바로 기록되지 않게
    private float waitSince = -10f;                   // 확인을 기다리기 시작한 때
    private readonly int[] freeConfirmed = new int[3];   // 자유 측정 — 방식마다 확인(기록)한 측정의 수(3점 · 2선만 쓴다)
    private Vector3 armedCtrlPos, markCtrlPos;                              // 찍는 순간의 컨트롤러 자세 — 로그에 남겨, 나중에 끝점을 바꿔 다시 계산해 볼 수 있게
    private Quaternion armedCtrlRot = Quaternion.identity, markCtrlRot = Quaternion.identity;

    // 모니터·기록(10-07)
    private readonly RomLineMonitor monitor = new RomLineMonitor();
    private readonly StringBuilder monitorSb = new StringBuilder(4096), monitorLive = new StringBuilder(4096);
    private string monitorBody = "";      // 상태 가운데 «바뀔 때만 만드는 부분» — Refresh가 만든다
    private float monitorNext;
    private readonly List<string> csvRows = new List<string>(128);
    private string csvPath;

    // 탭 · 단계 멈춤(10-12 저녁 · 10-14 홈)
    private bool home = true;             // 홈 탭 — 처음은 여기서 시작하고, 네 탭(굴곡 · 신전 · 측굴 · 회전)을 다 재면 여기로 돌아온다(10-14)
    // ★11-17 사용자 지시: 홈 메뉴 — 스틱 위아래로 고르고 트리거로 정한다. 홈의 A · B(목록)는 아무 일도 안 한다.
    //   (11-17 오전판 = 항목 셋 · 커서 없음에서 시작 · 초기화 · 나가기 트리거 길게 → 오후 시안 V0.4로 바꿨다. 벽면 확인 창 resetAsk · calibAsk는 없앴다.)
    // ★11-17 시안 V0.4(사용자 결정 «추천대로»): 항목 넷 · 홈에 올 때마다 ROM 기본 선택(커서 없는 상태 없음) · 트리거 길게를 없애고
    //   초기화 · 설정(끝점 보정) · 나가기는 설명 상자 안 확인 안내(트리거 : 실행 · B : 취소)를 한 번 거친다. ROM은 바로 측정 탭으로.
    private const int MenuRom = 0, MenuReset = 1, MenuSettings = 2, MenuExit = 3;
    private static readonly string[] MenuLabel = { "ROM", "측정값 초기화", "설정", "나가기" };
    // 설정 안의 항목(탭) — 지금은 끝점 보정 하나. 둘 이상이 되면 확인 안내 중 스틱 좌우가 이 탭을 옮긴다(위 탭 줄은 안 옮긴다 · 시안 ⑦).
    private static readonly string[] SettingsName = { "끝점 보정" };
    private int menuCursor = MenuRom;
    private bool menuAsk;                 // 설명 상자에 확인 안내가 떠 있다(초기화 · 설정 · 나가기)
    private int settingsTab;
    private bool confirmWait;             // 동작 하나(세 직선)를 다 찍었다 — A/X(확인)를 기다린다
    private readonly bool[] confirmed = new bool[6];   // 동작마다 확인까지 마쳤는가
    private bool axesAsk;                 // «축 다시 잡기»를 불렀다(스틱 누름) — 스틱을 밀면 잡는다
    private float axesAskStart;
    private const float AxesAskSeconds = 8f;
    private float undoHoldStart = -1f;    // B/Y — 짧게 : 직선 취소(뗄 때), 길게 : 이 탭 지우기
    private bool undoHoldFired;
    private const float ResetHoldSeconds = 1.0f;

    // 끝점 보정(10-07) — 끝을 한 점에 대고 돌린 자세들
    private bool calibrating;
    private float calibStart, calibNextText, calibRange;
    private readonly List<Vector3> calibPos = new List<Vector3>(600);
    private readonly List<Quaternion> calibRot = new List<Quaternion>(600);
    private const int CalibMinSamples = 40;
    private const float CalibMinRange = 40f;       // 처음 자세에서 이만큼(도)은 돌려야 끝점이 정해진다
    private const float CalibMaxRms = 0.003f;      // 맞춘 뒤 남은 흔들림이 이보다 크면(m) 적용하지 않는다
    private const float CalibTimeout = 40f;
    private bool stickReady = true;
    private int switchedFrame = -1;
    private Vector3 ctrlPos;
    private Quaternion ctrlRot = Quaternion.identity;
    private float hapticOff = -1f;

    // 손(10-06) — 판 조작만 한다. 점은 컨트롤러로만 찍는다(끝점이 정해져 있는 쪽).
    private readonly RomRecordHands hands = new RomRecordHands();
    private readonly bool[] fingerValid = new bool[2];      // 0 왼손 · 1 오른손 검지 끝
    private readonly Vector3[] fingerPos = new Vector3[2];
    private readonly bool[] pinching = new bool[2];
    private readonly bool[] pinchDown = new bool[2];
    private bool simultaneousOn;         // 우리가 켠 «컨트롤러 + 맨손 동시 추적» — 켠 것만 끈다
    private int trackMask = -1;
    private float nextTrackLog;

    // 표시
    private readonly RomLineView view = new RomLineView();
    private bool dirty = true;
    private string noteText = "";
    private float nextSilentLog;
    private string lastSilent;

    private AudioSource audioSrc;
    private AudioClip sndMark, sndUndo, sndDeny, sndPress, sndDone;

    private const float PinchClose = 0.02f, PinchOpen = 0.04f;   // 엄지–검지 끝 거리(m). 사이는 그대로 — 떨림으로 깜박이지 않게
    private const float ArmThreshold = 0.15f;      // 트리거를 이만큼 당기기 시작한 순간의 자리를 찍는 자리로 쓴다
    private const float FixedEyeHeight = 1.5f;     // 메인 화면을 세울 때 쓰는 기준 눈높이(바닥에서, m) — 헤드셋을 쓰지 않고 내려놓고 잴 때도 같은 자리에 서게 고정값을 쓴다
    private const float FixedAxesAhead = 1.0f;     // 바닥 십자를 추적 원점에서 정면으로 띄우는 거리(m)

    /// <summary>여섯 동작을 모두 확인까지 마쳤다. ★group은 이제 19(끝)로 가지 않는다 — 마지막에 확인한 동작의 압박에 머문다(10-12 저녁).</summary>
    private bool Done => confirmed[0] && confirmed[1] && confirmed[2] && confirmed[3] && confirmed[4] && confirmed[5];
    private int CurTab => MotionOf(Mathf.Clamp(group, 1, GroupCount - 1)) / 2;   // 0 굴곡·신전 · 1 측굴 · 2 회전
    private bool HasLine(int g) => has[GroupStart(g)] && has[GroupStart(g) + 1];
    private bool TabComplete(int tab) => confirmed[tab * 2] && confirmed[tab * 2 + 1];
    /// <summary>그 탭을 재는 중이다 — 점이 하나라도 찍혔고 아직 다 재지 않았다. 이 동안에는 다른 탭으로 못 간다.</summary>
    /// <summary>지금 탭의 번호 — 0 홈 · 1~3 순서 측정 · 4 자유 측정.</summary>
    private int CurTop => home ? 0 : freeMode ? 4 : CurTab + 1;
    private string TopName(int top) => top == 4 ? FreeTopName[freeTab] : TopTabName[top];

    // ★10-16 사용자 지시: 자유 측정(3점 · 2선 · 연속)은 지금 쓰지 않는다 — 탭을 감추고 들어가는 길(스틱 · 모니터 · 홈 B)을 막는다. 코드는 그대로 둔다.
    //   다시 쓰려면 이 값만 true로 바꾼다(런타임 값이라 씬에 굳지 않는다).
    private static readonly bool FreeTabOn = false;
    private static int LastTop => FreeTabOn ? 4 : 3;
    private bool TabLocked
    {
        get
        {
            if (freeMode || home) return false;
            int tab = CurTab;
            if (TabComplete(tab)) return false;
            for (int i = GroupStart(GroupOf(tab * 2, 0)), end = GroupStart(GroupOf(tab * 2 + 1, 2)) + 2; i < end; i++)
                if (has[i]) return true;
            return false;
        }
    }

    // ── 단계 ↔ 점 ───────────────────────────────────────────────────
    /// <summary>그 묶음에서 찍는 점 수. 비교 시험이 켜져 있으면 굴곡·신전(묶음 1~6)만 Gn을 더해 셋이다.</summary>
    private int SizeOf(int g) => compareGlGn && g >= 1 && g <= 6 ? 3 : 2;   // ★compareGlGn은 Start에서 끈다(10-08) — 늘 2다
    private static int GroupStart(int g) => g * 2;
    private static int MotionOf(int g) => (g - 1) / 3;
    private static int PhaseOf(int g) => (g - 1) % 3;          // 0 중립 · 1 능동 · 2 압박
    private static int GroupOf(int motion, int phase) => 1 + motion * 3 + phase;

    /// <summary>그 동작이 쓰는 두 점. ★검증기(RomLineVerify)도 이 함수를 그대로 쓴다.</summary>
    public static void LineOf(RomLineMotion m, out RomLinePoint a, out RomLinePoint b)
    {
        switch (m)
        {
            // ★10-08 사용자 결정: 굴곡 · 신전도 정중선(Gl ~ Gn)으로 잰다(종전은 오른쪽 Tr ~ Gl — 9/30 기준).
            //   시뮬레이션에서 Tr ~ Gl 선은 옆면에서 33.7° 들려 있어 다른 동작이 섞일 때 ±6.8° 틀어졌고, 정중선은 +1.3°였다.
            case RomLineMotion.Flexion:
            case RomLineMotion.Extension:
            case RomLineMotion.LateralRight:
            case RomLineMotion.LateralLeft: a = RomLinePoint.Gl; b = RomLinePoint.Gn; break;       // 정중선: Gl ~ Gn
            // ★10-16 교수님 지시(사용자 전달): 모든 동작이 미간점을 쓰므로 <b>미간점에서 시작</b>으로 통일한다(종전 회전은 이주점 → 미간점).
            //   두 선(중립 · 측정)이 함께 뒤집히므로 평면 각 · 직접 각의 크기와 부호는 그대로다(RomLineVerify로 확인).
            case RomLineMotion.RotationRight: a = RomLinePoint.Gl; b = RomLinePoint.TrLeft; break; // 미간점 ~ 반대쪽(왼쪽) Tr
            default: a = RomLinePoint.Gl; b = RomLinePoint.TrRight; break;                         // 좌회전: 미간점 ~ 오른쪽 Tr
        }
    }

    private RomLinePoint ExpectedPoint(int g, int i)
    {
        if (g == 0) return (RomLinePoint)i;   // 기준: 오른쪽 Tr → 왼쪽 Tr
        if (i >= 2) return RomLinePoint.Gn;   // 비교 시험의 세 번째 점
        LineOf((RomLineMotion)MotionOf(g), out RomLinePoint a, out RomLinePoint b);
        return i == 0 ? a : b;
    }

    private void Start()
    {
        EnablePassthrough();

        // ★10-22 사용자 지시: 관제 화면의 영상 녹화 파일 이름에 사용자명을 넣는다 — 결과 기록(TrainingResultTracker)과 같은 출처(PlayerPrefs)
        var login = new LoginStateStore().LoadLoginInfo();
        monitorUser = login.hasData && !string.IsNullOrEmpty(login.username) ? login.username : "";
        Debug.Log($"[선실측] 관제 녹화 사용자명 — {(monitorUser.Length > 0 ? monitorUser : "없음(파일 이름에서 뺀다)")}");

        // ★11-13 실습 / 평가(재현성) — 로비 모드 팝업이 고른 값. 고른 게 없으면(별도 판 · 에디터 직접 Play) 실습.
        evalMode = LobbyModeChoice.TryConsume(out InfoPanelController.ModeType chosen, out _) && chosen == InfoPanelController.ModeType.Evaluation;
        monitorTitle = MonitorTitle + (evalMode ? " 평가" : " 실습");
        if (evalMode)
        {
            hasBase = RomLineRepeatScore.TryLoadBaseline(monitorUser, baseVals, out baseWhen);
            Debug.Log(hasBase ? $"[선실측] 평가 모드 — 비교할 실습값 있음({baseWhen} · {RomLineRepeatScore.BaselinePath(monitorUser)}) · 측정 중에는 안 보인다"
                              : $"[선실측] ★평가 모드 — 이 사용자({monitorUser})의 실습값이 없다 — 점수를 못 낸다(실습 먼저)");
        }
        else Debug.Log($"[선실측] 실습 모드 — 확인할 때마다 실습값을 저장한다({RomLineRepeatScore.BaselinePath(monitorUser)})");

        // 서버 전송(11-16) — 이 판의 번호를 정하고, 지난 판에서 못 보낸 것부터 보낸다
        uploadId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        uploadStartedAt = RomLineUpload.Now();
        RomLineDevLog.Begin(uploadId, evalMode, uploadStartedAt);   // 개발용 로그(11-17) — OnDestroy에서 끈다
        RomLineUpload.Flush(uploadId, "시작 — 지난 판").Forget();

        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig != null) trackingSpace = rig.trackingSpace;

        TMP_FontAsset font = KoreanFontResolver.Resolve();
        Shader sh = Shader.Find("Sprites/Default");   // Always Included에 들어 있다(기존 실측과 같은 셰이더)
        if (sh == null)
        {
            Debug.LogError("[선실측] ★Sprites/Default 셰이더를 못 찾았다 — 표시를 만들 수 없어 멈춘다.");
            enabled = false;
            return;
        }
        view.Build(transform, font, new Material(sh));
        freeView.Build(view, transform, view.FreeArea, view.FreeTableTop);
        spaceGauge.Build(view, transform);
        // ★11-17 폐색 시험: 벽면UI만 현실 깊이에 가려지게(환자가 UI 앞에 보이게). 깊이는 추적이 붙은 뒤 Update에서 한 번 더 켠다.
        RomLineOcclusion.ApplyToWall(view.Panel);
        occOn = RomLineOcclusion.Enable(gameObject, "시작");
        BuildSounds();

        // ★10-08 사용자 지시: 탭 다섯 — 굴곡(3직선) · 신전(3) · 측굴(좌우 6) · 회전(좌우 6)을 <b>순서대로</b> 재고, 자유 측정은 «기타»다.
        //   기준(양쪽 Tr) 묶음(0)은 건너뛴다 — 면은 고정 3축으로 세운다(SetAxes가 frame에 넣는다). 처음은 굴곡 · 중립.
        //   ★비교 시험(compareGlGn)은 씬에 true로 굳어 있다. 굴곡 · 신전의 선이 Gl ~ Gn이 됐으니 뜻이 없어 여기서 끈다(세 점씩 찍게 되는 것을 막는다).
        compareGlGn = false;
        group = GroupOf(0, 0);
        freeMode = false;
        home = true;   // ★10-14 사용자 지시: 홈에서 시작한다(스틱 좌우로 탭을 고른다)
        HidePokeCursors();
        LoadTipOffset();
        view.SetMode(false);
        view.SetTabCount(LastTop + 1);   // 10-16: 자유 측정을 끄면 탭 넷
        freeView.SetVisible(true);   // 바닥의 3축 십자는 두 모드가 같이 쓴다

        if (monitorPort > 0)
        {
            // ★10-16 사용자 지시: 화면은 관제 미러링(mirror.html)이다 — 종전 monitor.html · webrtc 미러링은 쓰지 않는다.
            var pageAsset = Resources.Load<TextAsset>("RomLine/mirror");
            // ★11-13 A안(추나 앱에 합침): 로비에서 들어오면 추나 웹 미러링이 8090 · 8092를 쥐고 있다 → 비켜 달라고 하고, OnDestroy에서 되돌린다
            ChunaWebMirror.Suspend("선 실측");
            suspendedChunaMirror = true;
            if (monitor.Start(monitorPort, pageAsset != null ? pageAsset.text : ""))
            {
                // ★10-14 사용자 지시: 메인 화면의 모니터 주소 줄을 뺐다(모니터는 기기에서 확인된 적이 없다). 서버는 그대로 열리고 주소는 로그에 남는다.
                monitor.SetCsv(ResultCsv());   // ★10-22: 관제 «측정값 저장» = 결과표 값만(상세 기록은 기기 파일에만)
                if (mirror == null)
                {
                    // ★런타임에 붙이면 인스펙터 튜닝이 저장되지 않는다 — 튜닝하려면 씬의 이 오브젝트에 RomLineMirror를 올린다.
                    mirror = gameObject.AddComponent<RomLineMirror>();
                    Debug.Log("[선실측] 미러링 — 씬에 RomLineMirror가 없어 기본값으로 붙였다(튜닝하려면 씬에 올릴 것)");
                }
                mirror.Init(monitor, view);
            }
        }
        try
        {
            csvPath = System.IO.Path.Combine(Application.persistentDataPath, "romline_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            System.IO.File.WriteAllText(csvPath, CsvHeader + "\n", new UTF8Encoding(true));
            Debug.Log("[선실측] 기록 파일 — " + csvPath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[선실측] ★기록 파일을 만들지 못했다({e.Message}) — 모니터의 «CSV 받기»로만 받을 수 있다.");
            csvPath = null;
        }

        // 한 손에 컨트롤러, 다른 손은 맨손 — 둘을 함께 추적하게 한다. 지원하지 않으면 false가 돌아오고 손은 컨트롤러를 내려놨을 때만 잡힌다.
        //   ★handInput이 꺼져 있으면(기본) 요청하지 않는다 — 컨트롤러만 쓴다.
        simultaneousOn = handInput && OVRInput.EnableSimultaneousHandsAndControllers();
        Debug.Log(handInput
            ? $"[선실측] 컨트롤러 + 맨손 동시 추적 요청 — {(simultaneousOn ? "받아들여짐" : "★거절됨(이 기기·연결에서는 컨트롤러를 쥔 동안 손이 안 잡힐 수 있다)")}"
            : "[선실측] 손 입력 끔 — 컨트롤러만 쓴다(인스펙터 «Hand Input»으로 켠다)");

        Debug.Log($"[선실측] 붙었다 — 리그 {(rig != null ? "있음" : "★없음")} · 추적공간 {(trackingSpace != null ? "있음" : "★없음(컨트롤러 자리를 못 잡는다)")} · " +
                  $"셰이더 {(sh != null ? "있음" : "★없음")} · 폰트 {(font != null ? font.name : "★없음")} · 끝점 오프셋 {tipOffset:F3} · " +
                  $"비교 시험(미간점~턱끝점) {(compareGlGn ? "켬 — 굴곡·신전은 세 점씩" : "끔")}");
    }

    private void Update()
    {
        if (eye == null && Camera.main != null) eye = Camera.main.transform;
        if (eye == null) { Silent("눈(Camera.main)이 없다"); return; }

        if (!view.PanelPlaced) PlaceFixed();
        TickOcclusion();

        ReadController();
        ReadHands();
        HandleInput();

        if (dirty) Refresh();
        PumpMonitor();
        if (Time.unscaledTime >= batteryNext) PollBattery();
        view.Tick(eye, tipShown, tipValid, tipPos, !home && (freeMode || (!confirmWait && !TabComplete(CurTab))));
        freeView.Tick(eye);
        spaceGauge.Tick(eye);
    }

    private void OnDestroy()
    {
        if (hapticOff > 0f) OVRInput.SetControllerVibration(0f, 0f, active);
        if (simultaneousOn) OVRInput.DisableSimultaneousHandsAndControllers();   // 우리가 켠 것만 되돌린다
        monitor.Stop();
        SaveUpload("나감", final: true);
        RomLineUpload.Flush(null, "나감").Forget();   // 정적 비동기 — 씬이 내려가도 AuthenticationService(DontDestroyOnLoad)로 끝까지 간다
        if (suspendedChunaMirror) { suspendedChunaMirror = false; ChunaWebMirror.Resume("선 실측"); }   // 우리가 비키게 한 것만 되돌린다
        RomLineDevLog.Save("나감", tipOffset * 1000f);
        RomLineDevLog.End();   // Start에서 켠 것을 끈다
        RomLineOcclusion.Disable();   // 우리가 붙인 깊이 매니저만 끈다
    }
    private bool suspendedChunaMirror;

    // ── 폐색(11-17 시험) — Start에서는 XR이 아직 안 붙어 깊이가 없을 수 있다(09-21 실측랩 실측). 눈이 제자리에 온 뒤 한 번 더 켜고,
    //   깊이 텍스처가 처음 들어온 순간을 한 번 남긴다. 상태가 바뀔 때만 로그(매 프레임 할당 없음).
    private bool occOn, occRetried, occReadyLogged;
    private void TickOcclusion()
    {
        if (!occRetried && eye.position.y > 0.3f && Time.timeSinceLevelLoad > 2f)
        {
            occRetried = true;
            if (!occOn) occOn = RomLineOcclusion.Enable(gameObject, "추적이 붙은 뒤");
        }
        if (occOn && !occReadyLogged && RomLineOcclusion.DepthReady)
        {
            occReadyLogged = true;
            Debug.Log($"[선실측] 폐색 — 깊이 텍스처 준비됨(첫 프레임 · 시작 {Time.timeSinceLevelLoad:F1}초)");
        }
    }

    /// <summary>앱이 내려가면(헤드셋을 벗음 · 홈 버튼) 개발용 로그를 남겨 둔다 — 그대로 꺼질 수 있다(11-17).</summary>
    private void OnApplicationPause(bool paused)
    {
        if (paused) RomLineDevLog.Save("앱 내려감", tipOffset * 1000f);
    }

    // ── 손 ──────────────────────────────────────────────────────────
    private void ReadHands()
    {
        if (handInput) hands.Resolve();
        for (int h = 0; h < 2; h++)
        {
            if (!handInput) { fingerValid[h] = pinching[h] = pinchDown[h] = false; continue; }   // 손 입력 끔 — 컨트롤러만
            bool left = h == 0;
            pinchDown[h] = false;
            fingerValid[h] = hands.TryJoint(left, HandJointId.HandIndexTip, out fingerPos[h]) && !hands.SystemGesture(left);
            if (!fingerValid[h]) { pinching[h] = false; continue; }
            if (!hands.TryJoint(left, HandJointId.HandThumbTip, out Vector3 thumb)) continue;
            float gap = Vector3.Distance(thumb, fingerPos[h]);
            if (!pinching[h] && gap < PinchClose) pinching[h] = pinchDown[h] = true;
            else if (pinching[h] && gap > PinchOpen) pinching[h] = false;
        }

        // 무엇이 잡히고 있는지 — 바뀔 때만(1초에 한 번까지) 남긴다. «컨트롤러와 맨손이 함께 잡히는가»를 로그로 가른다.
        int mask = (tipValid ? 4 : 0) | (fingerValid[0] ? 1 : 0) | (fingerValid[1] ? 2 : 0);
        if (handInput && mask != trackMask && Time.unscaledTime >= nextTrackLog)   // 컨트롤러의 추적 상태는 ReadController가 따로 남긴다
        {
            trackMask = mask;
            nextTrackLog = Time.unscaledTime + 1f;
            Debug.Log($"[선실측] 추적 — 컨트롤러 {(tipValid ? "O" : "X")} · 왼손 {(fingerValid[0] ? "O" : "X")} · 오른손 {(fingerValid[1] ? "O" : "X")} · t {Time.unscaledTime:F1}s");
        }
    }

    /// <summary>
    /// ★10-14 사용자 지시: 컨트롤러 끝의 흰 커서를 없앤다. 그 정체는 씬의 ControllerPokeInteractor(Meta 프리팹)에 든
    ///   «PokeLocation» — 1cm 흰 구(Unlit, 흰색)다(프리팹 YAML로 확인 · 기기 미확인). 이 씬은 화면을 손으로 누르지 않으므로(10-12 저녁부터 버튼 없음)
    ///   찌르기 인터랙터를 통째로 끈다. 씬 파일은 건드리지 않는다 — 씬이 내려가면 같이 사라진다.
    /// </summary>
    private void HidePokeCursors()
    {
        int off = 0;
        foreach (Oculus.Interaction.PokeInteractor p in FindObjectsByType<Oculus.Interaction.PokeInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            p.gameObject.SetActive(false);
            off++;
        }
        Debug.Log($"[선실측] 찌르기 인터랙터(컨트롤러 끝 흰 커서) {off}개를 껐다{(off == 0 ? " ★하나도 없었다 — 흰 커서가 남아 있으면 다른 것이다" : "")}");
    }

    private void Buzz(float seconds, float amplitude)
    {
        if (!tipValid) return;
        OVRInput.SetControllerVibration(1f, amplitude, active);
        hapticOff = Time.unscaledTime + seconds;
    }

    // ── 컨트롤러 ────────────────────────────────────────────────────
    private void ReadController()
    {
        tipValid = tipShown = false;
        if (trackingSpace == null) { Silent("추적공간이 없다 — OVRCameraRig를 못 찾았다"); return; }

        // 쥐고 있지 않은 쪽에서 무언가 눌리면 그쪽으로 바꾼다. ★바꾼 그 누름은 «전환»으로만 쓴다 —
        //   컨트롤러를 바꿔 쥐다 눌린 트리거가 점으로 찍히지 않게.
        //   ★11-17 사용자 결정: 오른쪽 컨트롤러만 쓴다 — 반대쪽 버튼으로 넘어가는 전환을 막는다(AllowControllerSwitch).
        //     왼손 좌표 뒤집기(tipOffset x)는 남겨 둔다 — 전환을 다시 열면 그대로 쓴다. 관제 명령 · 설정으로 바꾸는 길은 없다(11-17 확인).
        OVRInput.Controller other = active == OVRInput.Controller.RTouch ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        if (!AllowControllerSwitch)
        {
            if (AnyDown(other)) Debug.Log("[선실측] 반대쪽 컨트롤러 누름 — 무시(오른쪽만 쓴다)");
        }
        else if (AnyDown(other))
        {
            active = other;
            prevTrigger = 1f;       // 이번 당김은 끝날 때까지 무시
            armedTime = -10f;
            switchedFrame = Time.frameCount;
            Debug.Log($"[선실측] 쓰는 컨트롤러 → {(active == OVRInput.Controller.RTouch ? "오른쪽" : "왼쪽")}");
            Play(sndPress);
        }

        // ★기기는 컨트롤러의 자리를 두 단계로 알려 준다.
        //   «추적(Tracked)» = 헤드셋 카메라가 컨트롤러의 LED를 실제로 보고 있다 → mm 단위로 믿을 수 있다.
        //   «유효(Valid)»   = 자리가 있긴 하다. 카메라가 못 보는 동안에는 컨트롤러 안의 센서로 이어 붙인 추정값이고, 곧 틀어진다.
        //   우리는 «추적»일 때만 점을 찍게 한다(프로그램이 건 제한). «유효»이기만 할 때는 끝 구슬을 빨갛게 보여 주고 찍기를 막는다.
        bool connected = OVRInput.IsControllerConnected(active);
        bool posTracked = connected && OVRInput.GetControllerPositionTracked(active);
        bool posValid = connected && OVRInput.GetControllerPositionValid(active);
        bool oriTracked = connected && OVRInput.GetControllerOrientationTracked(active);

        if (posValid)
        {
            Vector3 p = trackingSpace.TransformPoint(OVRInput.GetLocalControllerPosition(active));
            Quaternion r = trackingSpace.rotation * OVRInput.GetLocalControllerRotation(active);
            ctrlPos = p;
            ctrlRot = r;
            Vector3 off = tipOffset;
            if (active == OVRInput.Controller.LTouch) off.x = -off.x;   // 왼손 컨트롤러는 좌우가 거울상이다
            tipPos = p + r * off;
            tipShown = true;
        }
        tipValid = posTracked && posValid;
        LogTracking(connected, posTracked, posValid, oriTracked);

        if (!tipValid)
        {
            Silent($"{(active == OVRInput.Controller.RTouch ? "오른쪽" : "왼쪽")} 컨트롤러가 연결·추적되지 않는다 — 점을 찍을 수 없다");
            return;
        }
        lastSilent = null;
    }

    /// <summary>헤드셋에서 본 컨트롤러의 자리 — 거리(mm) · 좌우 각(오른쪽 +) · 위아래 각(위 +). 헤드셋 카메라의 시야를 벗어났는지 가리는 데 쓴다.</summary>
    private void FromHeadset(out float distanceMm, out float sideDeg, out float upDeg)
    {
        Vector3 rel = Quaternion.Inverse(eye.rotation) * (ctrlPos - eye.position);
        distanceMm = rel.magnitude * 1000f;   // ★10-13 사용자 지시: 길이는 화면 · 로그 · CSV 모두 mm이고 단위 글자를 붙이지 않는다
        sideDeg = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg;
        upDeg = Mathf.Atan2(rel.y, new Vector2(rel.x, rel.z).magnitude) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 컨트롤러의 추적 상태가 바뀔 때마다 «어디서 어떤 자세로» 바뀌었는지 남긴다(0.2초에 한 번까지).
    /// 놓친 이유를 가르려는 것이다: 시야 밖(좌우·위아래 각이 크다) / 멀다(거리) / 가렸다(가까운데 놓쳤다) / LED 면이 반대쪽(컨트롤러 방향).
    /// </summary>
    private void LogTracking(bool connected, bool posTracked, bool posValid, bool oriTracked)
    {
        int state = (connected ? 1 : 0) | (posTracked ? 2 : 0) | (posValid ? 4 : 0) | (oriTracked ? 8 : 0);
        if (state == trackState || Time.unscaledTime < trackLogNext) return;
        float held = trackState < 0 ? 0f : Time.unscaledTime - trackSince;
        trackState = state;
        trackSince = Time.unscaledTime;
        trackLogNext = Time.unscaledTime + 0.2f;

        string what = !connected ? "★연결 끊김" : posTracked ? "잡힘" : posValid ? "★놓침(자리는 추정값)" : "★놓침(자리 없음)";
        var sb = new StringBuilder(260);
        sb.Append("[선실측] 추적 — 컨트롤러 ").Append(what).Append(" · t ").Append(Time.unscaledTime.ToString("F1")).Append("s · 앞 상태 ").Append(held.ToString("F1")).Append("초");
        if (posValid && eye != null)
        {
            FromHeadset(out float dist, out float side, out float up);
            Vector3 toHead = (eye.position - ctrlPos).normalized;
            sb.Append(" · 헤드셋에서 ").Append(dist.ToString("F0")).Append(" · 좌우 ").Append(side.ToString("+0;-0")).Append("° · 위아래 ").Append(up.ToString("+0;-0"))
              .Append("° · 컨트롤러 윗면→헤드셋 ").Append(Vector3.Dot(ctrlRot * Vector3.up, toHead).ToString("+0.00;-0.00"))
              .Append(" · 앞면→헤드셋 ").Append(Vector3.Dot(ctrlRot * Vector3.forward, toHead).ToString("+0.00;-0.00"))
              .Append(" · 속도 ").Append(OVRInput.GetLocalControllerVelocity(active).magnitude.ToString("F2")).Append("m/s");
        }
        sb.Append(" · 플래그 연결 ").Append(connected ? 'O' : 'X').Append(" 자리추적 ").Append(posTracked ? 'O' : 'X').Append(" 자리유효 ").Append(posValid ? 'O' : 'X')
          .Append(" 방향추적 ").Append(oriTracked ? 'O' : 'X');
        Debug.Log(sb.ToString());
    }

    private static bool AnyDown(OVRInput.Controller c)
    {
        return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, c) || OVRInput.GetDown(OVRInput.Button.One, c)
            || OVRInput.GetDown(OVRInput.Button.Two, c) || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, c);
    }

    private void HandleInput()
    {
        if (hapticOff > 0f && Time.unscaledTime >= hapticOff)
        {
            OVRInput.SetControllerVibration(0f, 0f, active);
            hapticOff = -1f;
        }
        if (Time.frameCount == switchedFrame) return;   // 컨트롤러를 바꾼 그 누름은 전환으로만 쓴다
        if (calibrating && Time.unscaledTime - calibStart > CalibTimeout) EndCalibration("시간이 다 됐다");

        // 트리거를 당기기 시작한 순간의 자리를 잡아 둔다 — 끝까지 당기는 동안 컨트롤러가 흔들린다.
        float trigger = tipValid ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, active) : prevTrigger;
        if (prevTrigger < ArmThreshold && trigger >= ArmThreshold)
        {
            armedPos = tipPos;
            armedCtrlPos = ctrlPos;
            armedCtrlRot = ctrlRot;
            armedTime = Time.unscaledTime;
        }
        bool triggerDown = tipValid && prevTrigger < 0.55f && trigger >= 0.55f;
        prevTrigger = trigger;

        // B/Y — 짧게 눌렀다 떼면 «직선 취소», 1초 누르고 있으면 «이 탭 지우기». 길게 누르기와 겹치므로 취소는 뗄 때 듣는다.
        //   ★버튼 상태는 여기서 매 프레임 본다(아래의 추적 조건으로 일찍 돌아가도 누름이 다음으로 밀려 엉뚱한 때 듣지 않게).
        //     그 프레임에 아래까지 못 가면 그 누름은 버린다.
        bool undoTap = false, undoLong = false;
        if (OVRInput.GetDown(OVRInput.Button.Two, active)) { undoHoldStart = Time.unscaledTime; undoHoldFired = false; }
        else if (undoHoldStart >= 0f)
        {
            if (!OVRInput.Get(OVRInput.Button.Two, active))
            {
                undoTap = !undoHoldFired;
                undoHoldStart = -1f;
            }
            else if (!undoHoldFired && Time.unscaledTime - undoHoldStart >= ResetHoldSeconds) undoHoldFired = undoLong = true;
        }

        if (!tipValid) return;           // 아래는 컨트롤러가 추적될 때만 듣는다

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, active);
        if (stick.magnitude < 0.3f) stickReady = true;
        bool stickPush = stickReady && stick.magnitude > 0.7f;
        bool aDown = OVRInput.GetDown(OVRInput.Button.One, active);

        // ── 스틱 누르기 — 끝점 보정 중이면 끝내기, 아니면 «축 다시 잡기»를 부른다(부른 뒤 스틱을 밀어야 잡힌다) ──
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, active))
        {
            if (calibrating) EndCalibration("스틱");
            else SetAxesAsk(!axesAsk, "스틱 누름");
            stickReady = false;          // 누르면서 밀린 스틱이 그대로 «밀기»로 읽히지 않게 — 가운데로 돌아온 뒤에 듣는다
            return;
        }
        // ── 끝점 보정(화면 이름 «촉 끝 맞추기») — 보정 중 트리거 : 끝(스틱 누르기로도 끝난다) ──
        //   ★10-22 사용자 지시: 홈에서 트리거를 실수로 누르는 일이 많다 → 트리거로 바로 시작하지 않는다.
        //   홈에서 B → 확인 창 «촉 끝 맞추기를 시작할까요?» → 트리거 : 시작 · B : 취소(10-14~10-21에는 홈의 트리거가 바로 시작했다).
        if (calibrating)
        {
            if (triggerDown) EndCalibration("트리거");
            else SampleCalibration();
            return;
        }

        // ── 축 다시 잡기를 불러 둔 동안 — 스틱을 밀면(어느 쪽이든) 잡는다. 다른 버튼 · 트리거는 그만두기(점은 찍지 않는다) ──
        if (axesAsk)
        {
            if (stickPush)
            {
                stickReady = false;
                axesAsk = false;
                Buzz(0.08f, 0.6f);
                CaptureAxes("스틱", true);
            }
            else if (aDown || undoTap || undoLong || triggerDown) SetAxesAsk(false, "다른 버튼");
            else if (Time.unscaledTime - axesAskStart > AxesAskSeconds) SetAxesAsk(false, "시간이 다 됐다");
            return;
        }

        // ── 홈 메뉴(11-17 시안 V0.4) — 스틱 위아래 · 트리거(ROM = 측정 탭으로 · 나머지 = 확인 안내) · 확인 안내 중 트리거 / B ──
        if (home && HandleHomeMenu(stick, stickPush, triggerDown, undoTap || undoLong)) return;

        // ── A/X — ★10-14 사용자 지시: 탭 창(A로 여닫기)을 없앴다. 측정 확인은 트리거(10-13, Mark) ──
        if (aDown)
        {
            // ★11-17: 홈의 A는 아무 일도 안 한다 — 측정값 초기화는 홈 메뉴(스틱 ▲▼ → 트리거 → 확인 안내)
            if (!home) Deny("측정값 초기화는 홈 메뉴에서 — 스틱 좌우로 홈(메뉴) → 측정값 초기화");
            return;
        }

        // ── B/Y ── 홈에서는 자유 측정의 방식 바꾸기(3점 → 2선 → 연속 — 탭 이름이 따라 바뀐다, 10-14). 측정 탭에서는 취소 · 지우기
        if (undoTap || undoLong)
        {
            if (home)
            {
                if (FreeTabOn) SetFreeTab((freeTab + 1) % 3);   // 길게 눌러도 같다(길게는 뗄 때 짧은 누름으로 읽히지 않는다)
                // ★11-17: 자유 측정을 끈 홈의 B는 아무 일도 안 한다 — 끝점 보정은 홈 메뉴로 옮겼다(10-22~11-16에는 촉 끝 맞추기 확인 창이었다)
            }
            else if (undoLong)
            {
                Debug.Log($"[선실측] B/Y 길게({ResetHoldSeconds:F1}초) — {(freeMode ? "이 방식의 점 지우기(자유 측정)" : "이 탭 지우기")}");
                Buzz(0.12f, 0.8f);
                if (freeMode) FreeClear(); else ResetTab(CurTab, "B/Y 길게");
            }
            else Undo();
            return;
        }

        // ── 스틱 좌우 — 탭을 바로 옮긴다(10-14: 탭 줄이 늘 보인다). 재는 중인 탭이면 받지 않는다. 확인 창이 떠 있는 동안은 A · B만 듣는다 ──
        if (stickPush && Mathf.Abs(stick.x) > 0.7f && Mathf.Abs(stick.y) < 0.5f)
        {
            stickReady = false;
            // ★11-17 사용자 지시: 끝에서 순환 — 마지막 탭에서 → 첫 탭(≡ 홈) · 첫 탭에서 ← 마지막 탭(재는 중 잠금은 GoTab이 그대로 막는다)
            int nextTop = CurTop + (stick.x > 0f ? 1 : -1);
            bool wrapTop = nextTop < 0 || nextTop > LastTop;
            if (nextTop < 0) nextTop = LastTop; else if (nextTop > LastTop) nextTop = 0;
            GoTab(nextTop, wrapTop ? "스틱 · 순환" : "스틱");   // 확인 안내 중의 좌우는 HandleHomeMenu가 먼저 받는다
            return;
        }

        // ── 점 찍기 ──
        if (triggerDown)
        {
            bool armed = Time.unscaledTime - armedTime < 0.6f;
            Vector3 at = armed ? armedPos : tipPos;
            float shake = armed ? Vector3.Distance(armedPos, tipPos) : 0f;
            markCtrlPos = armed ? armedCtrlPos : ctrlPos;
            markCtrlRot = armed ? armedCtrlRot : ctrlRot;
            Mark(at, shake);
        }
    }

    // ── 홈 메뉴(11-17 · 시안 V0.4) ────────────────────────────────────
    /// <summary>
    /// 홈에서만 부른다. 목록: 스틱 ▲▼ = 항목(끝에서 순환 — 11-17) · 트리거 = ROM이면 측정 탭으로, 나머지는 확인 안내.
    /// 확인 안내 중: 트리거 = 실행 · B = 취소(목록으로) · 스틱 ▲▼는 받지 않음 · 스틱 ◀▶는 설정 탭이 둘 이상일 때만 설정 탭 이동(아니면 받지 않음).
    /// 목록에서 스틱 ◀▶ · B · A는 받지 않고 넘긴다(false — 위 탭 줄 이동 · 자유 측정 B는 그대로). 입력을 썼으면 true.
    /// </summary>
    private bool HandleHomeMenu(Vector2 stick, bool stickPush, bool triggerDown, bool bPressed)
    {
        bool vertical = stickPush && Mathf.Abs(stick.y) > 0.7f && Mathf.Abs(stick.x) < 0.5f;
        bool horizontal = stickPush && Mathf.Abs(stick.x) > 0.7f && Mathf.Abs(stick.y) < 0.5f;
        if (menuAsk)
        {
            if (vertical || horizontal)
            {
                stickReady = false;
                if (horizontal && menuCursor == MenuSettings && SettingsName.Length > 1)
                {
                    int next = Mathf.Clamp(settingsTab + (stick.x > 0f ? 1 : -1), 0, SettingsName.Length - 1);
                    if (next != settingsTab) { settingsTab = next; Debug.Log($"[선실측] 홈 메뉴 — 설정 탭 → {SettingsName[settingsTab]}"); Play(sndPress); dirty = true; }
                }
                return true;
            }
            if (bPressed)
            {
                menuAsk = false;
                Debug.Log($"[선실측] 홈 메뉴 — {MenuLabel[menuCursor]} 취소(B)");
                Play(sndUndo);
                dirty = true;
                return true;
            }
            if (!triggerDown) return false;
            int item = menuCursor;
            menuAsk = false;
            Debug.Log($"[선실측] 홈 메뉴 — {MenuLabel[item]} 실행(트리거){(item == MenuSettings ? " · " + SettingsName[settingsTab] : "")}");
            Buzz(0.12f, 0.8f);
            dirty = true;
            if (item == MenuReset) ResetAll("홈 메뉴");
            else if (item == MenuSettings) BeginCalibration();   // 설정 탭 0 = 끝점 보정(지금 하나뿐)
            else Exit();
            return true;
        }

        if (vertical)
        {
            stickReady = false;
            // ★11-17 사용자 지시: 끝에서 순환 — 맨 아래(나가기)에서 ▼ → 맨 위(ROM) · 맨 위에서 ▲ → 맨 아래
            int next = menuCursor + (stick.y < 0f ? 1 : -1);
            bool wrap = next < 0 || next > MenuLabel.Length - 1;
            if (next < 0) next = MenuLabel.Length - 1; else if (next > MenuLabel.Length - 1) next = 0;
            if (next != menuCursor)
            {
                menuCursor = next;
                Debug.Log($"[선실측] 홈 메뉴 — 커서 → {MenuLabel[menuCursor]}{(wrap ? " (순환)" : "")}");
                Play(sndPress);
                dirty = true;
            }
            return true;
        }
        if (!triggerDown) return false;
        if (menuCursor == MenuRom)
        {
            int tab = RomEntryTab();
            Debug.Log($"[선실측] 홈 메뉴 — ROM 실행(트리거) → {GuidedTabName[tab]}");
            GoTab(tab + 1, "홈 메뉴 ROM");
            return true;
        }
        menuAsk = true;
        settingsTab = 0;
        Debug.Log($"[선실측] 홈 메뉴 — 확인 안내 → {MenuLabel[menuCursor]}");
        Play(sndPress);
        dirty = true;
        return true;
    }

    /// <summary>ROM으로 들어갈 탭 — 아직 다 재지 않은 첫 탭 · 다 쟀으면 굴곡·신전(0).</summary>
    private int RomEntryTab()
    {
        for (int t = 0; t < 3; t++) if (!TabComplete(t)) return t;
        return 0;
    }

    /// <summary>홈에 올 때 — 늘 ROM을 고른 목록(확인 안내는 닫는다).</summary>
    private void ResetMenu(string why)
    {
        bool changed = menuCursor != MenuRom || menuAsk;
        menuCursor = MenuRom;
        menuAsk = false;
        settingsTab = 0;
        if (changed) Debug.Log($"[선실측] 홈 메뉴 — 커서 → ROM({why})");
        dirty = true;
    }

    // ── 끝점 보정 ───────────────────────────────────────────────────
    private void BeginCalibration()
    {
        menuAsk = false;   // 11-17 V0.4: 보정 중엔 메뉴를 내리고(Refresh) 끝나면 목록으로 돌아온다(커서는 그대로)
        calibrating = true;
        calibStart = Time.unscaledTime;
        calibNextText = 0f;
        calibRange = 0f;
        calibPos.Clear();
        calibRot.Clear();
        noteText = "끝점 보정 — 촉 끝을 한 점에 대고 여러 방향으로 돌리세요";
        Debug.Log($"[선실측] 끝점 보정 시작 — 지금 값 {tipOffset * 1000f:F1}mm · 끝을 움직이지 않는 한 점에 댄 채 컨트롤러를 여러 방향으로 돌린다(트리거 · 스틱 누르기 = 끝)");
        Play(sndPress);
        dirty = true;
    }

    private void SampleCalibration()
    {
        int n = calibPos.Count;
        if (n < 600 && (n == 0 || Quaternion.Angle(calibRot[n - 1], ctrlRot) > 1.5f))   // 방향이 바뀐 자세만 모은다(가만히 있는 동안 같은 표본이 쌓이지 않게)
        {
            calibPos.Add(ctrlPos);
            calibRot.Add(ctrlRot);
            calibRange = Mathf.Max(calibRange, Quaternion.Angle(calibRot[0], ctrlRot));
        }
        if (Time.unscaledTime < calibNextText) return;
        calibNextText = Time.unscaledTime + 0.5f;   // 글자는 0.5초에 한 번만 바꾼다
        noteText = $"끝점 보정 중 — 표본 {calibPos.Count} · 돌린 범위 {calibRange:F0}° · 트리거 : 끝";
        dirty = true;
    }

    private void EndCalibration(string why)
    {
        calibrating = false;
        int n = calibPos.Count;
        if (n < CalibMinSamples || calibRange < CalibMinRange)
        {
            Deny($"끝점 보정 — 모자라다(표본 {n} · 돌린 범위 {calibRange:F0}°). 더 크게, 여러 방향으로 돌려야 한다");
            return;
        }
        if (!RomLineMath.PivotFit(calibPos, calibRot, out Vector3 fit, out Vector3 pivot, out float rms, out float max))
        {
            Deny("끝점 보정 — 계산을 못 했다(한 방향으로만 돌렸다)");
            return;
        }

        bool left = active == OVRInput.Controller.LTouch;
        Vector3 used = tipOffset;
        if (left) used.x = -used.x;                     // 왼손 컨트롤러는 좌우를 뒤집어 쓴다 — 견줄 때도 같은 좌표로
        RomLineMath.TipSpread(calibPos, calibRot, used, out float oldRms, out float oldMax);
        Vector3 asRight = fit;
        if (left) asRight.x = -asRight.x;               // tipOffset은 오른손 기준으로 적는다
        float moved = Vector3.Distance(asRight, tipOffset);
        bool apply = rms <= CalibMaxRms;

        Debug.Log($"[선실측] 끝점 보정 끝({why}) — 표본 {n} · 돌린 범위 {calibRange:F0}° · {(left ? "왼쪽" : "오른쪽")} 컨트롤러 · " +
                  $"맞춘 끝점 {asRight * 1000f:F1}mm ↔ 쓰던 값 {tipOffset * 1000f:F1}mm (차이 {moved * 1000f:F1}mm) · " +
                  $"맞춘 값으로 남은 흔들림 rms {rms * 1000f:F1}mm(최대 {max * 1000f:F1}mm) · 쓰던 값으로는 rms {oldRms * 1000f:F1}mm(최대 {oldMax * 1000f:F1}mm) · " +
                  $"고정점 {pivot:F3} · {(apply ? "★적용했다 · 기기에 저장(다음 실행도 이 값)" : "적용 안 함 — 흔들림이 크다(끝이 미끄러졌거나 추적이 흔들렸다)")}");
        if (apply)
        {
            tipOffset = asRight;
            SaveTipOffset();
            noteText = $"끝점 ({asRight.x * 1000f:F0}, {asRight.y * 1000f:F0}, {asRight.z * 1000f:F0})mm 로 바꿈 · 전과 {moved * 1000f:F0}mm 차이 · 흔들림 {oldRms * 1000f:F1} → {rms * 1000f:F1}mm";
            Play(sndDone);
        }
        else
        {
            noteText = $"끝점 보정 — 흔들림 {rms * 1000f:F1}mm로 커서 적용 안 함";
            Play(sndDeny);
        }
        dirty = true;
    }

    // ★10-14: 보정한 끝점을 기기에 남긴다(PlayerPrefs) — 촉을 단 채 앱을 다시 켜도 그 값을 쓴다. 저장값이 있으면 씬의 tipOffset보다 먼저다(Start에서 읽는다).
    //   종전에는 그 실행에서만 쓰였다(Play를 멈추거나 앱을 끄면 씬 값으로 돌아갔다). 지우려면 PlayerPrefs의 RomLine.Tip 키를 지운다.
    private const string TipKey = "RomLine.Tip";

    private void SaveTipOffset()
    {
        PlayerPrefs.SetFloat(TipKey + ".x", tipOffset.x);
        PlayerPrefs.SetFloat(TipKey + ".y", tipOffset.y);
        PlayerPrefs.SetFloat(TipKey + ".z", tipOffset.z);
        PlayerPrefs.Save();
        Debug.Log($"[선실측] 끝점 저장 — {tipOffset * 1000f:F1}mm(다음 실행부터 이 값을 쓴다)");
    }

    private void LoadTipOffset()
    {
        if (!PlayerPrefs.HasKey(TipKey + ".z")) { Debug.Log($"[선실측] 저장된 끝점 없음 — 씬 값 {tipOffset * 1000f:F1}mm"); return; }
        Vector3 scene = tipOffset;
        tipOffset = new Vector3(PlayerPrefs.GetFloat(TipKey + ".x"), PlayerPrefs.GetFloat(TipKey + ".y"), PlayerPrefs.GetFloat(TipKey + ".z"));
        Debug.Log($"[선실측] 저장된 끝점을 쓴다 — {tipOffset * 1000f:F1}mm(씬 값 {scene * 1000f:F1}mm)");
    }

    /// <summary>찍는 순간의 컨트롤러 자세와 시각 — 찍음 로그 끝에 붙인다(끝점을 바꿔 다시 계산하거나, 추적이 끊긴 때와 견줄 수 있게).</summary>
    private string PoseSuffix()
    {
        FromHeadset(out float dist, out float side, out float up);
        return $" · 컨트롤러 자리 {markCtrlPos:F4} 회전 {markCtrlRot:F4} · 헤드셋에서 {dist:F0} 좌우 {side:+0;-0}° 위아래 {up:+0;-0}° · 추적된 지 {Time.unscaledTime - trackSince:F1}초 · t {Time.unscaledTime:F1}s";
    }

    // ── 동작 ────────────────────────────────────────────────────────
    private void Mark(Vector3 pos, float shake)
    {
        if (home) { Deny("홈 — 스틱 좌우 : 잴 탭 고르기 · 스틱 위아래 : 메뉴"); return; }
        if (freeMode) { FreeMark(pos, shake); return; }
        // ★10-13 사용자 지시: 확인은 트리거다 — 안내에 뜬 각을 보고 한 번 더 당기면 그때 결과표에 기록된다.
        if (confirmWait) { if (Time.unscaledTime - waitSince >= ConfirmGuard) Confirm("트리거"); return; }
        if (TabComplete(CurTab))
        {
            Deny(Done ? "측정이 끝났다 — 다시 재려면 홈 메뉴 «측정값 초기화» · 그 탭만이면 B 길게 : 이 탭 지우기"
                      : $"{GuidedTabName[CurTab]} 탭은 다 쟀다 — 스틱으로 다른 탭을 고르거나 B 길게로 이 탭을 지운다");
            return;
        }

        int start = GroupStart(group);
        if (index == 0)
        {
            // 이미 찍은 묶음에 다시 들어와 첫 점을 찍으면 그 묶음을 새로 잰다.
            has[start] = has[start + 1] = false;
            hasExtra[group] = false;
            Invalidate(group);
        }

        RomLinePoint which = ExpectedPoint(group, index);
        if (index < 2) { points[start + index] = pos; has[start + index] = true; }
        else { extra[group] = pos; hasExtra[group] = true; }
        Debug.Log($"[선실측] 찍음 — {GroupTitle(group)} · {PointName[(int)which]} ({index + 1}/{SizeOf(group)}) · 자리 {pos:F3} · " +
                  $"당기는 동안 끝점 이동 {shake * 1000f:F1}mm" + PoseSuffix());
        index++;
        noteText = "";
        Play(sndMark);
        Buzz(0.06f, 0.6f);

        if (index >= SizeOf(group)) CompleteGroup();
        dirty = true;
    }

    private void CompleteGroup()
    {
        int start = GroupStart(group);
        if (Vector3.Distance(points[start], points[start + 1]) < 0.005f)
        {
            has[start] = has[start + 1] = false;
            index = 0;
            Deny("두 점이 같은 자리에 찍혔다 — 다시 찍는다");
            return;
        }
        if (hasExtra[group] && Vector3.Distance(points[start + 1], extra[group]) < 0.005f)
        {
            hasExtra[group] = false;
            index = 2;
            Deny("미간점과 턱끝점이 같은 자리에 찍혔다 — 턱끝점을 다시 찍는다");
            return;
        }

        if (group == 0)
        {
            frame = RomLineMath.BuildFrame(points[0], points[1]);
            if (!frame.valid)
            {
                has[0] = has[1] = false;
                index = 0;
                Deny("양쪽 이주점이 위아래로 겹쳐 찍혔다 — 기준을 다시 찍는다");
                return;
            }
            Debug.Log($"[선실측] 기준 방향 — 오른쪽 {frame.right:F2} · 앞뒤 {frame.forward:F2} · 위 {frame.up:F2} · " +
                      $"이주점 간격 {Vector3.Distance(points[0], points[1]) * 1000f:F0}");
            for (int m = 0; m < 6; m++) { FaceCheck(m); RecomputeMotion(m); }   // 기준만 다시 찍었을 때 남아 있는 측정을 다시 잰다
        }
        else
        {
            int m = MotionOf(group), ph = PhaseOf(group);
            if (ph == 0)
            {
                Debug.Log($"[선실측] 중립선 — {MotionName[m]} · 길이 {Vector3.Distance(points[start], points[start + 1]) * 1000f:F0}");
                FaceCheck(m);
                RecomputeMotion(m);
            }
            else Compute(m, ph);
            CsvGuided(m, ph);
        }

        index = 0;
        if (group > 0 && PhaseOf(group) == 2)
        {
            // ★10-12 저녁: 동작 하나(세 직선)를 다 찍으면 멈춘다 — 확인(10-13부터 트리거)을 해야 결과표에 기록되고 다음 동작으로 넘어간다. group은 압박에 머문다.
            confirmWait = true;
            waitSince = Time.unscaledTime;
            Play(sndDone);
            Debug.Log($"[선실측] 확인 대기 — {MotionName[MotionOf(group)]}의 세 직선을 다 찍었다 · 트리거 : 확인(기록) → {NextName(MotionOf(group))}");
        }
        else group++;
    }

    /// <summary>
    /// 그 동작의 중립선에 든 Gl로 앞뒤를 확인한다. Gl이 뒤쪽에 있으면 양쪽 Tr을 바꿔 찍은 것이라 좌우를 바로잡는다.
    /// ★각의 크기는 안 바뀐다 — 부호(방향이 맞는가 판정)만 바뀐다.
    /// </summary>
    private void FaceCheck(int m)
    {
        if (!frame.valid) return;
        if (!has[0] || !has[1]) return;   // 양쪽 Tr을 찍지 않는다(10-08) — 앞뒤는 고정 3축이 정한다
        int n0 = GroupStart(GroupOf(m, 0));
        if (!has[n0] || !has[n0 + 1]) return;
        LineOf((RomLineMotion)m, out RomLinePoint a, out RomLinePoint b);
        int gi = a == RomLinePoint.Gl ? 0 : b == RomLinePoint.Gl ? 1 : -1;
        if (gi < 0) return;
        if (!RomLineMath.FaceForward(ref frame, (points[0] + points[1]) * 0.5f, points[n0 + gi])) return;

        (points[0], points[1]) = (points[1], points[0]);
        noteText = "양쪽 이주점이 바뀌어 찍혀 바로잡았다";
        Debug.Log("[선실측] ★양쪽 이주점을 바꿔 찍은 것으로 보인다(미간점이 뒤쪽에 있었다) — 좌우를 바로잡았다.");
        for (int k = 0; k < 6; k++) RecomputeMotion(k);
    }

    private void RecomputeMotion(int m)
    {
        Compute(m, 1);
        Compute(m, 2);
    }

    /// <summary>그 동작의 중립선과 측정선(ph 1 능동 · 2 압박)이 다 있으면 각을 낸다.</summary>
    private void Compute(int m, int ph)
    {
        int slot = m * 2 + ph - 1;
        ClearResult(slot);
        int n0 = GroupStart(GroupOf(m, 0)), s0 = GroupStart(GroupOf(m, ph));
        if (!has[n0] || !has[n0 + 1] || !has[s0] || !has[s0 + 1]) return;

        var motion = (RomLineMotion)m;
        string title = MotionName[m] + " · " + PhaseName[ph];
        LogCoordinates(m, ph, n0, s0, title);
        if (compareGlGn && m < 2) ComputeCompare(m, ph, n0, s0, title);

        // 직접 — 기준 없이 3차원에서 그대로.
        RomLineDirect dr = RomLineMath.MeasureDirect(points[n0], points[n0 + 1], points[s0], points[s0 + 1]);
        direct[slot] = dr;
        if (!dr.valid) { Debug.LogWarning($"[선실측] ★{title} — 각을 못 냈다(같은 자리를 두 번 찍었다)."); return; }

        // 면 — 기준(양쪽 Tr)이 있을 때만. 회전은 바닥면이라 기준 없이도 낸다.
        bool byGravity = !frame.valid && !RomLineMath.NeedsReference(motion);
        if (!frame.valid && !byGravity)
        {
            Debug.Log($"[선실측] 값 — {title} 직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 1000f:F0} · 면 위 —(기준 이주점을 안 찍었다)");
            return;
        }
        RomLineResult r = RomLineMath.Measure(points[n0], points[n0 + 1], points[s0], points[s0 + 1], RomLineMath.PlaneNormal(motion, frame));
        results[slot] = r;
        if (!r.valid)
        {
            Debug.LogWarning($"[선실측] ★{title} — 면 위 각을 못 냈다(선이 면에 수직이다). 직접 {dr.angle:F1}°");
            return;
        }

        bool match = RomLineMath.DirectionMatches(motion, r.signedAngle);
        Debug.Log($"[선실측] 값 — {title} 면 위 {r.angle:F1}°{(byGravity ? "(기준 없이 · 바닥면)" : "")} (부호 {r.signedAngle:+0.0;-0.0}{(match ? "" : " ★반대 방향")}) · " +
                  $"직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 1000f:F0} · " +
                  $"면 밖 각 중립선 {r.neutralOffPlane:F1}° → 측정선 {r.markOffPlane:F1}° (변화 {r.markOffPlane - r.neutralOffPlane:+0.0;-0.0}°) · " +
                  $"교점 {(r.pivotOnLines ? $"선 위(중립 첫 점에서 {Vector3.Distance(r.pivot, r.neutralA) * 1000f:F0})" : "멀거나 평행 — 첫 점에 세움")}");
        if (!match && GroupOf(m, ph) == group)
        {
            noteText = $"{MotionName[m]} 단계인데 반대쪽으로 움직인 값이다";
            Play(sndDeny);
        }
    }

    /// <summary>
    /// 값을 낸 점들의 좌표를 한 줄에 모아 남긴다(월드, m, 0.1mm 자리까지) — 나중에 로그만으로 다시 계산해 볼 수 있게.
    /// 찍을 때마다 남기는 «찍음» 줄과 달리, 다시 찍거나 취소한 뒤 <b>실제로 값에 쓰인</b> 점만 들어간다.
    /// </summary>
    private void LogCoordinates(int m, int ph, int n0, int s0, string title)
    {
        LineOf((RomLineMotion)m, out RomLinePoint a, out RomLinePoint b);
        int gN = GroupOf(m, 0), gS = GroupOf(m, ph);
        var sb = new StringBuilder(260);
        sb.Append("[선실측] 좌표 — ").Append(title)
          .Append(" · 중립 ").Append(PointName[(int)a]).Append(' ').Append(points[n0].ToString("F4"))
          .Append(' ').Append(PointName[(int)b]).Append(' ').Append(points[n0 + 1].ToString("F4"));
        if (hasExtra[gN]) sb.Append(" 턱끝점 ").Append(extra[gN].ToString("F4"));
        sb.Append(" → ").Append(PhaseName[ph]).Append(' ').Append(PointName[(int)a]).Append(' ').Append(points[s0].ToString("F4"))
          .Append(' ').Append(PointName[(int)b]).Append(' ').Append(points[s0 + 1].ToString("F4"));
        if (hasExtra[gS]) sb.Append(" 턱끝점 ").Append(extra[gS].ToString("F4"));
        if (has[0] && has[1])
            sb.Append(" · 기준 오른쪽 이주점 ").Append(points[0].ToString("F4")).Append(" 왼쪽 이주점 ").Append(points[1].ToString("F4"));
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 비교 시험 — 같은 자세에서 찍은 Gl ~ Gn 선으로 굴곡·신전을 한 번 더 잰다(계산은 Tr ~ Gl 선과 같은 함수).
    /// ★부호는 보지 않는다 — 선이 달라 +가 뜻하는 방향을 검증기로 확인한 적이 없다. 크기만 쓴다.
    /// </summary>
    private void ComputeCompare(int m, int ph, int n0, int s0, string title)
    {
        int gN = GroupOf(m, 0), gS = GroupOf(m, ph);
        if (!hasExtra[gN] || !hasExtra[gS])
        {
            Debug.Log($"[선실측] 비교 — {title} 미간점~턱끝점 값 없음(턱끝점을{(hasExtra[gN] ? "측정" : "중립")} 자세에서 안 찍었다)");
            return;
        }
        int slot = m * 2 + ph - 1;
        RomLineDirect cd = RomLineMath.MeasureDirect(points[n0 + 1], extra[gN], points[s0 + 1], extra[gS]);
        cmpDirect[slot] = cd;
        if (!cd.valid) { Debug.LogWarning($"[선실측] ★비교 — {title} 미간점~턱끝점 각을 못 냈다."); return; }
        RomLineResult cr = default;
        if (frame.valid)
        {
            cr = RomLineMath.Measure(points[n0 + 1], extra[gN], points[s0 + 1], extra[gS], RomLineMath.PlaneNormal((RomLineMotion)m, frame));
            cmpResults[slot] = cr;
        }
        RomLineDirect td = RomLineMath.MeasureDirect(points[n0], points[n0 + 1], points[s0], points[s0 + 1]);
        Debug.Log($"[선실측] 비교 — {title} · 이주점~미간점 직접 {td.angle:F1}° · 미간점~턱끝점 직접 {cd.angle:F1}° (엇갈림 {cd.gap * 1000f:F0}) · " +
                  $"미간점~턱끝점 면 위 {(cr.valid ? cr.angle.ToString("F1") + "°" : "—(기준 이주점 없음)")} · " +
                  $"선 길이 미간점~턱끝점 중립 {Vector3.Distance(points[n0 + 1], extra[gN]) * 1000f:F0} → 측정 {Vector3.Distance(points[s0 + 1], extra[gS]) * 1000f:F0} · " +
                  $"이주점~미간점 중립 {Vector3.Distance(points[n0], points[n0 + 1]) * 1000f:F0} → 측정 {Vector3.Distance(points[s0], points[s0 + 1]) * 1000f:F0}");
    }

    /// <summary>그 묶음의 점이 바뀌면 거기에 기대던 값을 지운다.</summary>
    private void Invalidate(int g)
    {
        if (g == 0)
        {
            frame = default;
            for (int i = 0; i < results.Length; i++)   // 면 값만 — 직접 값은 기준과 무관하다. 회전의 면 값도 기준과 무관해 남긴다
                if (RomLineMath.NeedsReference((RomLineMotion)(i / 2))) results[i] = default;
            for (int i = 0; i < cmpResults.Length; i++) cmpResults[i] = default;
            return;
        }
        if (g >= GroupCount) return;
        int m = MotionOf(g), ph = PhaseOf(g);
        if (ph == 0) { ClearResult(m * 2); ClearResult(m * 2 + 1); }
        else ClearResult(m * 2 + ph - 1);
    }

    /// <summary>
    /// 직선 단위 취소(10-12 저녁). 찍다 만 직선이 있으면 그 첫 점만, 없으면 마지막 직선(두 점)을 통째로 지운다.
    /// 지금 탭 안에서만 거슬러 올라간다 — 앞 탭의 직선은 건드리지 않는다. 확인을 마친 동작의 직선을 지우면 그 확인도 풀린다.
    /// </summary>
    private void Undo()
    {
        if (home) { Deny("홈에서는 지울 것이 없다"); return; }
        if (freeMode) { FreeUndo(); return; }
        if (index > 0)
        {
            has[GroupStart(group)] = false;
            index = 0;
            Debug.Log($"[선실측] 취소 — {GroupTitle(group)}의 찍던 점");
        }
        else
        {
            int tab = CurTab;
            int g = confirmWait || TabComplete(tab) ? group : group - 1;   // 마지막으로 다 찍은 직선
            if (g < GroupOf(tab * 2, 0) || !HasLine(g)) { Deny("지울 직선이 없다"); return; }
            int st = GroupStart(g);
            has[st] = has[st + 1] = false;
            hasExtra[g] = false;
            Invalidate(g);
            confirmed[MotionOf(g)] = false;
            confirmWait = false;
            group = g;
            Debug.Log($"[선실측] 취소 — 직선 {GroupTitle(g)} → 그 직선부터 다시 찍는다");
        }
        noteText = "";
        Play(sndUndo);
        dirty = true;
    }

    private string LockText() => $"{GuidedTabName[CurTab]} 탭을 재는 중이다 — 다 잰 뒤에 옮긴다(그만두려면 B 길게 : 이 탭 지우기)";

    /// <summary>탭을 옮긴다 — top 0 홈 · 1 굴곡·신전 · 2 측굴 · 3 회전 · 4 자유 측정(10-14). 재는 중인 탭이 있으면 받지 않는다(false).</summary>
    private bool GoTab(int top, string why)
    {
        if (top == CurTop) return true;
        if (top < 0 || top > LastTop) return false;   // 10-16: 자유 측정을 끄면 4는 없다
        if (TabLocked) { Deny(LockText()); return false; }
        menuAsk = false;   // 11-17: 탭을 옮기면 확인 안내는 닫힌다(홈에 다시 오면 ROM부터 — GoHome)
        if (top == 0) { GoHome(why); return true; }
        home = false;
        if (top == 4) { SetFreeMode(true); return true; }
        SetFreeMode(false);
        Resume(top - 1, why);
        return true;
    }

    /// <summary>홈으로 — 메인 화면은 순서 측정의 결과표를 보여 준다. 공간의 선 · 점은 그리지 않는다(Refresh).</summary>
    private void GoHome(string why)
    {
        SetFreeMode(false);
        home = true;
        noteText = "";
        ResetMenu("홈");   // ★11-17 V0.4: 홈에 올 때마다 ROM을 고른 목록(결과 창이 뜬 뒤 포함)
        Debug.Log($"[선실측] 탭 → 홈 ({why})");
        Play(sndPress);
        dirty = true;
    }

    /// <summary>그 탭에서 이어서 잴 자리로 간다 — 아직 확인하지 않은 첫 동작의, 아직 없는 첫 직선. 세 직선이 다 있으면 확인을 기다린다.</summary>
    private void Resume(int tab, string why)
    {
        if (index > 0) has[GroupStart(group)] = false;   // 찍다 만 점(재는 중에는 탭을 못 옮기므로 실제로는 오지 않는다)
        index = 0;
        confirmWait = false;
        group = GroupOf(tab * 2 + 1, 2);                 // 다 잰 탭이면 마지막 직선에 머문다(찍기는 Mark가 막는다)
        for (int m = tab * 2; m < tab * 2 + 2; m++)
        {
            if (confirmed[m]) continue;
            int ph = 0;
            while (ph < 3 && HasLine(GroupOf(m, ph))) ph++;
            confirmWait = ph == 3;
            group = GroupOf(m, Mathf.Min(ph, 2));
            break;
        }
        noteText = "";
        Debug.Log($"[선실측] 탭 → {GuidedTabName[tab]} · {(TabComplete(tab) ? "다 잰 탭" : GroupTitle(group) + (confirmWait ? "(확인 대기)" : ""))} ({why})");
        Play(sndPress);
        dirty = true;
    }

    /// <summary>확인 뒤에 갈 탭 — 그 탭에 남은 동작이 있으면 그 탭, 없으면 그다음 덜 잰 탭. 다 쟀으면 -1.</summary>
    private int NextTab(int tab)
    {
        for (int k = 0; k < 3; k++)
        {
            int t = (tab + k) % 3;
            if (!TabComplete(t)) return t;
        }
        return -1;
    }

    /// <summary>동작 m을 확인하면 이어질 것의 이름(화면 · 로그용).</summary>
    private string NextName(int m)
    {
        int pair = m ^ 1;
        if (!confirmed[pair]) return MotionName[pair];
        for (int k = 1; k < 3; k++)
        {
            int t = (m / 2 + k) % 3;
            if (!TabComplete(t)) return GuidedTabName[t];
        }
        return "측정 끝";
    }

    /// <summary>동작 하나(세 직선)를 확인한다 — 같은 탭의 다음 동작으로, 탭을 다 쟀으면 다음 탭으로 저절로 넘어간다.</summary>
    private void Confirm(string why)
    {
        if (!confirmWait) return;
        int m = MotionOf(group), tab = m / 2;
        confirmed[m] = true;
        confirmWait = false;
        lastConfirmedTab = tab;   // 공간 각도기(11-16)
        Debug.Log($"[선실측] 확인 — {MotionName[m]} · 능동 {(results[m * 2].valid ? results[m * 2].angle.ToString("F1") : "—")}° · " +
                  $"압박 {(results[m * 2 + 1].valid ? results[m * 2 + 1].angle.ToString("F1") : "—")}° ({why})");
        Buzz(0.06f, 0.6f);
        SaveBaselineIfPractice("확인");
        SaveUpload("확인 " + MotionName[m]);
        int next = NextTab(tab);
        if (next >= 0) { Resume(next, "확인"); return; }

        // ★10-14 사용자 지시: 네 탭(굴곡 · 신전 · 측굴 · 회전)을 다 재면 홈으로 돌아간다. 그 사이에는 종전처럼 다음 탭으로 저절로 넘어간다.
        group = GroupOf(tab * 2 + 1, 2);
        LogSummary("끝");
        GoHome("측정 끝");
        Play(sndDone);
    }

    /// <summary>축 다시 잡기를 부른다 / 그만둔다. 부른 동안 스틱을 밀면 잡힌다(HandleInput).</summary>
    private void SetAxesAsk(bool on, string why)
    {
        if (axesAsk == on) return;
        axesAsk = on;
        axesAskStart = Time.unscaledTime;
        noteText = on ? "축 다시 잡기 — 헤드셋이 환자 쪽을 보게 두고 스틱을 미세요 (스틱을 다시 누르면 그만둠)" : "축 다시 잡기를 그만뒀다";
        Debug.Log($"[선실측] 축 다시 잡기 {(on ? "부름" : "그만둠")} ({why})");
        Play(on ? sndPress : sndUndo);
        dirty = true;
    }

    // ── 자유 측정(10-07) ────────────────────────────────────────────
    //   ★10-13 사용자 지시: 동작(굴곡·신전 / 측굴 / 회전)을 가르지 않는다 — 면에 내리지 않고 <b>찍은 점들이 이루는 각 그대로</b>(3차원)를 낸다.
    //     3점 측정 = 세 점을 이어 찍는다. 가운데 점(2)에서 선 1-2와 선 2-3이 벌어진 각. 처음 찍은 묶음이 기준이고,
    //                그 뒤 묶음은 기준과 견준 값 셋을 함께 낸다: 선 1-2가 돈 각 · 선 2-3이 돈 각 · 벌어진 각의 차(지금 − 기준).
    //     2선 측정(10-14까지 «4점») = 두 점씩 선 둘(1-2 · 3-4). 두 선의 방향이 이루는 각.
    //     연속 측정 = 점을 이어 가며 찍는다. 셋째 점부터, 앞 꼭짓점에서 꺾인 각(앞 선과 새 선이 그 점에서 벌어진 각)이 하나씩 나온다.
    private static readonly string[] FreeTabName = { "3점", "2선", "연속" };   // «자유 측정»은 앞에 따로 붙는다(10-13)
    private static readonly int[] FreeSetSize = { 3, 4, 0 };   // 한 묶음의 점 수. 0 = 묶음 없이 이어서
    private static readonly string[] FreePlaneName = { "옆면", "앞면", "바닥면" };   // 순서 측정의 모니터 · CSV가 쓴다(자유 측정은 면을 쓰지 않는다)

    private List<Vector3> FreePts => freePts[freeTab];
    /// <summary>자유 측정(3점 · 2선)에서 묶음을 다 찍고 확인(트리거)을 기다리는 중인가. 연속 측정은 확인 없이 바로 기록한다.</summary>
    private bool FreeWait => FreeSetSize[freeTab] > 0 && FreeCount() > freeConfirmed[freeTab];


    private void SetFreeMode(bool on)
    {
        if (freeMode == on) return;
        freeMode = on;
        noteText = "";
        view.SetMode(on);
        if (on)
        {
            // 6동작의 공간 표시는 끈다(찍어 둔 값은 그대로 — 돌아오면 다시 그린다).
            for (int i = 0; i < PointCount; i++) view.SetPoint(i, false, points[i]);
            for (int ph = 0; ph < 3; ph++) view.SetExtraPoint(ph, false, Vector3.zero);
            view.HideMeasure();
            view.HideCompare();
        }
        Debug.Log($"[선실측] 모드 → {(on ? $"자유 측정 · {FreeTabName[freeTab]}(찍어 둔 점 {freePts[0].Count}/{freePts[1].Count}/{freePts[2].Count}개 · 3축 표시 {(axesOn ? "켬" : "끔")})" : "6동작")}");
        Play(sndPress);
        dirty = true;
    }

    private void SetFreeTab(int tab)
    {
        if (tab == freeTab) return;
        freeTab = tab;
        noteText = "";
        Debug.Log($"[선실측] 자유 — 방식 → {FreeTabName[tab]}(찍어 둔 점 {FreePts.Count}개)");
        Play(sndPress);
        dirty = true;
    }

    /// <summary>
    /// ★10-13 사용자 지시: 메인 화면과 3축을 <b>정해 둔 자리</b>에 세운다 — 추적 원점에서 정면(원점의 +Z)을 보는 쪽이다.
    ///   환자는 측정자를 마주 본다고 놓으므로 환자 정면은 원점 쪽(−Z)이다. 헤드셋이 어디를 보고 있었는지는 쓰지 않는다
    ///   (헤드셋을 쓰지 않고 내려놓고 잴 때도 같은 자리여야 한다). 자리를 맞추는 것은 메타 퀘스트의 «위치 재설정»이다 —
    ///   씬의 추적 원점이 바닥 높이(FloorLevel)이고 OVRManager.AllowRecenter가 기본(true)이라 재설정하면 원점이 사용자 쪽으로 온다(코드로 읽은 것 · 기기 미검증).
    ///   그 뒤 «축 다시 잡기»(스틱 누르고 밀기)는 3축만 돌린다 — 메인 화면은 그대로 둔다.
    /// </summary>
    private void PlaceFixed()
    {
        Vector3 origin = trackingSpace != null ? trackingSpace.position : Vector3.zero;
        Vector3 fwd = trackingSpace != null ? Vector3.ProjectOnPlane(trackingSpace.forward, Vector3.up) : Vector3.forward;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        view.PlacePanel(origin + Vector3.up * FixedEyeHeight, fwd, screenDistance, screenWidth, screenRise);
        view.FollowGuide(guideDistance, guideDrop, guideRise);
        Debug.Log($"[선실측] 메인 화면을 세웠다(고정 자리) — 추적 원점 {origin:F2} · 정면 {fwd:F2} · 화면 {view.Panel.position:F2} · 거리 {screenDistance:F1}m · 폭 {screenWidth:F1}m · 기준 눈높이 {FixedEyeHeight:F1}m 위 {screenRise:F2}m · 지금 눈 {eye.position:F2}");
        SetAxes(fwd, origin + fwd * FixedAxesAhead, "처음", "고정 자리(추적 원점의 정면)");
    }

    /// <summary>
    /// 고정 3축을 지금 헤드셋이 보는 방향으로 다시 잡는다(«축 다시 잡기» — 필요할 때만 쓴다. 처음 자리는 PlaceFixed가 정한다).
    /// ★헤드셋을 쓰지 않고 정해 둔 자리에 놓아 기준으로 삼을 때도 같은 함수다.
    /// ★10-12 저녁 사용자 지시: 다시 잡을 때도 헤드셋 방향이다(10-08의 «컨트롤러가 가리키는 방향»은 뺐다 — 그 함수는 git 231cb83에 있다).
    ///   부르는 길은 «스틱을 누른 뒤 밀기»와 모니터의 버튼 둘이다.
    /// </summary>
    private void CaptureAxes(string why, bool tell)
    {
        // 지움: 헤드셋의 숙임·기울임 → 세 축은 늘 수평·수직이다(위아래는 중력이 맡는다).
        Vector3 look = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
        if (look.sqrMagnitude < 0.01f) look = Vector3.forward;   // 바로 위·아래를 보고 있었다
        look.Normalize();
        SetAxes(look, eye.position + look * 1.0f, why, "헤드셋이 보던 방향");
        if (!tell) return;
        noteText = "축을 다시 잡았다 — 헤드셋이 보는 쪽이 환자";   // 메인 화면은 옮기지 않는다(10-13)
        Play(sndPress);
    }

    /// <summary>toward = 측정자 쪽에서 환자를 향하는 수평 방향. 환자는 그 반대를 본다고 놓는다.</summary>
    private void SetAxes(Vector3 toward, Vector3 crossAt, string why, string how)
    {
        axes = RomLineMath.FrameFacing(-toward);   // 검증기가 «양쪽 Tr로 만든 기준»과 같은 방향·부호인지 본다
        frame = axes;                              // 순서대로 재는 흐름의 «면»도 이 축으로 세운다(양쪽 Tr을 찍지 않는다, 10-08)
        for (int m = 0; m < 6; m++) RecomputeMotion(m);   // 축을 다시 잡으면 찍어 둔 선의 면 값이 바뀐다
        dirty = true;
        axesOrigin = crossAt;
        axesOrigin.y = trackingSpace != null ? trackingSpace.position.y : 0f;   // 바닥(추적 원점이 바닥 높이일 때)
        Debug.Log($"[선실측] 자유 — 3축을 잡았다({why} · {how}) · 환자 정면 {axes.forward:F3} · 환자 오른쪽 {axes.right:F3} · 십자 자리 {axesOrigin:F2} · 눈 {eye.position:F2}");
    }

    /// <summary>가운데 점 b에서 선 b-a와 선 b-c가 벌어진 각(도, 0 ~ 180). 한 줄로 곧게 찍으면 180이다.</summary>
    private static float Corner(Vector3 a, Vector3 b, Vector3 c) => Vector3.Angle(a - b, c - b);

    /// <summary>지금 방식에서 나온 측정의 수 — 3점 · 2선은 다 찍은 묶음, 연속은 꼭짓점(셋째 점부터 하나씩).</summary>
    private int FreeCount()
    {
        int n = FreePts.Count, size = FreeSetSize[freeTab];
        return size > 0 ? n / size : Mathf.Max(0, n - 2);
    }

    /// <summary>
    /// k번째 측정의 값. angle = 그 측정의 각 · at = 각 글자를 띄울 자리.
    /// 3점 측정의 둘째 묶음부터는 기준(첫 묶음)과 견준 값도 준다(hasCompare): turn12 · turn23 = 선 1-2 · 선 2-3이 기준의 같은 선에서 돈 각, diff = 벌어진 각의 차(지금 − 기준).
    /// ★화면 · 로그 · CSV · 모니터가 전부 이 함수 하나를 탄다.
    /// </summary>
    private void FreeMeasure(int k, out float angle, out Vector3 at, out bool hasCompare, out float turn12, out float turn23, out float diff)
    {
        List<Vector3> p = FreePts;
        hasCompare = false;
        turn12 = turn23 = diff = 0f;
        if (freeTab == 0)
        {
            int o = k * 3;
            angle = Corner(p[o], p[o + 1], p[o + 2]);
            at = p[o + 1];
            if (k == 0) return;
            hasCompare = true;
            turn12 = Vector3.Angle(p[1] - p[0], p[o + 1] - p[o]);
            turn23 = Vector3.Angle(p[2] - p[1], p[o + 2] - p[o + 1]);
            diff = angle - Corner(p[0], p[1], p[2]);
        }
        else if (freeTab == 1)
        {
            int o = k * 4;
            angle = Vector3.Angle(p[o + 1] - p[o], p[o + 3] - p[o + 2]);
            at = (p[o] + p[o + 1] + p[o + 2] + p[o + 3]) * 0.25f;
        }
        else
        {
            angle = Corner(p[k], p[k + 1], p[k + 2]);
            at = p[k + 1];
        }
    }

    private void FreeMark(Vector3 pos, float shake)
    {
        if (FreeWait) { if (Time.unscaledTime - waitSince >= ConfirmGuard) FreeConfirm(); return; }
        List<Vector3> pts = FreePts;
        int size = FreeSetSize[freeTab], before = FreeCount();
        bool joined = pts.Count > 0 && (size == 0 || pts.Count % size != 0);   // 앞 점과 같은 묶음이다(선으로 이어지거나 한 각을 이룬다)
        if (joined && Vector3.Distance(pts[pts.Count - 1], pos) < 0.005f)
        {
            Deny("앞 점과 같은 자리에 찍혔다 — 다시 찍는다");
            return;
        }
        pts.Add(pos);
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 찍음 · 점 {pts.Count} · 자리 {pos:F4} · 당기는 동안 끝점 이동 {shake * 1000f:F1}mm" + PoseSuffix());
        if (FreeCount() > before)
        {
            LogFree(before);
            if (size > 0) waitSince = Time.unscaledTime;   // 3점 · 2선 — 확인(트리거)을 받아야 기록한다
            else CsvAdd("측정", before);
        }
        noteText = "";
        Play(sndMark);
        Buzz(0.06f, 0.6f);
        dirty = true;
    }

    /// <summary>자유 측정(3점 · 2선)의 확인 — 다 찍은 묶음을 기록한다(화면의 결과 줄 · CSV).</summary>
    private void FreeConfirm()
    {
        int k = freeConfirmed[freeTab];
        freeConfirmed[freeTab] = FreeCount();
        for (int i = k; i < freeConfirmed[freeTab]; i++) CsvAdd("측정", i);
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 확인(트리거) · {freeConfirmed[freeTab]}번째 측정을 기록했다");
        noteText = "";
        Play(sndDone);
        Buzz(0.06f, 0.6f);
        dirty = true;
    }

    /// <summary>취소(B) — 찍다 만 묶음이 있으면 마지막 점 하나, 없으면 마지막 묶음을 통째로. 연속 측정은 늘 마지막 점 하나다.</summary>
    private void FreeUndo()
    {
        List<Vector3> pts = FreePts;
        if (pts.Count == 0) { Deny("지울 점이 없다"); return; }
        int size = FreeSetSize[freeTab], count = FreeCount();
        if (size > 0 && pts.Count % size == 0)
        {
            if (freeConfirmed[freeTab] >= count) { CsvAdd("취소", count - 1); freeConfirmed[freeTab] = count - 1; }   // 기록한 묶음만 «취소»를 남긴다
            pts.RemoveRange(pts.Count - size, size);
        }
        else
        {
            if (size == 0 && count > 0) CsvAdd("취소", count - 1);
            pts.RemoveAt(pts.Count - 1);
        }
        noteText = "";
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 취소 → 남은 점 {pts.Count}개");
        Play(sndUndo);
        dirty = true;
    }

    private void FreeClear()
    {
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 이 방식의 점을 지웠다(점 {FreePts.Count}개)");
        if (FreePts.Count > 0) CsvAdd("지움", -1);
        FreePts.Clear();
        freeConfirmed[freeTab] = 0;
        noteText = "";
        Play(sndUndo);
        dirty = true;
    }

    /// <summary>
    /// 3점 측정 — k번째 측정의 각에서 <b>바로 앞 측정</b>의 각을 뺀 값(10-14 사용자 지시: 기준(첫 묶음)과의 차와 함께 보인다).
    /// 셋째 묶음부터만 낸다(둘째 묶음은 바로 앞이 곧 기준이라 같은 값이다).
    /// </summary>
    private bool FreePrevDiff(int k, out float prev)
    {
        prev = 0f;
        if (freeTab != 0 || k < 2) return false;
        List<Vector3> p = FreePts;
        int o = k * 3, q = (k - 1) * 3;
        prev = Corner(p[o], p[o + 1], p[o + 2]) - Corner(p[q], p[q + 1], p[q + 2]);
        return true;
    }

    private void LogFree(int k)
    {
        FreeMeasure(k, out float angle, out _, out bool cmp, out float t12, out float t23, out float diff);
        var sb = new StringBuilder(200);
        sb.Append("[선실측] 자유(").Append(FreeTabName[freeTab]).Append(") — ").Append(k + 1).Append(" · 각 ").Append(angle.ToString("F1")).Append('°');
        if (freeTab == 0 && k == 0) sb.Append(" · 기준");
        if (cmp)
            sb.Append(" ‖ 기준(1)과 견줌 — 선 1-2 ").Append(t12.ToString("F1")).Append("° · 선 2-3 ").Append(t23.ToString("F1"))
              .Append("° · 각의 차 ").Append(diff.ToString("+0.0;-0.0")).Append('°');
        if (FreePrevDiff(k, out float prev)) sb.Append(" ‖ 이전(").Append(k).Append(")과의 차 ").Append(prev.ToString("+0.0;-0.0")).Append('°');
        Debug.Log(sb.ToString());
    }

    private void RefreshFree()
    {
        dirty = false;
        List<Vector3> pts = FreePts;
        int size = FreeSetSize[freeTab], count = FreeCount();

        string note = noteText;   // 아랫줄은 알림이 있을 때만(10-13)
        // ★10-13 사용자 지시: «점 1/3»은 찍은 수인지 찍을 차례인지 헷갈린다 → «1번째 점을 찍으세요»로 적는다.
        //   3점 · 2선은 묶음을 다 찍으면 안내에 각을 띄우고 확인(트리거)을 기다린다 — 확인해야 화면의 결과 줄에 오른다.
        bool wait = FreeWait;
        int shown = size > 0 ? Mathf.Min(count, freeConfirmed[freeTab]) : count;   // 화면의 결과 줄에 올릴 측정의 수
        string step = (size > 0 ? pts.Count % size + 1 : pts.Count + 1) + "번째 점";   // 컨트롤러 끝의 이름표 · 모니터용
        string instruction, guideLine, guideNote = note;
        if (wait)
        {
            FreeMeasure(count - 1, out float wa, out _, out bool wc, out _, out _, out float wd);
            // 기준 = 첫 묶음과의 각의 차 · 이전 = 바로 앞 묶음과의 각의 차(10-14 · 셋째 묶음부터)
            string v = "각 " + wa.ToString("F1") + "°", vd = wc ? "기준 " + wd.ToString("+0.0;-0.0") + "°" : "";
            if (FreePrevDiff(count - 1, out float wp)) vd += " · 이전 " + wp.ToString("+0.0;-0.0") + "°";
            instruction = FreeTabName[freeTab] + " — " + v + (wc ? " · 차 " + vd : "") + " · 트리거 : 확인";
            guideLine = FreeTabName[freeTab] + "\n<color=#73E68C>" + v + "</color>" + (wc ? "\n" + vd : "");   // 안내는 폭이 좁다 — 줄을 나눈다
            if (guideNote.Length == 0) guideNote = "트리거 : 기록";
        }
        else
        {
            instruction = FreeTabName[freeTab] + " — " + step + "을 찍으세요";
            // ★10-13 오후 사용자 지시: 안내 글은 최대한 줄인다 — 찍을 점만큼 빈 동그라미를 놓고 찍을 때마다 하나씩 채운다. 연속은 묶음이 없어 찍은 수만 적는다.
            //   ★10-14 사용자 지시: 2선 측정은 선마다 동그라미를 띄어 나눈다(«●● ●○») — 두 점씩 선 둘이라는 것이 보이게.
            string dots = freeTab == 1 ? Dots(Mathf.Min(pts.Count % size, 2), 2) + " " + Dots(Mathf.Max(0, pts.Count % size - 2), 2)
                        : size > 0 ? Dots(pts.Count % size, size) : "● " + pts.Count;
            guideLine = FreeTabName[freeTab] + "  <color=#FFF233>" + dots + "</color>";
        }
        view.SetTip(wait ? "" : step);
        view.SetGuide(guideLine, guideNote);
        view.SetFreeTab(freeTab);
        RefreshTabs();

        freeView.SetShape(pts, freeTab);
        if (freeTab == 0) freeView.SetHeads("번호", "각", "선 1-2 / 선 2-3이 돈 각", "차(기준 / 이전)");
        else if (freeTab == 1) freeView.SetHeads("번호", "두 선의 각", "", "");
        else freeView.SetHeads("꼭짓점", "꺾인 각", "", "");
        freeView.ShowAxes(axesOn, axesOrigin, axes);

        // 판 — 가장 최근 측정들(한 줄에 하나)과 공간의 각 글자. 같은 값을 FreeMeasure 하나에서 받는다.
        freeAngleAt.Clear();
        freeAngleText.Clear();
        int firstRow = Mathf.Max(0, shown - RomLineFreeView.Rows);
        for (int k = 0; k < count; k++)
        {
            FreeMeasure(k, out float angle, out Vector3 at, out bool cmp, out float t12, out float t23, out float diff);
            string a = angle.ToString("F1") + "°";
            freeAngleAt.Add(at);
            freeAngleText.Add(a);
            int row = k - firstRow;
            if (row < 0 || k >= shown) continue;   // 확인하지 않은 묶음은 공간의 글자에만 뜬다
            bool isRef = freeTab == 0 && k == 0;
            string name = freeTab == 2 ? "점 " + (k + 2) : (k + 1).ToString();
            if (isRef) freeView.SetRow(row, name, a, "기준", "", true);
            else if (cmp)
            {
                string d = diff.ToString("+0.0;-0.0") + "°";
                if (FreePrevDiff(k, out float pd)) d += " / " + pd.ToString("+0.0;-0.0") + "°";
                freeView.SetRow(row, name, a, t12.ToString("F1") + "° / " + t23.ToString("F1") + "°", d, false);
            }
            else freeView.SetRow(row, name, a, "", "", false);
        }
        for (int row = Mathf.Max(0, shown - firstRow); row < RomLineFreeView.Rows; row++) freeView.SetRow(row, "", "", "", "", false);
        freeView.SetAngles(freeAngleAt, freeAngleText);

        // 모니터로 내보낼 상태 — 선마다 한 줄. 그 선에서 각이 하나 완성되면(3점의 둘째 선 · 2선의 둘째 선 · 연속의 둘째 선부터) 그 값을 함께 싣는다.
        StringBuilder js = monitorSb;
        js.Length = 0;
        js.Append("\"mode\":\"free\",\"top\":4,\"tab\":").Append(freeTab);
        AppendTabsJson(js).Append(",\"instruction\":");
        JsonStr(js, instruction).Append(",\"note\":");
        JsonStr(js, note);
        AppendPopupJson(js).Append(",\"axesOn\":").Append(axesOn ? "true" : "false").Append(",\"planeOn\":true,\"plane\":\"\",\"lines\":[");
        int seg = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (!RomLineFreeView.Linked(freeTab, i)) continue;
            int k = freeTab == 0 ? (i % 3 == 1 ? i / 3 : -1) : freeTab == 1 ? (i % 4 == 2 ? i / 4 : -1) : i - 1;   // 이 선에서 완성되는 측정(없으면 -1)
            string plane = "", warn = "";
            float angle = 0f;
            if (k >= 0 && k < count)
            {
                FreeMeasure(k, out angle, out _, out bool cmp, out float t12, out float t23, out float diff);
                plane = angle.ToString("F1") + "°";
                if (cmp) warn = "선 1-2 " + t12.ToString("F1") + "° · 선 2-3 " + t23.ToString("F1") + "° · 각의 차 " + diff.ToString("+0.0;-0.0") + "°";
                if (FreePrevDiff(k, out float pd)) warn += " · 이전과의 차 " + pd.ToString("+0.0;-0.0") + "°";
            }
            if (seg++ > 0) js.Append(',');
            js.Append("{\"n\":").Append(seg).Append(",\"ref\":").Append(freeTab == 0 && i < 3 ? "true" : "false").Append(",\"refOf\":1")
              .Append(",\"len\":").Append((Vector3.Distance(pts[i], pts[i + 1]) * 1000f).ToString("F0", CultureInfo.InvariantCulture)).Append(",\"plane\":");
            JsonStr(js, plane).Append(",\"direct\":").Append(angle.ToString("F1", CultureInfo.InvariantCulture)).Append(",\"warn\":");
            JsonStr(js, warn).Append(",\"a\":");
            JsonVec(js, InAxes(pts[i])).Append(",\"b\":");
            JsonVec(js, InAxes(pts[i + 1])).Append('}');
        }
        js.Append("],\"pending\":");
        bool loose = pts.Count > 0 && (pts.Count < 2 || !RomLineFreeView.Linked(freeTab, pts.Count - 2));   // 마지막 점이 아직 어느 선에도 안 들었다
        if (loose) JsonVec(js, InAxes(pts[pts.Count - 1])); else js.Append("null");
        monitorBody = js.ToString();
    }


    // ── 모니터(10-07) ───────────────────────────────────────────────
    /// <summary>월드 좌표를 고정 3축의 좌표로 — [환자 오른쪽, 위(바닥에서), 환자 앞](m). 모니터의 세 방향 그림이 이 좌표로 그린다.</summary>
    private Vector3 InAxes(Vector3 world)
    {
        Vector3 d = world - axesOrigin;
        return new Vector3(Vector3.Dot(d, axes.right), d.y, Vector3.Dot(d, axes.forward));
    }

    private static StringBuilder JsonVec(StringBuilder sb, Vector3 v)
    {
        return sb.Append('[').Append(v.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("F4", CultureInfo.InvariantCulture))
                 .Append(',').Append(v.z.ToString("F4", CultureInfo.InvariantCulture)).Append(']');
    }

    private static StringBuilder JsonStr(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");   // ★10-20: 관제 화면이 안내(guide)를 헤드셋처럼 줄을 나눠 그린다 — 종전엔 빈칸으로 바꿔 한 줄이 됐다
            else if (c < ' ') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"');
    }

    /// <summary>
    /// 모니터 화면이 보낸 명령을 처리하고, 보는 화면이 있을 때만 0.1초에 한 번 상태를 건넨다.
    /// 상태 = «바뀔 때만 만드는 부분»(monitorBody — Refresh가 만든다) + «늘 바뀌는 부분»(컨트롤러 끝·추적 여부).
    /// </summary>
    // ── 배터리(10-21 사용자 지시: 벽면UI · 컨트롤러UI에 잔량을 보인다 · 잔량별 색) ── 2초마다 읽고, 바뀔 때만 글자를 만든다.
    private float batteryNext;
    private int batteryShown = int.MinValue;
    private bool batteryChargingShown;

    private void PollBattery()
    {
        batteryNext = Time.unscaledTime + 2f;
        int pct = RomLineView.BatteryPercent();
        bool charging = RomLineView.BatteryCharging();
        if (pct == batteryShown && charging == batteryChargingShown) return;
        if (RomLineView.BatteryBand(pct) != RomLineView.BatteryBand(batteryShown) || charging != batteryChargingShown)
            Debug.Log($"[선실측] 배터리 — {(pct < 0 ? "읽을 수 없음(에디터 등)" : pct + "%")} · {(charging ? "충전 중" : "충전 안 함")}");
        batteryShown = pct;
        batteryChargingShown = charging;
        view.SetBattery(pct, charging);
    }

    private const string MonitorTitle = "XR-ROM경추";   // 11-13 사용자 «경추ROM진단 → XR-ROM경추»   // 관제 화면 타이틀 — 이 씬이 돌리는 실습
    private string monitorUser = "";                     // 관제 화면 녹화 파일 이름의 사용자명(10-22) — 기기에 남은 로그인 정보(LoginStateStore), 없으면 빈 글자

    private void PumpMonitor()
    {
        if (monitor.Port == 0) return;
        while (monitor.TryDequeue(out string command)) RunMonitorCommand(command);
        if (!monitor.Watched || Time.unscaledTime < monitorNext) return;   // 보는 화면이 없으면 글자를 만들지 않는다
        monitorNext = Time.unscaledTime + 0.1f;

        StringBuilder js = monitorLive;
        js.Length = 0;
        js.Append("{\"page\":\"rom\",\"t\":").Append(Time.unscaledTime.ToString("F1", CultureInfo.InvariantCulture))   // 11-13 page — 추나 관제 페이지가 열려 있으면 새로고침시킨다
          .Append(",\"tracked\":").Append(tipValid ? "true" : "false").Append(",\"calib\":").Append(calibrating ? "true" : "false")
          // 10-21 관제 화면 아래 줄: 실행 중인 실습 이름(타이틀) · 좌우 컨트롤러 연결 · 찍는 쪽
          .Append(",\"title\":\"").Append(monitorTitle).Append('"')
          .Append(",\"user\":"); JsonStr(js, monitorUser);   // 10-22: 녹화 파일 이름(실습명_사용자명_날짜) — 없으면 브라우저가 뺀다
        js
          .Append(",\"ctrlL\":").Append(OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) ? "true" : "false")
          .Append(",\"ctrlR\":").Append(OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) ? "true" : "false")
          .Append(",\"active\":\"").Append(UsingLeft ? 'L' : 'R').Append('"')
          .Append(",\"tip\":");
        if (axes.valid) JsonVec(js, InAxes(tipPos)); else js.Append("null");
        js.Append(",\"eye\":");
        if (axes.valid && eye != null) JsonVec(js, InAxes(eye.position)); else js.Append("null");
        js.Append(",\"tipOffset\":[").Append((tipOffset.x * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.y * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append((tipOffset.z * 1000f).ToString("F1", CultureInfo.InvariantCulture))
          .Append("],\"csv\":").Append(csvRows.Count).Append(",\"url\":");
        JsonStr(js, monitor.Url);
        if (monitorBody.Length > 0) js.Append(',').Append(monitorBody);
        js.Append('}');
        monitor.SetState(js.ToString());
    }

    private void RunMonitorCommand(string command)
    {
        Debug.Log($"[선실측] 모니터 명령 — {command}");
        switch (command)
        {
            case "tab0": case "tab1": case "tab2":
                if (!freeMode && !GoTab(4, "모니터")) break;   // 재는 중인 탭이 있으면 못 간다
                SetFreeTab(command[3] - '0');
                break;
            case "top0": case "top1": case "top2": case "top3": case "top4": GoTab(command[3] - '0', "모니터"); break;   // 10-14: 0 홈 · 1~3 순서 측정 · 4 자유 측정
            case "undo": Undo(); break;
            case "confirm": if (!freeMode && !home && confirmWait) Confirm("모니터"); else Deny("확인할 것이 없다 — 동작 하나의 세 직선을 다 찍은 뒤에 누른다"); break;
            case "clear": if (home) Deny("홈에서는 지울 탭이 없다"); else if (freeMode) FreeClear(); else ResetTab(CurTab, "모니터"); break;
            case "resetAll": ResetAll("모니터"); break;
            case "rebase": Deny("기준 바꾸기는 없앴다 — 3점 측정은 처음 찍은 묶음이 기준이다"); break;
            case "axes":   // 모니터의 버튼은 한 번에 잡는다(화면에서 따로 되묻는다)
                CaptureAxes("모니터", true);
                break;
            case "calib": if (calibrating) EndCalibration("모니터"); else BeginCalibration(); break;
            case "exit": Exit(); break;
            case "cam0": case "cam1": case "cam2": if (mirror != null) mirror.RequestCamResolution(command[3] - '0'); break;   // ★10-20: 관제 화면의 해상도 버튼
            default: Debug.LogWarning($"[선실측] ★모니터 명령 «{command}»을 모른다"); break;
        }
    }

    // ── 기록(CSV) ───────────────────────────────────────────────────
    private const string CsvHeader = "시각,경과(초),사건,동작,선,기준선,길이(mm),면,방향,면 각(도),직접 각(도),알림,움직인 모양,첫 점 x,첫 점 y,첫 점 z,둘째 점 x,둘째 점 y,둘째 점 z,환자 정면 x,환자 정면 z,끝점 x(mm),끝점 y(mm),끝점 z(mm)";

    /// <summary>
    /// 한 줄을 기록한다 — 기기의 파일과 모니터의 «CSV 받기» 양쪽에. line이 0 이상이면 그 선의 값을 함께 적는다.
    /// ★지운 선도 줄은 남는다(«취소»·«지움» 줄이 뒤따른다) — 무슨 일이 있었는지를 남기는 기록이다.
    /// </summary>
    private void CsvAdd(string what, int line)
    {
        StringBuilder sb = monitorLive;   // 잠깐 빌려 쓴다(같은 주 스레드, PumpMonitor가 쓸 때는 처음에 비운다)
        sb.Length = 0;
        sb.Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',').Append(Time.unscaledTime.ToString("F1", CultureInfo.InvariantCulture))
          .Append(',').Append(what).Append(',').Append(FreeTabName[freeTab]).Append(',');
        if (line >= 0 && line < FreeCount())
        {
            // 열은 순서 측정과 같은 자리를 쓴다: 선 = 측정 번호 · 기준선 = 견준 기준(3점의 둘째 묶음부터 1) · 길이 = 그 측정에 든 선들의 길이 합(mm) ·
            //   직접 각 = 그 측정의 각 · 알림 = 기준과 견준 값. 면 · 방향 · 면 각은 비운다(면을 쓰지 않는다). 점은 그 측정의 첫 점과 끝 점이다(사이 점은 로그 «찍음»에 있다).
            List<Vector3> p = FreePts;
            int size = FreeSetSize[freeTab], first = size > 0 ? line * size : line, last = size > 0 ? first + size - 1 : line + 2;
            float len = 0f;
            for (int i = first; i < last; i++)
                if (RomLineFreeView.Linked(freeTab, i)) len += Vector3.Distance(p[i], p[i + 1]);
            FreeMeasure(line, out float angle, out _, out bool cmp, out float t12, out float t23, out float diff);
            sb.Append(line + 1).Append(',').Append(cmp ? "1" : "").Append(',').Append((len * 1000f).ToString("F0", CultureInfo.InvariantCulture)).Append(",,,,")
              .Append(angle.ToString("F1", CultureInfo.InvariantCulture)).Append(',');
            if (cmp)
                sb.Append("선 1-2 ").Append(t12.ToString("F1", CultureInfo.InvariantCulture)).Append(" · 선 2-3 ").Append(t23.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(" · 각의 차 ").Append(diff.ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
            if (FreePrevDiff(line, out float pd))
                sb.Append(" · 이전과의 차 ").Append(pd.ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
            sb.Append(",,");
            for (int k = 0; k < 2; k++)
            {
                Vector3 v = p[k == 0 ? first : last];
                sb.Append(v.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.z.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            }
        }
        else sb.Append(",,,,,,,,,,,,,,,");
        sb.Append(axes.forward.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(axes.forward.z.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.x * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append((tipOffset.y * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.z * 1000f).ToString("F1", CultureInfo.InvariantCulture));
        string rowText = sb.ToString();
        csvRows.Add(rowText);
        // ★10-22 사용자 지시: 관제 «측정값 저장»은 결과표 값만 준다(ResultCsv) — 이 상세 기록은 기기 파일(개발용)에만 남긴다.
        if (csvPath == null) return;
        try { System.IO.File.AppendAllText(csvPath, rowText + "\n", new UTF8Encoding(false)); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[선실측] ★기록 파일에 못 적었다({e.Message}) — 모니터의 «CSV 받기»로는 받을 수 있다. 파일 기록을 멈춘다.");
            csvPath = null;
        }
    }

    /// <summary>탭 하나(두 동작 · 여섯 직선)를 지우고 그 탭의 처음부터 다시 잰다 — B/Y 길게(10-12 저녁).</summary>
    private void ResetTab(int tab, string why)
    {
        for (int m = tab * 2; m < tab * 2 + 2; m++)
        {
            for (int ph = 0; ph < 3; ph++)
            {
                int g = GroupOf(m, ph), st = GroupStart(g);
                has[st] = has[st + 1] = false;
                hasExtra[g] = false;
            }
            ClearResult(m * 2);
            ClearResult(m * 2 + 1);
            confirmed[m] = false;
        }
        group = GroupOf(tab * 2, 0);
        index = 0;
        confirmWait = false;
        noteText = "";
        if (lastConfirmedTab == tab) lastConfirmedTab = -1;   // 공간 각도기(11-16)
        if (resultOn) resultOffWhy = "이 탭 지우기 · " + GuidedTabName[tab];   // 결과 창(11-17)
        Debug.Log($"[선실측] 이 탭 지우기 — {GuidedTabName[tab]} ({why})");
        SaveBaselineIfPractice("탭 지우기");
        SaveUpload("탭 지우기");
        Play(sndUndo);
        dirty = true;
    }

    /// <summary>전체 측정 결과를 지운다 — 순서 측정의 18직선과 자유 측정의 선 전부. 기록 파일(CSV)의 줄은 남는다. 굴곡 · 중립으로 돌아간다.</summary>
    private void ResetAll(string why)
    {
        for (int i = 0; i < PointCount; i++) has[i] = false;
        for (int i = 0; i < GroupCount; i++) hasExtra[i] = false;
        for (int i = 0; i < 6; i++) confirmed[i] = false;
        lastConfirmedTab = -1;   // 공간 각도기(11-16)
        if (resultOn) resultOffWhy = "측정값 초기화 · " + why;   // 결과 창(11-17)
        frame = axes;   // 고정 3축은 그대로다
        for (int i = 0; i < 12; i++) ClearResult(i);
        int keep = freeTab, freePoints = 0;
        for (int t = 0; t < 3; t++)
        {
            freeTab = t;                       // CsvAdd가 지금 방식의 이름으로 적는다
            freePoints += freePts[t].Count;
            if (freePts[t].Count > 0) CsvAdd("지움", -1);
            freePts[t].Clear();
            freeConfirmed[t] = 0;
        }
        freeTab = keep;
        ResetMenu("리셋");
        confirmWait = false;
        SetFreeMode(false);
        home = true;    // 리셋은 홈에서 한다 — 홈에 머문다(10-14)
        group = 1;      // 굴곡 · 중립부터(묶음 0 = 기준은 쓰지 않는다)
        index = 0;
        noteText = "";
        Debug.Log($"[선실측] 리셋 — 전체 측정 결과를 지웠다(자유 측정의 점 {freePoints}개 포함) ({why})");
        RomLineDevLog.Save("측정값 초기화", tipOffset * 1000f);
        Play(sndUndo);
        dirty = true;
    }

    private void ClearResult(int i)
    {
        results[i] = default;
        direct[i] = default;
        if (i < cmpResults.Length) { cmpResults[i] = default; cmpDirect[i] = default; }
    }

    private void Exit()
    {
        LogSummary("나가기");
        Debug.Log("[선실측] 나가기 → " + lobbySceneName);
        SceneLoader.LoadScene(lobbySceneName);
    }

    private void Deny(string why)
    {
        noteText = why;
        Debug.Log("[선실측] 받지 않음 — " + why);
        Play(sndDeny);
        dirty = true;
    }

    // ── 표시 갱신(상태가 바뀔 때만) ──────────────────────────────────
    /// <summary>찍은 점은 ●, 남은 점은 ○ — 안내의 진행 표시(10-13 오후). 상태가 바뀔 때만 만든다.</summary>
    private static string Dots(int filled, int total) => new string('●', Mathf.Clamp(filled, 0, total)) + new string('○', Mathf.Max(0, total - filled));

    // ★10-14 사용자 지시: 본문(주 정보)과 조작 설명(서브 정보)이 <b>함께</b> 있을 때만 조작 설명을 작게 쓴다(리셋 확인 · 보정 중). 홈처럼 전부 조작 설명이면 줄이지 않는다.
    //   안내 아랫줄(알림)은 원래 작아(0.085) 그대로 둔다.
    private const string SubInfo = "<size=72%>";

    /// <summary>안내에 넣는 기록 한 줄 — «굴곡  37.2° / 41.8° / +4.6°»(능동 / 압박 / 차이, 색은 결과표와 같다). 다 잰 탭에서만 부른다(상태가 바뀔 때만).</summary>
    private string RecordLine(int m)
    {
        string a = results[m * 2].valid ? results[m * 2].angle.ToString("F1") + "°" : "—";
        string p = results[m * 2 + 1].valid ? results[m * 2 + 1].angle.ToString("F1") + "°" : "—";
        return MotionName[m] + "  <color=#FF6B05>" + a + "</color> / <color=#F2994D>" + p + "</color> / " + DiffText(m);   // ★10-15 사용자 지시: 압박 뒤에 « / 차이»
    }

    /// <summary>차이 = 압박 − 능동(능동에서 압박으로 얼마나 더 갔나) — «+4.6°». 둘 중 하나라도 없으면 «—». ★10-15 사용자 지시(부호 붙임).</summary>
    /// <summary>
    /// 관제 «측정값 저장» 파일(10-22 사용자 지시) — 결과표에 보이는 것만: 측정 명칭 · 능동 · 압박 · 차이(압박 − 능동).
    /// 결과표와 같은 규칙으로 채운다 — 확인(트리거)한 동작만, 나머지는 빈 칸. 단위(°)는 머리글에만 둔다(엑셀에서 숫자로 읽히게).
    /// </summary>
    private string ResultCsv()
    {
        resultCsv.Length = 0;
        resultCsv.Append("측정 명칭,능동(°),압박(°),차이(°)\n");
        for (int m = 0; m < 6; m++)
        {
            RomLineResult a = results[m * 2], pr = results[m * 2 + 1];
            bool aOn = a.valid && confirmed[m], pOn = pr.valid && confirmed[m];
            resultCsv.Append(MotionName[m]).Append(',')
                     .Append(aOn ? a.angle.ToString("F1") : "").Append(',')
                     .Append(pOn ? pr.angle.ToString("F1") : "").Append(',')
                     .Append(aOn && pOn ? (pr.angle - a.angle).ToString("+0.0;-0.0;0.0") : "").Append('\n');
        }
        // 11-13 평가가 끝났으면 관제 «측정값 저장»에도 비교를 붙인다(끝나기 전엔 실습값을 안 보인다 — 사용자)
        if (evalMode && evalScored)
        {
            resultCsv.Append("\n측정 명칭,실습 능동(°),실습 압박(°),오차 능동(°),오차 압박(°)\n");
            for (int m = 0; m < 6; m++)
                resultCsv.Append(MotionName[m]).Append(',')
                         .Append(float.IsNaN(baseVals[m * 2]) ? "" : baseVals[m * 2].ToString("F1")).Append(',')
                         .Append(float.IsNaN(baseVals[m * 2 + 1]) ? "" : baseVals[m * 2 + 1].ToString("F1")).Append(',')
                         .Append(ErrText(m * 2) == "—" ? "" : ErrText(m * 2)).Append(',')
                         .Append(ErrText(m * 2 + 1) == "—" ? "" : ErrText(m * 2 + 1)).Append('\n');
            resultCsv.Append("평가 점수,").Append(evalScore.ToString("F1")).Append(",평균 오차,").Append(evalMeanErr.ToString("F2")).Append(",\n");
        }
        return resultCsv.ToString();
    }
    private readonly StringBuilder resultCsv = new StringBuilder(256);

    // ── 11-13 실습 / 평가(재현성) — RomLineRepeatScore 설명 참고 ──────────────────
    private bool evalMode, hasBase, evalScored;
    private string baseWhen = "", monitorTitle = MonitorTitle;
    private readonly float[] baseVals = new float[RomLineRepeatScore.Slots];
    private readonly float[] curVals = new float[RomLineRepeatScore.Slots];
    private float evalScore, evalMeanErr;
    private int evalN;

    /// <summary>결과표와 같은 규칙(확인한 동작만)으로 지금 값 12칸을 채운다 — 없는 칸은 NaN.</summary>
    private void FillCurrent()
    {
        for (int s = 0; s < RomLineRepeatScore.Slots; s++)
            curVals[s] = results[s].valid && confirmed[s / 2] ? results[s].angle : float.NaN;
    }

    private void SaveBaselineIfPractice(string why)
    {
        if (evalMode) return;
        FillCurrent();
        RomLineRepeatScore.SaveBaseline(monitorUser, curVals);
        Debug.Log($"[선실측] 실습값 저장({why}) — {RomLineRepeatScore.BaselinePath(monitorUser)}");
    }

    /// <summary>판 맨 아래 줄 · 평가가 끝났으면 값 칸에 실습값 · 차이 칸에 오차를 덧쓴다(Refresh 안 · 결과표를 채운 뒤).</summary>
    private void RefreshRepeat()
    {
        if (!evalMode) { view.SetFooter("실습 — 이 측정값이 평가의 비교 기준이 됩니다"); return; }
        if (!hasBase) { view.SetFooter("평가 — ★이 사용자의 실습 기록이 없어 점수를 낼 수 없습니다(실습 먼저)"); evalScored = false; return; }
        if (!Done) { view.SetFooter("평가 — 여섯 동작을 다 재면 실습값과 비교해 점수를 냅니다"); evalScored = false; return; }

        // ★평가가 끝난 뒤에만 실습값을 보인다(사용자 11-13)
        FillCurrent();
        evalScore = RomLineRepeatScore.Total(baseVals, curVals, out evalMeanErr, out evalN);
        // ★11-17 결과 창(시안 V0.2): 값 칸 = 평가 값 + 그 아래 «(실습 값)» · 차이 칸 자리 = 오차 칩 둘. 종전 «값 (실습 x)» 한 줄 · «오차 a / p» 글은 칸이 겹쳐 바꿨다.
        var ci = CultureInfo.InvariantCulture;
        for (int s = 0; s < RomLineRepeatScore.Slots; s++)
        {
            bool hasCur = !float.IsNaN(curVals[s]), both = hasCur && !float.IsNaN(baseVals[s]);
            view.SetEvalCell(s, both, hasCur ? curVals[s].ToString("F1", ci) + "°" : "—",
                             both ? "(실습 " + baseVals[s].ToString("F1", ci) + ")" : "", both ? curVals[s] - baseVals[s] : 0f);
        }
        view.SetFooter(evalN > 0
            ? $"평가 점수 {evalScore:F1}점 · 평균 오차 {evalMeanErr:F1}° · 비교 {evalN}칸 · 실습 {baseWhen} 기준 (오차 = 평가 - 실습, 능동 / 압박)"
            : "평가 — 실습과 겹치는 측정값이 없어 점수를 낼 수 없습니다");
        if (!evalScored)
        {
            evalScored = true;
            Debug.Log($"[선실측] ★평가 점수 {evalScore:F1} · 평균 오차 {evalMeanErr:F2}° · {evalN}칸 · 실습 {baseWhen}");
            SaveEvalFile();
            SaveUpload("평가 점수");
        }
    }

    // ── 결과 창(11-17 사용자 결정 · 시안 V0.2) ── 6동작을 다 확인하면(Done) 벽면 판이 결과 창으로 바뀐다 — 탭은 Confirm이 이미 홈으로 보낸다(10-14).
    //   닫기는 없다. Done이 풀리면(홈 메뉴 «측정값 초기화» · 측정 탭의 B 길게 «이 탭 지우기») 결과가 더 이상 완전하지 않으니 사라진다.
    //   다 못 잰 판은 결과 창이 없다(값만 기록 · 점수 없음 — 종전 그대로).
    private bool resultOn, resultEvalOn;
    private string resultWhen = "", resultOffWhy = "결과가 비었다";

    private void RefreshResultWindow()
    {
        bool on = Done;
        bool eval = on && evalMode && evalScored && evalN > 0;
        if (on == resultOn && eval == resultEvalOn) return;   // 상태가 바뀔 때만
        if (on && !resultOn) resultWhen = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string user = monitorUser.Length > 0 ? " · 사용자 " + monitorUser : "";
        string title = "경추 ROM 결과 · " + (evalMode ? "평가" : "실습");
        string sub = !evalMode ? resultWhen + user + " · 6 / 6 동작 확인"
                   : hasBase ? resultWhen + user + " · 실습 " + baseWhen + " 기준"
                   : resultWhen + user + " · 비교할 실습 기록 없음";
        view.SetResultWindow(on, eval, title, sub);
        if (eval) view.SetEvalScore(evalScore, $"평균 오차 {evalMeanErr:F1}° · 비교 {evalN}칸 · 오차 = 평가 - 실습");
        if (on) Debug.Log($"[선실측] 결과 창 — 띄움({(evalMode ? "평가" : "실습")}{(eval ? $" · 점수 {evalScore:F1} · 평균 오차 {evalMeanErr:F2}° · {evalN}칸" : evalMode ? " · 점수 없음" : "")})");
        else Debug.Log($"[선실측] 결과 창 — 사라짐({resultOffWhy})");
        if (on) RomLineDevLog.Save(eval ? "결과 창 · 평가" : "결과 창", tipOffset * 1000f);   // 한 판이 끝났다 — 개발용 로그(11-17)
        resultOn = on;
        resultEvalOn = eval;
        resultOffWhy = "결과가 비었다";
    }

    private string ErrText(int s)
    {
        if (float.IsNaN(baseVals[s]) || float.IsNaN(curVals[s])) return "—";
        return (curVals[s] - baseVals[s]).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }

    // ── 서버 전송(11-16 사용자 지시) ── 판 하나 = 큐 파일 하나. 이벤트 때마다 덮어쓰고, 나갈 때 · 다음 실행 때 보낸다(RomLineUpload).
    private string uploadId, uploadStartedAt;
    private bool uploadSaved;
    private static readonly string[] MotionCode = { "flexion", "extension", "lateral_right", "lateral_left", "rotation_right", "rotation_left" };
    private static readonly string[] PhaseCode = { "neutral", "active", "pressure" };

    private void SaveUpload(string why, bool final = false)
    {
        bool any = uploadSaved;
        for (int i = 0; i < PointCount && !any; i++) any = has[i];
        if (!any) return;   // 아무것도 안 찍은 판은 안 남긴다

        var login = new LoginStateStore();
        var info = login.LoadLoginInfo();
        var p = new RomLineUpload.Payload
        {
            sessionId = uploadId,
            status = Done ? "completed" : final ? "exited" : "in_progress",
            orgID = login.LoadOrgID(),
            userId = info.hasData ? info.userID : 0,
            username = info.hasData ? info.username : "",
            deviceSN = login.LoadDeviceSN(),
            appVersion = Application.version,
            mode = evalMode ? "evaluation" : "practice",
            startedAt = uploadStartedAt,
            updatedAt = RomLineUpload.Now(),
            tipOffsetMm = new RomLineUpload.Vec3(tipOffset * 1000f),
            frame = new RomLineUpload.Frame
            {
                valid = frame.valid,
                right = new RomLineUpload.Vec3(frame.right),
                forward = new RomLineUpload.Vec3(frame.forward),
                up = new RomLineUpload.Vec3(frame.up),
                sidesSwapped = frame.sidesSwapped,
            },
            rawCsv = new List<string>(csvRows.Count + 1) { CsvHeader },
        };
        p.rawCsv.AddRange(csvRows);
        for (int i = 0; i < 2; i++)
            if (has[i]) p.frame.baseline.Add(new RomLineUpload.Point { phase = "baseline", label = ((RomLinePoint)i).ToString(), pos = new RomLineUpload.Vec3(points[i]) });

        for (int m = 0; m < 6; m++)
        {
            LineOf((RomLineMotion)m, out RomLinePoint a, out RomLinePoint b);
            var mo = new RomLineUpload.Motion
            {
                code = MotionCode[m], name = MotionName[m], confirmed = confirmed[m],
                active = UploadAngle(m, m * 2), pressure = UploadAngle(m, m * 2 + 1),
            };
            for (int ph = 0; ph < 3; ph++)
            {
                int st = GroupStart(GroupOf(m, ph));
                if (has[st]) mo.points.Add(new RomLineUpload.Point { phase = PhaseCode[ph], label = a.ToString(), pos = new RomLineUpload.Vec3(points[st]) });
                if (has[st + 1]) mo.points.Add(new RomLineUpload.Point { phase = PhaseCode[ph], label = b.ToString(), pos = new RomLineUpload.Vec3(points[st + 1]) });
            }
            p.motions.Add(mo);
        }

        if (evalMode && evalScored)
        {
            var ev = new RomLineUpload.Evaluation { baselineAt = baseWhen, score = evalScore, meanError = evalMeanErr };
            for (int s = 0; s < RomLineRepeatScore.Slots; s++)
            {
                bool hb = !float.IsNaN(baseVals[s]), hc = !float.IsNaN(curVals[s]);
                ev.slots.Add(new RomLineUpload.Slot
                {
                    code = MotionCode[s / 2], kind = s % 2 == 0 ? "active" : "pressure",
                    practice = hb ? baseVals[s] : (float?)null, evaluation = hc ? curVals[s] : (float?)null,
                    error = hb && hc ? curVals[s] - baseVals[s] : (float?)null,
                    score = hb && hc ? RomLineRepeatScore.ScoreOf(curVals[s] - baseVals[s]) : (float?)null,
                });
            }
            p.evaluation = ev;
        }

        RomLineUpload.Save(p);
        uploadSaved = true;
        Debug.Log($"[XR-ROM 전송] 큐 저장({why}) — {p.status} · 확인 {CountConfirmed()}/6 · 상세 {csvRows.Count}줄");
    }

    private RomLineUpload.Angle UploadAngle(int m, int slot) => new RomLineUpload.Angle
    {
        valid = results[slot].valid,
        angle = results[slot].valid ? results[slot].angle : 0f,
        signedAngle = results[slot].valid ? results[slot].signedAngle : 0f,
        directionMatches = results[slot].valid && RomLineMath.DirectionMatches((RomLineMotion)m, results[slot].signedAngle),
        directAngle = direct[slot].valid ? direct[slot].angle : -1f,
    };

    // ── 홈 메뉴 글(11-17 시안 V0.4 부록 문구) — 상태가 바뀔 때만 만든다(Refresh) ──
    private const string MenuKey = RomLineView.SmallTagOpen + "<color=#FFF233>";   // 조작 문구 = 규격 12px(설명 14px의 86%) · 컨트롤러UI 조작 안내의 노랑
    private const string MenuKeyEnd = "</color>" + RomLineView.SmallTagClose;
    private const string AskOpen = "<size=114%><b><color=#F2F3F5>";               // 확인 질문 = 규격 16 B(설명 14의 114%) · 흰
    private const string AskClose = "</color></b></size>";
    // 상자 아래 힌트 한 줄(시안 부록) — 목록 / 확인 안내
    private const string MenuListHint = "스틱 ▲▼ : 고르기 · ◀▶ : 탭 이동";
    private const string MenuAskHint = "B : 목록으로";

    /// <summary>설명 상자의 글 — 목록이면 그 항목의 설명 + 조작, 확인 안내 중이면 질문 + 조작.</summary>
    private string MenuDesc(int item, bool ask)
    {
        int done = CountConfirmed();
        if (ask)
        {
            switch (item)
            {
                case MenuReset:
                    return AskOpen + "측정값을 모두 지울까요?" + AskClose + "\n" + MenuKey + "트리거 : 초기화 · B : 취소" + MenuKeyEnd;
                case MenuSettings:
                    return AskOpen + SettingsName[settingsTab] + "을 시작할까요?" + AskClose + "\n" + MenuKey + "트리거 : 시작 · B : 취소" + MenuKeyEnd;
                default:
                    return AskOpen + "정말 나갈까요?" + AskClose + "\n"
                         + (done < 6 ? "<color=#FF8C8C>아직 " + (6 - done) + "개 동작 남음</color>\n" : "")
                         + MenuKey + "트리거 : 나가기 · B : 취소" + MenuKeyEnd;
            }
        }
        switch (item)
        {
            case MenuRom:
                return "경추 ROM 측정을\n시작한다\n"
                     + (Done ? "<color=#73E68C>" + (evalMode ? "평가가 끝났다" : "6동작을 모두 쟀다") + "</color>\n" : "")
                     + "→ " + GuidedTabName[RomEntryTab()] + (Done ? "" : "부터") + "\n"
                     + MenuKey + "트리거 : 측정 시작" + MenuKeyEnd;
            case MenuReset:
                return "이번 판에 잰 값을\n모두 지운다\n" + MenuKey + "트리거 : 선택" + MenuKeyEnd;
            case MenuSettings:
                return "끝점 보정 등\n측정 준비\n" + MenuKey + "트리거 : 열기" + MenuKeyEnd;
            default:
                return "로비로 돌아간다\n"
                     + (AnyPointTaken() ? "잰 값은 기기에 남는다\n" : "")
                     + MenuKey + "트리거 : 선택" + MenuKeyEnd;
        }
    }

    /// <summary>벽면 안내 띠 · 관제 페이지의 한 줄(시안 V0.4 벽면 동기화 A) — 리치 텍스트 없이.</summary>
    private string MenuBandLine(int item, bool ask)
    {
        if (ask)
        {
            switch (item)
            {
                case MenuReset: return "메뉴 · 측정값 초기화 — 측정값을 모두 지울까요? 트리거 : 초기화 · B : 취소";
                case MenuSettings: return "메뉴 · 설정 — " + SettingsName[settingsTab] + "을 시작할까요? 트리거 : 시작 · B : 취소";
                default:
                    int done = CountConfirmed();
                    return "메뉴 · 나가기 — 정말 나갈까요?" + (done < 6 ? " (아직 " + (6 - done) + "개 동작 남음)" : "") + " 트리거 : 나가기 · B : 취소";
            }
        }
        switch (item)
        {
            case MenuRom: return "메뉴 · ROM — 경추 ROM 측정을 시작한다(" + GuidedTabName[RomEntryTab()] + ") · 트리거 : 측정 시작";
            case MenuReset: return "메뉴 · 측정값 초기화 — 이번 판에 잰 값을 모두 지운다 · 트리거 : 선택";
            case MenuSettings: return "메뉴 · 설정 — 끝점 보정 등 측정 준비 · 트리거 : 열기";
            default: return "메뉴 · 나가기 — 로비로 돌아간다 · 트리거 : 선택";
        }
    }

    /// <summary>점을 하나라도 찍었나(나가기 설명의 «잰 값은 기기에 남는다» — 찍은 판만 큐 파일이 남는다).</summary>
    private bool AnyPointTaken()
    {
        for (int i = 0; i < has.Length; i++) if (has[i]) return true;
        return CountConfirmed() > 0;
    }

    private int CountConfirmed()
    {
        int n = 0;
        for (int m = 0; m < 6; m++) if (confirmed[m]) n++;
        return n;
    }

    private void SaveEvalFile()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(512);
        sb.Append("측정 명칭,구분,실습(°),평가(°),오차(°),점수\n");
        for (int s = 0; s < RomLineRepeatScore.Slots; s++)
        {
            bool both = !float.IsNaN(baseVals[s]) && !float.IsNaN(curVals[s]);
            sb.Append(MotionName[s / 2]).Append(',').Append(s % 2 == 0 ? "능동" : "압박").Append(',')
              .Append(float.IsNaN(baseVals[s]) ? "" : baseVals[s].ToString("F1", ci)).Append(',')
              .Append(float.IsNaN(curVals[s]) ? "" : curVals[s].ToString("F1", ci)).Append(',')
              .Append(both ? (curVals[s] - baseVals[s]).ToString("F1", ci) : "").Append(',')
              .Append(both ? RomLineRepeatScore.ScoreOf(curVals[s] - baseVals[s]).ToString("F1", ci) : "").Append('\n');
        }
        sb.Append("종합,,,,").Append(evalMeanErr.ToString("F2", ci)).Append(',').Append(evalScore.ToString("F1", ci)).Append('\n');
        sb.Append("실습 기준,").Append(baseWhen).Append(",,,,\n");
        try
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "romline_eval_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[선실측] 평가 결과 파일 — " + path);
        }
        catch (System.Exception e) { Debug.LogWarning($"[선실측] ★평가 결과 파일 실패({e.Message})"); }
    }

    private string DiffText(int m)
    {
        if (!results[m * 2].valid || !results[m * 2 + 1].valid) return "—";
        return (results[m * 2 + 1].angle - results[m * 2].angle).ToString("+0.0;-0.0;0.0") + "°";
    }

    private string GroupTitle(int g)
    {
        if (g <= 0) return "기준(쓰지 않음)";
        if (g >= GroupCount) return "측정 끝";
        return MotionName[MotionOf(g)] + " · " + PhaseName[PhaseOf(g)];
    }

    private void Refresh()
    {
        if (freeMode) { view.SetHomeMenu(-1, null, "", "", false); SetSpaceGaugeTab(-1, "자유 측정"); RefreshFree(); return; }
        dirty = false;
        RefreshSpaceGauge();

        int tab = CurTab, cm = MotionOf(group);
        string instruction, tipText = "", guide, note = noteText, forMonitor, guideNote = noteText;
        // ★10-13 사용자 지시: 안내는 한 문장이다 — «○○ · 중립 / 능동 / 압박을 측정하세요». 어느 점을 찍으라는 말은 하지 않는다(컨트롤러 끝의 이름표에만 남는다).
        //   동작 하나를 다 재면 안내에 능동 · 압박의 각을 띄우고 확인(트리거)을 기다린다 — ★확인해야 결과표에 기록된다(10-13 오후). 아랫줄은 알림, 없으면 버튼 안내.
        if (home)
        {
            if (calibrating)
            {
                forMonitor = instruction = "끝점 보정 중 — 트리거 : 끝";
                guide = "<color=#FFF233>끝점 보정 중</color>\n" + SubInfo + "트리거 : 끝</size>";
            }
            else
            {
                // ★11-17 시안 V0.4: 홈 = 메뉴(목록 + 설명 상자 — 컨트롤러UI는 view.SetHomeMenu, 아래). 벽면 안내 띠 · 관제 페이지는 한 줄(동기화 A).
                //   안내 띠 아랫줄(알림) = 다 잰 뒤면 끝났다는 말 · 아니면 조작 힌트(Meta 버튼 길게 = 메타 퀘스트 «뷰 재설정» — PlaceFixed).
                // ★11-17 글꼴 KoPub(정적 SDF)에 ↕ · ∞ · −(U+2212)가 없다 → 화면 글은 ▲▼ · «Meta 버튼» · -로 쓴다(fallback Noto 동적 글꼴이 글자를 굽지 않게 · LFS 사고 예방).
                forMonitor = instruction = MenuBandLine(menuCursor, menuAsk);
                guide = forMonitor;
                if (guideNote.Length == 0)
                    guideNote = Done ? (evalMode ? "평가가 끝났습니다" : "6동작을 모두 쟀습니다") + " · 다시 재려면 메뉴 «측정값 초기화»"
                                     : "스틱 ▲▼ : 고르기 · ◀▶ : 탭 이동 · Meta 버튼 길게 : 뷰 재설정";
            }
        }
        // ★10-14 사용자 정정: 여섯 동작을 다 잰 뒤에도 각 탭에서는 그 탭의 기록을 보인다 — 종전에는 «측정 완료» 한 줄로 덮였다(Done 분기가 아래 «다 잰 탭» 분기보다 앞이었다).
        else if (confirmWait)
        {
            // 동작 하나를 다 찍었다 — 확인을 기다린다
            string av = results[cm * 2].valid ? results[cm * 2].angle.ToString("F1") + "°" : "—";
            string pv = results[cm * 2 + 1].valid ? results[cm * 2 + 1].angle.ToString("F1") + "°" : "—";
            string dv = DiffText(cm);   // ★10-15 사용자 지시: 압박 뒤에 « / 차이»
            forMonitor = instruction = $"{MotionName[cm]} 측정 완료 · 능동 {av} · 압박 {pv} / {dv} · 중립으로 돌아간 뒤 트리거 : 확인";
            guide = $"<color=#73E68C>{MotionName[cm]}</color>\n능동 {av}\n압박 {pv} / {dv}";   // 안내는 폭이 좁다 — 줄을 나눈다
            if (guideNote.Length == 0) guideNote = "트리거 : 기록";
        }
        else if (TabComplete(tab))
        {
            // ★10-14 사용자 지시: 다 잰 탭에서는 안내에 그 탭의 기록(두 동작 × 능동 / 압박)을 보이고, 마지막 줄에 초기화 방법을 적는다.
            //   두 동작의 순서는 결과표와 같다 — ★10-16부터 재는 순서(우 → 좌 · RomLineView.DisplayRow).
            int first = tab * 2, second = tab * 2 + 1;
            forMonitor = instruction = $"{GuidedTabName[tab]} 측정 완료";
            guide = "<color=#73E68C>" + GuidedTabName[tab] + " 완료</color>\n" + RecordLine(first) + "\n" + RecordLine(second);
            if (guideNote.Length == 0) guideNote = "B 길게 : 이 탭 지우기";
        }
        else
        {
            RomLinePoint p = ExpectedPoint(group, index);
            instruction = $"{MotionName[cm]} · {PhaseName[PhaseOf(group)]}을 측정하세요";
            tipText = PointName[(int)p];
            guide = $"{MotionName[cm]} · {PhaseName[PhaseOf(group)]}  <color=#FFF233>{Dots(index, SizeOf(group))}</color>";   // 찍을 점만큼 동그라미 — 찍으면 채운다
            forMonitor = instruction + $"  ({PointName[(int)p]} · {SizeOf(group)}점 중 {index + 1}번째)";   // 모니터에는 찍을 점도 남긴다
            if (guideNote.Length == 0 && PhaseOf(group) == 2 && results[cm * 2].valid) guideNote = "능동 " + results[cm * 2].angle.ToString("F1") + "°";   // 압박을 재는 동안 방금 잰 능동 값
        }
        view.SetTip(tipText);
        view.SetGuide(guide, guideNote);
        bool menuOn = home && !calibrating;
        view.SetHomeMenu(menuOn ? menuCursor : -1, MenuLabel, menuOn ? MenuDesc(menuCursor, menuAsk) : "",
                         menuOn ? (menuAsk ? MenuAskHint : MenuListHint) : "", menuAsk);
        RefreshGuidedExtras(tab, forMonitor, note, guide, guideNote);
        RefreshTabs();

        // 결과표(10-12) — 한 줄에 동작 하나: 능동 · 압박의 «면 위 각»과 그 동작의 단면에서 본 각도기.
        //   ★그림이 따로 계산하지 않는다 — 숫자와 같은 값(results)을 그린다. 반대쪽으로 움직인 값은 로그 · 알림과 같은 함수(DirectionMatches)로 가른다.
        int current = group >= 1 && !Done && !home && PhaseOf(group) > 0 ? MotionOf(group) * 2 + PhaseOf(group) - 1 : -1;
        view.SetRowNow(Done || home ? -1 : MotionOf(group));
        for (int m = 0; m < 6; m++)
        {
            // ★10-13 오후 사용자 지시: 결과표의 값 · 각도기는 <b>확인(트리거)한 동작만</b> 채운다 — 찍는 대로 뜨지 않는다. 확인 전의 값은 안내와 공간의 글자에만 있다.
            RomLineResult a = results[m * 2], pr = results[m * 2 + 1];
            bool aOn = a.valid && confirmed[m], pOn = pr.valid && confirmed[m];
            bool aOpp = aOn && !RomLineMath.DirectionMatches((RomLineMotion)m, a.signedAngle);
            bool pOpp = pOn && !RomLineMath.DirectionMatches((RomLineMotion)m, pr.signedAngle);
            view.SetValue(m * 2, aOn ? a.angle.ToString("F1") + "°" : "—", m * 2 == current, aOpp);
            view.SetValue(m * 2 + 1, pOn ? pr.angle.ToString("F1") + "°" : "—", m * 2 + 1 == current, pOpp);
            view.SetDiff(m, aOn && pOn ? DiffText(m) : "—", current >= 0 && current / 2 == m);   // ★10-15: 차이 칸(압박 − 능동)
            view.SetGauge(m, aOn, a.angle, aOpp, pOn, pr.angle, pOpp);
        }
        RefreshRepeat();
        RefreshResultWindow();
        monitor.SetCsv(ResultCsv());   // 결과표가 바뀔 때마다(Refresh는 dirty일 때만 돈다)

        // 점 — 기준 둘은 늘, 그 밖은 지금 동작 것만.
        int motion = group == 0 || home ? -1 : MotionOf(Mathf.Min(group, GroupCount - 1));   // 홈에서는 공간에 아무것도 그리지 않는다(10-14)
        if (motion >= 0 && confirmed[motion]) motion = -1;   // ★10-14 사용자 지시: 측정을 마친(확인한) 동작의 선 · 점은 공간에서 싹 지운다(다 잰 탭에 남던 마지막 동작의 선)
        for (int i = 0; i < PointCount; i++)
        {
            bool show = has[i] && (i < 2 || (i - 2) / 6 == motion);
            view.SetPoint(i, show, points[i]);
        }

        // 비교 시험 — 지금 동작의 Gn 셋(중립·능동·압박)과 Gl ~ Gn 값 한 줄.
        bool cmp = compareGlGn && motion >= 0 && motion < 2;
        for (int ph = 0; ph < 3; ph++)
        {
            int g = cmp ? GroupOf(motion, ph) : 0;
            view.SetExtraPoint(ph, cmp && hasExtra[g], extra[g]);
        }
        // 판의 비교 줄은 굴곡·신전 둘 다 늘 보여 준다 — 지금 어느 단계에 있든 찍어 둔 값이 남아 있어야 견줄 수 있다.
        for (int m = 0; m < 2; m++)
            view.SetCompare(m, compareGlGn,
                            cmpResults[m * 2].valid ? cmpResults[m * 2].angle.ToString("F1") + "°" : "—",
                            cmpDirect[m * 2].valid ? cmpDirect[m * 2].angle.ToString("F1") + "°" : "—",
                            cmpResults[m * 2 + 1].valid ? cmpResults[m * 2 + 1].angle.ToString("F1") + "°" : "—",
                            cmpDirect[m * 2 + 1].valid ? cmpDirect[m * 2 + 1].angle.ToString("F1") + "°" : "—");
        if (cmp)
        {
            int ca = motion * 2, cp = motion * 2 + 1;

            // Gl ~ Gn 선도 환자 위에 그린다. 값은 Tr ~ Gl 쪽과 같은 규칙 — 기준이 있으면 «면», 없으면 «직접».
            int gN = GroupOf(motion, 0), gA = GroupOf(motion, 1), gP = GroupOf(motion, 2);
            int iN = GroupStart(gN) + 1, iA = GroupStart(gA) + 1, iP = GroupStart(gP) + 1;   // 각 자세의 Gl
            bool hn = has[iN] && hasExtra[gN], ha = has[iA] && hasExtra[gA], hp = has[iP] && hasExtra[gP];
            string label = "";
            if (cmpDirect[ca].valid)
            {
                label = "미간점~턱끝점 " + (cmpResults[ca].valid ? cmpResults[ca].angle : cmpDirect[ca].angle).ToString("F1") + "°";
                if (cmpDirect[cp].valid)
                    label += " / 압박 " + (cmpResults[cp].valid ? cmpResults[cp].angle : cmpDirect[cp].angle).ToString("F1") + "°";
            }
            view.ShowCompare(hn, points[iN], extra[gN], ha, points[iA], extra[gA], hp, points[iP], extra[gP],
                             label, cmpDirect[ca].pivot - Vector3.up * 0.035f);
        }
        else view.HideCompare();

        // 선 — 지금 동작 것만(10-14 사용자 지시: 각 글자 대신 길이 mm).
        //   측정선은 값을 낸 면 위의 선(results — 각과 같은 선)을, 값이 아직 없으면 찍은 그대로를 그린다.
        //   길이 글자는 찍은 두 점 사이의 실제 거리다(모니터 · CSV의 «길이»와 같다).
        if (motion < 0) { view.HideMeasure(); return; }
        for (int ph = 0; ph < 3; ph++)
        {
            int s = GroupStart(GroupOf(motion, ph));
            bool show = has[s] && has[s + 1];
            Vector3 a = points[show ? s : 0], b = points[show ? s + 1 : 0];
            if (show)
            {
                RomLineResult r = ph == 0 ? results[motion * 2] : results[motion * 2 + ph - 1];   // 중립선은 능동 값이 있으면 그 면 위의 중립선
                if (r.valid) { a = ph == 0 ? r.neutralA : r.markA; b = ph == 0 ? r.neutralB : r.markB; }
            }
            view.SetSegment(ph, show, a, b, show ? (Vector3.Distance(points[s], points[s + 1]) * 1000f).ToString("F0") : "");
        }
    }

    // ── 공간 각도기(11-16 사용자 지시) ──────────────────────────────
    //   ★계측 도구라 «찍는 동안 각이 보여 손이 숫자를 따라가는 것»을 막는다 — 트리거로 <b>확인한 뒤에만</b> 뜬다.
    //     평가(재현성)는 6동작이 다 끝날 때까지(evalScored) 숨긴다 — 실습값을 끝나기 전에 안 보이는 것과 같은 이유다.
    //   어느 탭: 지금 탭에 확인한 동작이 있으면 지금 탭 · 없으면 방금 끝낸 탭(다음 탭의 첫 점을 찍기 전까지 남긴다 — 탭은 확인하면 저절로 넘어가서
    //     그러지 않으면 탭의 두 번째 동작 각도기는 뜨자마자 사라진다). 홈 · 자유 측정에서는 숨긴다(홈에선 공간에 아무것도 안 그린다 — 10-14).
    //   자리: 탭이 바뀔 때 한 번 — 환자 중심(그 탭 첫 확인 동작의 네 점 평균)에서 사용자 반대편.
    //     굴곡·신전 · 측굴 = 면의 법선 방향으로 사용자와 반대쪽 · 회전 = 수평으로 사용자 반대쪽 + 위로 spaceGaugeRotationRise.
    private void RefreshSpaceGauge()
    {
        int want = -1;
        string why = "";
        if (home) why = "홈";
        else if (evalMode && !evalScored) why = "평가 — 6동작이 끝날 때까지 숨김";
        else
        {
            int tab = CurTab;
            if (confirmed[tab * 2] || confirmed[tab * 2 + 1]) { want = tab; why = "지금 탭에 확인한 동작"; }
            else if (lastConfirmedTab >= 0 && lastConfirmedTab != tab && !AnyPointInTab(tab)) { want = lastConfirmedTab; why = "방금 끝낸 탭(새 탭의 첫 점 전까지)"; }
            else why = "확인한 동작 없음";
        }
        SetSpaceGaugeTab(want, why);
        if (want < 0) return;

        int m0 = want * 2, m1 = want * 2 + 1;
        bool on0 = confirmed[m0] && results[m0 * 2].valid, on1 = confirmed[m1] && results[m1 * 2].valid;
        if (!on0 && !on1) { SetSpaceGaugeTab(-1, "확인했지만 능동 값 없음"); return; }
        RomLineResult first = on0 ? results[m0 * 2] : results[m1 * 2];
        Vector3 n = RomLineMath.PlaneNormal((RomLineMotion)(on0 ? m0 : m1), frame).normalized;   // 값을 낸 것과 같은 법선
        // ★11-17 사용자 지적 «각도기가 판 안에서 180° 돌아가 있고 조금 기울어 있다» → 0°를 고정 기준으로.
        //   종전 zeroDir = first.neutralDir 는 중립선(미간 → 턱끝 순서)이라 <b>아래</b>를 향했다(평가 판 실측 y 1.501 → 1.403)
        //   → LookRotation(normal, zeroDir)에서 0°(+Y)가 아래 = 법선 둘레 180°, 얼굴선이 수직에서 벗어난 만큼(~5° · 계산) 기울었다.
        //   옆면 · 앞면(법선이 수평) = 월드 위를 면에 투영한 방향 · 바닥면(회전 · 법선 = 위) = 환자 정면(frame.forward)을 면에 투영.
        //   도달점은 그대로 signedAngle(중립 대비)을 Dir(a)로 — 회전 방향(법선 둘레)은 같아 값 · 부호는 결과표와 같다.
        //   ★방향은 추론으로 확정하지 않는다(규칙 9) — 아래 로그의 두 각으로 Play에서 대조한다.
        Vector3 zeroRef = want == 2 ? (frame.valid ? frame.forward : Vector3.forward) : Vector3.up;
        Vector3 zeroDir = Vector3.ProjectOnPlane(zeroRef, n);
        if (zeroDir.sqrMagnitude < 1e-6f) zeroDir = -first.neutralDir;   // 면이 기준과 수직에 가까우면(드묾) 중립선을 뒤집어 쓴다
        zeroDir.Normalize();
        LogSpaceGaugeDir(want, n, zeroRef, zeroDir, first);
        // ★디자인은 VR ROM 각도기와 통일(11-16) — 눈금 색 = 면(탭) · 도달점 = 숫자만(동작 이름 없음 · 능동 하늘색 / 압박 붉은 주황)
        spaceGauge.Show(spaceGaugeCenter, n, zeroDir, spaceGaugeRadius, want,
                        on0, results[m0 * 2].signedAngle, results[m0 * 2 + 1].valid, results[m0 * 2 + 1].signedAngle,
                        on1, results[m1 * 2].signedAngle, results[m1 * 2 + 1].valid, results[m1 * 2 + 1].signedAngle);
    }

    /// <summary>
    /// 각도기 방향 대조 로그(11-17) — 값이 바뀔 때만 한 줄. ①0°와 기준(옆·앞면 = 월드 위 / 바닥면 = 환자 정면)의 각 — 0이면 0°가 정확히 그쪽이다.
    /// ②능동 도달점 방향(0°에서 signedAngle만큼 법선 둘레로 돌린 것) vs 실제 능동선을 뒤집은 방향(−markDir — 0°가 «위/앞»이라 선도 그쪽 끝을 본다)의 각 — 작으면 도달점이 실제 선과 같은 쪽.
    /// ③0°와 −중립선의 각 = ②가 0이 아닌 이유(얼굴선이 기준에서 벗어난 만큼).
    /// </summary>
    private void LogSpaceGaugeDir(int tab, Vector3 n, Vector3 zeroRef, Vector3 zeroDir, RomLineResult first)
    {
        if (tab == lastGaugeDirTab && Mathf.Approximately(first.signedAngle, lastGaugeDirAngle)) return;
        lastGaugeDirTab = tab;
        lastGaugeDirAngle = first.signedAngle;
        Vector3 shown = Quaternion.AngleAxis(first.signedAngle, n) * zeroDir;
        float refAng = Vector3.Angle(zeroDir, zeroRef);
        float vsLine = Vector3.Angle(shown, -first.markDir);
        float vsNeutral = Vector3.Angle(zeroDir, -first.neutralDir);
        Debug.Log($"[선실측·각도기] 방향 — {GuidedTabName[tab]} · 0°↔{(tab == 2 ? "환자 정면" : "월드 위")} {refAng:F1}° · " +
                  $"능동 도달점({first.signedAngle:F1}°) ↔ 실제 능동선(뒤집음) {vsLine:F1}° · 0° ↔ 중립선(뒤집음) {vsNeutral:F1}° · 법선 {n:F2}");
    }
    private int lastGaugeDirTab = -1;
    private float lastGaugeDirAngle = float.NaN;

    private bool AnyPointInTab(int tab)
    {
        for (int m = tab * 2; m < tab * 2 + 2; m++)
            for (int ph = 0; ph < 3; ph++)
            {
                int st = GroupStart(GroupOf(m, ph));
                if (has[st] || has[st + 1]) return true;
            }
        return false;
    }

    /// <summary>그릴 탭이 바뀔 때만 자리를 다시 정한다(고정 — 사용자를 따라 돌지 않는다). 로그 [선실측·각도기]로 자리 · 방향을 남긴다(반대편이 맞는지 Play에서 대조).</summary>
    private void SetSpaceGaugeTab(int tab, string why)
    {
        if (tab == spaceGaugeTab) return;
        int was = spaceGaugeTab;
        spaceGaugeTab = tab;
        if (tab < 0)
        {
            spaceGauge.Hide();
            if (was >= 0) Debug.Log($"[선실측·각도기] 숨김 — {why}");
            return;
        }
        int m = confirmed[tab * 2] && results[tab * 4].valid ? tab * 2 : tab * 2 + 1;
        RomLineResult r = results[m * 2];
        Vector3 c = (r.neutralA + r.neutralB + r.markA + r.markB) * 0.25f;
        Vector3 n = RomLineMath.PlaneNormal((RomLineMotion)m, frame).normalized;
        Vector3 toEye = eye != null ? eye.position - c : -n;
        Vector3 pos;
        string how;
        if (tab == 2)
        {
            Vector3 away = Vector3.ProjectOnPlane(-toEye, Vector3.up);
            if (away.sqrMagnitude < 1e-4f) away = frame.valid ? frame.forward : Vector3.forward;
            pos = c + away.normalized * spaceGaugeDistance + Vector3.up * spaceGaugeRotationRise;
            how = "수평 반대편 + 위";
        }
        else
        {
            float side = Vector3.Dot(toEye, n);
            pos = c - n * (side >= 0f ? 1f : -1f) * spaceGaugeDistance;
            how = $"법선 반대편(눈은 법선 {(side >= 0f ? "+" : "−")}쪽 · {side:F2}m)";
        }
        spaceGaugeCenter = pos;
        float eyeSide = eye != null ? Vector3.Dot(eye.position - c, pos - c) : 0f;
        Debug.Log($"[선실측·각도기] 띄움 — {GuidedTabName[tab]} ({why}) · {how} · 환자 중심 {c:F2} · 각도기 {pos:F2} · 눈 {(eye != null ? eye.position : Vector3.zero):F2} · " +
                  $"법선 {n:F2} · 눈·각도기가 환자 반대쪽인가 {(eyeSide < 0f ? "예" : "★아니오")}({eyeSide:F3})");
    }

    /// <summary>순서대로 재는 흐름의 나머지 표시 — 진행 줄 · 바닥의 3축 · 안내 음성 · 모니터로 보낼 상태.</summary>
    private void RefreshGuidedExtras(int tab, string instruction, string note, string guide, string guideNote)
    {
        int m = MotionOf(Mathf.Min(group, GroupCount - 1));
        int plane = m / 2;

        // 바닥의 3축 — 자유 측정의 점·선은 지우고 십자만 남긴다(찍어 둔 자유 측정 점은 그대로 있다. «기타»로 가면 다시 그린다).
        freeView.SetShape(NoPoints, 2);
        freeView.SetAngles(NoPoints, NoTexts);
        freeView.ShowAxes(true, axesOrigin, axes);

        // 모니터 — 지금 동작의 세 선(중립 · 능동 · 압박)을 자유 측정과 같은 모양으로 보낸다.
        StringBuilder js = monitorSb;
        js.Length = 0;
        js.Append("\"mode\":\"guided\",\"top\":").Append(CurTop).Append(",\"tab\":").Append(plane);
        AppendTabsJson(js).Append(",\"instruction\":");
        JsonStr(js, instruction).Append(",\"note\":");
        JsonStr(js, note).Append(",\"axesOn\":true,\"planeOn\":true,\"plane\":");
        JsonStr(js, FreePlaneName[plane]);
        // ★10-20 사용자 지시: 관제 화면은 컨트롤러UI 디자인으로 탭 · 안내를 그린다 — 헤드셋 안내와 같은 글(TMP 서식 태그 그대로 · 화면이 HTML로 바꾼다)과 다 잰 탭(1 · 0)을 보낸다.
        js.Append(",\"guide\":");
        JsonStr(js, guide).Append(",\"guideNote\":");
        JsonStr(js, guideNote).Append(",\"tabDone\":\"");
        for (int t = 0; t < 3; t++) js.Append(TabComplete(t) ? '1' : '0');
        js.Append('"');

        // 결과표 · 진행 정보(10-12) — 판과 같은 값을 보낸다. steps = 묶음 1~18을 다 찍었는가(1 · 0), results = 동작 × (능동 a · 압박 p)
        AppendPopupJson(js).Append(",\"cur\":").Append(Done ? -1 : m).Append(",\"phase\":").Append(Done ? -1 : PhaseOf(group))
          .Append(",\"index\":").Append(index).Append(",\"hint\":");
        JsonStr(js, Done ? "" : PhaseHint[PhaseOf(group)]).Append(",\"steps\":\"");
        for (int g = 1; g < GroupCount; g++) js.Append(has[GroupStart(g)] && has[GroupStart(g) + 1] ? '1' : '0');
        js.Append("\",\"results\":[");
        for (int k = 0; k < 6; k++)
        {
            if (k > 0) js.Append(',');
            js.Append("{\"a\":");
            AppendResultJson(js, k, results[k * 2]).Append(",\"p\":");
            AppendResultJson(js, k, results[k * 2 + 1]).Append('}');
        }
        js.Append("],\"lines\":[");
        bool first = true;
        for (int ph = 0; ph < 3; ph++)
        {
            int n = GroupStart(GroupOf(m, ph));
            if (!has[n] || !has[n + 1]) continue;
            int slot = m * 2 + ph - 1;
            string planeText = "", warn = "";
            float directAngle = 0f;
            if (ph > 0)
            {
                if (direct[slot].valid) directAngle = direct[slot].angle;
                if (results[slot].valid)
                {
                    planeText = MotionName[m] + " " + results[slot].angle.ToString("F1") + "°";
                    if (!RomLineMath.DirectionMatches((RomLineMotion)m, results[slot].signedAngle)) warn = "반대쪽으로 움직인 값";
                }
            }
            if (!first) js.Append(',');
            first = false;
            js.Append("{\"n\":").Append(ph + 1).Append(",\"name\":");
            JsonStr(js, PhaseName[ph]).Append(",\"ref\":").Append(ph == 0 ? "true" : "false").Append(",\"refOf\":1,\"len\":")
              .Append((Vector3.Distance(points[n], points[n + 1]) * 1000f).ToString("F0", CultureInfo.InvariantCulture)).Append(",\"plane\":");
            JsonStr(js, planeText).Append(",\"direct\":").Append(directAngle.ToString("F1", CultureInfo.InvariantCulture)).Append(",\"warn\":");
            JsonStr(js, warn).Append(",\"a\":");
            JsonVec(js, InAxes(points[n])).Append(",\"b\":");
            JsonVec(js, InAxes(points[n + 1])).Append('}');
        }
        js.Append("],\"pending\":");
        if (!Done && index == 1) JsonVec(js, InAxes(points[GroupStart(group)])); else js.Append("null");
        AppendMenuResultJson(js);
        monitorBody = js.ToString();
    }

    /// <summary>
    /// ★11-17 사용자 지시: 관제 화면도 컨트롤러UI처럼(홈 메뉴 목록 · 설명).
    ///   menu = {on, cur, ask, labels, desc, hint} — desc는 헤드셋과 같은 글(TMP 태그 그대로 · 화면이 HTML로 바꾼다).
    ///   result = {on, eval, title, sub, score, mean, n, cells[12] = {v 평가 · b 실습 · e 오차}} — 결과 창과 같은 함수(FillCurrent · RomLineRepeatScore.Total)로 낸다.
    ///   Refresh(바뀔 때만)에서 부른다 — RefreshRepeat보다 먼저 불려도 같은 값이 나오게 여기서 직접 계산한다.
    /// </summary>
    private void AppendMenuResultJson(StringBuilder js)
    {
        bool menuOn = home && !calibrating && !freeMode;
        js.Append(",\"menu\":{\"on\":").Append(menuOn ? "true" : "false").Append(",\"cur\":").Append(menuCursor)
          .Append(",\"ask\":").Append(menuAsk ? "true" : "false").Append(",\"labels\":[");
        for (int i = 0; i < MenuLabel.Length; i++) { if (i > 0) js.Append(','); JsonStr(js, MenuLabel[i]); }
        js.Append("],\"desc\":");
        JsonStr(js, menuOn ? MenuDesc(menuCursor, menuAsk) : "").Append(",\"hint\":");
        JsonStr(js, menuOn ? (menuAsk ? MenuAskHint : MenuListHint) : "").Append('}');

    }

    /// <summary>모니터로 보내는 값 한 칸 — 없으면 null, 있으면 {"v":각,"o":반대쪽으로 움직였나}.</summary>
    private static StringBuilder AppendResultJson(StringBuilder js, int motion, RomLineResult r)
    {
        if (!r.valid) return js.Append("null");
        return js.Append("{\"v\":").Append(r.angle.ToString("F1", CultureInfo.InvariantCulture)).Append(",\"o\":")
                 .Append(RomLineMath.DirectionMatches((RomLineMotion)motion, r.signedAngle) ? "false" : "true").Append('}');
    }

    /// <summary>모니터로 보내는 탭 창 · 단계 멈춤의 상태 — 두 모드가 같은 이름으로 보낸다.</summary>
    private StringBuilder AppendPopupJson(StringBuilder js)
    {
        js.Append(",\"popup\":false,\"cursor\":").Append(CurTop)   // 탭 창은 없앴다(10-14) — 모니터가 읽는 이름만 남긴다
          .Append(",\"locked\":").Append(TabLocked ? "true" : "false").Append(",\"confirm\":").Append(!freeMode && confirmWait ? "true" : "false")
          .Append(",\"resetAsk\":").Append(menuAsk && menuCursor == MenuReset ? "true" : "false").Append(",\"confirmed\":\"");
        for (int m = 0; m < 6; m++) js.Append(confirmed[m] ? '1' : '0');
        return js.Append('"');
    }

    /// <summary>
    /// 탭 줄의 글자 — 탭 다섯의 이름뿐이다(10-14: 늘 보인다 · 메인 화면 아래와 안내 위 두 곳). 지금 탭만 색으로 가른다.
    /// 자유 측정 탭의 이름은 지금 고른 방식이다(홈에서 B로 바꾼다).
    /// </summary>
    private void RefreshTabs()
    {
        // ★10-14 사용자 지시: 재는 중에 못 가는 탭의 흐린 글자는 뺐다(못 가면 스틱을 밀 때 알림이 뜬다). 다 잰 탭의 초록 글자는 (같은 날 다시) 둔다.
        int cur = CurTop;
        for (int t = 0; t < RomLineView.PopupTabs; t++)
        {
            string name = TopName(t);
            view.SetTab(t, t >= 1 && t <= 3 && TabComplete(t - 1) ? "<color=#73E68C>" + name + "</color>" : name, t == cur, false);
        }
        // ★11-17 V0.4: 벽면 확인 창(ShowAsk)은 쓰지 않는다 — 확인 안내는 컨트롤러UI 설명 상자 + 벽면 안내 띠 한 줄(Refresh)
    }

    private static readonly List<Vector3> NoPoints = new List<Vector3>(0);
    private static readonly List<string> NoTexts = new List<string>(0);

    /// <summary>모니터로 보내는 탭 목록 — 맨 위 다섯(topTabs)과 자유 측정의 방식 셋(tabs · counts = 방식마다 찍어 둔 점 수).</summary>
    private StringBuilder AppendTabsJson(StringBuilder js)
    {
        js.Append(",\"topTabs\":[");
        for (int i = 0; i < RomLineView.PopupTabs; i++) { if (i > 0) js.Append(','); JsonStr(js, TopName(i)); }
        return js.Append("],\"tabs\":[\"3점\",\"2선\",\"연속\"],\"counts\":[")
                 .Append(freePts[0].Count).Append(',').Append(freePts[1].Count).Append(',').Append(freePts[2].Count).Append(']');
    }

    // ── 안내 음성 ── ★10-14 사용자 지시로 없앴다. 파일과 만드는 도구(romline_narration.py)는 남아 있다 — 되살리려면 git 0f449c1의 LoadNarration · Announce.
    //   짧은 효과음(찍음 · 취소 · 거부 · 누름 · 끝)은 그대로다.

    /// <summary>순서대로 재는 흐름의 기록 한 줄 — 자유 측정과 같은 열에 맞춘다(사건 = «순서»).</summary>
    private void CsvGuided(int m, int ph)
    {
        int n = GroupStart(GroupOf(m, ph));
        if (!has[n] || !has[n + 1]) return;
        int plane = m / 2;
        StringBuilder sb = monitorLive;
        sb.Length = 0;
        sb.Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',').Append(Time.unscaledTime.ToString("F1", CultureInfo.InvariantCulture))
          .Append(",순서,").Append(MotionName[m]).Append(',').Append(PhaseName[ph]).Append(',').Append(ph == 0 ? "" : "중립").Append(',')
          .Append((Vector3.Distance(points[n], points[n + 1]) * 1000f).ToString("F0", CultureInfo.InvariantCulture)).Append(',').Append(FreePlaneName[plane]).Append(',');
        if (ph == 0) sb.Append(",,,,,");
        else
        {
            int slot = m * 2 + ph - 1;
            bool ok = results[slot].valid;
            bool match = ok && RomLineMath.DirectionMatches((RomLineMotion)m, results[slot].signedAngle);
            sb.Append(ok ? MotionName[m] : "").Append(',')
              .Append(ok ? results[slot].angle.ToString("F1", CultureInfo.InvariantCulture) : "").Append(',')
              .Append(direct[slot].valid ? direct[slot].angle.ToString("F1", CultureInfo.InvariantCulture) : "").Append(',')
              .Append(ok && !match ? "반대쪽으로 움직인 값" : "").Append(",,");
        }
        for (int k = 0; k < 2; k++)
        {
            Vector3 v = points[n + k];
            sb.Append(v.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.z.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
        }
        sb.Append(axes.forward.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(axes.forward.z.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.x * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append((tipOffset.y * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.z * 1000f).ToString("F1", CultureInfo.InvariantCulture));
        string rowText = sb.ToString();
        csvRows.Add(rowText);
        // ★10-22 사용자 지시: 관제 «측정값 저장»은 결과표 값만 준다(ResultCsv) — 이 상세 기록은 기기 파일(개발용)에만 남긴다.
        if (csvPath == null) return;
        try { System.IO.File.AppendAllText(csvPath, rowText + "\n", new UTF8Encoding(false)); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[선실측] ★기록 파일에 못 적었다({e.Message}) — 파일 기록을 멈춘다.");
            csvPath = null;
        }
    }

    private void LogSummary(string why)
    {
        var sb = new StringBuilder(320);
        sb.Append("[선실측] 요약(").Append(why).Append(") — ");
        for (int m = 0; m < 6; m++)
        {
            sb.Append(MotionName[m]).Append(" 면 ")
              .Append(results[m * 2].valid ? results[m * 2].angle.ToString("F1") : "—").Append('/')
              .Append(results[m * 2 + 1].valid ? results[m * 2 + 1].angle.ToString("F1") : "—")
              .Append(" 직접 ")
              .Append(direct[m * 2].valid ? direct[m * 2].angle.ToString("F1") : "—").Append('/')
              .Append(direct[m * 2 + 1].valid ? direct[m * 2 + 1].angle.ToString("F1") : "—");
            if (m < 5) sb.Append(" · ");
        }
        sb.Append("  (능동/압박, 도)");
        if (compareGlGn)
        {
            sb.Append(" ‖ 비교 미간점~턱끝점 — ");
            for (int m = 0; m < 2; m++)
            {
                sb.Append(MotionName[m]).Append(" 면 ")
                  .Append(cmpResults[m * 2].valid ? cmpResults[m * 2].angle.ToString("F1") : "—").Append('/')
                  .Append(cmpResults[m * 2 + 1].valid ? cmpResults[m * 2 + 1].angle.ToString("F1") : "—")
                  .Append(" 직접 ")
                  .Append(cmpDirect[m * 2].valid ? cmpDirect[m * 2].angle.ToString("F1") : "—").Append('/')
                  .Append(cmpDirect[m * 2 + 1].valid ? cmpDirect[m * 2 + 1].angle.ToString("F1") : "—");
                if (m == 0) sb.Append(" · ");
            }
        }
        Debug.Log(sb.ToString());
    }

    // ★막힐 때만 말한다(2초에 한 번). 잘 돌면 한 줄도 안 남긴다.
    private void Silent(string reason)
    {
        if (reason == lastSilent && Time.unscaledTime < nextSilentLog) return;
        lastSilent = reason;
        nextSilentLog = Time.unscaledTime + 2f;
        Debug.LogWarning("[선실측] 멈춰 있다 — " + reason);
    }

    // ── 패스스루·소리(기존 실측과 같은 방식) ─────────────────────────
    private void EnablePassthrough()
    {
        // 이 씬은 패스스루 전용이라 우리가 켜고 씬이 내려갈 때 같이 사라진다(다른 씬과 공유하는 상태가 아니다).
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
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 알파 0이어야 뒤의 패스스루가 보인다
        }
        Debug.Log($"[선실측] 패스스루 — 레이어 {(layer != null ? "O" : "★없음")} · OVRManager {(OVRManager.instance != null ? "O" : "★없음")} · 카메라 {(cam != null ? "O" : "★없음")}");
    }

    private void BuildSounds()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;
        sndMark = Tone("찍음", 880f, 1320f, 0.11f);
        sndUndo = Tone("취소", 700f, 440f, 0.09f);
        sndDeny = Tone("거부", 300f, 300f, 0.12f);
        sndPress = Tone("누름", 1250f, 1250f, 0.045f);
        sndDone = Tone("끝", 660f, 1760f, 0.25f);
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
            phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
            float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);
            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        var clip = AudioClip.Create("[선실측] " + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip c)
    {
        if (audioSrc != null && c != null && soundVolume > 0f) audioSrc.PlayOneShot(c, soundVolume);
    }

    // ── 에디터 ──────────────────────────────────────────────────────
    /// <summary>끝점 맞추기용(에디터 인스펙터가 읽는다). 컨트롤러가 잡혀 있을 때만 뜻이 있다.</summary>
    public bool TipValid => tipValid;
    public Vector3 TipWorld => tipPos;
    public bool UsingLeft => active == OVRInput.Controller.LTouch;
    public Vector3 ControllerWorld => ctrlPos;
    public bool ControllerModelShown => modelsOn.Count > 0;

    // 끝점을 맞출 때만 켜는 컨트롤러 모형. 씬에서는 꺼 둔 채로 두고(패스스루로 실제 컨트롤러가 보인다 — 10-01),
    // 인스펙터 스위치가 Play 중에만 켠다. ★우리가 켠 것만 끈다. Play가 끝나면 씬 값(꺼짐)으로 돌아가므로 빌드에는 나오지 않는다.
    private readonly System.Collections.Generic.List<GameObject> modelsOn = new System.Collections.Generic.List<GameObject>();

    public void ShowControllerModel(bool on)
    {
        if (!on)
        {
            foreach (GameObject g in modelsOn) if (g != null) g.SetActive(false);
            modelsOn.Clear();
            Debug.Log("[선실측] 컨트롤러 모형 끔");
            return;
        }
        if (modelsOn.Count > 0) return;
        int found = 0;
        foreach (OVRControllerHelper helper in FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            found++;
            // 모형은 꺼진 부모(OVRControllerVisual) 아래에 있다 — 꺼져 있는 조상을 찾아 켠다.
            for (Transform a = helper.transform; a != null; a = a.parent)
                if (!a.gameObject.activeSelf)
                {
                    a.gameObject.SetActive(true);
                    modelsOn.Add(a.gameObject);
                }
        }
        Debug.Log($"[선실측] 컨트롤러 모형 켬 — 모형 {found}개 중 꺼져 있던 {modelsOn.Count}곳을 켰다" +
                  (found == 0 ? " ★씬에 컨트롤러 모형(OVRControllerHelper)이 없다" : modelsOn.Count == 0 ? " (이미 켜져 있었다)" : ""));
    }

    // 씬 창에서 컨트롤러 원점(세 축 3cm)과 찍는 점까지의 선을 본다 — 끝점 오프셋이 어느 쪽으로 얼마인지 눈으로 확인하는 용도.
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !tipValid) return;
        Gizmos.color = Color.red; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.right * 0.03f);
        Gizmos.color = Color.green; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.up * 0.03f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.forward * 0.03f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(ctrlPos, tipPos);
        Gizmos.DrawWireSphere(tipPos, 0.004f);
    }
}
