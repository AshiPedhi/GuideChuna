using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// ★XR-ROM 평가 모드 = <b>재현성</b> 확인(2026-11-13 사용자 지시).
///   «실습은 실제 계측 · 평가는 이전 실습 계측값과 평가 계측값의 유사성 확인 — 같은 기기로 같은 환자에게 같은 방법으로 재면 오차가 일정 수준 안이어야 한다».
///   · 비교 대상 = 같은 로그인 사용자의 <b>가장 최근 실습</b>(2인 1조 · 사람이 바뀌면 실습을 다시 한다 — 사용자). 환자 구분은 두지 않는다.
///   · 점수 = 다른 술기처럼 <b>90점 기준</b>으로 오차에 따라 차등(사용자 «통과는 상관없고»). 칸(6동작 × 능동 · 압박)마다 점수를 내 평균한다.
///   · 실습값은 평가가 끝난 뒤에만 보인다(사용자).
/// ★오차 구간(FullErr · BaseErr · FloorErr)은 11-13 내가 낸 <b>제안값</b>이다 — 근거 없음 · 교수님 기준이 오면 바꾼다.
/// </summary>
public static class RomLineRepeatScore
{
    public const int Slots = 12;                 // 동작 6 × (능동 · 압박) — RomLineSession.results와 같은 순서(m * 2 + 압박 여부)
    public const float FullErr = 2f;             // 이 안이면 100점          ★제안값
    public const float BaseErr = 5f;             // 여기서 90점(기준)        ★제안값
    public const float FloorErr = 10f;           // 여기부터 80점(최저)      ★제안값
    public const float Full = 100f, Base = 90f, Floor = 80f;

    /// <summary>칸 하나의 점수 — 오차(절댓값, 도)에 따라 100 → 90 → 80으로 곧게 내려간다.</summary>
    public static float ScoreOf(float err)
    {
        err = Mathf.Abs(err);
        if (err <= FullErr) return Full;
        if (err <= BaseErr) return Mathf.Lerp(Full, Base, (err - FullErr) / (BaseErr - FullErr));
        return Mathf.Lerp(Base, Floor, Mathf.Clamp01((err - BaseErr) / (FloorErr - BaseErr)));
    }

    /// <summary>둘 다 값이 있는 칸만 모아 평균 점수 · 평균 오차를 낸다. 비교한 칸이 없으면 n = 0.</summary>
    public static float Total(float[] baseVals, float[] evalVals, out float meanErr, out int n)
    {
        float sum = 0f, errSum = 0f; n = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (float.IsNaN(baseVals[i]) || float.IsNaN(evalVals[i])) continue;
            float e = evalVals[i] - baseVals[i];
            sum += ScoreOf(e); errSum += Mathf.Abs(e); n++;
        }
        meanErr = n > 0 ? errSum / n : 0f;
        return n > 0 ? sum / n : 0f;
    }

    // ── 실습값(기준) 파일 — 사용자마다 하나 · 실습에서 확인할 때마다 통째로 다시 쓴다(= 가장 최근 실습) ──
    public static string BaselinePath(string user) => Path.Combine(Application.persistentDataPath, "romline_baseline_" + Safe(user) + ".csv");

    public static void SaveBaseline(string user, float[] vals)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        sb.Append("saved,").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci)).Append('\n');
        for (int i = 0; i < Slots; i++) sb.Append(i).Append(',').Append(float.IsNaN(vals[i]) ? "" : vals[i].ToString("F2", ci)).Append('\n');
        try { File.WriteAllText(BaselinePath(user), sb.ToString()); }
        catch (Exception e) { Debug.LogWarning($"[선실측] ★실습값 저장 실패({e.Message})"); }
    }

    public static bool TryLoadBaseline(string user, float[] vals, out string when)
    {
        when = "";
        for (int i = 0; i < Slots; i++) vals[i] = float.NaN;
        string path = BaselinePath(user);
        if (!File.Exists(path)) return false;
        try
        {
            int got = 0;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] c = line.Split(',');
                if (c.Length < 2) continue;
                if (c[0] == "saved") { when = c[1]; continue; }
                if (int.TryParse(c[0], out int i) && i >= 0 && i < Slots &&
                    float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) { vals[i] = v; got++; }
            }
            return got > 0;
        }
        catch (Exception e) { Debug.LogWarning($"[선실측] ★실습값 읽기 실패({e.Message})"); return false; }
    }

    private static string Safe(string user)
    {
        if (string.IsNullOrEmpty(user)) return "_noname";
        var sb = new StringBuilder(user.Length);
        foreach (char ch in user) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }
}
