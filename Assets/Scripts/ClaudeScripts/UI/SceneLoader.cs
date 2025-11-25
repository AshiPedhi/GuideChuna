using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환 관리 유틸리티 클래스 (Meta Quest 최적화)
/// MoveSceneManager를 대체하며 로딩씬을 통한 부드러운 씬 전환 제공
///
/// Quest 최적화:
/// - WaitForSeconds 객체 캐싱으로 GC Allocation 최소화
/// - AsyncOperation 최적화
/// - 리소스 정리 강화
/// </summary>
public class SceneLoader : MonoBehaviour
{
    #region Singleton
    private static SceneLoader instance;
    public static SceneLoader Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("[SceneLoader]");
                instance = go.AddComponent<SceneLoader>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }
    #endregion

    #region Cached Wait Objects (Quest Optimization)
    private WaitForFixedUpdate cachedWaitForFixedUpdate;
    private WaitForEndOfFrame cachedWaitForEndOfFrame;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeCache();
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void InitializeCache()
    {
        // Quest 최적화: WaitForSeconds 객체 미리 생성하여 GC 방지
        cachedWaitForFixedUpdate = new WaitForFixedUpdate();
        cachedWaitForEndOfFrame = new WaitForEndOfFrame();
    }
    #endregion

    #region Scene Loading Methods
    /// <summary>
    /// 씬을 로드합니다 (로딩씬 사용 여부 선택 가능)
    /// </summary>
    /// <param name="sceneName">로드할 씬 이름</param>
    /// <param name="useLoadingScene">로딩씬을 사용할지 여부 (기본값: true)</param>
    public static void LoadScene(string sceneName, bool useLoadingScene = true)
    {
        if (useLoadingScene)
        {
            Instance.LoadSceneWithLoading(sceneName);
        }
        else
        {
            Instance.LoadSceneDirect(sceneName);
        }
    }

    /// <summary>
    /// 로딩씬을 거쳐서 씬을 로드합니다
    /// </summary>
    /// <param name="sceneName">로드할 씬 이름</param>
    public void LoadSceneWithLoading(string sceneName)
    {
        Debug.Log($"[SceneLoader] 로딩씬을 통해 '{sceneName}' 씬 로드 시작");
        StartCoroutine(LoadSceneWithLoadingCoroutine(sceneName));
    }

    /// <summary>
    /// 바로 씬을 로드합니다 (로딩씬 없이)
    /// </summary>
    /// <param name="sceneName">로드할 씬 이름</param>
    public void LoadSceneDirect(string sceneName)
    {
        Debug.Log($"[SceneLoader] '{sceneName}' 씬 직접 로드");
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// 비동기로 씬을 로드합니다 (로딩씬 없이)
    /// </summary>
    /// <param name="sceneName">로드할 씬 이름</param>
    public void LoadSceneAsync(string sceneName)
    {
        Debug.Log($"[SceneLoader] '{sceneName}' 씬 비동기 로드");
        StartCoroutine(LoadSceneAsyncCoroutine(sceneName));
    }
    #endregion

    #region Coroutines
    private IEnumerator LoadSceneWithLoadingCoroutine(string targetSceneName)
    {
        // 1. 로딩씬 로드
        SceneManager.LoadScene("LoadingScene");

        // 2. 로딩씬이 완전히 로드될 때까지 대기 (Quest 최적화: 캐시된 객체 사용)
        yield return cachedWaitForFixedUpdate;

        // 3. 목표 씬 비동기 로드 (Quest 최적화: allowSceneActivation으로 제어)
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetSceneName);
        asyncLoad.allowSceneActivation = false; // 로딩 완료 후 수동 활성화

        // 4. 로딩 완료 대기 (Quest 최적화: 90%까지만 자동 진행)
        while (asyncLoad.progress < 0.9f)
        {
            // 로딩 진행률: asyncLoad.progress (0.0 ~ 0.9)
            yield return null;
        }

        // 5. 리소스 정리 (Quest 최적화: 씬 활성화 전에 정리)
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        // 6. 한 프레임 대기 후 씬 활성화
        yield return null;
        asyncLoad.allowSceneActivation = true;

        // 7. 완전히 활성화될 때까지 대기
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        Debug.Log($"[SceneLoader] '{targetSceneName}' 씬 로드 완료");
    }

    private IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        // Quest 최적화: allowSceneActivation으로 리소스 정리 타이밍 제어
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // 90%까지 로딩 대기
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // 리소스 정리
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        // 씬 활성화
        yield return null;
        asyncLoad.allowSceneActivation = true;

        // 완료 대기
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        Debug.Log($"[SceneLoader] '{sceneName}' 씬 비동기 로드 완료");
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// 현재 활성화된 씬 이름을 반환합니다
    /// </summary>
    public static string GetCurrentSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    /// <summary>
    /// 현재 씬을 다시 로드합니다
    /// </summary>
    /// <param name="useLoadingScene">로딩씬을 사용할지 여부</param>
    public static void ReloadCurrentScene(bool useLoadingScene = true)
    {
        string currentScene = GetCurrentSceneName();
        LoadScene(currentScene, useLoadingScene);
    }
    #endregion
}
