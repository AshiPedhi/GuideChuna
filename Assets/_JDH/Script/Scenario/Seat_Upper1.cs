using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
public class Seat_Upper1 : MonoBehaviour
{
    public static Seat_Upper1 instance = null;

    private void Awake()
    {
        instance = this;
    }

    public State m_State = State.Middle;

    public bool practice = false;

    public List<TextMeshProUGUI> timeTextS = new();
    public List<TextMeshProUGUI> oxTextS = new();

    protected ResultData ncs = new();

    protected int currentStep = 0;
    public TextMeshProUGUI userName;
    public int maxStep = 0;

    private void Start()
    {
        if (AuthManager.instance != null)
        {
            ncs.orgID = AuthManager.instance.currentOrgID;
            ncs.userId = AuthManager.instance.currentUserID;
            ncs.username = AuthManager.instance.currentRunUser;
            ncs.subject = AuthManager.instance.currentContents;
            //���� �и� �߰� ����
            ncs.competenyUnit = CU;
            ncs.learnModule = LLM;
            ncs.learnLevel1 = LL1;
            ncs.learnLevel2 = LL2;

            ncs.totalCnt = maxStep.ToString();
            ncs.doneCnt = (currentStep - 1).ToString();
            ncs.runtime = runtime.ToString();

            userName.text = ncs.username;
        }

        if (practice)
            PlayPractice();
    }

    public bool testMode;
    public float timeLimit = 10;
    public float timeCountTest = 0;
    public int order = 0;

    public AudioClip[] narration;
    public AudioSource stepNarration;

    public AudioSource dingdong;

    public GameObject uiCanvas;

    public TextMeshProUGUI mainText;

    [Header("�÷��̾� ��")]
    public GameObject rightHand;
    public GameObject leftHand;

    public int stepNo = 0;

    public bool leftHandOn = false;
    public bool rightHandOn = false;

    public Animator chunaAnim;
    public Animator chunaAnim2;

    public float timeCount = 0;

    public TextMeshProUGUI timeText;


    public TextMeshProUGUI user;
    public float runtime;

    public float keepingTime;

    public AudioClip[] testNarration;

    public void CurrentUser()
    {
        if (AuthManager.instance != null)
            user.text = AuthManager.instance.currentRunUser;
    }

    Coroutine runTimeChecker;

    IEnumerator Runtime()
    {
        while (true)
        {
            runtime += Time.deltaTime;

            int m = (int)(runtime / 60);
            int s = (int)(runtime % 60);

            timeText.text = m.ToString("D2") + ":" + s.ToString("D2");
            yield return new WaitForFixedUpdate();
        }
    }

    public GameObject miniA;
    public GameObject miniD;

    public GameObject step1RHand;
    public GameObject step1LHand;

    public GameObject skelmus;
    public GameObject[] human;
    public GameObject[] humanBantu;

    public Coroutine stepCor;

    public AudioSource wrongA;

    public void CheckTestResult(float time, string ox)
    {
        if (!testMode)
            return;
        timeTextS[currentStep - 1].text = time.ToString("F1") + "��";
        //checkStepTime += ("/" + time.ToString("F2") + "��");
        oxTextS[currentStep - 1].text = ox;
        //checkOX += ("/" + ox);
        ncs.learnLevel2 += currentStep + "�ܰ�: " + time.ToString("F2") + "��/" + ox + "\n";
    }

    IEnumerator TimeLimitCheck()
    {
        while (true)
        {
            timeCountTest += Time.deltaTime;

            if (timeCountTest >= timeLimit)
            {
                CheckTestResult(timeCountTest, "X");

                timeCountTest = 0;

                StopCoroutine(stepCor);

                gunchukMenu.SetActive(false);
                hwanheaMenu.SetActive(false);
                jungbuMenu.SetActive(false);
                gunhweaMenu.SetActive(false);

                warning.mute = true;
                limitLine.mute = true;
                wrongA.mute = false;
                wrongA.Play();

                if (order == 1)
                {
                    Step3Start();
                }
                else if (order == 2)
                {
                    Step3Start();
                }
                else if (order == 3)
                {
                    if (m_State == State.Middle)
                    {
                        Step5_1Start();
                    }
                    else if (m_State == State.Front)
                    {
                        Step4_1Start();
                    }
                    else
                    {
                        Step9Start();
                    }
                }
                else if (order == 4)
                {
                    Step5Start();
                }
                else if (order == 5)
                {
                    Step5Start();
                }
                else if (order == 6)
                {
                    Step5Start();
                }
                else if (order == 7)
                {
                    if (stepNo == 11)
                    {
                        if (m_State == State.Middle)
                        {
                            m_State = State.Front;
                            Step3Start();
                        }
                        else if (m_State == State.Front)
                        {
                            m_State = State.Back;
                            Step3Start();
                        }
                        else
                        {
                            Debug.Log("���⼭ ���߸� ������");
                            mainText.gameObject.SetActive(false);
                            resultPage.SetActive(true);
                            miniMap.SetActive(false);

                            resultImage.SetActive(true);
                            middleImage.SetActive(false);

                            //resultText.text = "";

                            end.SetActive(false);
                            end.GetComponentInParent<Button>().enabled = true;

                            stepNarration.mute = false;
                            stepNarration.clip = testNarration[3];
                            stepNarration.Play();
                            break;
                        }

                        stepNo = 0;
                    }
                    else
                    {
                        DungCheok();
                    }
                }
                else if (order == 8)
                {
                    Sinjang();
                }
                else if (order == 9)
                {
                    if (m_State == State.Middle)
                    {
                        Step5_1Start();
                    }
                    else if (m_State == State.Front)
                    {
                        Step4_1Start();
                    }
                    else
                    {
                        Step9Start();
                    }
                }
            }

            if (resultPage.activeSelf)
            {
                break;
            }

            yield return null;
        }
    }
    //���� ���� 1 - 2 - 3 -4.1 - 5.1 - 9 - 5 - ��ô - ���� - 2 - 3 - 4.1 - 5.1 - 9 - 5

