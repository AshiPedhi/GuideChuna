using System.Text;
using UnityEngine;

/// <summary>
/// ★★<b>[임시 · A-12 교차검증 전용]</b> 손으로 잰 각을 방향별로 모아 두고, 결과 화면에 붙일 부록을 만든다.
///
/// <b>왜 임시인가</b> — 실습(교육)모드의 각은 대본이 정하는 값이라 이미 결과표에 나온다.
/// 여기 모으는 것은 "그 대본 각을 손으로 재면 같은 값이 나오는가"를 확인하는 검증 자료다.
/// 검증이 끝나면 필요 없다.
///
/// <b>지우는 법</b> — 아래 세 가지만 지우면 흔적이 없다. 다른 파일은 손댈 필요가 없다.
///   1. 이 파일과 <see cref="CervicalRomHandAngleProbe"/>
///   2. <c>CervicalRomDriver</c>의 <c>OnMeasurementRecorded</c> 이벤트 선언 1줄 + <c>Record()</c> 안의 발행 1줄
///   3. <c>TrainingResultData</c>의 <c>RomAppendixProvider</c> 필드 1줄 + <c>BuildRomSummaryText()</c> 안의 호출 2줄
/// 2·3은 남겨 둬도 컴파일과 동작에 아무 영향이 없다(제공자가 없으면 부록이 안 붙는다).
///
/// ★판정·채점에는 전혀 관여하지 않는다. 읽어서 표로 찍는 것이 전부다.
/// </summary>
public static class CervicalRomHandAngleLog
{
    /// <summary>한 시점의 기록. 손으로 잰 값과 그때의 대본 각을 함께 남긴다.</summary>
    public struct Sample
    {
        public bool has;        // 기록됐는가
        public bool measurable; // 그 순간 손으로 잴 수 있었는가(면 성분이 충분했는가)
        public float hand;      // 손으로 잰 각(도)
        public float scripted;  // 그때의 대본 각(도)
        public float Delta => hand - scripted;
    }

    public struct Entry
    {
        public Sample active;
        public Sample passive;
        public bool Any => active.has || passive.has;
    }

    private const int DirectionCount = 7;   // CervicalRomDriver.Direction의 개수(None 포함)
    private static readonly Entry[] entries = new Entry[DirectionCount];

    /// <summary>기록이 하나라도 있는가.</summary>
    public static bool HasAny
    {
        get
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Any) return true;
            return false;
        }
    }

    /// <summary>
    /// 전부 지운다. ★프로브가 Awake에서 부른다 —
    /// static이라 씬을 다시 열어도 값이 남는데, 그러면 지난 판 기록이 이번 결과에 섞인다.
    /// </summary>
    public static void Clear()
    {
        for (int i = 0; i < entries.Length; i++) entries[i] = default;
    }

    public static void Record(CervicalRomDriver.Direction d, bool isActive,
                              bool measurable, float handDegrees, float scriptedDegrees)
    {
        int i = (int)d;
        if (i <= 0 || i >= entries.Length) return;

        var s = new Sample { has = true, measurable = measurable, hand = handDegrees, scripted = scriptedDegrees };
        Entry e = entries[i];
        if (isActive) e.active = s; else e.passive = s;
        entries[i] = e;
    }

    public static Entry Get(CervicalRomDriver.Direction d)
    {
        int i = (int)d;
        return (i <= 0 || i >= entries.Length) ? default : entries[i];
    }

    // ── 결과 화면 부록 ────────────────────────────────────────────────────

    // ★결과표와 같은 방식으로 칸을 맞춘다 — 비례 폰트라 공백 패딩으로는 안 맞고,
    //   TMP의 <pos=x%>로 컬럼 위치를 고정해야 한다.
    private const string H1 = "<pos=26%>";   // 손 능동
    private const string H2 = "<pos=48%>";   // 손 수동
    private const string H3 = "<pos=70%>";   // 대본 대비 차

    private static readonly CervicalRomDriver.Direction[] order =
    {
        CervicalRomDriver.Direction.Flexion,
        CervicalRomDriver.Direction.Extension,
        CervicalRomDriver.Direction.LateralLeft,
        CervicalRomDriver.Direction.LateralRight,
        CervicalRomDriver.Direction.RotationLeft,
        CervicalRomDriver.Direction.RotationRight,
    };

    /// <summary>결과 화면 맨 아래에 붙일 부록. 기록이 없으면 빈 문자열 — 그러면 아무것도 안 붙는다.</summary>
    public static string BuildAppendix()
    {
        if (!HasAny) return "";

        var sb = new StringBuilder(320);
        sb.AppendLine("<size=85%><color=#9aa4b2>─────────────────────────────────────────</color></size>");
        sb.AppendLine("<color=#ffd54f>[검증용 · 임시] 손으로 잰 각</color>");
        sb.AppendLine($"<size=85%>방향{H1}손 능동{H2}손 수동{H3}대본 대비</size>");

        for (int i = 0; i < order.Length; i++)
        {
            Entry e = Get(order[i]);
            if (!e.Any) continue;

            sb.AppendLine($"{NameOf(order[i])}{H1}{Cell(e.active)}{H2}{Cell(e.passive)}{H3}{DeltaCell(e)}");
        }

        sb.Append("<size=80%><color=#9aa4b2>※ 판정·점수에는 쓰이지 않는다. 대본 각과 손 측정값을 견주는 검증 자료다.</color></size>");
        return sb.ToString();
    }

    private static string Cell(Sample s)
    {
        if (!s.has) return "<color=#707070>-</color>";
        if (!s.measurable) return "<color=#ffcc55>못 잼</color>";
        return $"{s.hand:F0}°";
    }

    /// <summary>대본 대비 차. 능동·수동 둘 다 있으면 "+2° / -1°" 꼴로 나란히 적는다.</summary>
    private static string DeltaCell(Entry e)
    {
        string a = DeltaOne(e.active);
        string p = DeltaOne(e.passive);
        if (a == null && p == null) return "<color=#707070>-</color>";
        return $"{a ?? "-"} / {p ?? "-"}";
    }

    private static string DeltaOne(Sample s)
    {
        if (!s.has || !s.measurable) return null;
        float d = s.Delta;
        // 3도를 넘게 벌어지면 눈에 띄게 한다 — 그게 곧 검증에서 보려던 것이다.
        string body = $"{d:+0;-0;0}°";
        return Mathf.Abs(d) >= 3f ? $"<color=#ff8a65>{body}</color>" : body;
    }

    private static string NameOf(CervicalRomDriver.Direction d)
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
}
