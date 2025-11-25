using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oculus.Interaction;

public class UIManager : MonoBehaviour
{
    public GameObject mainUI;

    public GameObject replayPopup;
    public GameObject replayActive;
    public GameObject replayDeactive;

    public GameObject logoutPopup;
    public GameObject logoutActive;
    public GameObject logoutDeactive;

    public GameObject nimiMapA;
    public GameObject nimiMapD;

    public GameObject humanMapA;
    public GameObject humanMapD;

    public GameObject settingObj;
    public GameObject settingA;
    public GameObject settingD;

    public GameObject passA;
    public GameObject passD;

    public GameObject nonPass;
    public GameObject pass;
    public GameObject table;

    public GameObject humanA;
    public GameObject humanD;

    Rigidbody rig;

    private void Awake()
    {
        rig = GetComponent<Rigidbody>();
    }

    public void HumanActive()
    {
        if (humanA.activeSelf)
        {
            humanA.SetActive(false);
            humanD.SetActive(true);
        }
        else
        {
            humanA.SetActive(true);
            humanD.SetActive(false);
        }
    }

    public void PassThrough()
    {
        if(passA.activeSelf)
        {
            passA.SetActive(false);
            passD.SetActive(true);
            //nonPass.SetActive(true);
            //pass.SetActive(false);
            table.SetActive(true);
        }
        else
        {
            passA.SetActive(true);
            passD.SetActive(false);
            //nonPass.SetActive(false);
            //pass.SetActive(true);
            table.SetActive(false);
        }
    }

    public void LogoutPopUp()
    {
        mainUI.SetActive(false);
        replayPopup.SetActive(false);
        replayDeactive.SetActive(false);
        replayActive.SetActive(true);

        if (logoutPopup.activeSelf)
        {
            logoutPopup.SetActive(false);
            logoutDeactive.SetActive(false);
            logoutActive.SetActive(true);
            mainUI.SetActive(true);
        }
        else
        {
            logoutPopup.SetActive(true);
            logoutDeactive.SetActive(true);
            logoutActive.SetActive(false);
        }
    }
    public void ReplayPopUp()
    {
        mainUI.SetActive(false);
        logoutPopup.SetActive(false);
        logoutDeactive.SetActive(false);
        logoutActive.SetActive(true);

        if (replayPopup.activeSelf)
        {
            replayPopup.SetActive(false);
            replayDeactive.SetActive(false);
            replayActive.SetActive(true);
            mainUI.SetActive(true);
        }
        else
        {
            replayPopup.SetActive(true);
            replayDeactive.SetActive(true);
            replayActive.SetActive(false);
        }
    }

    public void FreezeUI()
    {
        rig.constraints = RigidbodyConstraints.FreezeAll;
    }

    public void UnfreezeUI()
    {
        rig.constraints = RigidbodyConstraints.None;
    }

    public void ToLobby()
    {
        SceneLoader.LoadScene("lobby");
    }
    public void ToLobby2()
    {
        SceneLoader.LoadScene("AuthMain Copy");
    }

    public void MiniMap()
    {
        if (nimiMapA.activeSelf)
        {
            nimiMapA.SetActive(false);
            nimiMapD.SetActive(true);
        }
        else
        {
            nimiMapA.SetActive(true);
            nimiMapD.SetActive(false);
        }
    }

    public void HumanMap()
    {
        if (humanMapA.activeSelf)
        {
            humanMapA.SetActive(false);
            humanMapD.SetActive(true);
        }
        else
        {
            humanMapA.SetActive(true);
            humanMapD.SetActive(false);
        }
    }

    public void Setting()
    {
        // 다른 팝업들 먼저 닫기
        mainUI.SetActive(false);
        replayPopup.SetActive(false);
        replayDeactive.SetActive(false);
        replayActive.SetActive(true);
        logoutPopup.SetActive(false);
        logoutDeactive.SetActive(false);
        logoutActive.SetActive(true);

        if (settingObj.activeSelf)
        {
            // 설정 패널 닫기
            settingObj.SetActive(false);
            settingA.SetActive(false);
            settingD.SetActive(true);
            // 메인 UI 다시 보여주기
            mainUI.SetActive(true);
        }
        else
        {
            // 설정 패널 열기
            settingObj.SetActive(true);
            settingA.SetActive(true);
            settingD.SetActive(false);
            // 메인 UI는 숨김 상태 유지
        }
    }
}