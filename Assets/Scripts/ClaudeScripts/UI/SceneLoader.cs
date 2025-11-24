using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환 관리 유틸리티 클래스
/// MoveSceneManager를 대체하며 로딩씬을 통한 부드러운 씬 전환 제공
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

    #region Unity Lifecycle
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
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

        // 2. 로딩씬이 완전히 로드될 때까지 대기
        yield return new WaitForFixedUpdate();

        // 3. 목표 씬 비동기 로드
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetSceneName);

        // 4. 로딩 완료 대기
        while (!asyncLoad.isDone)
        {
            // 로딩 진행률: asyncLoad.progress (0.0 ~ 1.0)
            yield return null;
        }

        // 5. 사용하지 않는 리소스 정리
        Resources.UnloadUnusedAssets();
        System.GC.Collect();

        Debug.Log($"[SceneLoader] '{targetSceneName}' 씬 로드 완료");
    }

    private IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        Resources.UnloadUnusedAssets();
        System.GC.Collect();

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
