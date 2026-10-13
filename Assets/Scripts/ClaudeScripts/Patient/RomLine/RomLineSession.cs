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
///   A/X = 탭 창 여닫기. 띄운 채 스틱 좌우로 탭을 옮기면 <b>옮기는 대로</b> 그 탭이 된다. «리셋»만 A로 골라야 하고 확인 창이 한 번 더 뜬다(A = 지우기 · B = 취소).
///   ★탭을 <b>재는 중</b>(점이 하나라도 찍혔고 아직 다 재지 않았다)이면 스틱을 밀어도 탭이 바뀌지 않는다. 그만두려면 B/Y 길게로 그 탭을 지운다.
///   ★동작 하나(중립 · 능동 · 압박 세 직선)를 다 찍으면 <b>멈춘다</b>. A/X(확인)를 눌러야 다음 동작으로 넘어간다 — 신전을 확인하면 측굴이 저절로 시작된다.
///   지우기 셋: B/Y 짧게 = <b>직선 하나</b>(찍다 만 점이 있으면 그 점) · B/Y 1초 길게 = <b>지금 탭 전체</b> · 탭 창의 «리셋» = <b>전체 측정 결과</b>.
///   축 다시 잡기 = 스틱을 <b>눌러</b> 부르고, 스틱을 <b>밀어</b> 잡는다(두 단계 — 실수로 잡히지 않게). 방향은 <b>헤드셋이 보는 방향</b>이다
///     (10-08의 «컨트롤러가 가리키는 방향»을 되돌렸다 — 그때는 판의 버튼을 누르느라 헤드셋이 판을 봤다. 이제 판 버튼이 없다). 메인 화면도 그쪽에 다시 선다.
///   컨트롤러에 없는 일(끝점 보정 · 기준선 바꾸기 · 나가기)은 모니터(브라우저)의 버튼으로만 한다. 자유 측정의 동작 갈래는 탭 창을 닫은 채 스틱 좌우다.
///
/// ★자유 측정(10-07): 기준점도 정해진 순서도 없다. 두 점마다 선 하나, 개수 제한 없이 찍는다.
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
    // ★10-12 사용자 지시: 영어 약어(Tr · Gl · Gn)를 화면 · 음성 · 문서에 쓰지 않는다 — 받은 한글 명칭 그대로(미주점 · 미간점 · 턱끝점).
    //   판의 안내 글자 · 공간의 이름표 · 로그 · 안내 음성(romline_narration.py)이 전부 이 이름이다.
    public static readonly string[] PointName = { "오른쪽 미주점", "왼쪽 미주점", "미간점", "턱끝점" };
    private static readonly string[] TopTabName = { "굴곡 · 신전", "측굴", "회전", "리셋", "자유 측정" };   // 탭 창의 다섯(10-12 저녁). 3 = 리셋은 탭이 아니라 명령이다
    private static readonly string[] PhaseHint = { "환자 정면을 바닥 화살표에 맞추세요 · 정면을 본 편안한 자세", "환자가 스스로 끝까지 움직인 자세", "측정자가 같은 방향으로 조금 더 눌러 준 자세" };
    private static readonly string[] PhaseName = { "중립", "능동", "압박" };

    [Header("=== 컨트롤러 ===")]
    [Tooltip("컨트롤러 기준 «찍는 점»의 자리(m, 오른손 기준 — 왼손은 x를 뒤집어 쓴다). 노란 점이 여기에 뜬다.\n" +
             "★0.033 = 컨트롤러 모형 경계 상자의 맨 앞(브리지 romline-ctrl로 잰 값, 10-01). 실제 컨트롤러 앞면과 어긋나면 인스펙터로 맞춘다.")]
    [SerializeField] private Vector3 tipOffset = new Vector3(0f, 0f, 0.033f);

    [Header("=== 비교 시험 ===")]
    [Tooltip("켜면 굴곡·신전에서 한 자세마다 세 점(오른쪽 미주점 → 미간점 → 턱끝점)을 찍어 미주점~미간점 선과 미간점~턱끝점 선의 값을 나란히 낸다.\n" +
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

    [Header("=== 메인 화면(10-12) ===")]
    [Tooltip("헤드셋에서 메인 화면까지의 거리(m). 화면은 헤드셋이 보는 쪽에 선다 — 처음 한 번, 그리고 «축 다시 잡기»(스틱 누르고 밀기) 때마다 다시 선다.")]
    [SerializeField] private float screenDistance = 2.5f;
    [Tooltip("메인 화면의 폭(m). 높이는 폭의 0.475배다. 탭 창은 같은 폭으로 그 아래에 붙는다.")]
    [SerializeField] private float screenWidth = 2.5f;
    [Tooltip("메인 화면의 아랫변이 헤드셋 눈높이보다 높은 만큼(m). 환자 머리를 가리지 않게 띄운다. 탭 창은 이 아래로 내려온다(띄웠을 때만 보인다).")]
    [SerializeField] private float screenRise = 0.30f;

    [Header("=== 안내(10-13) ===")]
    [Tooltip("헤드셋에서 안내 글까지의 거리(m). 지금 할 일을 한 문장으로 보여 준다 — 판 없이 반투명 테두리와 글만 있고, 헤드셋이 보는 쪽을 따라온다.")]
    [SerializeField] private float guideDistance = 0.60f;
    [Tooltip("안내 글을 시선의 가운데보다 아래로 내리는 만큼(m). 찍는 자리를 가리면 더 내린다.")]
    [SerializeField] private float guideDrop = 0.22f;

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
    private int freeTab;                // 0 굴곡·신전 · 1 측굴 · 2 회전 — 갈래마다 점을 따로 모은다
    private readonly List<Vector3>[] freePts = { new List<Vector3>(64), new List<Vector3>(64), new List<Vector3>(64) };
    // 선마다 «견준 기준선의 번호». 자기 번호와 같으면 그 선이 기준선이다(첫 선, 그리고 «기준선 바꾸기»로 삼은 선).
    private readonly List<int>[] freeRefOf = { new List<int>(32), new List<int>(32), new List<int>(32) };
    private readonly RomLineFreeView freeView = new RomLineFreeView();
    private bool axesOn = true;
    private RomLineFrame axes;          // 공간에 고정된 세 축 — 양쪽 Tr이 하던 일을 대신한다
    private Vector3 axesOrigin;         // 바닥의 십자를 그리는 자리(방향만 쓰고 자리는 계산에 안 들어간다)
    private const float FreeMaxOffPlane = 75f;   // 선이 그 면에서 이보다 많이 들려 있으면 그 면의 값은 비운다(그림자가 짧아 값이 튄다)
    private int group;      // 0 = 기준(양쪽 Tr), 1~18 = 동작 × (중립·능동·압박), 19 = 끝
    private int index;      // 그 묶음 안에서 다음에 찍을 점

    // 입력
    private Transform trackingSpace;
    private Transform eye;
    private OVRInput.Controller active = OVRInput.Controller.RTouch;
    private bool tipValid;              // 헤드셋 카메라가 컨트롤러를 보고 있다(자리가 «추적» 상태) — 이때만 점을 찍는다
    private bool tipShown;              // 자리가 있긴 하다(못 보는 동안은 기기가 센서로 추정한 값) — 끝 구슬을 빨갛게라도 보여 준다
    private int trackState = -1;        // 연결·자리 추적·자리 유효·방향 추적을 묶은 값 — 바뀔 때 로그를 남긴다
    private float trackSince, trackLogNext;
    private Vector3 tipPos;
    private float prevTrigger;
    private Vector3 armedPos;
    private float armedTime = -10f;
    private Vector3 armedCtrlPos, markCtrlPos;                              // 찍는 순간의 컨트롤러 자세 — 로그에 남겨, 나중에 끝점을 바꿔 다시 계산해 볼 수 있게
    private Quaternion armedCtrlRot = Quaternion.identity, markCtrlRot = Quaternion.identity;

    // 모니터·기록(10-07)
    private readonly RomLineMonitor monitor = new RomLineMonitor();
    private readonly StringBuilder monitorSb = new StringBuilder(4096), monitorLive = new StringBuilder(4096);
    private string monitorBody = "";      // 상태 가운데 «바뀔 때만 만드는 부분» — Refresh가 만든다
    private float monitorNext;
    private readonly List<string> csvRows = new List<string>(128);
    private string csvPath;

    // 탭 창 · 단계 멈춤(10-12 저녁)
    private bool popupOn;
    private int popupCursor;              // 탭 창에서 스틱이 올라가 있는 탭(0 굴곡·신전 · 1 측굴 · 2 회전 · 3 리셋 · 4 자유 측정)
    private bool resetAsk;                // «전체 측정 결과를 지울까요?» 확인 창
    private bool confirmWait;             // 동작 하나(세 직선)를 다 찍었다 — A/X(확인)를 기다린다
    private readonly bool[] confirmed = new bool[6];   // 동작마다 확인까지 마쳤는가
    private bool axesAsk;                 // «축 다시 잡기»를 불렀다(스틱 누름) — 스틱을 밀면 잡는다
    private float axesAskStart;
    private const float AxesAskSeconds = 8f;
    private float undoHoldStart = -1f;    // B/Y — 짧게 = 직선 취소(뗄 때), 길게 = 이 탭 지우기
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
    private float trackingWaitStart = -1f;
    private float nextSilentLog;
    private string lastSilent;

    private AudioSource audioSrc;
    private AudioClip sndMark, sndUndo, sndDeny, sndPress, sndDone;

    private const float PinchClose = 0.02f, PinchOpen = 0.04f;   // 엄지–검지 끝 거리(m). 사이는 그대로 — 떨림으로 깜박이지 않게
    private const float ArmThreshold = 0.15f;      // 트리거를 이만큼 당기기 시작한 순간의 자리를 찍는 자리로 쓴다
    private const float TrackingWaitSeconds = 8f;

    private bool EyeReady => eye != null && eye.position.y > 0.3f;
    /// <summary>여섯 동작을 모두 확인까지 마쳤다. ★group은 이제 19(끝)로 가지 않는다 — 마지막에 확인한 동작의 압박에 머문다(10-12 저녁).</summary>
    private bool Done => confirmed[0] && confirmed[1] && confirmed[2] && confirmed[3] && confirmed[4] && confirmed[5];
    private int CurTab => MotionOf(Mathf.Clamp(group, 1, GroupCount - 1)) / 2;   // 0 굴곡·신전 · 1 측굴 · 2 회전
    private bool HasLine(int g) => has[GroupStart(g)] && has[GroupStart(g) + 1];
    private bool TabComplete(int tab) => confirmed[tab * 2] && confirmed[tab * 2 + 1];
    /// <summary>그 탭을 재는 중이다 — 점이 하나라도 찍혔고 아직 다 재지 않았다. 이 동안에는 다른 탭으로 못 간다.</summary>
    private bool TabLocked
    {
        get
        {
            if (freeMode) return false;
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
            case RomLineMotion.RotationRight: a = RomLinePoint.TrLeft; b = RomLinePoint.Gl; break; // 반대쪽(왼쪽) Tr ~ Gl
            default: a = RomLinePoint.TrRight; b = RomLinePoint.Gl; break;                         // 좌회전: 오른쪽 Tr ~ Gl
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
        BuildSounds();

        // ★10-08 사용자 지시: 탭 다섯 — 굴곡(3직선) · 신전(3) · 측굴(좌우 6) · 회전(좌우 6)을 <b>순서대로</b> 재고, 자유 측정은 «기타»다.
        //   기준(양쪽 Tr) 묶음(0)은 건너뛴다 — 면은 고정 3축으로 세운다(SetAxes가 frame에 넣는다). 처음은 굴곡 · 중립.
        //   ★비교 시험(compareGlGn)은 씬에 true로 굳어 있다. 굴곡 · 신전의 선이 Gl ~ Gn이 됐으니 뜻이 없어 여기서 끈다(세 점씩 찍게 되는 것을 막는다).
        compareGlGn = false;
        group = GroupOf(0, 0);
        freeMode = false;
        view.SetMode(false);
        freeView.SetVisible(true);   // 바닥의 3축 십자는 두 모드가 같이 쓴다

        if (monitorPort > 0)
        {
            var pageAsset = Resources.Load<TextAsset>("RomLine/monitor");
            if (monitor.Start(monitorPort, pageAsset != null ? pageAsset.text : ""))
            {
                view.SetFooter("모니터  " + monitor.Url);
                monitor.SetCsv(CsvHeader + "\n");
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

        // ★첫 프레임에 자리를 잡지 않는다 — 추적이 붙기 전엔 눈이 원점이라 판이 발밑에 선다(09-21 전례).
        if (!view.PanelPlaced)
        {
            if (!EyeReady)
            {
                if (trackingWaitStart < 0f) trackingWaitStart = Time.unscaledTime;
                if (Time.unscaledTime - trackingWaitStart < TrackingWaitSeconds) { Silent("헤드셋 추적을 기다린다"); return; }
            }
            view.PlacePanel(eye, screenDistance, screenWidth, screenRise);
            view.FollowGuide(guideDistance, guideDrop);
            Debug.Log($"[선실측] 메인 화면을 세웠다 — 눈 {eye.position:F2} · 화면 {view.Panel.position:F2} · 거리 {screenDistance:F1}m · 폭 {screenWidth:F1}m · 눈높이 위 {screenRise:F2}m");
            CaptureAxes("처음", false);
        }

        ReadController();
        ReadHands();
        HandleInput();

        if (dirty) Refresh();
        PumpMonitor();
        view.Tick(eye, tipShown, tipValid, tipPos, freeMode || (!confirmWait && !TabComplete(CurTab)));
        freeView.Tick(eye);
    }

    private void OnDestroy()
    {
        if (hapticOff > 0f) OVRInput.SetControllerVibration(0f, 0f, active);
        if (simultaneousOn) OVRInput.DisableSimultaneousHandsAndControllers();   // 우리가 켠 것만 되돌린다
        monitor.Stop();
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
        OVRInput.Controller other = active == OVRInput.Controller.RTouch ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        if (AnyDown(other))
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
        if (calibrating) { SampleCalibration(); return; }

        // ── 축 다시 잡기를 불러 둔 동안 — 스틱을 밀면(어느 쪽이든) 잡는다. 다른 버튼 · 트리거는 그만두기(점은 찍지 않는다) ──
        if (axesAsk)
        {
            if (stickPush)
            {
                stickReady = false;
                axesAsk = false;
                Buzz(0.08f, 0.6f);
                view.PlacePanel(eye, screenDistance, screenWidth, screenRise);
                CaptureAxes("스틱", true);
            }
            else if (aDown || undoTap || undoLong || triggerDown) SetAxesAsk(false, "다른 버튼");
            else if (Time.unscaledTime - axesAskStart > AxesAskSeconds) SetAxesAsk(false, "시간이 다 됐다");
            return;
        }

        // ── A/X — 리셋 확인 > 탭 창 닫기(«리셋» 위에서는 확인 창 띄우기) > 동작 확인 > 탭 창 열기 ──
        if (aDown)
        {
            if (resetAsk)
            {
                Buzz(0.12f, 0.8f);
                ResetAll("탭 창의 리셋");
                SetPopup(false, "리셋", true);
            }
            else if (popupOn && popupCursor == 3)
            {
                resetAsk = true;
                Debug.Log("[선실측] 리셋 확인 창 — A = 지우기 · B = 취소");
                Play(sndPress);
                dirty = true;
            }
            else if (popupOn) SetPopup(false, "A/X 버튼");
            else if (!freeMode && confirmWait) Confirm("A/X 버튼");
            else SetPopup(true, "A/X 버튼");
            return;
        }

        // ── B/Y ──
        if (undoTap || undoLong)
        {
            if (resetAsk)
            {
                resetAsk = false;
                Debug.Log("[선실측] 리셋 확인 — 취소(B/Y)");
                Play(sndUndo);
                dirty = true;
            }
            else if (undoLong)
            {
                Debug.Log($"[선실측] B/Y 길게({ResetHoldSeconds:F1}초) — {(freeMode ? "이 동작 지우기(자유 측정)" : "이 탭 지우기")}");
                Buzz(0.12f, 0.8f);
                if (freeMode) FreeClear(); else ResetTab(CurTab, "B/Y 길게");
            }
            else Undo();
            return;
        }

        // ── 스틱 좌우 — 탭 창을 띄운 동안에는 탭 고르기, 닫은 채로는 자유 측정의 동작 갈래만 ──
        if (stickPush && Mathf.Abs(stick.x) > 0.7f && Mathf.Abs(stick.y) < 0.5f)
        {
            stickReady = false;
            int d = stick.x > 0f ? 1 : -1;
            if (popupOn) MoveCursor(d);
            else if (freeMode) SetFreeTab(Mathf.Clamp(freeTab + d, 0, 2));
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

    // ── 끝점 보정 ───────────────────────────────────────────────────
    private void BeginCalibration()
    {
        calibrating = true;
        calibStart = Time.unscaledTime;
        calibNextText = 0f;
        calibRange = 0f;
        calibPos.Clear();
        calibRot.Clear();
        noteText = "끝점 보정 — 끝을 한 점에 대고 여러 방향으로 돌리세요";
        Debug.Log($"[선실측] 끝점 보정 시작 — 지금 값 {tipOffset * 1000f:F1}mm · 끝을 움직이지 않는 한 점에 댄 채 컨트롤러를 여러 방향으로 돌린다(스틱을 다시 누르면 끝)");
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
        noteText = $"끝점 보정 중 — 표본 {calibPos.Count} · 돌린 범위 {calibRange:F0}° · 스틱을 다시 누르면 끝";
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
                  $"고정점 {pivot:F3} · {(apply ? "★이번 Play에 적용했다(Play를 멈추면 씬 값에 옮겨 적는다)" : "적용 안 함 — 흔들림이 크다(끝이 미끄러졌거나 추적이 흔들렸다)")}");
        if (apply)
        {
            tipOffset = asRight;
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

    /// <summary>찍는 순간의 컨트롤러 자세와 시각 — 찍음 로그 끝에 붙인다(끝점을 바꿔 다시 계산하거나, 추적이 끊긴 때와 견줄 수 있게).</summary>
    private string PoseSuffix()
    {
        FromHeadset(out float dist, out float side, out float up);
        return $" · 컨트롤러 자리 {markCtrlPos:F4} 회전 {markCtrlRot:F4} · 헤드셋에서 {dist:F0} 좌우 {side:+0;-0}° 위아래 {up:+0;-0}° · 추적된 지 {Time.unscaledTime - trackSince:F1}초 · t {Time.unscaledTime:F1}s";
    }

    // ── 동작 ────────────────────────────────────────────────────────
    private void Mark(Vector3 pos, float shake)
    {
        SetPopup(false, "점 찍기", true);   // 재기 시작하면 결과표로 돌아간다
        if (freeMode) { FreeMark(pos, shake); return; }
        if (confirmWait) { Deny($"{MotionName[MotionOf(group)]}을 다 쟀다 — A 버튼으로 확인한다(고치려면 B = 직선 취소)"); return; }
        if (TabComplete(CurTab))
        {
            Deny(Done ? "측정이 끝났다 — 다시 재려면 B 길게(이 탭 지우기)나 탭 창의 리셋"
                      : $"{TopTabName[CurTab]} 탭은 다 쟀다 — A로 다른 탭을 고르거나 B 길게로 이 탭을 지운다");
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
                Deny("양쪽 미주점이 위아래로 겹쳐 찍혔다 — 기준을 다시 찍는다");
                return;
            }
            Debug.Log($"[선실측] 기준 방향 — 오른쪽 {frame.right:F2} · 앞뒤 {frame.forward:F2} · 위 {frame.up:F2} · " +
                      $"미주점 간격 {Vector3.Distance(points[0], points[1]) * 1000f:F0}");
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
            // ★10-12 저녁: 동작 하나(세 직선)를 다 찍으면 멈춘다 — A/X(확인)를 눌러야 다음 동작으로 넘어간다. group은 압박에 머문다.
            confirmWait = true;
            Play(sndDone);
            Debug.Log($"[선실측] 확인 대기 — {MotionName[MotionOf(group)]}의 세 직선을 다 찍었다 · A/X = 확인 → {NextName(MotionOf(group))}");
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
        noteText = "양쪽 미주점이 바뀌어 찍혀 바로잡았다";
        Debug.Log("[선실측] ★양쪽 미주점을 바꿔 찍은 것으로 보인다(미간점이 뒤쪽에 있었다) — 좌우를 바로잡았다.");
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
            Debug.Log($"[선실측] 값 — {title} 직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 1000f:F0} · 면 위 —(기준 미주점을 안 찍었다)");
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
            sb.Append(" · 기준 오른쪽 미주점 ").Append(points[0].ToString("F4")).Append(" 왼쪽 미주점 ").Append(points[1].ToString("F4"));
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
        Debug.Log($"[선실측] 비교 — {title} · 미주점~미간점 직접 {td.angle:F1}° · 미간점~턱끝점 직접 {cd.angle:F1}° (엇갈림 {cd.gap * 1000f:F0}) · " +
                  $"미간점~턱끝점 면 위 {(cr.valid ? cr.angle.ToString("F1") + "°" : "—(기준 미주점 없음)")} · " +
                  $"선 길이 미간점~턱끝점 중립 {Vector3.Distance(points[n0 + 1], extra[gN]) * 1000f:F0} → 측정 {Vector3.Distance(points[s0 + 1], extra[gS]) * 1000f:F0} · " +
                  $"미주점~미간점 중립 {Vector3.Distance(points[n0], points[n0 + 1]) * 1000f:F0} → 측정 {Vector3.Distance(points[s0], points[s0 + 1]) * 1000f:F0}");
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

    /// <summary>탭 창을 여닫는다. 열 때 스틱 자리는 지금 탭이다. quiet = 소리 없이(점을 찍어 닫힐 때 — 찍는 소리와 겹친다).</summary>
    private void SetPopup(bool on, string why, bool quiet = false)
    {
        if (resetAsk) { resetAsk = false; dirty = true; }   // 탭 창이 여닫히면 리셋 확인도 접는다
        if (popupOn == on) return;
        popupOn = on;
        if (on) popupCursor = freeMode ? 4 : CurTab;
        Debug.Log($"[선실측] 탭 창 {(on ? "열림" : "닫힘")} ({why}) — {(freeMode ? "자유 측정 · " + FreeTabName[freeTab] : GroupTitle(group))}{(on && TabLocked ? " · 재는 중이라 탭을 옮길 수 없다" : "")}");
        if (!quiet) Play(sndPress);
        dirty = true;
    }

    /// <summary>탭 창에서 스틱을 민다. «리셋»(3) 위에서는 멈춰 있기만 한다 — A를 눌러야 확인 창이 뜬다. 나머지는 옮기는 대로 그 탭이 된다.</summary>
    private void MoveCursor(int d)
    {
        if (resetAsk) return;   // 확인 창이 떠 있는 동안은 A · B만 듣는다
        int next = Mathf.Clamp(popupCursor + d, 0, 4);
        if (next == popupCursor) return;
        if (TabLocked) { Deny(LockText()); return; }
        popupCursor = next;
        if (next == 3)
        {
            Debug.Log("[선실측] 탭 창 — 스틱이 «리셋» 위에 있다(A를 눌러야 묻는다)");
            Play(sndPress);
        }
        else GoTab(next, "스틱");
        dirty = true;
    }

    private string LockText() => $"{TopTabName[CurTab]} 탭을 재는 중이다 — 다 잰 뒤에 옮긴다(그만두려면 B 길게 = 이 탭 지우기)";

    /// <summary>탭을 옮긴다 — 0 굴곡·신전 · 1 측굴 · 2 회전 · 4 자유 측정. 재는 중인 탭이 있으면 받지 않는다(false).</summary>
    private bool GoTab(int tab, string why)
    {
        int cur = freeMode ? 4 : CurTab;
        if (tab == cur) return true;
        if (tab == 3 || tab < 0 || tab > 4) return false;
        if (TabLocked) { Deny(LockText()); return false; }
        if (tab == 4) { SetFreeMode(true); return true; }
        SetFreeMode(false);
        Resume(tab, why);
        return true;
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
        Debug.Log($"[선실측] 탭 → {TopTabName[tab]} · {(TabComplete(tab) ? "다 잰 탭" : GroupTitle(group) + (confirmWait ? "(확인 대기)" : ""))} ({why})");
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
            if (!TabComplete(t)) return TopTabName[t];
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
        Debug.Log($"[선실측] 확인 — {MotionName[m]} · 능동 {(results[m * 2].valid ? results[m * 2].angle.ToString("F1") : "—")}° · " +
                  $"압박 {(results[m * 2 + 1].valid ? results[m * 2 + 1].angle.ToString("F1") : "—")}° ({why})");
        Buzz(0.06f, 0.6f);
        int next = NextTab(tab);
        if (next >= 0) { Resume(next, "확인"); return; }

        group = GroupOf(tab * 2 + 1, 2);
        noteText = "";
        Play(sndDone);
        LogSummary("끝");
        dirty = true;
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
    private static readonly string[] FreeTabName = { "굴곡·신전", "측굴", "회전" };
    private static readonly string[] FreePlaneName = { "옆면", "앞면", "바닥면" };
    // 면마다 부호가 +일 때 / −일 때의 이름(RomLineMath.PositiveMeans: 굴곡 · 좌측굴 · 우회전이 +)
    private static readonly string[] FreePlus = { "굴곡", "좌측굴", "우회전" };
    private static readonly string[] FreeMinus = { "신전", "우측굴", "좌회전" };
    private const float FreeReversedAngle = 150f;   // 기준선과 이보다 크게 벌어지면 두 점의 순서가 반대일 수 있다고 알린다
    private const float FreeJudgeMinAngle = 10f;    // 이보다 작게 움직인 선은 «어느 동작처럼 움직였나»를 가르지 않는다(찍는 오차가 더 크다)

    private List<Vector3> FreePts => freePts[freeTab];
    private List<int> FreeRefs => freeRefOf[freeTab];
    /// <summary>회전의 면(바닥)은 중력만 쓰므로 3축을 꺼도 나온다. 굴곡·신전과 측굴의 면은 3축이 있어야 한다.</summary>
    private bool FreePlaneOn => freeTab == 2 || axesOn;

    private void SetFreeMode(bool on)
    {
        if (freeMode == on) return;
        freeMode = on;
        noteText = "";
        view.SetMode(on);
        announced = -1;                              // 순서 탭으로 돌아오면 그 단계의 안내를 다시 들려준다
        if (on && narrSrc != null) narrSrc.Stop();
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
        Debug.Log($"[선실측] 자유 — 동작 → {FreeTabName[tab]}(찍어 둔 점 {FreePts.Count}개)");
        Play(sndPress);
        dirty = true;
    }

    /// <summary>
    /// 고정 3축을 지금 헤드셋이 보는 방향으로 잡는다. 환자는 사용자(헤드셋)를 마주 본다고 놓는다 — 정면 = 헤드셋 쪽.
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
        noteText = "축을 다시 잡았다 — 헤드셋이 보는 쪽이 환자 · 메인 화면도 그쪽에 다시 세웠다";
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

    /// <summary>지금 갈래에서 새 선이 견줄 기준선의 번호(가장 최근에 기준선으로 삼은 선). 아직 없으면 -1.</summary>
    private int FreeCurrentRef()
    {
        List<int> refs = FreeRefs;
        for (int i = refs.Count - 1; i >= 0; i--)
            if (refs[i] == i) return i;
        return -1;
    }

    private void FreeMark(Vector3 pos, float shake)
    {
        List<Vector3> pts = FreePts;
        pts.Add(pos);
        int n = pts.Count, line = (n + 1) / 2;
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 찍음 · 선 {line}의 {(n % 2 == 1 ? "첫" : "둘째")} 점 · 자리 {pos:F4} · 당기는 동안 끝점 이동 {shake * 1000f:F1}mm" + PoseSuffix());
        if (n % 2 == 0)
        {
            if (Vector3.Distance(pts[n - 2], pts[n - 1]) < 0.005f)
            {
                pts.RemoveAt(n - 1);
                Deny("두 점이 같은 자리에 찍혔다 — 둘째 점을 다시 찍는다");
                return;
            }
            int idx = line - 1, rf = FreeCurrentRef();
            FreeRefs.Add(rf < 0 ? idx : rf);   // 첫 선은 스스로 기준선이다
            LogFreeLine(idx);
            CsvAdd("선", idx);
        }
        noteText = "";
        Play(sndMark);
        Buzz(0.06f, 0.6f);
        dirty = true;
    }

    /// <summary>직선 단위 취소(10-12 저녁) — 찍다 만 선이 있으면 그 첫 점만, 없으면 마지막 선(두 점)을 통째로.</summary>
    private void FreeUndo()
    {
        List<Vector3> pts = FreePts;
        if (pts.Count == 0) { Deny("지울 직선이 없다"); return; }
        if (pts.Count % 2 == 1) pts.RemoveAt(pts.Count - 1);
        else
        {
            CsvAdd("취소", FreeRefs.Count - 1);
            FreeRefs.RemoveAt(FreeRefs.Count - 1);
            pts.RemoveRange(pts.Count - 2, 2);
        }
        noteText = "";
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 직선 취소 → 남은 점 {pts.Count}개");
        Play(sndUndo);
        dirty = true;
    }

    private void FreeClear()
    {
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 이 동작을 지웠다(점 {FreePts.Count}개)");
        if (FreePts.Count > 0) CsvAdd("지움", -1);
        FreePts.Clear();
        FreeRefs.Clear();
        noteText = "";
        Play(sndUndo);
        dirty = true;
    }

    /// <summary>방금 찍은 선을 새 기준선으로 삼는다 — 그 뒤에 찍는 선은 이 선과 견준다(앞서 찍은 선의 값은 그대로 둔다).</summary>
    private void FreeRebase()
    {
        int last = FreeRefs.Count - 1;
        if (last < 0) { Deny("기준선으로 삼을 선이 없다 — 선을 먼저 찍는다"); return; }
        if (FreeRefs[last] == last) { Deny($"선 {last + 1}은 이미 기준선이다"); return; }
        FreeRefs[last] = last;
        CsvAdd("기준선으로", last);
        noteText = $"선 {last + 1}을 기준선으로 삼았다";
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 기준선 → 선 {last + 1}(길이 {FreeLength(last) * 1000f:F0})");
        Play(sndPress);
        dirty = true;
    }

    private float FreeLength(int line) => Vector3.Distance(FreePts[line * 2], FreePts[line * 2 + 1]);
    private Vector3 FreeDir(int line) => FreePts[line * 2 + 1] - FreePts[line * 2];

    /// <summary>선 line이 선 from에서 벌어진 각 — 3차원 그대로(«직접»).</summary>
    private RomLineDirect FreeDirect(int from, int line)
    {
        List<Vector3> p = FreePts;
        return RomLineMath.MeasureDirect(p[from * 2], p[from * 2 + 1], p[line * 2], p[line * 2 + 1]);
    }

    /// <summary>
    /// 선 line이 선 from에서 돌아간 각을 한 면에 내려 읽는다. plane 0 옆면(굴곡·신전) · 1 앞면(측굴) · 2 바닥면(회전).
    /// 선이 그 면에서 너무 들려 있으면(<see cref="FreeMaxOffPlane"/>) false — 그림자가 짧아 값이 튄다.
    /// ★6동작과 같은 함수(<see cref="RomLineMath.Measure"/> · <see cref="RomLineMath.PlaneNormal"/>)를 탄다.
    /// </summary>
    private bool FreePlane(int from, int line, int plane, out RomLineResult r)
    {
        List<Vector3> p = FreePts;
        r = RomLineMath.Measure(p[from * 2], p[from * 2 + 1], p[line * 2], p[line * 2 + 1], RomLineMath.PlaneNormal((RomLineMotion)(plane * 2), axes));
        return r.valid && r.neutralOffPlane <= FreeMaxOffPlane && r.markOffPlane <= FreeMaxOffPlane;
    }

    private static string FreeNamed(int plane, RomLineResult r)
    {
        return (r.signedAngle >= 0f ? FreePlus[plane] : FreeMinus[plane]) + " " + r.angle.ToString("F1") + "°";
    }

    /// <summary>
    /// 그 선에 붙일 알림. 두 점의 순서가 반대로 보이거나, 고른 동작과 다른 동작처럼 움직였을 때만 글자가 있다(없으면 빈 글자).
    /// ★알리기만 한다 — 값을 고치거나 찍기를 막지 않는다(회전은 실제로 90°를 넘고, 동작이 섞이는 것도 실제 일이다).
    /// </summary>
    private string FreeWarning(int from, int line, float directAngle, out int judged, out float first, out float second)
    {
        judged = RomLineMath.DominantPlane(FreeDir(from), FreeDir(line), axes, out first, out second);
        if (directAngle > FreeReversedAngle) return "순서 반대?";
        if (!axesOn || judged < 0 || judged == freeTab || directAngle < FreeJudgeMinAngle) return "";   // 3축을 껐으면 맞춰 놓지 않은 것이라 가르지 않는다
        return FreeTabName[judged] + "으로 보임";
    }

    private void LogFreeLine(int line)
    {
        int rf = FreeRefs[line];
        var sb = new StringBuilder(320);
        sb.Append("[선실측] 자유(").Append(FreeTabName[freeTab]).Append(") — 선 ").Append(line + 1)
          .Append(" · 길이 ").Append((FreeLength(line) * 1000f).ToString("F0"));
        if (rf == line) { sb.Append(" · 기준선"); Debug.Log(sb.ToString()); return; }

        RomLineDirect d = FreeDirect(rf, line);
        sb.Append(" ‖ 기준선(선 ").Append(rf + 1).Append(")과 직접 ").Append(d.angle.ToString("F1")).Append("°(엇갈림 ").Append((d.gap * 1000f).ToString("F0")).Append(")");
        // 그 동작의 면이 먼저, 나머지 두 면은 «다른 면»으로 — 화면에는 그 동작의 면만 나온다. 로그에는 나중에 견줘 볼 수 있게 셋 다 남긴다.
        for (int k = 0; k < 3; k++)
        {
            int p = (freeTab + k) % 3;
            bool ok = FreePlane(rf, line, p, out RomLineResult r);
            sb.Append(k == 0 ? " · " : k == 1 ? " ‖ 다른 면 " : " · ").Append(FreePlaneName[p]).Append(' ').Append(ok ? FreeNamed(p, r) : "—")
              .Append("(면 밖 ").Append(r.neutralOffPlane.ToString("F0")).Append("→").Append(r.markOffPlane.ToString("F0")).Append("°)");
        }
        string warn = FreeWarning(rf, line, d.angle, out int judged, out float first, out float second);
        sb.Append(" ‖ 움직인 모양 ").Append(judged < 0 ? "가를 수 없음" : FreeTabName[judged]).Append('(').Append(first.ToString("F2")).Append(" / ").Append(second.ToString("F2")).Append(')');
        if (warn.Length > 0) sb.Append(" ★").Append(warn);
        if (!axesOn) sb.Append(" ‖ 3축 끔");
        Debug.Log(sb.ToString());
    }

    private void RefreshFree()
    {
        dirty = false;
        List<Vector3> pts = FreePts;
        List<int> refs = FreeRefs;
        int lines = pts.Count / 2;
        bool half = pts.Count % 2 == 1;

        string note = noteText;   // 아랫줄은 알림이 있을 때만(10-13)
        string instruction = $"선 {lines + 1}의 {(half ? "둘째" : "첫")} 점을 찍으세요";
        view.SetTip("선 " + (lines + 1));
        view.SetGuide("자유 측정 · " + FreeTabName[freeTab] + " · <color=#FFF233>선 " + (lines + 1) + "</color>", note);
        view.SetFreeTab(freeTab);
        RefreshPopup();

        freeView.SetPoints(pts, refs);
        freeView.SetTab(freeTab, FreePlaneOn, FreePlaneName[freeTab] + " 각");
        freeView.ShowAxes(axesOn, axesOrigin, axes);

        // 모니터로 내보낼 상태 — 판과 같은 값을 같은 자리에서 만든다(따로 계산하면 두 화면이 어긋난다).
        StringBuilder js = monitorSb;
        js.Length = 0;
        js.Append("\"mode\":\"free\",\"top\":4,\"tab\":").Append(freeTab);
        AppendTabsJson(js).Append(",\"instruction\":");
        JsonStr(js, instruction).Append(",\"note\":");
        JsonStr(js, note);
        AppendPopupJson(js).Append(",\"axesOn\":").Append(axesOn ? "true" : "false").Append(",\"planeOn\":").Append(FreePlaneOn ? "true" : "false").Append(",\"plane\":");
        JsonStr(js, FreePlaneName[freeTab]).Append(",\"lines\":[");

        // 판 — 가장 최근 선들. 한 줄에 선 하나: 길이 · 그 동작의 면 각 · 직접 각 · 알림. 모니터에는 선을 전부 보낸다.
        int firstRow = Mathf.Max(0, lines - RomLineFreeView.Rows);
        string label = "";
        Vector3 at = Vector3.zero;
        for (int line = 0; line < lines; line++)
        {
            int row = line - firstRow, rf = refs[line];
            float len = FreeLength(line) * 1000f;   // mm
            string plane = "", warn = "";
            float directAngle = 0f;
            if (rf != line)
            {
                RomLineDirect d = FreeDirect(rf, line);
                directAngle = d.angle;
                plane = !FreePlaneOn ? "" : FreePlane(rf, line, freeTab, out RomLineResult r) ? FreeNamed(freeTab, r) : "—";
                warn = FreeWarning(rf, line, d.angle, out _, out _, out _);
                if (line == lines - 1)
                {
                    // 환자 위 — 가장 최근 측정선 곁에 값을 띄운다(면 각이 있으면 그것, 없으면 직접 각).
                    label = plane.Length > 1 ? plane : "직접 " + d.angle.ToString("F1") + "°";
                    at = (pts[line * 2] + pts[line * 2 + 1]) * 0.5f + Vector3.up * 0.04f;
                }
            }
            if (row >= 0)
            {
                string name = $"선 {line + 1}  {len:F0}";
                if (rf == line) freeView.SetRow(row, name, "기준선", "", "", true);
                else freeView.SetRow(row, name, plane, directAngle.ToString("F1") + "°", warn, false);
            }

            if (line > 0) js.Append(',');
            js.Append("{\"n\":").Append(line + 1).Append(",\"ref\":").Append(rf == line ? "true" : "false").Append(",\"refOf\":").Append(rf + 1)
              .Append(",\"len\":").Append(len.ToString("F0", CultureInfo.InvariantCulture)).Append(",\"plane\":");
            JsonStr(js, plane.Length > 1 ? plane : "").Append(",\"direct\":").Append(directAngle.ToString("F1", CultureInfo.InvariantCulture)).Append(",\"warn\":");
            JsonStr(js, warn).Append(",\"a\":");
            JsonVec(js, InAxes(pts[line * 2])).Append(",\"b\":");
            JsonVec(js, InAxes(pts[line * 2 + 1])).Append('}');
        }
        for (int row = Mathf.Max(0, lines - firstRow); row < RomLineFreeView.Rows; row++) freeView.SetRow(row, "", "", "", "", false);
        freeView.SetAngleLabel(label, at);

        js.Append("],\"pending\":");
        if (half) JsonVec(js, InAxes(pts[pts.Count - 1])); else js.Append("null");
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
            else if (c < ' ') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"');
    }

    /// <summary>
    /// 모니터 화면이 보낸 명령을 처리하고, 보는 화면이 있을 때만 0.1초에 한 번 상태를 건넨다.
    /// 상태 = «바뀔 때만 만드는 부분»(monitorBody — Refresh가 만든다) + «늘 바뀌는 부분»(컨트롤러 끝·추적 여부).
    /// </summary>
    private void PumpMonitor()
    {
        if (monitor.Port == 0) return;
        while (monitor.TryDequeue(out string command)) RunMonitorCommand(command);
        if (!monitor.Watched || Time.unscaledTime < monitorNext) return;   // 보는 화면이 없으면 글자를 만들지 않는다
        monitorNext = Time.unscaledTime + 0.1f;

        StringBuilder js = monitorLive;
        js.Length = 0;
        js.Append("{\"t\":").Append(Time.unscaledTime.ToString("F1", CultureInfo.InvariantCulture))
          .Append(",\"tracked\":").Append(tipValid ? "true" : "false").Append(",\"calib\":").Append(calibrating ? "true" : "false").Append(",\"tip\":");
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
            case "top0": case "top1": case "top2": case "top4": GoTab(command[3] - '0', "모니터"); break;
            case "undo": Undo(); break;
            case "popup": SetPopup(!popupOn, "모니터"); break;
            case "confirm": if (!freeMode && confirmWait) Confirm("모니터"); else Deny("확인할 것이 없다 — 동작 하나의 세 직선을 다 찍은 뒤에 누른다"); break;
            case "clear": if (freeMode) FreeClear(); else ResetTab(CurTab, "모니터"); break;
            case "resetAll": ResetAll("모니터"); break;
            case "rebase": if (freeMode) FreeRebase(); break;
            case "axes":   // 모니터의 버튼은 한 번에 잡는다(화면에서 따로 되묻는다)
                view.PlacePanel(eye, screenDistance, screenWidth, screenRise);
                CaptureAxes("모니터", true);
                break;
            case "calib": if (calibrating) EndCalibration("모니터"); else BeginCalibration(); break;
            case "exit": Exit(); break;
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
        if (line >= 0 && line < FreeRefs.Count)
        {
            int rf = FreeRefs[line];
            List<Vector3> p = FreePts;
            sb.Append(line + 1).Append(',').Append(rf + 1).Append(',').Append((FreeLength(line) * 1000f).ToString("F0", CultureInfo.InvariantCulture)).Append(',').Append(FreePlaneName[freeTab]).Append(',');
            if (rf == line) sb.Append(",,,,,");
            else
            {
                RomLineDirect d = FreeDirect(rf, line);
                bool ok = FreePlaneOn && FreePlane(rf, line, freeTab, out RomLineResult r0);
                FreePlane(rf, line, freeTab, out RomLineResult r);
                string warn = FreeWarning(rf, line, d.angle, out int judged, out _, out _);
                sb.Append(ok ? (r.signedAngle >= 0f ? FreePlus[freeTab] : FreeMinus[freeTab]) : "").Append(',')
                  .Append(ok ? r.angle.ToString("F1", CultureInfo.InvariantCulture) : "").Append(',')
                  .Append(d.angle.ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(warn).Append(',').Append(judged < 0 ? "" : FreeTabName[judged]).Append(',');
            }
            for (int k = 0; k < 2; k++)
            {
                Vector3 v = p[line * 2 + k];
                sb.Append(v.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(v.z.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            }
        }
        else sb.Append(",,,,,,,,,,,,,,,");
        sb.Append(axes.forward.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(axes.forward.z.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.x * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append((tipOffset.y * 1000f).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
          .Append((tipOffset.z * 1000f).ToString("F1", CultureInfo.InvariantCulture));
        string rowText = sb.ToString();
        csvRows.Add(rowText);
        monitor.SetCsv(CsvHeader + "\n" + string.Join("\n", csvRows) + "\n");
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
        Debug.Log($"[선실측] 이 탭 지우기 — {TopTabName[tab]} ({why})");
        Play(sndUndo);
        dirty = true;
    }

    /// <summary>전체 측정 결과를 지운다 — 순서 측정의 18직선과 자유 측정의 선 전부. 기록 파일(CSV)의 줄은 남는다. 굴곡 · 중립으로 돌아간다.</summary>
    private void ResetAll(string why)
    {
        for (int i = 0; i < PointCount; i++) has[i] = false;
        for (int i = 0; i < GroupCount; i++) hasExtra[i] = false;
        for (int i = 0; i < 6; i++) confirmed[i] = false;
        frame = axes;   // 고정 3축은 그대로다
        for (int i = 0; i < 12; i++) ClearResult(i);
        int keep = freeTab, freeLines = 0;
        for (int t = 0; t < 3; t++)
        {
            freeTab = t;                       // CsvAdd가 지금 갈래의 이름으로 적는다
            freeLines += freeRefOf[t].Count;
            if (freePts[t].Count > 0) CsvAdd("지움", -1);
            freePts[t].Clear();
            freeRefOf[t].Clear();
        }
        freeTab = keep;
        resetAsk = false;
        confirmWait = false;
        SetFreeMode(false);
        group = 1;      // 굴곡 · 중립부터(묶음 0 = 기준은 쓰지 않는다)
        index = 0;
        popupCursor = 0;
        noteText = "";
        Debug.Log($"[선실측] 리셋 — 전체 측정 결과를 지웠다(자유 측정의 선 {freeLines}개 포함) ({why})");
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
    private string GroupTitle(int g)
    {
        if (g <= 0) return "기준(쓰지 않음)";
        if (g >= GroupCount) return "측정 끝";
        return MotionName[MotionOf(g)] + " · " + PhaseName[PhaseOf(g)];
    }

    private void Refresh()
    {
        if (freeMode) { RefreshFree(); return; }
        dirty = false;

        int tab = CurTab, cm = MotionOf(group);
        string instruction, tipText = "", guide, note = noteText, forMonitor;
        // ★10-13 사용자 지시: 안내는 한 문장이다 — «○○ · 중립 / 능동 / 압박을 측정하세요». 어느 점을 찍으라는 말은 하지 않는다(컨트롤러 끝의 이름표에만 남는다).
        //   동작 하나를 다 재면 «측정 완료 · 중립으로 돌아간 뒤 A = 확인». 아랫줄은 알림이 있을 때만 쓴다.
        if (Done)
        {
            forMonitor = instruction = "모든 측정 완료";
            guide = "<color=#73E68C>모든 측정 완료</color>";
        }
        else if (confirmWait)
        {
            // 동작 하나를 다 찍었다 — 확인을 기다린다
            forMonitor = instruction = $"{MotionName[cm]} 측정 완료 · 중립으로 돌아간 뒤 A = 확인";
            guide = $"<color=#73E68C>{MotionName[cm]} 측정 완료</color> · 중립으로 돌아간 뒤 A = 확인";
        }
        else if (TabComplete(tab))
        {
            forMonitor = instruction = guide = $"{TopTabName[tab]} 측정 완료";
        }
        else
        {
            RomLinePoint p = ExpectedPoint(group, index);
            instruction = $"{MotionName[cm]} · {PhaseName[PhaseOf(group)]}을 측정하세요";
            tipText = PointName[(int)p];
            guide = $"{MotionName[cm]} · <color=#FFF233>{PhaseName[PhaseOf(group)]}</color>을 측정하세요";
            forMonitor = instruction + $"  ({PointName[(int)p]} {index + 1}/{SizeOf(group)})";   // 모니터에는 찍을 점도 남긴다
        }
        view.SetTip(tipText);
        view.SetGuide(guide, note);
        RefreshGuidedExtras(tab, forMonitor, note);
        RefreshPopup();

        // 결과표(10-12) — 한 줄에 동작 하나: 능동 · 압박의 «면 위 각»과 그 동작의 단면에서 본 각도기.
        //   ★그림이 따로 계산하지 않는다 — 숫자와 같은 값(results)을 그린다. 반대쪽으로 움직인 값은 로그 · 알림과 같은 함수(DirectionMatches)로 가른다.
        int current = group >= 1 && !Done && PhaseOf(group) > 0 ? MotionOf(group) * 2 + PhaseOf(group) - 1 : -1;
        view.SetRowNow(Done ? -1 : MotionOf(group));
        for (int m = 0; m < 6; m++)
        {
            RomLineResult a = results[m * 2], pr = results[m * 2 + 1];
            bool aOpp = a.valid && !RomLineMath.DirectionMatches((RomLineMotion)m, a.signedAngle);
            bool pOpp = pr.valid && !RomLineMath.DirectionMatches((RomLineMotion)m, pr.signedAngle);
            view.SetValue(m * 2, a.valid ? a.angle.ToString("F1") + "°" : "—", m * 2 == current, aOpp);
            view.SetValue(m * 2 + 1, pr.valid ? pr.angle.ToString("F1") + "°" : "—", m * 2 + 1 == current, pOpp);
            view.SetGauge(m, a.valid, a.angle, aOpp, pr.valid, pr.angle, pOpp);
        }

        // 점 — 기준 둘은 늘, 그 밖은 지금 동작 것만.
        int motion = group == 0 ? -1 : MotionOf(Mathf.Min(group, GroupCount - 1));
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
        string pre = cmp ? "미주점~미간점 " : "";   // 비교 시험 중에는 어느 선의 값인지 글자에 밝힌다

        // 선·각도기 — 지금 동작 것만.
        if (motion < 0) { view.HideMeasure(); return; }
        int n0 = GroupStart(GroupOf(motion, 0)), a0 = GroupStart(GroupOf(motion, 1)), p0 = GroupStart(GroupOf(motion, 2));
        if (!has[n0] || !has[n0 + 1]) { view.HideMeasure(); return; }

        RomLineResult ra = results[motion * 2], rp = results[motion * 2 + 1];
        RomLineDirect da = direct[motion * 2], dp = direct[motion * 2 + 1];
        if (ra.valid)
            view.ShowMeasure(ra, rp.valid, rp, pre + ra.angle.ToString("F1") + "°", rp.valid ? "압박 " + rp.angle.ToString("F1") + "°" : "");
        else if (da.valid)
        {
            view.ShowDirect(points[n0], points[n0 + 1], points[a0], points[a0 + 1], pre + da.angle.ToString("F1") + "°",
                            dp.valid, points[p0], points[p0 + 1], dp.valid ? "압박 " + dp.angle.ToString("F1") + "°" : "");
        }
        else view.ShowNeutralOnly(points[n0], points[n0 + 1]);   // 중립 기준선만 — 직선만 보인다
    }

    /// <summary>순서대로 재는 흐름의 나머지 표시 — 진행 줄 · 바닥의 3축 · 안내 음성 · 모니터로 보낼 상태.</summary>
    private void RefreshGuidedExtras(int tab, string instruction, string note)
    {
        int m = MotionOf(Mathf.Min(group, GroupCount - 1));
        int plane = m / 2;

        // 바닥의 3축 — 자유 측정의 점·선은 지우고 십자만 남긴다(찍어 둔 자유 측정 점은 그대로 있다. «기타»로 가면 다시 그린다).
        freeView.SetPoints(NoPoints, NoRefs);
        freeView.SetAngleLabel("", Vector3.zero);
        freeView.SetTab(plane, true, FreePlaneName[plane] + " 각");
        freeView.ShowAxes(true, axesOrigin, axes);

        Announce();

        // 모니터 — 지금 동작의 세 선(중립 · 능동 · 압박)을 자유 측정과 같은 모양으로 보낸다.
        StringBuilder js = monitorSb;
        js.Length = 0;
        js.Append("\"mode\":\"guided\",\"top\":").Append(tab).Append(",\"tab\":").Append(plane);
        AppendTabsJson(js).Append(",\"instruction\":");
        JsonStr(js, instruction).Append(",\"note\":");
        JsonStr(js, note).Append(",\"axesOn\":true,\"planeOn\":true,\"plane\":");
        JsonStr(js, FreePlaneName[plane]);

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
        monitorBody = js.ToString();
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
        js.Append(",\"popup\":").Append(popupOn ? "true" : "false").Append(",\"cursor\":").Append(popupCursor)
          .Append(",\"locked\":").Append(TabLocked ? "true" : "false").Append(",\"confirm\":").Append(!freeMode && confirmWait ? "true" : "false")
          .Append(",\"resetAsk\":").Append(resetAsk ? "true" : "false").Append(",\"confirmed\":\"");
        for (int m = 0; m < 6; m++) js.Append(confirmed[m] ? '1' : '0');
        return js.Append('"');
    }

    /// <summary>
    /// 탭 창의 글자 — 탭 다섯의 이름뿐이다(스틱이 올라간 탭 · 지금 탭 · 다 잰 탭은 색으로 가른다).
    /// 닫혀 있으면 글자를 만들지 않는다.
    /// </summary>
    private void RefreshPopup()
    {
        view.ShowPopup(popupOn);
        if (!popupOn) return;
        int cur = freeMode ? 4 : CurTab;
        bool locked = TabLocked;
        for (int t = 0; t < RomLineView.PopupTabs; t++)
        {
            // 탭 이름만 쓴다(10-12: 아래의 «완료» · 진행 글자와 설명 세 줄을 뺐다). 다 잰 탭은 이름을 초록으로 칠한다.
            string label = t < 3 && TabComplete(t) ? "<color=#73E68C>" + TopTabName[t] + "</color>" : TopTabName[t];
            view.SetPopupTab(t, label, t == popupCursor, t == cur, locked && t != cur);
        }
        view.ShowResetAsk(resetAsk);
    }

    private static readonly List<Vector3> NoPoints = new List<Vector3>(0);
    private static readonly List<int> NoRefs = new List<int>(0);

    /// <summary>모니터로 보내는 탭 목록 — 맨 위 다섯(topTabs)과 자유 측정의 갈래 셋(tabs · counts).</summary>
    private StringBuilder AppendTabsJson(StringBuilder js)
    {
        js.Append(",\"topTabs\":[");
        for (int i = 0; i < TopTabName.Length; i++) { if (i > 0) js.Append(','); JsonStr(js, TopTabName[i]); }
        return js.Append("],\"tabs\":[\"굴곡·신전\",\"측굴\",\"회전\"],\"counts\":[")
                 .Append(freePts[0].Count).Append(',').Append(freePts[1].Count).Append(',').Append(freePts[2].Count).Append(']');
    }

    // ── 안내 음성(10-08) ────────────────────────────────────────────
    //   단계가 바뀔 때마다 한 문장. 파일은 .claude/tools/romline_narration.py가 만든다(m{동작}_p{단계} · done).
    //   ★파일이 없으면 에러 없이 무음이다 — 그래서 읽은 개수와, 틀 때마다 어느 파일인지를 로그에 남긴다.
    private AudioSource narrSrc;
    // 안내창에 띄우는 글 — ★안내 음성(.claude/tools/romline_narration.py의 texts())과 <b>같은 문장</b>이어야 한다. 한쪽을 고치면 다른 쪽도 고친다.
    private static readonly string[] NarrName = { "굴곡", "신전", "오른쪽 측굴", "왼쪽 측굴", "오른쪽 회전", "왼쪽 회전" };
    private static readonly string[] NarrPoints = { "미간점과 턱끝점", "미간점과 턱끝점", "미간점과 턱끝점", "미간점과 턱끝점", "왼쪽 미주점과 미간점", "오른쪽 미주점과 미간점" };
    private static readonly string[] NarrMove = { "고개를 앞으로 끝까지 숙이게", "고개를 뒤로 끝까지 젖히게", "고개를 오른쪽 어깨 쪽으로 끝까지 기울이게",
                                                  "고개를 왼쪽 어깨 쪽으로 끝까지 기울이게", "고개를 오른쪽으로 끝까지 돌리게", "고개를 왼쪽으로 끝까지 돌리게" };

    private static string NarrationText(int m, int ph)
    {
        if (ph == 0) return $"{NarrName[m]}을 측정합니다. 환자가 정면을 본 편안한 자세에서, {NarrPoints[m]}을 차례로 찍으세요.";
        if (ph == 1) return $"환자가 스스로 {NarrMove[m]} 한 뒤, {NarrPoints[m]}을 차례로 찍으세요.";
        return $"측정자가 같은 방향으로 조금 더 눌러 준 상태에서, {NarrPoints[m]}을 차례로 찍으세요.";
    }

    private readonly AudioClip[] narr = new AudioClip[19];
    private int announced = -1;

    private void LoadNarration()
    {
        narrSrc = gameObject.AddComponent<AudioSource>();
        narrSrc.playOnAwake = false;
        narrSrc.spatialBlend = 0f;
        int found = 0;
        for (int i = 0; i < 18; i++)
        {
            narr[i] = Resources.Load<AudioClip>($"RomLine/Narration/m{i / 3}_p{i % 3}");
            if (narr[i] != null) found++;
        }
        narr[18] = Resources.Load<AudioClip>("RomLine/Narration/done");
        if (narr[18] != null) found++;
        if (found == narr.Length) Debug.Log($"[선실측] 안내 음성 {found}개를 읽었다");
        else Debug.LogWarning($"[선실측] ★안내 음성 {narr.Length}개 중 {found}개만 읽었다 — 없는 단계는 무음이다(Resources/RomLine/Narration).");
    }

    private void Announce()
    {
        // 확인을 기다리는 동안과 다 잰 탭을 보고 있는 동안은 말하지 않는다(-2) — 그 단계의 안내를 다시 틀지 않게.
        int key = Done ? 18 : confirmWait || TabComplete(CurTab) ? -2 : group - 1;
        if (key == announced) return;
        announced = key;
        if (narrSrc == null || key < 0) return;
        narrSrc.Stop();
        AudioClip c = key >= 0 && key < narr.Length ? narr[key] : null;
        Debug.Log($"[선실측] 안내 음성 — {GroupTitle(group)} · {(c != null ? c.name : "★파일 없음(무음)")}");
        if (c == null || soundVolume <= 0f) return;
        narrSrc.clip = c;
        narrSrc.volume = soundVolume;
        narrSrc.Play();
    }

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
        monitor.SetCsv(CsvHeader + "\n" + string.Join("\n", csvRows) + "\n");
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
        LoadNarration();
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
