using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.UI;
using System.Linq;
using Newtonsoft.Json.Linq;
using Cysharp.Threading.Tasks; // UniTask 추가 (Asset Store에서 설치)

public class AuthManager : MonoBehaviour
{
    public static AuthManager instance = null;
    public string BASE_API_URL = "https://qpqjpivcg1.execute-api.ap-northeast-2.amazonaws.com";
    public List<string> lobbySceneName = new();
    public string SWTitle;
    public string quizVer;
    public string DEVICE_SN;
    [HideInInspector] public DeviceResponseData deviceResponseData;
    [HideInInspector] public string currentOrgID;
    [HideInInspector] public int currentUserID;
    [HideInInspector] public string currentRunUser;
    public string currentContents = "ANA";
    [HideInInspector] public int currentLic;
    [HideInInspector] public MirroringData mirroringData;
    [HideInInspector] public bool getLic = false;
    [HideInInspector] public string savedSN;
    [HideInInspector] public List<QuizData> quizDatas = new();

    // 오브젝트 풀 추가 (UI 최적화)
    private Queue<GameObject> gradeTapPool = new();
    private Queue<GameObject> userButtonPool = new();
    private Queue<GameObject> scrollPool = new(); private const int MaxPoolSize = 20;
    private bool isLoadingUserList = false; // 재귀 방지 플래그

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
        DEVICE_SN = string.Empty;

        // AuthUI 초기화 대기
        if (AuthUI.instance == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("AuthUI.instance가 null입니다. 초기화 대기...");
#endif
            return; // 초기화 중단, Start에서 재시도
        }

