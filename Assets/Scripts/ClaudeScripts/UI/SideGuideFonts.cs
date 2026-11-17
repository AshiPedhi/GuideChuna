using TMPro;
using UnityEngine;

/// <summary>
/// 환자 옆 안내판이 쓰는 KoPubWorld돋움 세 굵기(2026-11-06 사용자 «텍스트 규격은 우리 정했던 규격» = UI 글꼴정리 V0.2).
/// 글꼴 자산은 <c>Assets/Fonts/KoPubWorld</c>(Resources 밖)에 있어 런타임에 이름으로 못 부른다 →
/// 이 작은 자산(<c>Resources/UI/SideGuideFonts</c>)이 세 자산을 참조만 한다. 만들기 = 메뉴 «GuideChuna/UI 개편/안내판 글꼴 묶음 만들기».
/// 없으면 안내판은 <see cref="KoreanFontResolver"/>의 글꼴(Noto) 하나로 돈다.
/// </summary>
[CreateAssetMenu(menuName = "GuideChuna/안내판 글꼴 묶음")]
public class SideGuideFonts : ScriptableObject
{
    public TMP_FontAsset bold, medium, light;

    private static SideGuideFonts cached; private static bool looked;
    public static SideGuideFonts Get()
    {
        if (!looked) { looked = true; cached = Resources.Load<SideGuideFonts>("UI/SideGuideFonts"); }
        return cached;
    }
}
