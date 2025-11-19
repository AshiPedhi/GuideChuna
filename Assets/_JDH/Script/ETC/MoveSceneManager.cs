using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MoveSceneManager : MonoBehaviour
{
    public static MoveSceneManager instance;
    public GameObject introUI;
    public GameObject lobbyUI;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            if (instance != this)
            {
                Destroy(instance.gameObject);
                instance = this;
                DontDestroyOnLoad(gameObject);
                introUI.SetActive(false);
                lobbyUI.SetActive(true);
            }
        }
    }
    public void MoveScene(string SceneName)
    {
        //DontDestroyOnLoad(GameObject.Find("Camera X").gameObject);
        if (GameObject.Find("Camera X") != null)
        {
            GameObject.Find("Camera X").transform.SetParent(GameObject.Find("MoveSceneManager").transform);
        }

        Debug.LogWarning("화면 넘어가요~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
        StartCoroutine(LoadMainScene(SceneName));
        //SceneManager.LoadScene(SceneName);
    }

    IEnumerator LoadMainScene(string sceneName)
    {
        SceneManager.LoadScene("LoadingScene");

        yield return new WaitForFixedUpdate();
        SceneManager.LoadSceneAsync(sceneName);
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }
}