    [Header("���� �и� �߰� ����")]
    public string CU; //�ܼ�, ����
    public string LLM; //����̸�
    public string LL1;  //�庮 ��ô ����...
    public string LL2;  //���� ���� ��...

    public RunStatus runStatus = new();

    public void Step1Start()
    {
        if (runTimeChecker == null)
        {
            runTimeChecker = StartCoroutine(Runtime());
        }

        stepCor = StartCoroutine(HandCheck());

        currentStep++;

        if (AuthManager.instance != null)
        {
            if (AuthManager.instance != null)
                runStatus.deviceSN = AuthManager.instance.DEVICE_SN;
            runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
            if (AuthManager.instance != null)
                AuthManager.instance.OnUpdateRunStatusAsync(runStatus);
        }

        step1LHand.transform.parent.gameObject.SetActive(true);
        step1RHand.transform.parent.gameObject.SetActive(true);

        if (testMode)
        {
            timeCountTest = 0;

            order = 1;
            StartCoroutine(TimeLimitCheck());
            stepNarration.mute = false;
            stepNarration.clip = testNarration[0];
            stepNarration.Play();

            mainText.text = "�����庮 Ȯ���ϱ�";

            return;
        }

        stepNo = 0;

        stepNarration.mute = false;
        stepNarration.clip = narration[0];
        stepNarration.Play();

        mainText.text = "1. �ֵ���\n" +
            "- ȯ���� �ĵκ�\n" +
            "2.������\n" +
            "- ��ν¸��\n" +
            "  ��� ������";

        nextD.SetActive(true);
        nextD.transform.parent.gameObject.GetComponent<Button>().enabled = false;
    }

    public void Step1LeftHand(bool on)
    {
        leftHandOn = on;
        step1LHand.SetActive(!on);
    }

    public void Step1RightHand(bool on)
    {
        rightHandOn = on;
        step1RHand.SetActive(!on);
    }

    IEnumerator HandCheck()
    {
        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;

                dingdong.Play();
                Step3Start();
                break;
            }

