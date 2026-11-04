using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Quest OS 설정 «텍스트 크기» · «화면 크기»를 읽어 로비 UI에 자동으로 맞춘다 (2026-10-28 사용자 «만들어줘»).
///
/// 실측(10-28 · Quest 3S):
///   텍스트 크기 → Configuration.fontScale(1.00 · 1.15 확인) — 앱 안 로그로 OS 값과 같음을 확인
///   화면 크기   → Configuration.densityDpi(200 ↔ 256 · adb 확인) — 기본 = DisplayMetrics.DENSITY_DEVICE_STABLE(200)
///   ★설정을 바꾸면 OS가 앱을 다시 켠다(프로세스 번호가 바뀜) → 시작 때 한 번 읽으면 된다.
///
/// 적용(로비 · 실습 — 10-28 사용자 «실습 내부에도 적용해봐» · «실습씬 정보 표시창이 좀 작긴 하던데»):
///   화면 크기 → 로비 브레인(LobbyAuthUI_Complete) · 실습 UI Group(InfoWindowArranger의 부모) 바로 아래 판마다 <b>제자리에서</b> 배율을 곱한다
///              (사람 위치 기준으로 키우면 멀어지기만 해서 보이는 크기가 그대로다).
///   실습 정보 창이 커지면 좌우 판이 겹치므로 InfoWindowArranger가 좌우 각도에도 배율을 곱한다.
///   텍스트 크기 → 그 아래 모든 TMP 글자 크기에 곱한다. 실행 중에 만드는 조 · 이름 칸은 LoginListLayout.ApplyLabel이 곱한다.
/// ★과하게 커지지 않게 클램프한다(글자 0.85~1.5 · 판 0.75~1.5). ★에디터에선 값을 못 읽어 1 = 종전 그대로.
/// ★로그는 Debug.Log — ChunaLogger.Log는 릴리스 빌드에서 컴파일이 안 된다.  adb logcat -s Unity | grep OsDisplayScale
/// </summary>
public static class OsDisplayScale
{
    public static float Font { get; private set; } = 1f;
    public static float Density { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Read();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void Read()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var res = activity.Call<AndroidJavaObject>("getResources"))
            using (var cfg = res.Call<AndroidJavaObject>("getConfiguration"))
            using (var dm = new AndroidJavaClass("android.util.DisplayMetrics"))
            {
                float font = cfg.Get<float>("fontScale");
                int dpi = cfg.Get<int>("densityDpi");
                int stable = dm.GetStatic<int>("DENSITY_DEVICE_STABLE");
                Font = Mathf.Clamp(font, 0.85f, 1.5f);
                Density = stable > 0 ? Mathf.Clamp(dpi / (float)stable, 0.75f, 1.5f) : 1f;
                Debug.Log($"[OsDisplayScale] 읽음 — fontScale {font:F2} · densityDpi {dpi}/{stable} → 글자 ×{Font:F2} · 판 ×{Density:F2}");
            }
        }
        catch (System.Exception e)
        {
            Font = Density = 1f;
            Debug.LogWarning($"[OsDisplayScale] 읽기 실패 — 종전 크기 그대로: {e.Message}");
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>에디터 넘침 검사용 — 기기와 같은 ApplyFont를 타게 배율만 바꾼다(LobbyV15Tool «OS 최대 넘침 검사»).</summary>
    public static void EditorSimulate(float font, float density) { Font = font; Density = density; }
    /// <summary>에디터 캡처용 — 기기의 씬 로드 때와 같은 Apply를 그 판 묶음에 돌린다(되돌리기는 부른 쪽 책임).</summary>
    public static void EditorApplyTo(Transform root) => Apply(root, "에디터");
#endif

    /// <summary>글자 크기에 OS 텍스트 크기 배율을 곱한다(실행 중에 만드는 글자용).</summary>
    public static void ApplyFont(TMP_Text t)
    {
        if (t == null || Mathf.Approximately(Font, 1f)) return;
        t.fontSize *= Font;
        t.fontSizeMax *= Font;
        t.fontSizeMin *= Font;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Mathf.Approximately(Font, 1f) && Mathf.Approximately(Density, 1f)) return;

        // 로비 = 로그인 브레인 바로 아래 판들
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth != null && auth.gameObject.scene == scene) { Apply(auth.transform, "로비"); return; }

        // 실습 = 정보 창 배치(InfoWindowArranger)가 든 UI Group 아래 판들(정보 창 셋 · 옛 정보 패널 · 진행 · 상단 바 · 설정)
        var arranger = Object.FindFirstObjectByType<InfoWindowArranger>(FindObjectsInactive.Include);
        if (arranger != null && arranger.gameObject.scene == scene && arranger.transform.parent != null)
            Apply(arranger.transform.parent, "실습");
    }

    private static void Apply(Transform root, string tag)
    {
        int plates = 0, texts = 0;
        foreach (Transform child in root)
        {
            if (child.GetComponentInChildren<Canvas>(true) == null) continue;   // 판이 아닌 것(빈 그룹 · 배치기 등)은 둔다
            child.localScale *= Density;
            plates++;
        }
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) { ApplyFont(t); texts++; }
        Debug.Log($"[OsDisplayScale] {tag} 적용 — {root.name} 아래 판 {plates}개 ×{Density:F2} · 글자 {texts}개 ×{Font:F2}");
    }
}
