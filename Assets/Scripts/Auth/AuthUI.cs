using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks; // UniTask 추가

public class AuthUI : MonoBehaviour
{
    public static AuthUI instance;
    [Header("Title")]
    public Text title;
    [Header("Auth")]
    public TMP_InputField authInputField;
    public GameObject authInputGameObject;
    public GameObject authFailGameObject;
    public GameObject authsuccessGameObject;
    public GameObject authPopUpGameObject;
    public GameObject authLicenseGameObject;
    public Text authInfoText;
    [Header("UserList")]
    public GameObject gradeTapGameObject;
    public GameObject gradeTap;
    public GameObject userButton;
    public GameObject userListScrollGameObject;
    public Transform Viewport; // GameObject → Transform (최적화)
    [Header("LoginButton")]
    public GameObject loginButton;
    [Header("Numbering")]
    public Text numbering;
    [Header("ScrollContent")]
    public ScrollRect scrollRect;
    [HideInInspector]
    public string gradeInfo;
    public List<string> grade = new List<string>();
    [HideInInspector]
    public List<GameObject> Taps = new List<GameObject>();
    [HideInInspector]
    public List<GameObject> contents = new List<GameObject>();
    [HideInInspector]
    public int classAmount = 0;
    [HideInInspector]
    public int currentTap = 0;

    // 오브젝트 풀 (AuthManager와 공유 가능하도록)
    private Queue<GameObject> gradeTapPool = new();
    private Queue<GameObject> userButtonPool = new();
    private Queue<GameObject> scrollPool = new();

    // 캐싱된 컴포넌트
    private Button loginButtonComponent;
    private StringBuilder inputBuilder = new StringBuilder(); // 문자열 처리 최적화

    private void Awake()
    {
        instance = this;
        loginButtonComponent = loginButton.GetComponent<Button>(); // 캐싱

        // 풀 초기화 (AuthManager와 동일 크기)
        for (int i = 0; i < 10; i++)
        {
            var tap = Instantiate(gradeTap);
            tap.SetActive(false);
            gradeTapPool.Enqueue(tap);

            var button = Instantiate(userButton);
            button.SetActive(false);
            userButtonPool.Enqueue(button);

            var scroll = Instantiate(userListScrollGameObject);
            scroll.SetActive(false);
            scrollPool.Enqueue(scroll);
        }
    }

    private void Start()
    {
        AuthInitAsync().Forget(); // 비동기 초기화
    }

    private async UniTask AuthInitAsync()
    {
        await AuthManager.instance.OnDeviceRegistUUIDAsync();
    }

    public void OnInputNumber(string number)
    {
        inputBuilder.Append(number);
        authInputField.text = inputBuilder.ToString();
    }

    public void OnDelAuthInput()
    {
        if (inputBuilder.Length > 0)
        {
            inputBuilder.Length--;
            authInputField.text = inputBuilder.ToString();
        }
    }

    public void OnClearAuthInput()
    {
        inputBuilder.Clear();
        authInputField.text = string.Empty;
    }

    public async UniTask OnAuthEnterAsync()
    {
        if (inputBuilder.Length == 0)
            return;

        if (!string.IsNullOrEmpty(AuthManager.instance.DEVICE_SN))
        {
            if (inputBuilder.ToString() == "0821")
            {
                await AuthManager.instance.OnDeviceResetAuthAsync();
#if UNITY_EDITOR
                Debug.Log("디바이스 초기화 요청");
#endif
            }
        }
        else
        {
            authInputGameObject.SetActive(false);
            DeviceRequest deviceRequest = new DeviceRequest
            {
                deviceSN = inputBuilder.ToString()
            };
            AuthManager.instance.savedSN = deviceRequest.deviceSN;
            await AuthManager.instance.OnDeviceRegistUUIDAsync(deviceRequest.deviceSN);
#if UNITY_EDITOR
            Debug.Log($"인증 요청: SN={deviceRequest.deviceSN}");
#endif
        }
    }

    public void OnAuthEnter()
    {
        OnAuthEnterAsync().Forget(); // 버튼 호출용
    }

    public void OnDisconnectedServer()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("AuthMain");
    }

    public void OnLongonButtonDeActive()
    {
        loginButtonComponent.onClick.RemoveAllListeners();
        loginButtonComponent.interactable = false;
    }

    public void OnResetDeviceSN()
    {
        authInputGameObject.SetActive(true);
        authsuccessGameObject.SetActive(false);
    }

    public void QuitApp()
    {
        Application.Quit();
    }

    // 풀 반환 메서드 (AuthManager와 동기화)
    public void ReturnToPool(GameObject obj, string type)
    {
        obj.SetActive(false);
        if (type == "gradeTap")
            gradeTapPool.Enqueue(obj);
        else if (type == "userButton")
            userButtonPool.Enqueue(obj);
        else if (type == "scroll")
            scrollPool.Enqueue(obj);
    }

    // 풀에서 가져오기
    public GameObject GetFromPool(string type)
    {
        Queue<GameObject> pool = type switch
        {
            "gradeTap" => gradeTapPool,
            "userButton" => userButtonPool,
            "scroll" => scrollPool,
            _ => null
        };
        return pool != null && pool.Count > 0 ? pool.Dequeue() : null;
    }
}