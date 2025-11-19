using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class New_SCM : MonoBehaviour
{
    public static New_SCM instance;

    [Header("UI추가 요소")]
    [SerializeField] private TextMeshProUGUI infotext;
    [SerializeField] private Image[] step_state;
    [SerializeField] private Image[] fmb_state;
    [SerializeField] private GameObject info_menu;
    [SerializeField] private Button button_use;
    public Color waiting;
    public Color doing;
    public Color done;
    public Color fmb_active;

    [Header("상태 및 데이터")]
    public State m_State = State.Middle;
    public bool practice = false;
    public List<TextMeshProUGUI> timeTextS = new();
    public List<TextMeshProUGUI> oxTextS = new();
    protected ResultData ncs = new();
    protected int currentStep = 0;
    public int maxStep = 0;
    public float runtime;
    public float keepingTime = 1f; // 기본값 설정
    private float uiUpdateTimer = 0f;
    private const float uiUpdateInterval = 0.1f; // UI 업데이트 간격 (메모리 및 CPU 부하 감소)
    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI userName;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI user;
    [SerializeField] private TextMeshProUGUI mainText;
    [SerializeField] private GameObject uiCanvas;
    [SerializeField] private GameObject miniA;
    [SerializeField] private GameObject miniD;
    [SerializeField] private GameObject nextD;
    [SerializeField] private GameObject next3D;
    [SerializeField] private GameObject resultPage;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private GameObject miniMap;
    [SerializeField] private GameObject middleImage;
    [SerializeField] private GameObject resultImage;
    [SerializeField] private GameObject replay;
    [SerializeField] private GameObject end;
    [Header("플레이어 손")]
    [SerializeField] private GameObject rightHand;
    [SerializeField] private GameObject leftHand;
    private bool leftHandOn = false;
    private bool rightHandOn = false;
    [SerializeField] private GameObject step1RHand;
    [SerializeField] private GameObject step1LHand;
    [SerializeField] private GameObject step2RHand;
    [SerializeField] private GameObject step3RHand;
    [SerializeField] private GameObject step4RHand;
    [SerializeField] private GameObject step5RHand;
    [SerializeField] private GameObject step9RHand;
    [SerializeField] private GameObject step3Col;
    [SerializeField] private GameObject step4Col;
    [SerializeField] private GameObject chukgul_obj;
    [SerializeField] private GameObject chukgul_obj2;
    [SerializeField] private GameObject chukgul_obj3;
    [SerializeField] private GameObject etcHand_obj;
    [SerializeField] private GameObject chukR_obj;
    [SerializeField] private GameObject chukM_obj;
    [SerializeField] private GameObject chukF_obj;
    [SerializeField] private GameObject chukB_obj;
    [SerializeField] private GameObject rotateH_obj;
    [SerializeField] private GameObject rotateG_obj;
    [Header("애니메이션 및 오디오")]
    [SerializeField] private Animator chunaAnim;
    [SerializeField] private Animator chunaAnim2;
    [SerializeField] private AudioClip[] narration;
    [SerializeField] private AudioClip[] testNarration;
    [SerializeField] private AudioSource stepNarration;
    [SerializeField] private AudioSource dingdong;
    [SerializeField] private AudioSource wrongA;
    [SerializeField] private AudioSource warning;
    [SerializeField] private AudioSource limitLine;
    [Header("단계별 설정")]
    private int stepNo = 0;
    [Header("UI - 측굴")]
    [SerializeField] private GameObject gunchukMenu;
    [SerializeField] private GameObject chukgulAngle;
    [SerializeField] private Image chukgulround;
    [SerializeField] private Slider chukgulSlider;
    [SerializeField] private Slider chukgulSlider2;
    [SerializeField] private GameObject strech;
    [SerializeField] private GameObject angledisplay;
    [Header("UI - 회전")]
    [SerializeField] private GameObject gunhweaMenu;
    [SerializeField] private GameObject gunhweaAngle;
    [SerializeField] private Image gunhwearound;
    [SerializeField] private GameObject angledisplay2;
    [SerializeField] private GameObject menu_f;
    [SerializeField] private GameObject menu_m;
    [SerializeField] private GameObject menu_b;
    [Header("테스트 모드")]
    public bool testMode;
    public float timeLimit = 10f;
    private float timeCountTest = 0f;
    private int order = 0;
    private float headStrechTime = 0f;
    [Header("세부 학목 추가 영역")]
    public string CU;
    public string LLM;
    public string LL1;
    public string LL2;
    public RunStatus runStatus = new();
    public int Count = 0;
    [SerializeField] private GameObject[] replayCount;
    [SerializeField] private GameObject skelmus;
    [SerializeField] private GameObject[] human;
    [SerializeField] private GameObject[] humanBantu;
    private bool passthroughOn = true;
    private bool humanOn = true;
    private Coroutine stepCor;
    private Coroutine runTimeChecker;
    private void Awake()
    {
        instance = this;
        if (!ValidateReferences()) // 초기화 시 참조 검증
        {
            Debug.LogError("Critical references are missing! Disabling Seat_Upper.");
            enabled = false;
        }
    }
    private bool ValidateReferences()
    {
        return chukgul_obj != null && rotateH_obj != null && rotateG_obj != null &&
               chunaAnim != null && chunaAnim2 != null && rightHand != null &&
               stepNarration != null && dingdong != null &&
               wrongA != null && warning != null && limitLine != null;
    }
    private void Start()
    {
        if (AuthManager.instance != null)
        {
            Debug.Log("AuthManager Start");
            ncs.orgID = AuthManager.instance.currentOrgID;
            ncs.userId = AuthManager.instance.currentUserID;
            ncs.username = AuthManager.instance.currentRunUser;
            ncs.subject = AuthManager.instance.currentContents;
            ncs.competenyUnit = CU;
            ncs.learnModule = LLM;
            ncs.learnLevel1 = LL1;
            ncs.learnLevel2 = LL2;
            UpdateNcsResult();
            if (userName != null) userName.text = ncs.username;
        }
    }
    public void CurrentUser()
    {
        if (AuthManager.instance != null && user != null)
            user.text = AuthManager.instance.currentRunUser;
    }
    private void UpdateNcsResult()
    {
        ncs.totalCnt = maxStep.ToString();
        ncs.doneCnt = (currentStep - 1).ToString();
        ncs.runtime = runtime.ToString();
    }
    private void UpdateNcsAndPost()
    {
        UpdateNcsResult();
        if (AuthManager.instance != null)
            AuthManager.instance.PostResultAsync(ncs);
    }

    private void SetStepCommon(int newOrder, string narrationClip, string mainTextContent)
    {
        currentStep++;
        runStatus.status = $"{LLM}/{maxStep}/{currentStep}";
        if (AuthManager.instance != null)
        {
            runStatus.deviceSN = AuthManager.instance.DEVICE_SN;
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);
        }
        if (stepNarration != null)
        {
            stepNarration.mute = false;
            stepNarration.clip = testMode ? testNarration[newOrder == 8 ? 1 : newOrder == 9 ? 2 : 0] :
                narrationClip != null ? narration[GetNarrationIndex(narrationClip)] : null;
            if (stepNarration.clip != null) stepNarration.Play();
        }
        if (mainText != null) mainText.text = mainTextContent;
    }
    private int GetNarrationIndex(string clipName)
    {
        return clipName switch
        {
            "0" => 0,
            "1" => 1,
            "2" => 2,
            "3_1" => 3,
            "3_2" => 4,
            "3_3" => 5,
            "4" => 6,
            "5" => 7,
            "6" => 8,
            "7" => 9,
            _ => 0
        };
    }

    IEnumerator Runtime()
    {
        while (true)
        {
            runtime += Time.deltaTime;
            if (timeText != null)
            {
                int m = (int)(runtime / 60);
                int s = (int)(runtime % 60);
                timeText.text = $"{m:D2}:{s:D2}";
            }
            yield return new WaitForFixedUpdate();
        }
    }
    public void CheckTestResult(float time, string ox)
    {
        if (!testMode || timeTextS.Count <= currentStep - 1 || oxTextS.Count <= currentStep - 1) return;
        timeTextS[currentStep - 1].text = $"{time:F1}초";
        oxTextS[currentStep - 1].text = ox;
        ncs.learnLevel2 += $"{currentStep}단계: {time:F2}초/{ox}\n";
    }

    public void InitInfo()
    {
        if (runTimeChecker == null)
            runTimeChecker = StartCoroutine(Runtime());
        info_menu.SetActive(true);
        SetStepCommon(0, "0", "사각근 이완강화기법");
    }

    public void StepSelect()
    {
        fmb_state[0].color = waiting;
        fmb_state[1].color = waiting;
        fmb_state[2].color = waiting;

        switch (m_State)
        {
            case State.Front:
                fmb_state[0].color = fmb_active;
                Step1Start();
                break;
            case State.Middle:
                fmb_state[1].color = fmb_active;
                Step3Start();
                break;
            case State.Back:
                fmb_state[2].color = fmb_active;
                Step3Start();
                break;
            default:
                FindAnyObjectByType<UIManager>().LogoutPopUp();
                break;
        }

        button_use.interactable = false;
    }

    public void Step1Start()
    {
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(HandCheck());
        SetStepCommon(1, "1", "1. 주동수\n- 환자의 후두부\n2.보조수\n- 상부승모근\n 쇄골 부착부");
        if (step1LHand != null && step1RHand != null)
        {
            step1LHand.transform.parent.gameObject.SetActive(true);
            step1RHand.transform.parent.gameObject.SetActive(true);
            etcHand_obj.SetActive(true);
        }
        if (nextD != null)
        {
            nextD.SetActive(true);
            var button = nextD.transform.parent.GetComponent<Button>();
            if (button != null) button.interactable = false;
        }

        infotext.transform.parent.gameObject.SetActive(true);
        infotext.text = "주동수 보조수 위치하기";
        step_state[0].color = doing;
        
    }
    public void Step1LeftHand(bool on)
    {
        leftHandOn = on;
        if (step1LHand != null) step1LHand.SetActive(!on);
    }
    public void Step1RightHand(bool on)
    {
        rightHandOn = on;
        if (step1RHand != null) step1RHand.SetActive(!on);
    }
    IEnumerator HandCheck()
    {
        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;
                if (dingdong != null) dingdong.Play();
                etcHand_obj.SetActive(false);
                Step3Start();
                break;
            }
            yield return null;
        }
    }
    public void Step3Start()
    {
        step_state[0].color = doing;
        step_state[1].color = waiting;
        step_state[2].color = waiting;
        step_state[3].color = waiting;
        step_state[4].color = waiting;
        rightHandOn = false;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(Chukgul());
        SetStepCommon(2, "2", "2.건측 측굴");
        infotext.transform.parent.gameObject.SetActive(true);
        infotext.text = "[공통] 건측 측굴하기";
    }

    IEnumerator Chukgul()
    {
        if (chukgul_obj == null || chunaAnim == null || chunaAnim2 == null)
            yield break;
        if (stepNo == 11 && stepNarration != null)
            stepNarration.mute = true;

        if (gunchukMenu != null) gunchukMenu.SetActive(true);
        if (rotateH_obj != null) rotateH_obj.SetActive(false);
        if (rotateG_obj != null) rotateG_obj.SetActive(false);
        if (chukgulround != null) chukgulround.gameObject.SetActive(true);

        if (chukgul_obj != null && chukgul_obj.GetComponent<LocalRotationAnimator>() != null)
        {
            chukgul_obj.GetComponent<LocalRotationAnimator>().enabled = true;
            chukgul_obj.SetActive(true);
        }

        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;
        if (step3RHand != null) step3RHand.SetActive(true);

        Debug.Log(chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime + " : " + stepNo + " : " + chunaAnim.GetCurrentAnimatorStateInfo(0).IsName("사각근 건측 측굴"));

        yield return new WaitUntil(() => chunaAnim.GetCurrentAnimatorStateInfo(0).IsName("사각근 건측 측굴"));

        float timeChukgul = 0;
        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                float normalizedTime = chunaAnim != null ? chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
                uiUpdateTimer += Time.deltaTime;
                if (uiUpdateTimer >= uiUpdateInterval)
                {
                    if (chukgulAngle != null)
                        chukgulAngle.transform.localEulerAngles = 90 * Mathf.Max(normalizedTime, 0) * Vector3.forward;
                    if (chukgulSlider != null) chukgulSlider.value = normalizedTime;
                    if (chukgulSlider2 != null) chukgulSlider2.value = normalizedTime * 10.5f;
                    uiUpdateTimer = 0f;
                }

                if (normalizedTime > 0.278f && normalizedTime < 0.5f)
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    angledisplay.GetComponent<TextMeshProUGUI>().color = Color.green;
                    if (limitLine != null) limitLine.mute = false;
                }
                else if (normalizedTime >= 0.5f)
                {
                    if (warning != null) warning.mute = false;
                    timeChukgul = 0;
                    angledisplay.GetComponent<TextMeshProUGUI>().color = Color.red;
                    if (limitLine != null) limitLine.mute = true;
                }
                else
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul = 0;
                    angledisplay.GetComponent<TextMeshProUGUI>().color = Color.white;
                    if (limitLine != null) limitLine.mute = true;
                }

                chukgulround.fillAmount = 1 - timeChukgul;
                chukgulround.transform.Find("Image (5)").GetComponent<Image>().fillAmount = 1 - timeChukgul;

                if (limitLine != null) limitLine.pitch = normalizedTime * 6;

                if (timeChukgul >= 1)
                {
                    if (warning != null) warning.mute = true;
                    if (limitLine != null) limitLine.mute = true;
                    if (dingdong != null) dingdong.Play();
                    if (step3RHand != null) step3RHand.SetActive(false);
                    if (chukgulSlider != null) chukgulSlider.value = 0;
                    if (chukgulSlider2 != null) chukgulSlider2.value = 0;
                    if (gunchukMenu != null) gunchukMenu.SetActive(false);
                    if (chukgul_obj != null) chukgul_obj.SetActive(false);
                    if (chunaAnim != null) chunaAnim.enabled = false;
                    if (chunaAnim2 != null) chunaAnim2.enabled = false;
                    if (chukgulround != null) chukgulround.gameObject.SetActive(false);

                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;

                    Step4_1Start();
                    break;
                }
            }
            else
            {
                if (warning != null) warning.mute = true;
                if (limitLine != null) limitLine.mute = true;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
        if (gunchukMenu != null) gunchukMenu.SetActive(false);
    }

    public void Step4_1Start()
    {
        if (chukgul_obj != null) chukgul_obj.SetActive(false);
        if (rotateG_obj != null) rotateG_obj.SetActive(true);
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(ChuckHwueJeon());

        switch (m_State)
        {
            case State.Middle:
                SetStepCommon(3, "3_1", "3.회전\n전부 섬유 - 건측 회전");
                infotext.text = "[전부] 건측 회전하기";
                break;
            case State.Front:
                SetStepCommon(3, "3_2", "3.회전\n중부 섬유 - 건측 회전");
                infotext.text = "[중부] 건측 회전하기";
                break;
            default:
                SetStepCommon(3, "3_3", "3.회전\n후부 섬유 - 건측 회전");
                infotext.text = "[후부] 건측 회전하기";
                break;
        }

        if (step4RHand != null) step4RHand.SetActive(true);
    }

    public float[] range_s;
    public float[] range_e;

    IEnumerator ChuckHwueJeon()
    {
        if (rotateH_obj == null || rotateG_obj == null || chunaAnim == null || chunaAnim2 == null)
            yield break;
        GameObject menu;
        GameObject angleObj;
        Image roundImage;
        GameObject rHand;
        GameObject round_menu;
        float rangeS;
        float rangeE;

        angledisplay.SetActive(false);
        angledisplay2.SetActive(true);

        menu = gunhweaMenu;
        angleObj = gunhweaAngle;
        roundImage = gunhwearound;
        rHand = step9RHand;
        rotateG_obj.SetActive(true);

        rangeS = m_State switch
        {
            State.Front => range_s[0],
            State.Middle => range_s[1],
            State.Back => range_s[2],
            _ => throw new System.NotImplementedException()
        };

        rangeE = m_State switch
        {
            State.Front => range_e[0],
            State.Middle => range_e[1],
            State.Back => range_e[2],
            _ => throw new System.NotImplementedException()
        };

        round_menu = m_State switch
        {
            State.Front => menu_f,
            State.Middle => menu_m,
            State.Back => menu_b,
            _ => throw new System.NotImplementedException()
        };

        if (menu == null || angleObj == null || roundImage == null || rHand == null)
            yield break;

        roundImage.gameObject.SetActive(true);
        if (rotateG_obj != null && rotateG_obj.GetComponent<LocalRotationAnimator>() != null)
            rotateG_obj.GetComponent<LocalRotationAnimator>().enabled = true;

        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;

        menu.SetActive(true);
        round_menu.SetActive(true);
        float timeChukgul = 0;

        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                float normalizedTime = chunaAnim != null ? chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
                uiUpdateTimer += Time.deltaTime;
                if (uiUpdateTimer >= uiUpdateInterval)
                {
                    angleObj.transform.localEulerAngles = 90 * normalizedTime * Vector3.forward;
                    uiUpdateTimer = 0f;
                }

                if (normalizedTime > rangeS && normalizedTime < rangeE)
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    angledisplay2.GetComponent<TextMeshProUGUI>().color = Color.green;
                    if (limitLine != null) limitLine.mute = false;
                }
                else if (normalizedTime >= rangeE)
                {
                    if (warning != null) warning.mute = false;
                    timeChukgul = 0;
                    angledisplay2.GetComponent<TextMeshProUGUI>().color = Color.red;
                    if (limitLine != null) limitLine.mute = true;
                }
                else
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul = 0;
                    angledisplay2.GetComponent<TextMeshProUGUI>().color = Color.white;
                    if (limitLine != null) limitLine.mute = true;
                }

                roundImage.fillAmount = 1 - timeChukgul;
                roundImage.transform.Find("Image (5)").GetComponent<Image>().fillAmount = 1 - timeChukgul;

                if (limitLine != null) limitLine.pitch = normalizedTime * 6;
                if (timeChukgul >= 1)
                {
                    if (dingdong != null) dingdong.Play();
                    rHand.SetActive(false);
                    round_menu.SetActive(false);
                    menu.SetActive(false);
                    if (warning != null) warning.mute = true;
                    if (limitLine != null) limitLine.mute = true;
                    if (rotateH_obj != null && rotateH_obj.GetComponent<LocalRotationAnimator>() != null)
                        rotateH_obj.GetComponent<LocalRotationAnimator>().enabled = false;
                    if (rotateG_obj != null && rotateG_obj.GetComponent<LocalRotationAnimator>() != null)
                        rotateG_obj.GetComponent<LocalRotationAnimator>().enabled = false;
                    if (chunaAnim != null) chunaAnim.enabled = false;
                    if (chunaAnim2 != null) chunaAnim2.enabled = false;
                    CheckTestResult(timeCountTest, "O");

                    angledisplay.SetActive(true);
                    angledisplay2.SetActive(false);
                    roundImage.gameObject.SetActive(false);

                    timeCountTest = 0;
                    if (stepNo == 11)
                        Step5Start();
                    else
                        LimitCheck();
                    break;
                }
            }
            else
            {
                if (warning != null) warning.mute = true;
                if (limitLine != null) limitLine.mute = true;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void Step5Start()
    {
        step_state[3].color = done;
        step_state[4].color = doing;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(JungRib());
        SetStepCommon(7, "7", "환자 중립상태로 되돌리기");
        if (step5RHand != null) step5RHand.SetActive(true);
        infotext.text = "중립상태로 복귀";
    }

    IEnumerator JungRib()
    {
        angledisplay.GetComponent<TextMeshProUGUI>().color = Color.white;

        chukR_obj = m_State switch
        {
            State.Middle => chukM_obj,
            State.Front => chukF_obj,
            State.Back => chukB_obj,
            _ => null,
        };

        if (chukR_obj == null || chunaAnim == null || chunaAnim2 == null)
            yield break;

        chukR_obj.SetActive(true);

        string aniclip_name = m_State switch
        {
            State.Middle => "중부 중립",
            State.Front => "전부 중립",
            _ => "후부 중립",
        };

        // 애니메이션 상태 전환 대기
        yield return new WaitUntil(() =>
        {
            if (chunaAnim == null) return false;
            var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
            return state.IsName(aniclip_name);
        });


        while (true)
        {
            if (chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.97f)
            {
                if (dingdong != null) dingdong.Play();
                if (step5RHand != null) step5RHand.SetActive(false);
                if (chukR_obj != null) chukR_obj.SetActive(false);
                if (chunaAnim != null) chunaAnim.enabled = false;
                if (chunaAnim2 != null) chunaAnim2.enabled = false;
                step_state[4].color = done;

                if (testMode)
                {
                    switch (m_State)
                    {
                        case State.Middle:
                            m_State = State.Back;
                            Step3Start();
                            break;
                        case State.Front:
                            m_State = State.Middle;
                            Step3Start();
                            break;
                        default:
                            if (mainText != null) mainText.gameObject.SetActive(false);
                            if (resultPage != null) resultPage.SetActive(true);
                            if (miniMap != null) miniMap.SetActive(false);
                            if (resultImage != null) resultImage.SetActive(true);
                            if (middleImage != null) middleImage.SetActive(false);
                            if (end != null)
                            {
                                end.SetActive(false);
                                var button = end.GetComponentInParent<Button>();
                                if (button != null) button.interactable = true;
                            }
                            if (stepNarration != null)
                            {
                                stepNarration.mute = false;
                                stepNarration.clip = testNarration[3];
                                if (stepNarration.clip != null) stepNarration.Play();
                            }
                            UpdateNcsResult();
                            break;
                    }
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                }
                else
                {
                    switch (m_State) //여기 수정해야함
                    {
                        case State.Front:
                            if (nextD != null)
                            {
                                nextD.SetActive(false);
                                var button = nextD.GetComponentInParent<Button>();
                                if (button != null) button.interactable = true;
                                chukgul_obj = chukgul_obj2;
                            }
                            m_State = State.Middle;
                            break;
                        case State.Middle:
                            if (next3D != null)
                            {
                                next3D.SetActive(false);
                                var button = next3D.GetComponentInParent<Button>();
                                if (button != null) button.interactable = true;
                                chukgul_obj = chukgul_obj3;
                            }
                            m_State = State.Back;
                            break;
                        default:
                            if (step1LHand != null) step1LHand.transform.parent.gameObject.SetActive(false);
                            if (step1RHand != null) step1RHand.transform.parent.gameObject.SetActive(false);
                            if (end != null)
                            {
                                end.SetActive(false);
                                var button = end.GetComponentInParent<Button>();
                                if (button != null) button.interactable = true;
                            }
                            m_State = State.None;
                            break;
                    }
                    button_use.interactable = true;
                }
                stepNo = 0;
                break;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void LimitCheck()
    {
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(AnimPlayCheck());
    }

    IEnumerator AnimPlayCheck()
    {
        if (chukgul_obj != null) chukgul_obj.SetActive(false);
        if (rotateH_obj != null) rotateH_obj.SetActive(false);
        if (rotateG_obj != null) rotateG_obj.SetActive(false);
        if (etcHand_obj != null) etcHand_obj.SetActive(true);

        string aniclip_name;

        if (stepNo == 11)
        {
            step_state[2].color = done;
            step_state[3].color = doing;
            SetStepCommon(6, "6", "3.스트레칭");
            infotext.text = "스트레칭 8초";
            aniclip_name = m_State switch
            {
                State.Middle => "중부 스트레칭",
                State.Front => "전부 스트레칭",
                _ => "후부 스트레칭",
            };

            chunaAnim.Play(aniclip_name, 0, 0);
            chunaAnim2.Play(aniclip_name, 0, 0);

            yield return new WaitUntil(() =>
            {
                var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
                return state.IsName(aniclip_name);
            });

            angledisplay.GetComponent<TextMeshProUGUI>().color = Color.blue;
        }
        else
        {
            step_state[0].color = done;
            step_state[1].color = doing;
            SetStepCommon(4, "4", "3.제한장벽 확인");
            infotext.text = "제한장벽 확인";
            aniclip_name = m_State switch
            {
                State.Middle => "사각근 장벽 중부",
                State.Front => "사각근 장벽 전부",
                _ => "사각근 장벽 후부",
            };

            chunaAnim.Play(aniclip_name, 0, 0);
            chunaAnim2.Play(aniclip_name, 0, 0);
        }

        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                if (chunaAnim != null) chunaAnim.enabled = true;
                if (chunaAnim2 != null) chunaAnim2.enabled = true;

                float normalizedTime = chunaAnim != null ? chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;

                Debug.Log(chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime + " : " + stepNo + " : " + aniclip_name);
                
                if (normalizedTime >= 0.99f)
                {
                    dingdong.Play();

                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;

                    if (stepNo == 11)
                    {
                        etcHand_obj.SetActive(false);
                        Step5Start();
                    }
                    else
                        DungCheok();
                    break;
                }
            }
            else
            {
                if (chunaAnim != null) chunaAnim.enabled = false;
                if (chunaAnim2 != null) chunaAnim2.enabled = false;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void DungCheok()
    {
        step_state[1].color = done;
        step_state[2].color = doing;
        infotext.text = "등척성 운동";
        stepNo = 11;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(AnimTimeControl());
        SetStepCommon(5, "5", "등척성 운동하기");
    }
    IEnumerator AnimTimeControl()
    {
        yield return new WaitForSeconds(1);
        if (!testMode && directionOfG != null)
        {
            foreach (var obj in directionOfG)
                if (obj != null) obj.SetActive(true);
        }
        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                headStrechTime += Time.deltaTime;
            }
            if (headStrechTime > 5)
            {
                if (dingdong != null) dingdong.Play();
                headStrechTime = 0;
                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;
                break;
            }
            yield return null;
        }
        if (!testMode && directionOfG != null)
        {
            foreach (var obj in directionOfG)
                if (obj != null) obj.SetActive(false);
        }
        LimitCheck();
    }

    public void Replay()
    {
        if (replay != null)
        {
            replay.SetActive(true);
            var button = replay.GetComponentInParent<Button>();
            if (button != null) button.interactable = false;
        }
        if (end != null)
        {
            end.SetActive(true);
            var button = end.GetComponentInParent<Button>();
            if (button != null) button.interactable = false;
        }
        if (replayCount != null && Count < replayCount.Length)
            replayCount[Count].SetActive(true);
        Count++;
        currentStep = 0;
    }
    public void Exit()
    {
        UpdateNcsAndPost();
        if (MoveSceneManager.instance != null)
            MoveSceneManager.instance.MoveScene("lobby");
    }
    public void Replay2()
    {
        UpdateNcsAndPost();
        if (MoveSceneManager.instance != null)
            MoveSceneManager.instance.MoveScene(SceneManager.GetActiveScene().name);
    }

    [SerializeField] private GameObject[] directionOfG;
    public void SkelHuman()
    {
        if (skelmus == null) return;
        skelmus.SetActive(!skelmus.activeSelf);
        if (humanOn)
        {
            foreach (var obj in human)
            {
                if (obj != null)
                {
                    var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                    if (renderer != null) renderer.enabled = !skelmus.activeSelf;
                }
            }
            foreach (var obj in humanBantu)
            {
                if (obj != null)
                {
                    var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                    if (renderer != null) renderer.enabled = skelmus.activeSelf;
                }
            }
        }
    }
    public void Human()
    {
        humanOn = !humanOn;
        foreach (var obj in human)
        {
            if (obj != null)
            {
                var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.enabled = humanOn && !(passthroughOn || skelmus.activeSelf);
            }
        }
        foreach (var obj in humanBantu)
        {
            if (obj != null)
            {
                var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.enabled = humanOn && (passthroughOn || skelmus.activeSelf);
            }
        }
    }
    public void PassThroughB()
    {
        passthroughOn = !passthroughOn;
        foreach (var obj in human)
        {
            if (obj != null)
            {
                var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.enabled = !passthroughOn;
            }
        }
        foreach (var obj in humanBantu)
        {
            if (obj != null)
            {
                var renderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (renderer != null) renderer.enabled = passthroughOn;
            }
        }
    }
}