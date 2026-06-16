using UnityEngine;

/// <summary>
/// 시나리오별·모드별 시도 횟수를 PlayerPrefs에 영속화.
/// 키 형식: ATTEMPT_COUNT_{평가|연습}_{시나리오명}
/// 모드는 공식 평가 / 그 외(연습·모의평가) 2종으로만 구분 (isOfficialEvaluation 기준).
/// 시도 횟수는 디바이스 단위로 누적되며 로그아웃해도 유지됨.
/// </summary>
public static class TrainingAttemptStore
{
    private static string BuildKey(string scenarioName, bool isEvaluation)
    {
        string mode = isEvaluation ? "평가" : "연습";
        string scenario = string.IsNullOrEmpty(scenarioName) ? "Unknown" : scenarioName;
        return $"{PrefsKeys.AttemptCountPrefix}{mode}_{scenario}";
    }

    /// <summary>
    /// 시도 횟수를 1 증가시키고 새 회차를 반환 (첫 시도 → 1).
    /// </summary>
    public static int IncrementAndGet(string scenarioName, bool isEvaluation)
    {
        string key = BuildKey(scenarioName, isEvaluation);
        int next = PlayerPrefs.GetInt(key, 0) + 1;
        PlayerPrefs.SetInt(key, next);
        PlayerPrefs.Save();
        return next;
    }

    /// <summary>
    /// 현재까지 기록된 시도 횟수 조회 (증가 없음, 미기록 시 0).
    /// </summary>
    public static int GetCount(string scenarioName, bool isEvaluation)
    {
        return PlayerPrefs.GetInt(BuildKey(scenarioName, isEvaluation), 0);
    }

    /// <summary>
    /// 특정 시나리오+모드의 시도 횟수 초기화.
    /// </summary>
    public static void Reset(string scenarioName, bool isEvaluation)
    {
        PlayerPrefs.DeleteKey(BuildKey(scenarioName, isEvaluation));
        PlayerPrefs.Save();
    }
}
