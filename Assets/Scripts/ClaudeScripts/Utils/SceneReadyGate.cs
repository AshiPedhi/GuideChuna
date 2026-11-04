using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 새 씬이 «보여 줘도 될 만큼» 자리를 잡았나 (2026-10-30 사용자 «생성될 때 딜레이가 있다 — 이런 거 다 잡으라고 로딩창 해 놓은 것»).
/// <see cref="SceneLoader"/>가 씬을 켠 뒤 로딩 가림막을 바로 걷지 않고, 그 씬이 요구하는 표시가 모두 들어올 때까지(상한 있음) 덮어 둔다.
/// 실습(TrainingScene) = «fit»(환자 자동 맞춤 · InfoPanelController) + «bar»(상단 메뉴 놓음 · TopBarPlacer). 다른 씬은 요구 없음(추적만 안정되면).
/// ★표시는 씬 이름으로 고른다 — 시나리오 이름은 쓰지 않는다(CLAUDE.md).
/// </summary>
public static class SceneReadyGate
{
    private static readonly HashSet<string> marks = new HashSet<string>();
    private static readonly string[] TrainingNeeds = { "fit", "bar" };

    /// <summary>씬을 켜기 직전에 SceneLoader가 부른다 — 지난 씬의 표시를 지운다.</summary>
    public static void Begin() => marks.Clear();

    public static void Mark(string what)
    {
        if (marks.Add(what)) Debug.Log($"[SceneReadyGate] «{what}» 들어옴 · {Time.timeSinceLevelLoad:F2}s");
    }

    public static bool IsSatisfied(string sceneName)
    {
        if (sceneName != "TrainingScene") return true;
        foreach (var n in TrainingNeeds) if (!marks.Contains(n)) return false;
        return true;
    }

    public static string Missing(string sceneName)
    {
        if (sceneName != "TrainingScene") return "";
        var list = new List<string>();
        foreach (var n in TrainingNeeds) if (!marks.Contains(n)) list.Add(n);
        return string.Join(", ", list);
    }
}
