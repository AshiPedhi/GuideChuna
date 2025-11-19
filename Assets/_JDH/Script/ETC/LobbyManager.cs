using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class LobbyManager : MonoBehaviour
{
    public GameObject silsub;
    public GameObject yeonsub;

    public TextMeshProUGUI username;

    public RunStatus runStatus = new();

    public void UIbgColorY()
    {
        yeonsub.SetActive(true);
        silsub.SetActive(false);
    }
    public void UIbgColorS()
    {
        silsub.SetActive(true);
        yeonsub.SetActive(false);
    }
    void Start()
    {
        if (AuthManager.instance == null)
            return;
        username.text = AuthManager.instance.currentRunUser;
        runStatus.deviceSN = AuthManager.instance.DEVICE_SN;
        runStatus.status = "로비//";
        AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        // RenderManager 초기화 지연 (튕김 방지)
    }
    public void MoveScene(string SceneName)
    {
        SceneManager.LoadScene(SceneName);
    }

    public void MoveToAuth()
    {
        AuthManager.instance.OnLogoffPost();
    }

    public void AppQuit()
    {
        Application.Quit();
    }

    public void CloseMenu(GameObject canvas)
    {
        if (canvas != null)
        {
            if (canvas.activeSelf)
            {
                return;
            }
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas"))
        {
            obj.SetActive(false);
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas2"))
        {
            obj.SetActive(false);
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas3"))
        {
            obj.SetActive(false);
        }

        if (canvas != null)
            canvas.SetActive(true);
    }

    public void CloseMenu2(GameObject canvas)
    {
        if (canvas != null)
        {
            if (canvas.activeSelf)
            {
                return;
            }
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas2"))
        {
            obj.SetActive(false);
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas3"))
        {
            obj.SetActive(false);
        }

        if (canvas != null)
            canvas.SetActive(true);
    }


    public void CloseMenu3(GameObject canvas)
    {
        if (canvas != null)
        {
            if (canvas.activeSelf)
            {
                return;
            }
        }

        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("InterCanvas3"))
        {
            obj.SetActive(false);
        }
  
        if (canvas != null)
            canvas.SetActive(true);
    }

    public void LoadScene(string name)
    {
        //GameObject.Find("Camera X").transform.SetParent(GameObject.Find("AuthManager").transform);
        SceneManager.LoadScene(name);
    }
}
