using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Chest_S : MonoBehaviour
{
    public static Chest_S instance;

    public InfoUICtrl iuc;

    [Header("»óÅÂ ¹× µ¥ÀÌÅÍ")]
    public State m_State = State.Middle;
    public bool practice = false;
    public List<TextMeshProUGUI> timeTextS = new();
    public List<TextMeshProUGUI> oxTextS = new();
    protected ResultData ncs = new();
    protected int currentStep = 0;
    public int maxStep = 0;
    public float runtime;
    public float keepingTime = 1f; // ±âº»°ª ¼³Á¤
    [Header("UI ¿ä¼Ò")]
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
    [SerializeField] private GameObject angledisplay;
    [SerializeField] private GameObject limitinfo;
    [SerializeField] private Image limit;
    [SerializeField] private Image strech;
    [Header("ÇÃ·¹ÀÌ¾î ¼Õ")]
    [SerializeField] private GameObject rightHand;
    [SerializeField] private GameObject leftHand;
    private bool leftHandOn = false;
    private bool rightHandOn = false;
    [SerializeField] private GameObject step1RHand;
    [SerializeField] private GameObject step1LHand;
    [SerializeField] private GameObject chukgul_obj;
    [SerializeField] private GameObject chukgul_obj2;
    [SerializeField] private GameObject chukgul_obj3;
    [SerializeField] private GameObject rotateH_obj;
    [SerializeField] private GameObject rotateG_obj;
    [SerializeField] private GameObject etcHand_objR;
    [SerializeField] private GameObject etcHand_objL;
    [SerializeField] private GameObject form_obj;
    [SerializeField] private GameObject chukR_obj;
    [SerializeField] private GameObject chukM_obj;
    [SerializeField] private GameObject chukF_obj;
    [SerializeField] private GameObject chukB_obj;
    [Header("¾Ö´Ï¸ÞÀÌ¼Ç ¹× ¿Àµð¿À")]
    [SerializeField] private Animator chunaAnim;
    [SerializeField] private Animator chunaAnim2;
    [SerializeField] private AudioClip[] narration;
    [SerializeField] private AudioClip[] testNarration;
    [SerializeField] private AudioSource stepNarration;
    [SerializeField] private AudioSource dingdong;
    [SerializeField] private AudioSource wrongA;
    [SerializeField] private AudioSource warning;
    [SerializeField] private AudioSource limitLine;
    [Header("´Ü°èº° ¼³Á¤")]
    private int stepNo = 0;
    [Header("Å×½ºÆ® ¸ðµå")]
    public bool testMode;
    public float timeLimit = 10f;
    private float timeCountTest = 0f;
    private int order = 0;
    private float headStrechTime = 0f;
    [Header("¼¼ºÎ ÇÐ¸ñ Ãß°¡ ¿µ¿ª")]
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
        if (!ValidateReferences()) // ÃÊ±âÈ­ ½Ã ÂüÁ¶ °ËÁõ
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
        iuc.infotext.text = mainTextContent;
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
            stepNarration.clip = narration[GetNarrationIndex(narrationClip)];
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
        timeTextS[currentStep - 1].text = $"{time:F1}ÃÊ";
        oxTextS[currentStep - 1].text = ox;
        ncs.learnLevel2 += $"{currentStep}´Ü°è: {time:F2}ÃÊ/{ox}\n";
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

    public void InitInfo()
    {
        if (runTimeChecker == null)
            runTimeChecker = StartCoroutine(Runtime());
        iuc.info_menu.SetActive(true);
        SetStepCommon(0, "0", "´ëÈä±Ù ÀÌ¿Ï°­È­±â¹ý");
    }

    public void StepSelect()
    {
        iuc.fmb_state[0].color = iuc.waiting;
        iuc.fmb_state[1].color = iuc.waiting;
        iuc.fmb_state[2].color = iuc.waiting;

        switch (m_State)
        {
            case State.Middle:
                iuc.fmb_state[0].color = iuc.fmb_active;
                ReadyToSubHand();
                break;
            case State.Front:
                iuc.fmb_state[1].color = iuc.fmb_active;
                ReadyToMainHand();
                break;
            case State.Back:
                iuc.fmb_state[2].color = iuc.fmb_active;
                ReadyToMainHand();
                break;
            default:
                FindAnyObjectByType<UIManager>().LogoutPopUp();
                break;
        }

        iuc.button_use.interactable = false;
    }

    public void ReadyToSubHand()
    {
        iuc.step_state[0].color = iuc.doing;
        form_obj.SetActive(true);
        stepCor = StartCoroutine(CheckAnimationStateS());
        SetStepCommon(1, "1", "º¸Á¶¼ö: Èä°ñ³»Ãø");
    }

    IEnumerator CheckAnimationStateS()
    {
        while (true)
        {
            if (chunaAnim != null && chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.99f)
            {
                if (dingdong != null) dingdong.Play();
                if (form_obj != null) form_obj.SetActive(false);
                ReadyToMainHand();
                break;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void ReadyToMainHand()
    {
        etcHand_objL.SetActive(true);
        iuc.step_state[0].color = iuc.doing;
        iuc.step_state[1].color = iuc.waiting;
        iuc.step_state[2].color = iuc.waiting;
        iuc.step_state[3].color = iuc.waiting;
        iuc.step_state[4].color = iuc.waiting;
        chukgul_obj.SetActive(true);
        stepCor = StartCoroutine(HandCheckM());
        SetStepCommon(2, "2", "ÁÖµ¿¼ö: ÆÈ²ÞÄ¡");
        //SetStepCommon(3, "narration_2", "2.µ¿Ãø °ß°üÀý ±¼°î ¹× ¿ÜÀü ÈÄ\n°¡½¿ ¹Ý´ë¹æÇâÀ¸·Î ³»Àü½ÃÅ°±â");
    }
    IEnumerator HandCheckM()
    {
        while (true)
        {
            if (leftHandOn)
            {
                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;
                if (dingdong != null) dingdong.Play();
                DoToMainHand();
                break;
            }
            yield return null;
        }
    }
    public void DoToMainHand()
    {
        chukgul_obj.SetActive(true);
        stepCor = StartCoroutine(CheckAnimationStateM());
        //SetStepCommon(3, "narration_2", "2.µ¿Ãø °ß°üÀý ±¼°î ¹× ¿ÜÀü ÈÄ\n°¡½¿ ¹Ý´ë¹æÇâÀ¸·Î ³»Àü½ÃÅ°±â");
    }

    IEnumerator CheckAnimationStateM()
    {
        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;
        yield return new WaitUntil(() =>
        {
            var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("´ëÈä±Ù ÁØºñ")) SetStepCommon(3, "3_1", "ÆÈ²ÞÄ¡ ±¼°î ¹× °ß°üÀý ±¼°î-¿ÜÀü-³»Àü");
            else if (state.IsName("´ëÈä±Ù ÁØºñ ´Á°ñ")) SetStepCommon(3, "3_2", "ÆÈ²ÞÄ¡ ±¼°î ¹× °ß°üÀý ±¼°î-¿ÜÀü-³»Àü");
            else if(state.IsName("´ëÈä±Ù ÁØºñ_¼â±¼")) SetStepCommon(3, "3_3", "ÆÈ²ÞÄ¡ ±¼°î ¹× °ß°üÀý ±¼°î-¿ÜÀü-³»Àü");

            return state.IsName("´ëÈä±Ù ÁØºñ")
                || state.IsName("´ëÈä±Ù ÁØºñ ´Á°ñ")
                || state.IsName("´ëÈä±Ù ÁØºñ_¼â±¼");
        });

        while (true)
        {
            if (chunaAnim != null && chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.99f)
            {
                if (dingdong != null) dingdong.Play();
                if (chukgul_obj != null) chukgul_obj.SetActive(false);
                etcHand_objL.SetActive(false);

                LimitCheck();
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

    public IEnumerator AnimPlayCheck()
    {
        limitinfo.SetActive(true);
        if (step1LHand != null && step1RHand != null)
        {
            step1LHand.transform.parent.gameObject.SetActive(true);
            step1RHand.transform.parent.gameObject.SetActive(true);
        }

        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;

        if (stepNo == 11)
        {
            iuc.step_state[2].color = iuc.done;
            iuc.step_state[3].color = iuc.doing;
            SetStepCommon(6, "6", "½ºÆ®·¹Äª 8ÃÊ");
            angledisplay.GetComponent<TextMeshProUGUI>().color = Color.blue;
        }
        else
        {
            iuc.step_state[0].color = iuc.done;
            iuc.step_state[1].color = iuc.doing;
            SetStepCommon(4, "4", "Á¦ÇÑÀåº® È®ÀÎ"); ;
            angledisplay.GetComponent<TextMeshProUGUI>().color = Color.green;
        }


            string aniclip_name = stepNo == 11
            ? m_State switch
            {
                State.Middle => "´ëÈä±Ù Èä°ñ ½ºÆ®·¹Äª",
                State.Front => "´ëÈä±Ù ´Á°ñ ½ºÆ®·¹Äª",
                _ => "´ëÈä±Ù ¼â°ñ ½ºÆ®·¹Äª",
            }
            : m_State switch
            {
                State.Middle => "´ëÈä±Ù Èä°ñ",
                State.Front => "´ëÈä±Ù ´Á°ñ",
                _ => "´ëÈä±Ù ¼â°ñ",
            };

        if (chunaAnim != null)
        {
            chunaAnim.Play(aniclip_name, 0, 0);
            Debug.Log($"Playing animation: {aniclip_name} on chunaAnim");
        }
        if (chunaAnim2 != null)
        {
            chunaAnim2.Play(aniclip_name, 0, 0);
            Debug.Log($"Playing animation: {aniclip_name} on chunaAnim2");
        }

        // ¾Ö´Ï¸ÞÀÌ¼Ç »óÅÂ ÀüÈ¯ ´ë±â
        yield return new WaitUntil(() =>
        {
            if (chunaAnim == null) return false;
            var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
            return state.IsName(aniclip_name);
        });

        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                if (chunaAnim != null) chunaAnim.enabled = true;
                if (chunaAnim2 != null) chunaAnim2.enabled = true;

                float normalizedTime = chunaAnim != null ? chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
                Debug.Log($"NormalizedTime: {normalizedTime:F2}, stepNo: {stepNo}, Animation: {aniclip_name}, rightHandOn: {rightHandOn}, leftHandOn: {leftHandOn}");
                if (stepNo == 11) strech.fillAmount = normalizedTime;
                else limit.fillAmount = normalizedTime;
                if (normalizedTime >= 0.99f)
                {
                    if (dingdong != null) dingdong.Play();
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                    if (stepNo == 11)
                    {
                        strech.fillAmount = 0;
                        limit.fillAmount = 0;
                        if (step1LHand != null) step1LHand.transform.parent.gameObject.SetActive(false);
                        if (step1RHand != null) step1RHand.transform.parent.gameObject.SetActive(false);
                        limitinfo.SetActive(false);
                        Step5Start();
                    }
                    else
                    {
                        DungCheok();
                    }
                    break;
                }
            }
            else
            {
                if (chunaAnim != null) chunaAnim.enabled = false;
                if (chunaAnim2 != null) chunaAnim2.enabled = false;
                Debug.Log($"Animation paused: rightHandOn={rightHandOn}, leftHandOn={leftHandOn}");
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void Step5Start()
    {
        iuc.step_state[3].color = iuc.done;
        iuc.step_state[4].color = iuc.doing;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(JungRib());
        SetStepCommon(7, "7", "Áß¸³»óÅÂ·Î º¹±Í");
    }
    IEnumerator JungRib()
    {
        angledisplay.GetComponent<TextMeshProUGUI>().color = Color.white;
        etcHand_objL.SetActive(true);
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
                 State.Middle => "´ëÈä±Ù Èä°ñ Áß¸³",
                 State.Front => "´ëÈä±Ù ´Á°ñ Áß¸³",
                 _ => "´ëÈä±Ù ¼â°ñ Áß¸³",
             };

        // ¾Ö´Ï¸ÞÀÌ¼Ç »óÅÂ ÀüÈ¯ ´ë±â
        yield return new WaitUntil(() =>
        {
            if (chunaAnim == null) return false;
            var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
            return state.IsName(aniclip_name);
        });

        while (true)
        {
            if (chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.96f)
            {
                if (dingdong != null) dingdong.Play();
                if (chukR_obj != null) chukR_obj.SetActive(false);
                if (chunaAnim != null) chunaAnim.enabled = false;
                if (chunaAnim2 != null) chunaAnim2.enabled = false;
                iuc.step_state[4].color = iuc.done;

                switch (m_State)
                {
                    case State.Middle:
                        if (nextD != null)
                        {
                            nextD.SetActive(false);
                            var button = nextD.GetComponentInParent<Button>();
                            if (button != null) button.enabled = true;
                            chukgul_obj = chukgul_obj2;
                        }
                        m_State = State.Front;
                        break;
                    case State.Front:
                        if (next3D != null)
                        {
                            next3D.SetActive(false);
                            var button = next3D.GetComponentInParent<Button>();
                            if (button != null) button.enabled = true;
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
                            if (button != null) button.enabled = true;
                        }
                        m_State = State.None;
                        break;
                }
                iuc.button_use.interactable = true;
                stepNo = 0;
                break;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public void DungCheok()
    {
        iuc.step_state[1].color = iuc.done;
        iuc.step_state[2].color = iuc.doing;
        stepNo = 11;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(AnimTimeControl());
        SetStepCommon(5, "5", "µîÃ´¼º ¿îµ¿");

        if (chunaAnim != null) chunaAnim.enabled = false;
        if (chunaAnim2 != null) chunaAnim2.enabled = false;
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
            if (button != null) button.enabled = false;
        }
        if (end != null)
        {
            end.SetActive(true);
            var button = end.GetComponentInParent<Button>();
            if (button != null) button.enabled = false;
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
}