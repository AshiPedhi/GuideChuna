using UnityEngine;

/// <summary>
/// 손 역할(주동수/보조수) <b>색 규약의 유일한 출처</b>.
///
/// ★왜 필요한가(2026-08-18 사용자 요구): "주동수·보조수 컬러링은 일괄 적용되게 하고,
/// 내가 화살표마다 이게 주동수 이게 보조수 지정하게."
///
/// 그전에는 색이 <b>오브젝트마다 따로 직렬화</b>돼 있었다 —
///   · 화살표는 <c>practitionerColor</c>/<c>patientColor</c>를 각자 들고 있었고
///     시술자 화살표는 주동수든 보조수든 <b>전부 같은 진녹</b>이라 역할 구분이 아예 없었다.
///   · <see cref="TargetAreaHighlight"/>는 주동수색/보조수색/중립색 세 칸을 오브젝트마다 갖고 있었다.
/// 그래서 색을 바꾸려면 씬의 오브젝트를 전부 찾아다녀야 했다.
///
/// 이제 <b>역할만 오브젝트에 지정하고 색은 여기서 온다.</b>
/// 색을 바꾸려면 이 파일의 상수만 고치면 화살표·하이라이트가 전부 따라온다.
///
/// ■ 색 규약 (2026-08-13 회의)
///   주동수 = 진한 녹색 / 보조수 = 연한 녹색 / 중립 = 노란끼 / 환자 = 하늘색
///
/// ★<b>시나리오 단위 좌우 자동 판정은 넣지 않는다</b>(2026-08-18 사용자 결정).
/// PJ 진단처럼 <b>한 substep 안에서 좌우를 번갈아</b> 하는 단계가 있어서
/// "시나리오마다 주동수 좌우를 한 번 적어 두면 자동" 이라는 전제가 성립하지 않는다.
/// 역할은 표시물마다 직접 지정한다.
/// </summary>
public static class HandRole
{
    /// <summary>표시물의 색 역할.</summary>
    public enum Role
    {
        /// <summary>★기본값 — 역할 색을 쓰지 않고 그 컴포넌트의 기존 색을 그대로 둔다(무회귀).</summary>
        기존색유지,
        /// <summary>주동수 — 힘을 주는 손. 진한 녹색.</summary>
        주동수,
        /// <summary>보조수 — 지지하는 손. 연한 녹색.</summary>
        보조수,
        /// <summary>손 역할과 무관한 표시(진단 목표 돌기 등). 노란끼.</summary>
        중립,
        /// <summary>환자가 능동적으로 내는 힘(등척성 저항 등). 하늘색.</summary>
        환자
    }

    /// <summary>주동수 — 힘을 주는 손.</summary>
    public static readonly Color 주동수색 = new Color(0.149f, 1f, 0.318f, 1f);
    /// <summary>보조수 — 지지하는 손. 같은 계열의 연한 녹색(파란색이 아니다, 08-13 정정판).</summary>
    public static readonly Color 보조수색 = new Color(0.60f, 1f, 0.72f, 1f);
    /// <summary>중립 — 손 역할과 무관한 목표 표시.</summary>
    public static readonly Color 중립색 = new Color(1f, 0.86f, 0.35f, 1f);
    /// <summary>환자가 내는 힘.</summary>
    public static readonly Color 환자색 = new Color(0.25f, 0.8f, 0.95f, 1f);

    /// <summary>역할 색. <see cref="Role.기존색유지"/>는 여기서 답할 수 없으므로 호출 전에 걸러야 한다.</summary>
    public static Color ColorOf(Role role)
    {
        switch (role)
        {
            case Role.보조수: return 보조수색;
            case Role.중립: return 중립색;
            case Role.환자: return 환자색;
            default: return 주동수색;
        }
    }

    /// <summary>이 역할이 규약 색을 쓰는가(= 기존색유지가 아닌가).</summary>
    public static bool UsesRoleColor(Role role) => role != Role.기존색유지;
}