        // 풀 초기화
        for (int i = 0; i < 10 && gradeTapPool.Count < MaxPoolSize; i++)
        {
            if (AuthUI.instance.gradeTap == null)
            {
#if UNITY_EDITOR
                Debug.LogError("gradeTap prefab이 할당되지 않았습니다!");
#endif
                break;
            }
            var tap = Instantiate(AuthUI.instance.gradeTap);
            tap.SetActive(false);
            gradeTapPool.Enqueue(tap);
#if UNITY_EDITOR
            Debug.Log($"초기화: gradeTap {tap.name} 추가, 풀 크기: {gradeTapPool.Count}");
#endif

            var button = Instantiate(AuthUI.instance.userButton);
            button.SetActive(false);
            userButtonPool.Enqueue(button);
#if UNITY_EDITOR
            Debug.Log($"초기화: userButton {button.name} 추가, 풀 크기: {userButtonPool.Count}");
#endif

            var scroll = Instantiate(AuthUI.instance.userListScrollGameObject);
            scroll.SetActive(false);
            scrollPool.Enqueue(scroll);
#if UNITY_EDITOR
            Debug.Log($"초기화: scroll {scroll.name} 추가, 풀 크기: {scrollPool.Count}");
#endif
        }
    }

    private void Start()
    {
        // AuthUI 초기화 재확인
        if (AuthUI.instance == null)
        {
#if UNITY_EDITOR
            Debug.LogError("AuthUI.instance가 Start에서도 null입니다. 씬에 AuthUI가 있는지 확인하세요.");
#endif
            return;
        }
    }

    public async UniTask LoadUserListAsync(string orgID)
    {
        if (isLoadingUserList) // 재귀 방지
        {
#if UNITY_EDITOR
            Debug.LogWarning("LoadUserListAsync 이미 실행 중, 중복 호출 방지");
#endif
            return;
        }
        isLoadingUserList = true;

        try
        {
            string url = BASE_API_URL + "/user/userlist/" + orgID;
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 10;
            var op = await request.SendWebRequest().WithCancellation(this.GetCancellationTokenOnDestroy());
            if (request.result != UnityWebRequest.Result.Success)
            {
#if UNITY_EDITOR
                Debug.Log("유저 목록 가져오기 실패: " + request.downloadHandler.text);
#endif
                AuthUI.instance.authsuccessGameObject.SetActive(true);
                return;
            }
            var result = JsonConvert.DeserializeObject<UserData[]>(request.downloadHandler.text);
            AuthUI.instance.authsuccessGameObject.SetActive(true);
            AuthUI.instance.grade.Clear();
            AuthUI.instance.classAmount = 0;
            var uniqueGrades = new HashSet<string>();
            foreach (UserData i in result)
            {
                if (uniqueGrades.Add(i.grade))
                {
                    AuthUI.instance.grade.Add(i.grade);
                    AuthUI.instance.classAmount++;
                }
            }
            AuthUI.instance.grade.Sort((a, b) => (a.Contains("--") || b.Contains("--")) ? -1 : a.CompareTo(b));
            AuthUI.instance.gradeTapGameObject.SetActive(AuthUI.instance.classAmount > 0);

            // 기존 탭/콘텐츠 클리어
            foreach (var oldTap in AuthUI.instance.Taps.ToArray())
            {
                oldTap.SetActive(false);
                if (gradeTapPool.Count < MaxPoolSize)
                {
                    gradeTapPool.Enqueue(oldTap);
#if UNITY_EDITOR
                    Debug.Log($"탭 반환: {oldTap.name}, 풀 크기: {gradeTapPool.Count}");
#endif
                }
            }
            AuthUI.instance.Taps.Clear();
            foreach (var oldContent in AuthUI.instance.contents.ToArray())
            {
                oldContent.SetActive(false);
                if (scrollPool.Count < MaxPoolSize)
                {
                    scrollPool.Enqueue(oldContent);
#if UNITY_EDITOR
                    Debug.Log($"스크롤 반환: {oldContent.name}, 풀 크기: {scrollPool.Count}");
#endif
                }
            }
            AuthUI.instance.contents.Clear();

            List<Toggle> allToggles = new();
            int maxIterations = Math.Min(AuthUI.instance.grade.Count, MaxPoolSize);
            for (int i = 0; i < maxIterations; i++)
            {
                GameObject tap;
                if (gradeTapPool.Count > 0)
                {
                    tap = gradeTapPool.Dequeue();
#if UNITY_EDITOR
                    Debug.Log($"탭 재사용: {tap.name}, 남은 풀: {gradeTapPool.Count}");
#endif
                }
                else
                {
                    if (AuthUI.instance.gradeTap == null)
                    {
#if UNITY_EDITOR
                        Debug.LogError("gradeTap prefab이 null입니다!");
#endif
                        continue;
                    }
#if UNITY_EDITOR
                    Debug.Log("탭 풀 고갈, 새로 생성");
#endif
                    tap = Instantiate(AuthUI.instance.gradeTap);
                }
                tap.transform.SetParent(AuthUI.instance.gradeTapGameObject.transform, false);
                tap.SetActive(true);
                var toggle = tap.GetComponent<Toggle>();
                toggle.group = AuthUI.instance.gradeTapGameObject.GetComponent<ToggleGroup>();
                toggle.onValueChanged.RemoveAllListeners(); // 이벤트 중복 방지
                tap.transform.Find("Background/Text (TMP)").GetComponent<TMPro.TextMeshProUGUI>().text = AuthUI.instance.grade[i];
                tap.transform.Find("Background/Checkmark/Text (TMP)").GetComponent<TMPro.TextMeshProUGUI>().text = AuthUI.instance.grade[i];
                tap.GetComponent<TapInfo>().tapNum = i;
                if (AuthUI.instance.classAmount > 6)
                    tap.GetComponent<RectTransform>().sizeDelta = new Vector2((956 - (AuthUI.instance.classAmount + 1) * 5f) / AuthUI.instance.classAmount, 50);
                AuthUI.instance.Taps.Add(tap);
                allToggles.Add(toggle);

                GameObject scroll;
                if (scrollPool.Count > 0)
                {
                    scroll = scrollPool.Dequeue();
#if UNITY_EDITOR
                    Debug.Log($"스크롤 재사용: {scroll.name}, 남은 풀: {scrollPool.Count}");
#endif
                }
                else
                {
                    if (AuthUI.instance.userListScrollGameObject == null)
                    {
#if UNITY_EDITOR
                        Debug.LogError("userListScrollGameObject prefab이 null입니다!");
#endif
                        continue;
                    }
#if UNITY_EDITOR
                    Debug.Log("스크롤 풀 고갈, 새로 생성");
#endif
                    scroll = Instantiate(AuthUI.instance.userListScrollGameObject);
                }
                scroll.transform.SetParent(AuthUI.instance.Viewport, false);
                scroll.SetActive(false);
                AuthUI.instance.contents.Add(scroll);
            }

            if (allToggles.Count > 0)
            {
                allToggles[0].isOn = true;
                AuthUI.instance.contents[0].SetActive(true);
            }

            foreach (UserData i in result)
            {
                GameObject userButtonObj;
                if (userButtonPool.Count > 0)
                {
                    userButtonObj = userButtonPool.Dequeue();
#if UNITY_EDITOR
                    Debug.Log($"버튼 재사용: {userButtonObj.name}, 남은 풀: {userButtonPool.Count}");
#endif
                }
                else
                {
                    if (AuthUI.instance.userButton == null)
                    {
#if UNITY_EDITOR
                        Debug.LogError("userButton prefab이 null입니다!");
#endif
                        continue;
                    }
#if UNITY_EDITOR
                    Debug.Log("버튼 풀 고갈, 새로 생성");
#endif
                    userButtonObj = Instantiate(AuthUI.instance.userButton);
                }
                userButtonObj.SetActive(true);
                userButtonObj.transform.GetChild(0).GetComponent<Text>().text = i.username;
                string grade = i.grade;
                int idx = AuthUI.instance.grade.IndexOf(grade);
                if (idx >= 0 && idx < AuthUI.instance.contents.Count)
                {
                    userButtonObj.transform.SetParent(AuthUI.instance.contents[idx].transform, false);
                }
                else
                {
#if UNITY_EDITOR
                    Debug.LogError($"[오류] 사용자 {i.username}의 grade '{grade}'에 해당하는 탭이 없습니다.");
#endif
                    userButtonObj.SetActive(false);
                    userButtonPool.Enqueue(userButtonObj);
                    continue;
                }
                var userInfo = userButtonObj.GetComponent<UserInfo>();
                userInfo.runUser = i.username;
                userInfo.userID = i.idx;
                userButtonObj.GetComponent<Button>().onClick.RemoveAllListeners();
                userButtonObj.GetComponent<Button>().onClick.AddListener(() => OnLongonButtonActive(userInfo));
#if UNITY_EDITOR
                Debug.Log($"idx: {i.idx}, username: {i.username}, grade: {i.grade}");
#endif
            }
#if UNITY_EDITOR
            Debug.Log("LoadUserListAsync 완료 - 생성된 탭 수: " + AuthUI.instance.Taps.Count);
#endif
        }
        finally
        {
            isLoadingUserList = false; // 플래그 해제
        }
    }

    private void OnDestroy()
    {
        // 풀 클린업 (필요 시)
    }

    public async UniTask OnDeviceResetAuthAsync()
    {
        var data = new DeviceRequest
        {
            deviceSN = DEVICE_SN,
            cocoModule = currentContents
        };
        string url = BASE_API_URL + "/device/regist/reset";
        string json = JsonConvert.SerializeObject(data);
        try
        {
            await PostAsync<string>(url, json);
            DEVICE_SN = "";
            PlayerPrefs.DeleteKey("DEVICE_SN");
            SceneManager.LoadScene("AuthMain");
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError("초기화 실패: " + e.Message);
#endif
        }
    }

    public async UniTask OnDeviceRegistUUIDAsync(string overrideDeviceSN = null)
    {
        var deviceUUID = new DeviceUUID
        {
            deviceSN = overrideDeviceSN ?? string.Empty,
            deviceUUID = SystemInfo.deviceUniqueIdentifier
        };
        string url = BASE_API_URL + "/device/auth/chuna";
        string json = JsonConvert.SerializeObject(deviceUUID);
        try
        {
            var result = await PostAsync<DeviceResponseData>(url, json);
            deviceResponseData = result;
            currentOrgID = deviceResponseData.orgID;
            if (deviceResponseData.licCHUNA >= 1)
            {
                currentLic = deviceResponseData.licCHUNA;
                DEVICE_SN = deviceUUID.deviceSN;
                await LoadUserListAsync(result.orgID);
            }
            else
            {
                AuthUI.instance.authLicenseGameObject.SetActive(true);
            }
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogWarning("UUID 인증 실패: " + e.Message);
#endif
            if (e.Message.Contains("등록된 장치입니다"))
            {
                await OnDeviceRegistUUIDAsync(SystemInfo.deviceUniqueIdentifier.Substring(0, 10));
                return;
            }
            else
            {
                await OnDeviceRegistUUIDAsync();
            }
        }
    }

    public async UniTask OnUpdateRunStatusAsync(RunStatus status)
    {
        string url = BASE_API_URL + "/device/update/runstatus";
        string json = JsonConvert.SerializeObject(status);
        try
        {
            string responseText = await PostAsyncRaw(url, json);
            var parsed = JObject.Parse(responseText);
            string result = parsed["result"]?.ToString()?.Trim();
            if (string.Equals(result, "success", StringComparison.OrdinalIgnoreCase))
            {
#if UNITY_EDITOR
                Debug.Log("상태 업데이트 성공");
#endif
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("상태 업데이트 실패: " + result);
#endif
            }
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError("상태 업데이트 예외: " + e.Message);
#endif
        }
    }

    public void OnLongonButtonActive(UserInfo userInfo)
    {
        var button = AuthUI.instance.loginButton.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.interactable = true;
        button.onClick.AddListener(async () => await OnLogonPostAsync(userInfo));
    }

    public async UniTask OnLogonPostAsync(UserInfo userInfo)
    {
        var localIPs = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
        string localIP = localIPs.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "";
        string snShort = DEVICE_SN.Length >= 3 ? DEVICE_SN.Substring(DEVICE_SN.Length - 3) : DEVICE_SN;
        var sb = new StringBuilder(localIP); // StringBuilder로 최적화
        sb.Append(" / ").Append(snShort);
        LogonData data = new()
        {
            deviceSN = DEVICE_SN,
            status = "LOGON",
            runUser = userInfo.runUser,
            runContents = currentContents,
            deviceInfo = sb.ToString()
        };
        currentUserID = userInfo.userID;
        currentRunUser = userInfo.runUser;
        string json = JsonConvert.SerializeObject(data);
        string url = BASE_API_URL + "/device/logon";
        try
        {
            mirroringData = await PostAsync<MirroringData>(url, json);
            AuthUI.instance.authPopUpGameObject.SetActive(false);
            AuthUI.instance.authsuccessGameObject.SetActive(false);
            SceneManager.LoadScene("LoadingScene");
            await UniTask.NextFrame(); // UniTask로 대체
            await SceneManager.LoadSceneAsync(lobbySceneName[currentLic - 1]);
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError("로그온 실패: " + e.Message);
#endif
        }
    }

    public async UniTask PostResultAsync(ResultData resultData)
    {
        string url = BASE_API_URL + "/result/chuna";
        string json = JsonConvert.SerializeObject(resultData);
        await PostAsync<string>(url, json);
    }

    public async UniTask GetQuizsAsync()
    {
        quizDatas.Clear();
        string url = BASE_API_URL + "/quiz/" + currentContents.ToLower();
        var request = new RequestQuizData
        {
            orgID = deviceResponseData.orgID,
            version = quizVer
        };
        string json = JsonConvert.SerializeObject(request);
        try
        {
            var result = await PostAsync<QuizData[]>(url, json);
            quizDatas.AddRange(result);
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError("퀴즈 로딩 실패: " + e.Message);
#endif
        }
    }

    public async UniTask<T> PostAsync<T>(string url, string json)
    {
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 10; // 타임아웃 추가
        var operation = await request.SendWebRequest().WithCancellation(this.GetCancellationTokenOnDestroy());
        if (request.result != UnityWebRequest.Result.Success)
            throw new Exception(request.downloadHandler.text);
        return JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
    }

    public async UniTask OnLogoffPost()
    {
        var data = new LogoffData
        {
            deviceSN = DEVICE_SN,
            status = "LOGOFF",
            runUser = currentRunUser,
            runContents = currentContents,
            deviceInfo = string.Empty
        };
        string json = JsonConvert.SerializeObject(data);
        string url = BASE_API_URL + "/device/logoff";
        try
        {
            string result = await PostAsyncRaw(url, json);
#if UNITY_EDITOR
            Debug.Log("로그오프 결과: " + result);
#endif
        }
        catch (Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError("로그오프 실패: " + e.Message);
#endif
        }
        if (MoveSceneManager.instance != null)
            Destroy(MoveSceneManager.instance);
        SceneManager.LoadScene("AuthMain Copy");
    }

    public async UniTask<string> PostAsyncRaw(string url, string json)
    {
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 10;
        var operation = await request.SendWebRequest().WithCancellation(this.GetCancellationTokenOnDestroy());
        if (request.result != UnityWebRequest.Result.Success)
            throw new Exception($"HTTP 오류: {request.responseCode}, {request.downloadHandler.text}");
        return request.downloadHandler.text.Trim();
    }

    private void OnApplicationQuit()
    {
        if (!string.IsNullOrEmpty(DEVICE_SN)) OnLogoffPost().Forget(); // UniTask로 백그라운드 실행
    }
}