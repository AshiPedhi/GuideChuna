using TMPro;
using UnityEngine;

/// <summary>
/// 한글이 나오는 TMP 폰트를 찾아 준다.
///
/// ★TMP 폰트를 비워 두면 기본값(LiberationSans)이 잡혀 <b>한글이 통째로 네모</b>가 된다.
///   이 프로젝트의 한글 폰트는 Noto Sans KR인데 <c>Assets/_NJS/</c>에 있어 Resources 밖이다.
///
/// 찾는 순서 —
///   ① <c>Resources</c> 아래에 있는 TMP 폰트 (이름에 Noto가 있으면 우선)
///      → <b>폰트를 Resources에 넣어 두면 여기서 자동으로 잡힌다.</b>
///   ② 진행 패널 지시문이 쓰고 있는 폰트 (씬에 이미 떠 있는 확실한 한글 폰트)
///   ③ 씬의 아무 TMP나 뒤져 이름에 Noto가 든 폰트
///
/// 한 번 찾으면 캐시한다. Awake에서 부르는 것을 전제로 한다.
/// </summary>
public static class KoreanFontResolver
{
    private static TMP_FontAsset cached;
    private static bool searched;

    /// <summary>찾은 폰트. 없으면 null(그때는 TMP 기본값이 쓰인다).</summary>
    public static TMP_FontAsset Resolve()
    {
        if (searched) return cached;
        searched = true;

        cached = FromResources() ?? FromResourcesRawFont() ?? FromGuidePanel() ?? FromAnyTextInScene();

        if (cached != null)
            ChunaLogger.Log($"<color=cyan>[폰트] 한글 폰트 '{cached.name}'를 쓴다.</color>");
        else
            ChunaLogger.LogWarning("[폰트] 한글 TMP 폰트를 못 찾았습니다 — 한글이 네모로 나올 수 있습니다. " +
                                   "Noto Sans KR SDF를 Assets/Resources/Fonts/ 에 넣어 두면 자동으로 잡습니다.");
        return cached;
    }

    /// <summary>씬을 갈아탈 때 다시 찾게 한다.</summary>
    public static void Invalidate()
    {
        cached = null;
        searched = false;
    }

    /// <summary>★Resources 아래 TMP 폰트. 여기 넣어 두기만 하면 알아서 잡힌다.</summary>
    private static TMP_FontAsset FromResources()
    {
        // TMP 관례 폴더부터 훑고, 마지막에 Resources 전체를 본다.
        string[] folders = { "Fonts", "Fonts & Materials", "" };
        TMP_FontAsset fallback = null;

        for (int f = 0; f < folders.Length; f++)
        {
            TMP_FontAsset[] found = Resources.LoadAll<TMP_FontAsset>(folders[f]);
            if (found == null) continue;

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                if (IsKorean(found[i].name)) return found[i];
                if (fallback == null) fallback = found[i];
            }
        }
        return fallback;
    }

    /// <summary>
    /// ★Resources에 <b>원본 폰트(.ttf/.otf)</b>만 넣은 경우. TMP는 SDF 폰트 애셋을 쓰므로
    ///   그대로는 못 쓴다 — 런타임에 동적 폰트 애셋으로 만들어 준다.
    ///   글리프를 필요할 때 굽는 방식이라 한글 전 글자를 미리 구울 필요가 없다.
    /// </summary>
    private static TMP_FontAsset FromResourcesRawFont()
    {
        Font[] fonts = Resources.LoadAll<Font>("");
        if (fonts == null) return null;

        Font pick = null;
        for (int i = 0; i < fonts.Length; i++)
        {
            if (fonts[i] == null) continue;
            if (IsKorean(fonts[i].name)) { pick = fonts[i]; break; }
            if (pick == null) pick = fonts[i];
        }
        if (pick == null) return null;

        TMP_FontAsset made = TMP_FontAsset.CreateFontAsset(pick);
        if (made == null) return null;

        made.name = pick.name + " (런타임 SDF)";
        ChunaLogger.Log($"<color=cyan>[폰트] Resources의 원본 폰트 '{pick.name}'로 동적 폰트 애셋을 만들었다.</color>");
        return made;
    }

    private static TMP_FontAsset FromGuidePanel()
    {
        ScenarioGuideUIController ui =
            Object.FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        return ui != null && ui.DescriptionLabel != null ? ui.DescriptionLabel.font : null;
    }

    private static TMP_FontAsset FromAnyTextInScene()
    {
        TMP_Text[] all = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            TMP_FontAsset f = all[i] != null ? all[i].font : null;
            if (f != null && IsKorean(f.name)) return f;
        }
        return null;
    }

    private static bool IsKorean(string name)
        => !string.IsNullOrEmpty(name)
           && (name.IndexOf("Noto", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("NanumG", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Pretendard", System.StringComparison.OrdinalIgnoreCase) >= 0);
}
