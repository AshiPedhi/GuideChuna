using UnityEngine;

/// <summary>
/// 설정 리모컨 — 환자 모델을 방향 버튼으로 옮긴다 (2026-10-27 사용자 지시 · 시안 V0.7 그림 6).
///
/// [버튼 8개] 자동 맞춤 · 위 · 아래 · 좌 · 우 · 앞 · 뒤 · 완료. 닫기(✕)는 두지 않는다(사용자 지시).
/// [자리] 켜질 때 한 번, 사용자 앞 약간 아래(forward · below)에 놓고 헤드셋을 향한다. 따라오지 않는다.
/// [방향] ★움직이는 모델 자신의 축이 기준이다 — "어디서 앞을 누르든 모델이 앞으로 가야지"(사용자).
///   앞뒤 · 좌우 = <see cref="axisReference"/>의 로컬 축을 바닥 평면에 투영한 것. 위아래 = 월드 수직.
///   ★축을 서로 외적으로 엮지 않는다(CLAUDE.md 규칙 9) — 앞 · 오른쪽을 각각 따로 고른다.
///   ★어느 로컬 축이 사람이 보는 «앞»인지는 Play에서 재서 정한다. 누를 때마다 월드 방향을 로그로 찍는다.
/// [누름] 짧게 = stepMeters 한 번 · holdDelay 넘게 누르고 있으면 holdSpeed로 계속(<see cref="PatientPositionRemoteButton"/>).
/// [정보창과 별개] 상단 바 · 정보 디스플레이를 켜거나 끄지 않는다.
///
/// 이동 대상은 <see cref="PracticeSettingsController.GetActualMoveTarget"/> — 자동 맞춤이 옮기는 것과 같은 오브젝트다.
/// </summary>
public class PatientPositionRemote : MonoBehaviour
{
    public enum Direction { Up, Down, Left, Right, Forward, Back }   // ★끝에만 추가(씬에 int로 직렬화)
    public enum LocalAxis { PosX, NegX, PosY, NegY, PosZ, NegZ }    // ★끝에만 추가

    [Header("연결")]
    [SerializeField] private PracticeSettingsController practiceSettings;
    [Tooltip("이 리모컨을 연 설정 버튼의 주인. [완료]가 설정 버튼을 끄게 한다.")]
    [SerializeField] private InfoPanelController infoPanel;
    [Tooltip("비우면 PracticeSettingsController의 헤드셋을 쓴다")]
    [SerializeField] private Transform headset;

    [Header("자리 (켜질 때 한 번)")]
    [SerializeField] private float spawnForward = 0.5f;
    [SerializeField] private float spawnBelow = 0.3f;
    [Tooltip("10-30 앉은 환자: 목에서 등 뒤로(m) ★추정값 — 헤드셋에서 조정")]
    [SerializeField] private float spawnBehindNeck = 0.25f;
    [Tooltip("10-30 앉은 환자: 목에서 아래로(m)")]
    [SerializeField] private float spawnBelowNeck = 0.10f;
    [Tooltip("10-30 판이 보는 쪽에서 위로 젖혀진 각(°) — 0 = 똑바로 섬 · 90 = 하늘을 봄. 내려다보며 읽게 45")]
    [SerializeField] private float viewTiltDeg = 45f;
    [Tooltip("10-30 누운 환자: 머리에서 베드 끝 쪽으로(m)")]
    [SerializeField] private float spawnBeyondHead = 0.35f;
    [Tooltip("10-30 누운 환자: 머리 위로(m)")]
    [SerializeField] private float spawnAboveHead = 0.15f;
    [Tooltip("11-03 누운 환자 · 사용자가 몸 옆에 설 때: 가슴에서 사용자 쪽으로(m) ★추정값 — 헤드셋에서 조정")]
    [SerializeField] private float spawnBesideBody = 0.45f;
    [Tooltip("11-03 척추 축(목 → 머리)의 수직 성분이 이보다 작으면 누움으로 본다. 0.6 ≈ 수평에서 37° 이내")]
    [SerializeField] private float lyingSpineMaxY = 0.6f;

    [Header("이동")]
    [SerializeField] private float stepMeters = 0.01f;
    [SerializeField] private float holdDelay = 0.35f;
    [SerializeField] private float holdSpeed = 0.10f;

    [Header("방향 기준 — 모델 자신의 축 (Play에서 재서 맞출 것)")]
    [Tooltip("비우면 이동 대상 자신")]
    [SerializeField] private Transform axisReference;
    [SerializeField] private LocalAxis forwardAxis = LocalAxis.PosZ;
    [SerializeField] private LocalAxis rightAxis = LocalAxis.PosX;

    [SerializeField] private bool showDebugLog = true;

    private bool holding;
    private Direction holdDir;
    private float holdStart;

    private void OnEnable()
    {
        holding = false;
        if (practiceSettings != null)
            practiceSettings.ForceDisablePatientPositionController();   // 옛 그랩 핸들이 매 프레임 덮어쓰지 않게
        PlaceInFrontOfUser();
    }

