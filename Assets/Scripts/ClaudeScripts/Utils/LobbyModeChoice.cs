using ChunaTraining;

/// <summary>
/// 로비 모드 팝업에서 고른 모드 · 난이도를 실습 씬으로 넘기는 한 번짜리 값 (2026-10-27 사용자 요청 4).
/// 씬 전환을 건너 살아남도록 정적으로 둔다. 실습 씬의 <see cref="InfoPanelController"/>가 한 번 꺼내 쓰고 비운다.
///
/// ★값이 없으면(에디터에서 TrainingScene을 바로 열었을 때 등) 실습 씬은 종전대로 모드 선택 페이지를 띄운다.
/// ★XR(실제 사람)은 아직 막혀 있어 여기에 담기지 않는다 — XR 진행 로직이 생기면 필드를 더한다.
/// </summary>
public static class LobbyModeChoice
{
    private static bool has;
    private static InfoPanelController.ModeType mode;
    private static DifficultyLevel difficulty;

    public static void Set(InfoPanelController.ModeType m, DifficultyLevel d)
    {
        has = true; mode = m; difficulty = d;
        ChunaLogger.Log($"[LobbyModeChoice] 고름: {m} · {d}");
    }

    public static bool TryConsume(out InfoPanelController.ModeType m, out DifficultyLevel d)
    {
        m = mode; d = difficulty;
        if (!has) return false;
        has = false;
        return true;
    }

    public static void Clear() => has = false;
}
