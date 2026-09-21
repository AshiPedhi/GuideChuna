using UnityEngine;

/// <summary>실측 기록 프로그램의 단계. ★씬에 직렬화되지 않는다(런타임 상태) — 그래도 끝에만 붙인다.</summary>
public enum RomRecordStep
{
    Setup = 0,          // 기준선 세팅 — 3축의 Y 회전·높이
    Landmarks = 1,      // 대추·미간(중립)·목 중앙 보정
    Flexion = 2,        // 굴곡 — 1회째 능동, 2회째 수동
    Extension = 3,      // 신전 — 같음
    LateralFlexion = 4, // 측굴 — 4회, 좌우 자동, 홀수 능동·짝수 수동
    Rotation = 5,       // 회전 — 같음
    Done = 6,
}

/// <summary>찍힌 동작 마커 하나.</summary>
public class RomRecordMark
{
    public Vector3 raw;        // 핀치로 고정된 자리(월드). 수정해도 이 값은 안 바뀐다 — 원래 찍힌 값을 남긴다.
    public float adjustDeg;    // 1° 단위 수정량(+ = 각이 커지는 쪽)
    // ★바늘로 맞춰 기록한 것인가(2026-09-21). 점찍기와 <b>따로</b> 비교하려고 구분해 남긴다 — 사용자가 둘 다 테스트한다.
    public bool byNeedle;
    public float dialDeg;      // 각도기 눈금으로 읽은 각(연직 0 · 회전은 정면 0). 육안 판독값과 같은 수다.
    public bool passive;       // false = 능동, true = 수동
    public int side;           // 측굴·회전: +1 환자 오른쪽, -1 환자 왼쪽, 0 = 해당 없음
    public Transform visual;   // 표시용 구체
}

/// <summary>
/// 실측 기록의 각도 계산 — <b>순수 계산만 한다</b>(표시·입력과 분리). 미리보기·기록이 모두 이 함수를 탄다(규칙 9).
///
/// ★정의(2026-09-18 사용자 확정)
///   · 값 = <b>중립 기준선(회전 중심 → 미간)에서 움직인 양</b>. 부호 없이 양수.
///   · 각도기 눈금의 0은 단면의 연직축(굴곡·신전·측굴) / 정면(회전) — 표시용일 뿐 값에는 안 들어간다.
///   · 각은 동작의 단면에 투영해서 잰다: 굴곡·신전 = 시상면, 측굴 = 관상면, 회전 = 횡단면.
///
/// ★기저는 서로 외적으로 엮지 않는다(규칙 9). 위 = <b>월드 연직</b> 그대로, 앞 = 사용자가 돌린 수평 방향,
///   오른쪽만 둘에서 나온다(둘 다 독립 입력이라 딸려 뒤집힐 것이 없다).
/// </summary>
public static class RomRecordGeometry
{
    public static readonly Vector3 Up = Vector3.up;

    /// <summary>yaw(도)로 환자 정면 방향(수평)을 만든다.</summary>
    public static Vector3 Forward(float yawDeg) => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;

    /// <summary>
    /// 환자 오른쪽. ★Unity에서 Cross(up, forward)는 forward를 바라볼 때 오른쪽이다.
    /// ★그래도 <b>Play에서 축 끝 글자("환자 오른쪽")로 눈으로 확인한다</b> — 부호는 추론으로 확정하지 않는다(규칙 9).
    /// </summary>
    public static Vector3 Right(float yawDeg) => Vector3.Cross(Up, Forward(yawDeg));

    /// <summary>그 단계의 단면 법선. 굴곡·신전 = 좌우축, 측굴 = 전후축, 회전 = 연직축.</summary>
    public static Vector3 PlaneNormal(RomRecordStep step, float yawDeg)
    {
        switch (step)
        {
            case RomRecordStep.Flexion:
            case RomRecordStep.Extension: return Right(yawDeg);
            case RomRecordStep.LateralFlexion: return Forward(yawDeg);
            case RomRecordStep.Rotation: return Up;
            default: return Right(yawDeg);
        }
    }