    private void OnDisable() => holding = false;

    private void Update()
    {
        if (!holding) return;
        if (Time.unscaledTime - holdStart < holdDelay) return;
        Move(holdDir, holdSpeed * Time.unscaledDeltaTime, false);
    }

    // ── 버튼이 부른다 ──
    public void Press(Direction d)
    {
        Move(d, stepMeters, true);
        holding = true;
        holdDir = d;
        holdStart = Time.unscaledTime;
    }

    public void Release(Direction d)
    {
        if (holding && holdDir == d && showDebugLog)
        {
            Transform t = MoveTarget();
            if (t != null) ChunaLogger.Log($"[PositionRemote] {d} 뗌 → 대상 위치 {t.position:F3}");
        }
        if (holdDir == d) holding = false;
    }

    public void AutoFit()
    {
        holding = false;
        if (practiceSettings == null) { ChunaLogger.LogWarning("[PositionRemote] practiceSettings 없음 — 자동 맞춤 건너뜀"); return; }
        practiceSettings.ApplyCustomPositioningOnce();
    }

    public void Done()
    {
        holding = false;
        Transform t = MoveTarget();
        if (showDebugLog && t != null) ChunaLogger.Log($"[PositionRemote] 완료 — 대상 {t.name} 위치 {t.position:F3}");
        if (infoPanel != null) infoPanel.CloseSettingsPopup();   // 설정 버튼 색 · 상태까지 같이 돌아온다
        else gameObject.SetActive(false);
    }

    // ── 내부 ──
    private Transform MoveTarget() => practiceSettings != null ? practiceSettings.GetActualMoveTarget() : null;

    private void Move(Direction d, float meters, bool log)
    {
        Transform t = MoveTarget();
        if (t == null) { if (log) ChunaLogger.LogWarning("[PositionRemote] 이동 대상 없음 — practiceSettings · targetObject 확인"); return; }

        Vector3 dir = WorldDirection(d, t);
        if (dir.sqrMagnitude < 1e-6f)
        {
            if (log) ChunaLogger.LogWarning($"[PositionRemote] {d} 방향이 수직이라 바닥 투영이 0 — axisReference의 {(d == Direction.Forward || d == Direction.Back ? forwardAxis : rightAxis)} 축을 바꿀 것");
            return;
        }
        Vector3 before = t.position;
        t.position += dir * meters;
        if (log && showDebugLog)
            ChunaLogger.Log($"[PositionRemote] {d} {meters * 100f:F1}cm · 월드 방향 {dir:F2} · {before:F3} → {t.position:F3}");
    }

    private Vector3 WorldDirection(Direction d, Transform target)
    {
        if (d == Direction.Up) return Vector3.up;
        if (d == Direction.Down) return Vector3.down;

        Transform r = axisReference != null ? axisReference : target;
        Vector3 v = (d == Direction.Forward || d == Direction.Back) ? Axis(r, forwardAxis) : Axis(r, rightAxis);
        if (d == Direction.Back || d == Direction.Left) v = -v;
        v.y = 0f;   // ★높이는 위/아래 버튼만 바꾼다 — 앞뒤 · 좌우의 수직 성분은 버린다
        return v.sqrMagnitude < 1e-6f ? Vector3.zero : v.normalized;
    }

    private static Vector3 Axis(Transform r, LocalAxis a)
    {
        switch (a)
        {
            case LocalAxis.PosX: return r.right;
            case LocalAxis.NegX: return -r.right;
            case LocalAxis.PosY: return r.up;
            case LocalAxis.NegY: return -r.up;
            case LocalAxis.PosZ: return r.forward;
            default: return -r.forward;
        }
    }

