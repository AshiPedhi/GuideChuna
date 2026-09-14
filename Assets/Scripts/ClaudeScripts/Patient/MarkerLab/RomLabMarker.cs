using UnityEngine;

/// <summary>
/// 실측 프로토타입의 마커 하나. 공간의 한 점이고, <b>부모 마커와 직선으로 이어진다</b>.
///
/// ★2026-09-14 신설. 정해진 절차를 밟는 기존 실측과 달리, 이쪽은
///   <b>점을 찍고 이어서 각을 읽는 도구</b>다 — 순서를 강제하지 않는다(사용자 요구).
/// ★<see cref="CervicalRomRealityMeasure"/>와 <b>완전히 별개</b>다. 서로 참조하지 않는다.
/// </summary>
public class RomLabMarker : MonoBehaviour
{
    /// <summary>각을 무엇으로 재는가.</summary>
    public enum AngleMode
    {
        /// <summary>부모→이 마커 직선이 <b>지면 수평면</b>과 이루는 각. 굴곡·신전에 맞는다.</summary>
        FromHorizontal = 0,

        /// <summary>마커를 찍을 때 <b>머리에 올린 손의 회전</b>을 그대로 적는다. 회전에 맞는다.</summary>
        FromHandRoll = 1,

        /// <summary>각을 안 읽는다(어깨처럼 기준점으로만 쓰는 마커).</summary>
        None = 2,
    }

    [Tooltip("이 마커가 무엇인가. 목 · 머리1(중립) · 머리2(능동) · 머리3(압박) · 어깨L …")]
    public string role = "새 마커";

    [Tooltip("직선으로 이어질 상대. 비어 있으면 외톨이 점이다.")]
    public RomLabMarker parent;

    [Tooltip("같은 측정 묶음. 순서를 강제하지 않으므로 <b>묶음으로 모은다</b>.")]
    public string group = "";

    public AngleMode angleMode = AngleMode.FromHorizontal;

    [Tooltip("손 회전으로 잰 각(도). angleMode가 FromHandRoll일 때만 뜻이 있다.")]
    public float handAngle;
    public bool hasHandAngle;

    /// <summary>부모까지의 직선이 지면 수평면과 이루는 각(도). 부모가 없으면 NaN.</summary>
    public float HorizontalAngle
    {
        get
        {
            if (parent == null) return float.NaN;
            Vector3 v = transform.position - parent.transform.position;
            if (v.sqrMagnitude < 1e-8f) return float.NaN;

            // ★수평면과의 각 = 90° − (수직축과의 각). 위로 갈수록 +, 아래로 갈수록 −.
            //   Vector3.Angle은 0~180이라 부호가 없다 — y 성분으로 부호를 준다.
            float fromUp = Vector3.Angle(v, Vector3.up);
            float fromPlane = 90f - fromUp;
            return fromPlane;
        }
    }

    /// <summary>화면에 적을 각. 모드에 따라 고른다. 읽을 값이 없으면 NaN.</summary>
    public float ReadableAngle
    {
        get
        {
            switch (angleMode)
            {
                case AngleMode.FromHorizontal: return HorizontalAngle;
                case AngleMode.FromHandRoll:   return hasHandAngle ? handAngle : float.NaN;
                default:                       return float.NaN;
            }
        }
    }
}