    /// <summary>각도기 눈금의 0 방향. 연직(굴곡·신전·측굴) / 정면(회전).</summary>
    public static Vector3 ScaleZero(RomRecordStep step, float yawDeg)
        => step == RomRecordStep.Rotation ? Forward(yawDeg) : Up;

    /// <summary>
    /// 중립선과 마커선이 단면 안에서 이루는 각(양수). 둘 중 하나라도 단면에 거의 안 걸리면 0.
    /// </summary>
    public static float PlaneAngle(Vector3 pivot, Vector3 neutral, Vector3 mark, Vector3 normal)
    {
        Vector3 a = Vector3.ProjectOnPlane(neutral - pivot, normal);
        Vector3 b = Vector3.ProjectOnPlane(mark - pivot, normal);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.Angle(a, b);
    }

    /// <summary>
    /// 마커가 중립에서 환자 오른쪽(+1)으로 갔나 왼쪽(-1)으로 갔나. 측굴·회전의 좌우 판별.
    /// 머리를 오른쪽으로 기울이거나 돌리면 미간이 환자 오른쪽으로 간다 — 그 이동 성분의 부호다.
    /// ★<paramref name="flip"/>은 Play에서 방향이 반대로 나오면 켜는 손잡이다(인스펙터).
    /// </summary>
    public static int Side(Vector3 neutral, Vector3 mark, float yawDeg, bool flip)
    {
        float d = Vector3.Dot(mark - neutral, Right(yawDeg));
        int s = d >= 0f ? 1 : -1;
        return flip ? -s : s;
    }

    /// <summary>
    /// 바늘이 환자 오른쪽(+1)을 가리키나 왼쪽(-1)을 가리키나. 측굴·회전의 좌우 판별.
    /// ★<see cref="Side"/>와 달리 <b>미간이 없어도</b> 된다 — 바늘은 미간 없이도 쓰는 수단이다(09-21).
    /// </summary>
    public static int SideOfDirection(Vector3 dir, float yawDeg, bool flip)
    {
        int s = Vector3.Dot(dir, Right(yawDeg)) >= 0f ? 1 : -1;
        return flip ? -s : s;
    }

    /// <summary>
    /// 단면에 투영한 단위 방향. 단면에 거의 안 걸리면 <paramref name="fallback"/>을 돌려준다.
    /// ★바늘(2026-09-21)은 <b>각이 아니라 방향</b>으로 들고 있는다 — 각으로 바꿔 들면 부호를 정해야 하고,
    ///   부호는 추론으로 맞히면 안 되는 것이다(규칙 9). 손이 간 쪽으로 바늘이 가면 그만이다.
    /// </summary>
    public static Vector3 OnPlane(Vector3 v, Vector3 normal, Vector3 fallback)
    {
        Vector3 p = Vector3.ProjectOnPlane(v, normal);
        return p.sqrMagnitude < 1e-8f ? fallback : p.normalized;
    }

    /// <summary>
    /// 1° 수정을 반영한 마커 자리. 회전 중심을 축으로 단면 안에서 돌린다.
    /// ★+ 수정은 <b>각이 커지는 쪽</b>이다 — 중립에서 멀어지는 방향으로 돈다.
    /// </summary>
    public static Vector3 Adjusted(Vector3 pivot, Vector3 neutral, Vector3 mark, Vector3 normal, float adjustDeg)
    {
        if (Mathf.Approximately(adjustDeg, 0f)) return mark;
        Vector3 a = Vector3.ProjectOnPlane(neutral - pivot, normal);
        Vector3 b = Vector3.ProjectOnPlane(mark - pivot, normal);
        float away = Vector3.SignedAngle(a, b, normal) >= 0f ? 1f : -1f;   // 중립에서 멀어지는 회전 방향
        return pivot + Quaternion.AngleAxis(away * adjustDeg, normal) * (mark - pivot);
    }
}