    private void PlaceInFrontOfUser()
    {
        Transform h = headset;
        if (h == null && practiceSettings != null) h = practiceSettings.HeadsetTransform;
        if (h == null) { ChunaLogger.LogWarning("[PositionRemote] 헤드셋 없음 — 자리를 옮기지 않는다"); return; }

        // ★10-30 사용자 «리모컨은 환자 등 뒤쪽 목 아래에 생성 · 버튼으로 움직여도 따라가지 말고» — 켜질 때 한 번만 환자 기준으로 놓는다.
        //   앉음 = 목에서 등 뒤로 spawnBehindNeck · 아래로 spawnBelowNeck / 누움(얼굴이 위 · 아래) = 머리 쪽 베드 끝 위(사용자 선택).
        //   방향은 PatientLocator가 뼈에서 잰다(얼굴 앞 = 머리 → 두 눈). 환자를 못 찾으면 종전대로 사용자 앞 아래
        if (PatientLocator.TryGet(out var pp) && pp.hasBones)
        {
            // ★10-30 사용자 «목 뒤쪽에 내가 내려다보면 보이게 · 그냥 고정된 각도 방향으로» — 방향은 머리를 따라가지 않고 환자 기준으로 고정:
            //   판이 «보는 쪽»(앉음 = 환자 등 쪽 · 누움 = 머리 쪽 베드 끝 바깥)을 향해 위로 viewTiltDeg 젖혀진 독서대 꼴
            Vector3 pos, viewSide; string how;
            // ★11-03 누움 판정 = 척추 축(목 → 머리)이 수평에 가까운가. 종전엔 얼굴 앞의 y(|face.y| ≥ 0.7)로 갈랐는데
            //   기기 실측 누운 환자 얼굴 앞 (0.01, 0.53, −0.85)(베개로 턱이 들림)이 «앉음»으로 빠져
            //   리모컨이 가슴 위에 · 발 쪽을 보고 생겼다(사용자 «누운 환자에게 리모컨 위치 불편 · 키 방향 이상»).
            Vector3 spine = pp.head - pp.neck;
            if (spine.sqrMagnitude < 1e-6f) spine = pp.head - pp.center;
            bool lying = spine.sqrMagnitude > 1e-6f && Mathf.Abs(spine.normalized.y) < lyingSpineMaxY;
            Vector3 back = -pp.face; back.y = 0f;
            if (!lying && back.sqrMagnitude > 1e-4f)
            {
                viewSide = back.normalized;
                pos = pp.neck + viewSide * spawnBehindNeck + Vector3.down * spawnBelowNeck; how = "앉음 · 목 뒤";
            }
            else
            {
                // 누움(바로 · 엎드려 · 옆으로) = 머리 쪽 베드 끝 · 몸 왼쪽 · 몸 오른쪽 셋 중 «사용자가 선 쪽»에 놓고 사용자를 향한다.
                //   자리는 환자 축에 맞춰 고정(머리를 따라가지 않음 — 10-30 사용자 «고정된 각도 방향») · 고르는 것만 헤드셋 쪽으로.
                Vector3 toHead = spine; toHead.y = 0f;
                toHead = toHead.sqrMagnitude > 1e-6f ? toHead.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, toHead);            // 몸 축에 수직인 수평 방향(부호는 아래에서 사용자 쪽으로)
                Vector3 toUser = h.position - pp.neck; toUser.y = 0f;
                float alongHead = Vector3.Dot(toUser, toHead), alongSide = Vector3.Dot(toUser, side);
                if (alongHead >= Mathf.Abs(alongSide))
                {
                    viewSide = toHead;
                    pos = pp.head + viewSide * spawnBeyondHead + Vector3.up * spawnAboveHead; how = "누움 · 머리 쪽 베드 끝 위";
                }
                else
                {
                    viewSide = alongSide >= 0f ? side : -side;
                    Vector3 chest = Vector3.Lerp(pp.neck, pp.center, 0.5f);
                    pos = chest + viewSide * spawnBesideBody; pos.y = pp.head.y + spawnAboveHead; how = "누움 · 몸 옆(사용자 쪽)";
                }
            }
            float tilt = viewTiltDeg * Mathf.Deg2Rad;
            Vector3 toViewer = viewSide * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt);   // 판 앞면이 보는 쪽 · 위를 향한다
            // ★10-30 기기 실측: 자동 맞춤이 추적 전 값으로 돌아 환자가 바닥 밑(목 y −0.29)에 가자 리모컨도 y −0.44에 떠 안 보였다
            //   → 눈보다 1.2m 넘게 낮으면(= 환자 자리가 이상) 환자 기준을 버리고 종전대로 사용자 앞 아래
            if (pos.y > h.position.y - 1.2f)
            {
                transform.position = pos;
                // UI 캔버스는 +Z가 화면 뒤쪽(종전 LookRotation(판 − 눈)과 같은 약속) → +Z = −보는 쪽
                transform.rotation = Quaternion.LookRotation(-toViewer, Vector3.up);
                Debug.Log($"[PositionRemote] 리모컨 소환 {pos:F2} — {how} · 보는 쪽 {viewSide:F2} · 위로 {viewTiltDeg}° · 얼굴 앞 {pp.face:F2} · 목 {pp.neck:F2} · 눈 {h.position:F2}");
                return;
            }
            Debug.Log($"[PositionRemote] 환자 기준 자리 {pos:F2}가 눈({h.position.y:F2})보다 1.2m 넘게 낮다 — 사용자 앞 아래로 대신");
        }
        Vector3 fwd = h.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        transform.position = h.position + fwd * spawnForward + Vector3.down * spawnBelow;
        // 판이 눈을 향하도록(아래에 있으니 위로 젖혀진다). UI 캔버스는 +Z가 화면 뒤쪽이다.
        transform.rotation = Quaternion.LookRotation(transform.position - h.position, Vector3.up);
        if (showDebugLog) ChunaLogger.Log($"[PositionRemote] 리모컨 소환 {transform.position:F2} (앞 {spawnForward}m · 아래 {spawnBelow}m)");
    }
}