            yield return null;
        }
    }

    public bool passthroughOn = false;

    public void SkelHuman()
    {
        if (skelmus.activeSelf)
        {
            skelmus.SetActive(false);

            if (humanOn)
            {
                foreach (GameObject obj in humanBantu)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = false;

                foreach (GameObject obj in human)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = true;
            }
        }
        else
        {
            skelmus.SetActive(true);

            if (humanOn)
            {
                foreach (GameObject obj in human)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = false;

                foreach (GameObject obj in humanBantu)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = true;
            }
        }
    }

    public bool humanOn = true;

    public void Human()
    {
        if (humanOn)
        {
            humanOn = false;

            foreach (GameObject obj in human)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = false;

            foreach (GameObject obj in humanBantu)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = false;
        }
        else
        {
            humanOn = true;

            if (passthroughOn || skelmus.activeSelf)
            {
                foreach (GameObject obj in humanBantu)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = true;
            }
            else
            {
                foreach (GameObject obj in human)
                    obj.GetComponent<SkinnedMeshRenderer>().enabled = true;
            }
        }
    }

    public void PassThroughB()
    {
        if (passthroughOn)
        {
            passthroughOn = false;

            foreach (GameObject obj in human)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = true;

            foreach (GameObject obj in humanBantu)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = false;
        }
        else
        {
            passthroughOn = true;

            foreach (GameObject obj in human)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = false;

            foreach (GameObject obj in humanBantu)
                obj.GetComponent<SkinnedMeshRenderer>().enabled = true;
        }
    }

    public GameObject step2RHand;

    public void Step2Start()
    {
        stepCor = StartCoroutine(AnimPlayCheck());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        if (testMode)
        {
            mainText.text = "�����庮 Ȯ���ϱ�";
            timeCountTest = 0;
            order = 2;
            return;
        }

        stepNarration.clip = narration[1];
        stepNarration.Play();

        mainText.text = "1.����";

        step2RHand.SetActive(true);

        if (m_State == State.Front)
        {
            next3D.SetActive(true);
            next3D.GetComponentInParent<Button>().enabled = false;
        }
    }

    public Rigidbody rightHandRg;

    IEnumerator AnimPlayCheck()
    {
        while (true)
        {
            if (rightHandOn && leftHandOn && rightHandRg.mass >= 2)
            {
                dingdong.Play();
                step2RHand.SetActive(false);
                chunaAnim.speed = 1;
                chunaAnim.Play("����1");
                chunaAnim2.speed = 1;
                chunaAnim2.Play("����1");
                break;
            }

            yield return null;
        }

        yield return new WaitForSeconds(2);


        CheckTestResult(timeCountTest, "O");
        timeCountTest = 0;
        Step3Start();
    }

    public GameObject step3RHand;
    public GameObject step3Col;

    public Transform step3startPoint;
    public Transform step3endPoint;

    public AudioSource limitLine;

    public float velocityLimit;

    public void Step3Start()
    {
        stepCor = StartCoroutine(Chukgul());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        dist = Vector3.Distance(step3startPoint.position, step3endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;

        if (testMode)
        {
            timeCountTest = 0;
            order = 3;
            return;
        }

        stepNarration.clip = narration[2];
        stepNarration.Play();

        mainText.text = "2.���� ����";

        step3RHand.SetActive(true);
    }

    public void PlayPractice()
    {
        if (runTimeChecker == null)
        {
            runTimeChecker = StartCoroutine(Runtime());
        }

        currentStep++;

        if (AuthManager.instance != null)
            runStatus.deviceSN = AuthManager.instance.DEVICE_SN;
        runStatus.status = "���۹� ����";
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        step1LHand.transform.parent.gameObject.SetActive(true);
        step1RHand.transform.parent.gameObject.SetActive(true);

        stepCor = StartCoroutine(ChukgulP());

        dist = Vector3.Distance(step3startPoint.position, step3endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;

        step3RHand.SetActive(true);
    }

    IEnumerator ChukgulP()
    {
        while (true)
        {
            if (step3endPoint.position.x > rightHand.transform.position.x)
                distX = Vector3.Distance(step3endPoint.position.x * Vector3.right, rightHand.transform.position.x * Vector3.right);

            normalize = 1 - (distX / dist);

            normalize = Mathf.Clamp(normalize, 0, 1);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.4f);

            currDist = normalize;

            if (rightHandOn && leftHandOn)
            {
                if (velocityLimit >= rightHandRg.linearVelocity.x)
                {
                    chunaAnim.speed = 0;
                    chunaAnim.Play("�ɾ� ��ν¸� ���� ����", -1, boganPoint);

                    chunaAnim2.speed = 0;
                    chunaAnim2.Play("�ɾ� ��ν¸� ���� ����", -1, boganPoint);

                    chukgulAngle.transform.localEulerAngles = 90 * boganPoint * Vector3.forward;

                    preDist = currDist;

                    chukgulSlider.value = normalize;
                    chukgulSlider2.value = normalize * 10.5f;
                    warningVel.text = "";
                }
                else if (velocityLimit <= rightHandRg.linearVelocity.x)
                {
                    warningVel.text = "õõ�� �����̼���";
                }
                else
                {
                    warningVel.text = "";
                }

                //���� ��� �����̵� ���� ���� ������ ���� �ð� ���� �ϱ�
                if (normalize > 0.278f && normalize < 0.5f)
                {
                    warning.mute = true;
                    limitLine.mute = false;
                }
                else if (normalize >= 0.5f)
                {
                    warning.mute = false;
                    limitLine.mute = true;
                }
                else
                {
                    warning.mute = true;
                    limitLine.mute = true;
                }

                limitLine.pitch = normalize * 6;

            }
            else
            {
                warning.mute = true;
                limitLine.mute = true;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public float dist;
    public bool rHandRot = false;

    [Range(0, 0.2f)] public float threshold;
    public float currDist = 0;
    public float preDist = 0;

    public float boganPoint = 0;

    public float distX = 0;
    public float normalize = 0;

    public AudioSource warning;

    public GameObject gunchukMenu;

    public GameObject chukgulAngle;

    public Image chukgulround;
    public Slider chukgulSlider;
    public Slider chukgulSlider2;
    public TextMeshProUGUI warningVel;

    IEnumerator Chukgul()
    {
        if (stepNo == 11)
            stepNarration.mute = true;

        gunchukMenu.SetActive(true);
        float timeChukgul = 0;

        while (true)
        {
            //Debug.Log(rightHandRg.velocity.x);
            if (step3endPoint.position.x > rightHand.transform.position.x)
                distX = Vector3.Distance(step3endPoint.position.x * Vector3.right, rightHand.transform.position.x * Vector3.right);

            normalize = 1 - (distX / dist);

            normalize = Mathf.Clamp(normalize, 0, 1);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.4f);

            currDist = normalize;

            //Debug.Log(normalize + " ��ֶ����� " + normalize);
            //mainText.text = normalize + " ��ֶ����� " + boganPoint;

            if (rightHandOn && leftHandOn)
            {
                if (velocityLimit >= rightHandRg.linearVelocity.x)
                {
                    //if(Mathf.Abs(currDist - preDist) >= threshold)
                    //{
                    if (stepNo == 11)
                    {
                        chunaAnim.speed = 0;
                        chunaAnim.Play("�ɾ� ��ν¸� ���� ���� �߰�", -1, boganPoint);

                        chunaAnim2.speed = 0;
                        chunaAnim2.Play("�ɾ� ��ν¸� ���� ���� �߰�", -1, boganPoint);
                    }
                    else
                    {
                        chunaAnim.speed = 0;
                        chunaAnim.Play("�ɾ� ��ν¸� ���� ����", -1, boganPoint);

                        chunaAnim2.speed = 0;
                        chunaAnim2.Play("�ɾ� ��ν¸� ���� ����", -1, boganPoint);
                    }

                    chukgulAngle.transform.localEulerAngles = 90 * boganPoint * Vector3.forward;

                    preDist = currDist;

                    chukgulSlider.value = normalize;
                    chukgulSlider2.value = normalize * 10.5f;
                    warningVel.text = "";
                    //}
                }
                else if (velocityLimit <= rightHandRg.linearVelocity.x)
                {
                    warningVel.text = "õõ�� �����̼���";
                }
                else
                {
                    warningVel.text = "";
                }

                //���� ��� �����̵� ���� ���� ������ ���� �ð� ���� �ϱ�
                if (normalize > 0.278f && normalize < 0.5f)
                {
                    //if (!testMode)
                    warning.mute = true;

                    timeChukgul += Time.deltaTime / keepingTime;
                    limitLine.mute = false;
                }
                else if (normalize >= 0.5f)
                {
                    //if (!testMode)
                    warning.mute = false;

                    timeChukgul = 0;
                    limitLine.mute = true;
                }
                else
                {
                    //if(!testMode)
                    warning.mute = true;

                    timeChukgul = 0;
                    limitLine.mute = true;
                }

                chukgulround.fillAmount = 1 - timeChukgul;
                chukgulround.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = (chukgulround.fillAmount * 3).ToString("F1");
                limitLine.pitch = normalize * 6;


                //���� ��� ���� ��ġ ���޽� �ڵ����� �Ϸ�
                //if(normalize >= 0.95f)
                if (timeChukgul >= 1)
                {
                    warning.mute = true;
                    limitLine.mute = true;
                    dingdong.Play();
                    step3RHand.SetActive(false);
                    chukgulSlider.value = 0;
                    chukgulSlider2.value = 0;

                    gunchukMenu.SetActive(false);
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;

                    if (m_State == State.Middle)
                    {
                        Step5Start();
                    }
                    else if (m_State == State.Front)
                    {
                        Step4_1Start();
                    }
                    else
                    {
                        Step9Start();
                    }

                    break;
                }
            }
            else
            {
                warning.mute = true;
                limitLine.mute = true;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }
        gunchukMenu.SetActive(false);
    }

    public void Step3RightHand()
    {
        rHandRot = true;
        step3RHand.SetActive(false);
        step3Col.SetActive(false);
    }

    public GameObject step4RHand;
    public GameObject step4Col;
    public bool rHandRot2 = false;

    public Transform step4startPoint;
    public Transform step4endPoint;

    public GameObject hwanheaMenu;

    public GameObject hwanheaAngle;

    public Image hwanhearound;
    public Slider hwanheaSlider;
    public Slider hwanheaSlider2;
    public Slider hwanheaSlider3;
    public TextMeshProUGUI warningVel2;

    public void Step4_1Start()
    {
        gunchukMenu.SetActive(false);
        dist = Vector3.Distance(step4startPoint.position, step4endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;

        stepCor = StartCoroutine(HwanChuckHwueJeon());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        if (testMode)
        {
            timeCountTest = 0;
            order = 4;
            return;
        }

        stepNarration.clip = narration[11];
        stepNarration.Play();

        mainText.text = "3.ȸ��\n" +
            "���� ���� - ȯ�� ȸ��";
        //", ���� ����, ȯ�� ȸ������ ��ν¸� ���漶���� �����庮�� Ȯ���Ѵ�";

        step4RHand.SetActive(true);
    }


    public void Step4Start()
    {
        stepNarration.clip = narration[3];
        stepNarration.Play();

        //", ���� ����, ȯ�� ȸ������ ��ν¸� ���漶���� �����庮�� Ȯ���Ѵ�";

        dist = Vector3.Distance(step4startPoint.position, step4endPoint.position);

        step4RHand.SetActive(true);

        normalize = 0;
        distX = 0;
        boganPoint = 0;
        StartCoroutine(HwanChuckHwueJeon());
    }

    IEnumerator HwanChuckHwueJeon()
    {
        hwanheaMenu.SetActive(true);
        float timeChukgul = 0;

        while (true)
        {
            if (step4endPoint.position.y > rightHand.transform.position.y)
                distX = Vector3.Distance(step4endPoint.position.y * Vector3.up,
                rightHand.transform.position.y * Vector3.up);

            normalize = (distX / dist);

            normalize = Mathf.Clamp(normalize, 0, 1);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.4f);

            currDist = normalize;

            //Debug.Log(normalize + " ��ֶ����� " + boganPoint);
            //mainText.text = normalize + " ��ֶ����� " + boganPoint;

            if (rightHandOn && leftHandOn)
            {
                hwanheaSlider3.value = rightHandRg.linearVelocity.y;

                if (velocityLimit >= rightHandRg.linearVelocity.y)
                {
                    chunaAnim.speed = 0;
                    chunaAnim.Play("�ɾ� ��ν¸� ȯ�� ȸ��", -1, boganPoint);
                    chunaAnim2.speed = 0;
                    chunaAnim2.Play("�ɾ� ��ν¸� ȯ�� ȸ��", -1, boganPoint);
                    hwanheaAngle.transform.localEulerAngles = Vector3.forward * 90 * boganPoint;
                    preDist = currDist;

                    hwanheaSlider.value = normalize;
                    hwanheaSlider2.value = normalize * 10.5f;
                    warningVel2.text = "";
                }
                else if (velocityLimit <= rightHandRg.linearVelocity.y)
                {
                    warningVel2.text = "õõ�� �����̼���";
                }
                else
                {
                    warningVel2.text = "";
                }

                //���� ��� �����̵� ���� ���� ������ ���� �ð� ���� �ϱ�
                if (normalize > 0.278f && normalize < 0.5f)
                {
                    //if (!testMode)
                    warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    limitLine.mute = false;
                }
                else if (normalize >= 0.5f)
                {
                    //if (!testMode)
                    warning.mute = false;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }
                else
                {
                    //if (!testMode)
                    warning.mute = true;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }

                hwanhearound.fillAmount = 1 - timeChukgul;
                hwanhearound.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = (hwanhearound.fillAmount * 3).ToString("F1");
                limitLine.pitch = normalize * 6;

                //if (normalize >= 0.95f)
                if (timeChukgul >= 1)
                {
                    dingdong.Play();
                    step4RHand.SetActive(false);

                    warning.mute = true;
                    limitLine.mute = true;
                    //yield return new WaitForSeconds(2);
                    //������ ���⼭ ���� 5 ����

                    hwanheaMenu.SetActive(false);
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                    Step5Start();
                    break;
                }
            }
            else
            {
                warning.mute = true;
                limitLine.mute = true;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }

        hwanheaMenu.SetActive(false);
    }

    public Transform step5startPoint;
    public Transform step5endPoint;

    public GameObject jungbuMenu;

    public GameObject jungbuAngle;

    public Image jungburound;
    public Slider jungbuSlider;
    public Slider jungbuSlider2;
    public Slider jungbuSlider3;
    public TextMeshProUGUI warningVel3;

    public void Step5_1Start()
    {
        hwanheaMenu.SetActive(false);
        dist = Vector3.Distance(step5startPoint.position, step5endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;
        stepCor = StartCoroutine(JungbuJungRib());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        if (testMode)
        {
            timeCountTest = 0;
            order = 5;
            return;
        }

        stepNarration.clip = narration[12];
        stepNarration.Play();

        mainText.text = "3.ȸ��\n" +
            "�ߺ� ���� - �߸�";
        //", ���� ����, ȯ�� ȸ������ ��ν¸� ���漶���� �����庮�� Ȯ���Ѵ�";
    }

    IEnumerator JungbuJungRib()
    {
        jungbuMenu.SetActive(true);
        float timeChukgul = 0;

        while (true)
        {
            if (step5startPoint.position.x > rightHand.transform.position.x)
                distX = Vector3.Distance(step5endPoint.position.x * Vector3.right,
                rightHand.transform.position.x * Vector3.right);

            normalize = 1 - (distX / dist);

            normalize = Mathf.Clamp(normalize, 0, 1);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.4f);

            currDist = normalize;

            //Debug.Log(normalize + " ��ֶ����� " + boganPoint);
            //mainText.text = normalize + " ��ֶ����� " + boganPoint;

            if (rightHandOn && leftHandOn)
            {
                jungbuSlider3.value = Mathf.Abs(rightHandRg.linearVelocity.x);

                if (velocityLimit >= Mathf.Abs(rightHandRg.linearVelocity.x))
                {
                    chunaAnim.speed = 0;
                    chunaAnim.Play("�ߺ� �߸�", -1, boganPoint);
                    chunaAnim2.speed = 0;
                    chunaAnim2.Play("�ߺ� �߸�", -1, boganPoint);
                    jungbuAngle.transform.localEulerAngles = 90 * boganPoint * Vector3.forward;
                    preDist = currDist;

                    jungbuSlider.value = normalize;
                    jungbuSlider2.value = normalize * 9f;
                    warningVel3.text = "";
                }
                else if (velocityLimit <= Mathf.Abs(rightHandRg.linearVelocity.x))
                {
                    warningVel3.text = "õõ�� �����̼���";
                }
                else
                {
                    warningVel3.text = "";
                }

                //���� ��� �����̵� ���� ���� ������ ���� �ð� ���� �ϱ�
                if (normalize > 0.5f && normalize < 0.66f)
                {
                    //if (!testMode)
                    warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    limitLine.mute = false;
                }
                else if (normalize >= 0.66f)
                {
                    //if (!testMode)
                    warning.mute = false;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }
                else
                {
                    //if (!testMode)
                    warning.mute = true;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }

                jungburound.fillAmount = 1 - timeChukgul;
                jungburound.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = (jungburound.fillAmount * 3).ToString("F1");
                limitLine.pitch = normalize * 4.8f;

                //if (normalize >= 0.95f)
                if (timeChukgul >= 1)
                {
                    dingdong.Play();
                    step4RHand.SetActive(false);
                    warning.mute = true;
                    limitLine.mute = true;

                    //yield return new WaitForSeconds(2);
                    //������ ���⼭ ���� 5 ����
                    jungbuMenu.SetActive(false);
                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                    Step5Start();
                    break;
                }
            }
            else
            {
                warning.mute = true;
                limitLine.mute = true;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }

        jungbuMenu.SetActive(false);
    }

    public void Step4RightHand()
    {
        rHandRot2 = true;
        step4RHand.SetActive(false);
        step4Col.SetActive(false);
    }

    public float headStrechTime = 0;

    public GameObject step5RHand;

    public void Step5Start()
    {
        dist = Vector3.Distance(step3startPoint.position, step3endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;

        stepCor = StartCoroutine(JungRib());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        if (testMode)
        {
            timeCountTest = 0;
            order = 7;
            return;
        }

        stepNarration.clip = narration[13];
        stepNarration.Play();

        //", ���� ����, ȯ�� ȸ������ ��ν¸� ���漶���� �����庮�� Ȯ���Ѵ�";
        //chunaAnim.SetTrigger("5");

        step5RHand.SetActive(true);
    }

    public GameObject nextD;

    [Header("Test Mode")]
    public GameObject resultPage;
    public TextMeshProUGUI resultText;
    public GameObject miniMap;
    public GameObject middleImage;
    public GameObject resultImage;

    IEnumerator JungRib()
    {
        while (true)
        {
            distX = Vector3.Distance(step3startPoint.position.x * Vector3.right,
                rightHand.transform.position.x * Vector3.right);

            normalize = 1 - (distX / dist);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.5f);

            currDist = normalize;

            //Debug.Log(normalize + " ��ֶ����� " + boganPoint);
            //mainText.text = normalize + " ��ֶ����� " + boganPoint;

            if (rightHandOn && leftHandOn)
            {

                if (Mathf.Abs(currDist - preDist) >= threshold)
                {
                    if (stepNo == 11)
                    {
                        string mfb;
                        if (m_State == State.Middle)
                        {
                            mfb = "�ɾ� ��ν¸� ���� �ߺ� ¡��";
                        }
                        else if (m_State == State.Front)
                        {
                            mfb = "�ɾ� ��ν¸� ���� �ߺ� ¡��";
                        }
                        else
                        {
                            mfb = "�ɾ� ��ν¸� ���� �ߺ� ¡��";
                        }

                        chunaAnim.speed = 0;
                        chunaAnim.Play(mfb, -1, boganPoint);

                        chunaAnim2.speed = 0;
                        chunaAnim2.Play(mfb, -1, boganPoint);
                    }
                    else
                    {
                        string mfb;
                        if (m_State == State.Middle)
                        {
                            mfb = "�ɾ� ��ν¸� ���� �߰�";
                        }
                        else if (m_State == State.Front)
                        {
                            mfb = "�ɾ� ��ν¸� ȯ�� ȸ�� �߸�";
                        }
                        else
                        {
                            mfb = "�ɾ� ��ν¸� ���� ȸ�� �߸�";
                        }

                        chunaAnim.speed = 0;
                        chunaAnim.Play(mfb, -1, boganPoint);

                        chunaAnim2.speed = 0;
                        chunaAnim2.Play(mfb, -1, boganPoint);
                    }

                    preDist = currDist;
                }

                if (normalize >= 0.95f)
                {
                    dingdong.Play();
                    step5RHand.SetActive(false);
                    //yield return new WaitForSeconds(2);

                    //StartCoroutine(AnimTimeControl());

                    if (stepNo == 11)
                    {
                        if (testMode)
                        {
                            if (m_State == State.Middle)
                            {
                                m_State = State.Front;
                                Step3Start();
                            }
                            else if (m_State == State.Front)
                            {
                                m_State = State.Back;
                                Step3Start();
                            }
                            else
                            {
                                mainText.gameObject.SetActive(false);
                                resultPage.SetActive(true);
                                miniMap.SetActive(false);

                                resultImage.SetActive(true);
                                middleImage.SetActive(false);

                                //resultText.text = "";

                                end.SetActive(false);
                                end.GetComponentInParent<Button>().enabled = true;

                                stepNarration.mute = false;
                                stepNarration.clip = testNarration[3];
                                stepNarration.Play();

                                //����� �ڵ����� ��� ������Ʈ

                                ncs.totalCnt = maxStep.ToString();
                                ncs.doneCnt = (currentStep - 1).ToString();
                                ncs.runtime = runtime.ToString();
                            }

                            CheckTestResult(timeCountTest, "O");
                            timeCountTest = 0;
                        }
                        else
                        {
                            if (m_State == State.Middle)
                            {
                                nextD.SetActive(false);
                                nextD.GetComponentInParent<Button>().enabled = true;
                                m_State = State.Front;
                            }
                            else if (m_State == State.Front)
                            {
                                next3D.SetActive(false);
                                next3D.GetComponentInParent<Button>().enabled = true;
                                m_State = State.Back;
                            }
                            else
                            {
                                step1LHand.transform.parent.gameObject.SetActive(false);
                                step1RHand.transform.parent.gameObject.SetActive(false);
                                replay.SetActive(false);
                                replay.GetComponentInParent<Button>().enabled = true;
                                end.SetActive(false);
                                end.GetComponentInParent<Button>().enabled = true;
                                m_State = State.Middle;
                            }
                        }

                        stepNo = 0;
                    }
                    else
                    {
                        CheckTestResult(timeCountTest, "O");
                        timeCountTest = 0;
                        DungCheok();
                    }

                    break;
                }
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }

        normalize = 0;
        distX = 0;
        boganPoint = 0;
    }

    public GameObject[] directionOfG;

    public void DungCheok()
    {
        stepCor = StartCoroutine(AnimTimeControl());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);
        mainText.text = "��ô�� ��ϱ�";

        if (testMode)
        {
            timeCountTest = 0;
            order = 8;
            stepNarration.mute = false;
            stepNarration.clip = testNarration[1];
            stepNarration.Play();
            return;
        }

        stepNarration.clip = narration[15];
        stepNarration.Play();
    }

    public GameObject next3D;

    IEnumerator AnimTimeControl()
    {
        yield return new WaitForSeconds(1);

        chunaAnim.Play("�ɾ� ��ν¸� �߰� ���");
        chunaAnim2.Play("�ɾ� ��ν¸� �߰� ���");

        if (!testMode)
        {
            foreach (GameObject obj in directionOfG)
            {
                obj.SetActive(true);
            }
        }

        while (true)
        {
            if (rightHandOn && leftHandOn)
            {
                headStrechTime += Time.deltaTime;
            }

            if (headStrechTime > 5)
            {
                dingdong.Play();

                headStrechTime = 0;

                CheckTestResult(timeCountTest, "O");
                timeCountTest = 0;

                break;
            }

            yield return null;
        }

        if (!testMode)
        {
            foreach (GameObject obj in directionOfG)
            {
                obj.SetActive(false);
            }
        }

        Sinjang();
    }

    public void Step9Start()
    {
        jungbuMenu.SetActive(false);
        dist = Vector3.Distance(step9startPoint.position, step9endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;

        stepCor = StartCoroutine(GeonChuckHwueJeon());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        if (testMode)
        {
            timeCountTest = 0;
            order = 6;
            return;
        }

        stepNarration.clip = narration[14];
        stepNarration.Play();

        mainText.text = "3.ȸ��\n" +
            "�ĺ� ���� - �ɾ� ��ν¸� ���� ȸ��";
        //", ���� ����, ȯ�� ȸ������ ��ν¸� ���漶���� �����庮�� Ȯ���Ѵ�";

        step9RHand.SetActive(true);
    }

    public GameObject step9RHand;

    public Transform step9startPoint;
    public Transform step9endPoint;

    public GameObject gunhweaMenu;
    public GameObject gunhweaAngle;

    public Image gunhwearound;
    public Slider gunhwaSlider;
    public Slider gunhwaSlider2;
    public Slider gunhwaSlider3;
    public TextMeshProUGUI warningVel4;


    IEnumerator GeonChuckHwueJeon()
    {
        gunhweaMenu.SetActive(true);
        float timeChukgul = 0;

        while (true)
        {
            if (step9startPoint.position.y > rightHand.transform.position.y)
                distX = Vector3.Distance(step9endPoint.position.y * Vector3.up,
                rightHand.transform.position.y * Vector3.up);

            normalize = 1 - (distX / dist);

            boganPoint = Mathf.Lerp(boganPoint, normalize, 0.5f);

            currDist = normalize;

            if (rightHandOn && leftHandOn)
            {
                jungbuSlider3.value = Mathf.Abs(rightHandRg.linearVelocity.y);

                if (velocityLimit >= Mathf.Abs(rightHandRg.linearVelocity.y))
                {
                    warningVel4.text = "";
                    chunaAnim.speed = 0;
                    chunaAnim.Play("�ɾ� ��ν¸� ���� ȸ��", -1, boganPoint);
                    chunaAnim2.speed = 0;
                    chunaAnim2.Play("�ɾ� ��ν¸� ���� ȸ��", -1, boganPoint);
                    //yield return new WaitForSeconds(Time.deltaTime);
                    //chunaAnim.speed = 0;
                    //chukgulA["�ɾ� ��ν¸� ���� ����"].normalizedTime = boganPoint;
                    gunhweaAngle.transform.localEulerAngles = 90 * boganPoint * Vector3.forward;
                    preDist = currDist;

                    gunhwaSlider.value = normalize;
                    gunhwaSlider2.value = normalize * 10.5f;
                }
                else if (velocityLimit <= Mathf.Abs(rightHandRg.linearVelocity.y))
                {
                    warningVel4.text = "õõ�� �����̼���";
                }
                else
                {
                    warningVel4.text = "";
                }

                //���� ��� �����̵� ���� ���� ������ ���� �ð� ���� �ϱ�
                if (normalize > 0.278f && normalize < 0.5f)
                {
                    //if (!testMode) 
                    warning.mute = true;
                    timeChukgul += Time.deltaTime / keepingTime;
                    limitLine.mute = false;
                }
                else if (normalize >= 0.5f)
                {
                    //if (!testMode)
                    warning.mute = false;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }
                else
                {
                    //if (!testMode)
                    warning.mute = true;
                    timeChukgul = 0;
                    limitLine.mute = true;
                }

                gunhwearound.fillAmount = 1 - timeChukgul;
                gunhwearound.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = (gunhwearound.fillAmount * 3).ToString("F1");
                limitLine.pitch = normalize * 6f;

                //if (normalize >= 0.95f)
                if (timeChukgul >= 1)
                {
                    dingdong.Play();

                    step9RHand.SetActive(false);
                    gunhweaMenu.SetActive(false);

                    yield return new WaitForSeconds(1);

                    warning.mute = true;
                    limitLine.mute = true;

                    CheckTestResult(timeCountTest, "O");
                    timeCountTest = 0;
                    Step5Start();
                    break;
                }
            }
            else
            {
                warning.mute = true;
                limitLine.mute = true;
            }


            yield return new WaitForSeconds(Time.deltaTime);
        }
    }

    public GameObject replay;
    public GameObject end;

    public void Sinjang()
    {
        stepNo = 11;
        stepCor = StartCoroutine(Chukgul());

        currentStep++;

        runStatus.status = LLM + "/" + maxStep + "/" + currentStep;
        if (AuthManager.instance != null)
            AuthManager.instance.OnUpdateRunStatusAsync(runStatus);

        dist = Vector3.Distance(step3startPoint.position, step3endPoint.position);

        normalize = 0;
        distX = 0;
        boganPoint = 0;
        mainText.text = "��Ʈ��Ī�ϱ�";

        if (testMode)
        {
            timeCountTest = 0;
            order = 9;
            stepNarration.mute = false;
            stepNarration.clip = testNarration[2];
            stepNarration.Play();
            return;
        }

        stepNarration.clip = narration[16];
        stepNarration.Play();

        if (Count >= 4)
        {
            replay.transform.parent.gameObject.SetActive(false);
        }

        step3RHand.SetActive(true);
    }

    public int Count = 0;

    public GameObject[] replayCount;

    public void Replay()
    {
        replay.SetActive(true);
        replay.GetComponentInParent<Button>().enabled = false;
        end.SetActive(true);
        end.GetComponentInParent<Button>().enabled = false;
        replayCount[Count].SetActive(true);
        Count++;

        currentStep = 0;
    }

    public void Exit()
    {
        ncs.totalCnt = maxStep.ToString();
        ncs.doneCnt = currentStep.ToString();
        ncs.runtime = runtime.ToString();

        AuthManager.instance.PostResultAsync(ncs);
        //SceneManager.LoadScene("NCSLobby_Test");
        MoveSceneManager.instance.MoveScene("lobby");
    }

    public void Replay2()
    {
        ncs.totalCnt = maxStep.ToString();
        ncs.doneCnt = currentStep.ToString();
        ncs.runtime = runtime.ToString();

        AuthManager.instance.PostResultAsync(ncs);

        MoveSceneManager.instance.MoveScene(SceneManager.GetActiveScene().name);
    }
}