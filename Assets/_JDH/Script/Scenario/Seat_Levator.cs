using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Seat_Levator : MonoBehaviour
{
    public static Seat_Levator instance;

    public InfoUICtrl iuc;

    [Header("상태 및 데이터")]
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
    [SerializeField] private GameObject chukgul_obj;
    [SerializeField] private GameObject rotateH_obj;
    [SerializeField] private GameObject rotateG_obj;
    [SerializeField] private GameObject etcHand_obj;
    [SerializeField] private GameObject chukR_obj;
    [SerializeField] private GameObject form_obj;
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
    [SerializeField] private GameObject angleZ;
    [Header("UI - 회전")]
    [SerializeField] private GameObject gunhweaMenu;
    [SerializeField] private GameObject gunhweaAngle;
    [SerializeField] private Image gunhwearound;
    [SerializeField] private GameObject angleY;
    [SerializeField] private GameObject angleX;
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
            "3" => 3,
            "4" => 4,
            "5" => 5,
            "6" => 6,
            "7" => 7,
            "8" => 8,
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
        iuc.info_menu.SetActive(true);
        SetStepCommon(0, "0", "견갑거근 이완강화기법");
    }
    public void StepSelect()
    {
        switch (stepNo)
        {
            case 11:
                FindAnyObjectByType<UIManager>().LogoutPopUp();
                break;
            default:
                ReadyToLeva();
                break;
        }

        iuc.button_use.interactable = false;
    }

    public void Step1Start()
    {
        if (runTimeChecker == null)
            runTimeChecker = StartCoroutine(Runtime());
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(HandCheck());
        SetStepCommon(2, "2", "1.주동수\n- 환자의 후두부\n2.보조수\n- 견갑골 상각 견갑거근 부착부");
        if (step1LHand != null && step1RHand != null)
        {
            step1LHand.transform.parent.gameObject.SetActive(true);
            step1RHand.transform.parent.gameObject.SetActive(true);
        }
        if (nextD != null)
        {
            nextD.SetActive(true);
            var button = nextD.transform.parent.GetComponent<Button>();
            if (button != null) button.enabled = false;
        }
        if (chukgul_obj != null) chukgul_obj.SetActive(true);

        iuc.infotext.transform.parent.gameObject.SetActive(true);
        iuc.infotext.text = "주동수:후두부 / 보조수: 견갑골 상각 견갑거근 부착부";
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
                Step3Start();
                break;
            }
            yield return null;
        }
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
    public void Step3Start()
    {
        iuc.step_state[0].color = iuc.doing;
        iuc.step_state[1].color = iuc.waiting;
        iuc.step_state[2].color = iuc.waiting;
        iuc.step_state[3].color = iuc.waiting;
        iuc.step_state[4].color = iuc.waiting;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(Chukgul());
        SetStepCommon(3, "3", "2.건측 측굴");

        iuc.infotext.transform.parent.gameObject.SetActive(true);
        iuc.infotext.text = "건측 측굴하기";
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

        chukgul_obj.GetComponent<LocalRotationAnimator>().enabled = true;
        chunaAnim.enabled = true;
        chunaAnim2.enabled = true;
        chukgul_obj.SetActive(true);
        float timeChukgul = 0;

        yield return new WaitUntil(() => chunaAnim.GetCurrentAnimatorStateInfo(0).IsName("앉아 견갑 건측 측굴"));

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
                    
                    uiUpdateTimer = 0f;
                }

                if (normalizedTime > 0.278f && normalizedTime < 0.5f)
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    angleZ.GetComponent<TextMeshProUGUI>().color = Color.green;
                    if (limitLine != null) limitLine.mute = false;
                }
                else if (normalizedTime >= 0.5f)
                {
                    if (warning != null) warning.mute = false;
                    timeChukgul = 0;
                    angleZ.GetComponent<TextMeshProUGUI>().color = Color.red;
                    if (limitLine != null) limitLine.mute = true;
                }
                else
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul = 0;
                    angleZ.GetComponent<TextMeshProUGUI>().color = Color.white;
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
                    if (gunchukMenu != null) gunchukMenu.SetActive(false);
                    if (chukgul_obj != null && chukgul_obj.GetComponent<LocalRotationAnimator>() != null)
                        chukgul_obj.GetComponent<LocalRotationAnimator>().enabled = false;
                    if (chunaAnim != null) chunaAnim.enabled = false;
                    if (chunaAnim2 != null) chunaAnim2.enabled = false;
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;

                    angleZ.SetActive(false);
                    angleY.SetActive(true);
                    Step9Start();

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
    public void Step9Start()
    {
        if (chukgul_obj != null) chukgul_obj.SetActive(false);
        if (rotateG_obj != null) rotateG_obj.SetActive(true);
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(ChuckHwueJeon());
        SetStepCommon(4, "4", "3.건측 회전");
        iuc.infotext.text = "건측 회전하기";
    }

    IEnumerator ChuckHwueJeon()
    {
        if (rotateH_obj == null || rotateG_obj == null || chunaAnim == null || chunaAnim2 == null)
            yield break;
        GameObject menu;
        GameObject angleObj;
        Image roundImage;

        menu = gunhweaMenu;
        angleObj = gunhweaAngle;
        roundImage = gunhwearound;
        rotateG_obj.SetActive(true);

        if (menu == null || angleObj == null || roundImage == null)
            yield break;

        else if (rotateG_obj != null && rotateG_obj.GetComponent<LocalRotationAnimator>() != null)
            rotateG_obj.GetComponent<LocalRotationAnimator>().enabled = true;

        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;
        menu.SetActive(true);
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

                if (normalizedTime > 0.278f && normalizedTime < 0.5f)
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    angleY.GetComponent<TextMeshProUGUI>().color = Color.green;
                    if (limitLine != null) limitLine.mute = false;
                }
                else if (normalizedTime >= 0.5f)
                {
                    if (warning != null) warning.mute = false;
                    timeChukgul = 0;
                    angleY.GetComponent<TextMeshProUGUI>().color = Color.red;
                    if (limitLine != null) limitLine.mute = true;
                }
                else
                {
                    if (warning != null) warning.mute = true;
                    timeChukgul = 0;
                    angleY.GetComponent<TextMeshProUGUI>().color = Color.white;
                    if (limitLine != null) limitLine.mute = true;
                }

                roundImage.fillAmount = 1 - timeChukgul;
                roundImage.transform.Find("Image (5)").GetComponent<Image>().fillAmount = 1 - timeChukgul;

                if (limitLine != null) limitLine.pitch = normalizedTime * 6;
                if (timeChukgul >= 1)
                {
                    if (dingdong != null) dingdong.Play();
                    menu.SetActive(false);
                    if (warning != null) warning.mute = true;
                    if (limitLine != null) limitLine.mute = true;
                    if (rotateG_obj != null && rotateG_obj.GetComponent<LocalRotationAnimator>() != null)
                        rotateG_obj.GetComponent<LocalRotationAnimator>().enabled = false;
                    if (chunaAnim != null) chunaAnim.enabled = false;
                    if (chunaAnim2 != null) chunaAnim2.enabled = false;
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                    angleY.SetActive(false);
                    angleX.SetActive(true);
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

    public void ReadyToLeva()
    {
        form_obj.SetActive(true);
        stepCor = StartCoroutine(CheckAnimationState());
        SetStepCommon(1, "1", "환자에게 자세 지시");
        iuc.infotext.text = "자세 지시하기";
        iuc.step_state[0].color = iuc.doing;
    }

    IEnumerator CheckAnimationState()
    {
        while (true)
        {
            if (chunaAnim != null && chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.99f)
            {
                if (dingdong != null) dingdong.Play();
                if (form_obj != null) form_obj.SetActive(false);
                Step1Start();
                break;
            }
            yield return new WaitForSeconds(Time.deltaTime);
        }
    }
    public void Step5Start()
    {
        if (rotateH_obj != null) rotateH_obj.SetActive(false);
        if (rotateG_obj != null) rotateG_obj.SetActive(false);
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(JungRib());
        SetStepCommon(8, "8", "마무리");
        iuc.infotext.text = "중립상태로 복귀";
        iuc.step_state[3].color = iuc.done;
        iuc.step_state[4].color = iuc.doing;
    }

    IEnumerator JungRib()
    {
        angleZ.GetComponent<TextMeshProUGUI>().color = Color.white;
        if (chukR_obj == null || chunaAnim == null || chunaAnim2 == null)
            yield break;

        chukR_obj.SetActive(true);

        yield return new WaitUntil(() =>
        {
            var state = chunaAnim.GetCurrentAnimatorStateInfo(0);
            return state.IsName("앉아 견갑 중립");
        });

        while (true)
        {
            if (chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.97f)
            {
                if (dingdong != null) dingdong.Play();
                if (chukR_obj != null) chukR_obj.SetActive(false);
                if (chunaAnim != null) chunaAnim.enabled = false;
                if (chunaAnim2 != null) chunaAnim2.enabled = false;
                iuc.step_state[4].color = iuc.done;

                if (testMode)
                {
                    if (mainText != null) mainText.gameObject.SetActive(false);
                    if (resultPage != null) resultPage.SetActive(true);
                    if (miniMap != null) miniMap.SetActive(false);
                    if (resultImage != null) resultImage.SetActive(true);
                    if (middleImage != null) middleImage.SetActive(false);
                    if (end != null)
                    {
                        end.SetActive(false);
                        var button = end.GetComponentInParent<Button>();
                        if (button != null) button.enabled = true;
                    }
                    if (stepNarration != null)
                    {
                        stepNarration.mute = false;
                        stepNarration.clip = testNarration[3];
                        if (stepNarration.clip != null) stepNarration.Play();
                    }
                    UpdateNcsResult();
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                }
                else
                {
                    if (step1LHand != null) step1LHand.transform.parent.gameObject.SetActive(false);
                    if (step1RHand != null) step1RHand.transform.parent.gameObject.SetActive(false);
                }
                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;

                iuc.button_use.interactable = true;
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
        if (chunaAnim != null) chunaAnim.enabled = true;
        if (chunaAnim2 != null) chunaAnim2.enabled = true;

        string aniclip_name;

        if (stepNo == 11)
        {
            angleX.GetComponent<TextMeshProUGUI>().color = Color.blue;
            iuc.step_state[2].color = iuc.done;
            iuc.step_state[3].color = iuc.doing;
            aniclip_name = "앉아 견갑 스트레칭";

            chunaAnim.Play(aniclip_name, 0, 0);
            chunaAnim2.Play(aniclip_name, 0, 0);
            SetStepCommon(7, "7", "3.스트레칭");
            iuc.infotext.text = "스트레칭 8초";

            Debug.Log(chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime + " : " + stepNo + " : " + aniclip_name);

            yield return new WaitUntil(() => chunaAnim.GetCurrentAnimatorStateInfo(0).IsName("앉아 견갑 스트레칭"));
        }
        else
        {
            angleX.GetComponent<TextMeshProUGUI>().color = Color.green;
            iuc.step_state[0].color = iuc.done;
            iuc.step_state[1].color = iuc.doing;
            aniclip_name = "앉아 견갑 제한장벽";

            SetStepCommon(5, "5", "3.제한장벽 확인");
            iuc.infotext.text = "제한장벽 확인";

            chunaAnim.Play(aniclip_name, 0, 0);
            chunaAnim2.Play(aniclip_name, 0, 0);

            Debug.Log(chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime + " : " + stepNo + " : " + aniclip_name);

            yield return new WaitUntil(() => chunaAnim.GetCurrentAnimatorStateInfo(0).IsName("앉아 견갑 제한장벽"));
        }

        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                if (chunaAnim != null) chunaAnim.enabled = true;
                if (chunaAnim2 != null) chunaAnim2.enabled = true;

                float normalizedTime = chunaAnim != null ? chunaAnim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;

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
        iuc.step_state[1].color = iuc.done;
        iuc.step_state[2].color = iuc.doing;
        stepNo = 11;
        if (stepCor != null) StopCoroutine(stepCor);
        stepCor = StartCoroutine(AnimTimeControl());
        SetStepCommon(6, "6", "등척성 운동하기");
        iuc.infotext.text = "등척성 운동";
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