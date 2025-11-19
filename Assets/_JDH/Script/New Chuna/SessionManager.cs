using UnityEngine.SceneManagement;

public static class SessionManager
{
    public static void FillResultData(ResultData data, int currentStep, int maxStep, float runtime)
    {
        if (AuthManager.instance != null)
        {
            data.orgID = AuthManager.instance.currentOrgID;
            data.userId = AuthManager.instance.currentUserID;
            data.username = AuthManager.instance.currentRunUser;
            data.subject = AuthManager.instance.currentContents;
        }

        data.totalCnt = maxStep.ToString();
        data.doneCnt = (currentStep - 1).ToString();
        data.runtime = runtime.ToString();
    }

    public static void UpdateRunStatus(RunStatus status, string llm, int maxStep, int currentStep)
    {
        if (AuthManager.instance != null)
        {
            status.deviceSN = AuthManager.instance.DEVICE_SN;
            status.status = $"{llm}/{maxStep}/{currentStep}";
            AuthManager.instance.OnUpdateRunStatusAsync(status);
        }
    }

    public static void PostResult(ResultData data)
    {
        if (AuthManager.instance != null)
        {
            AuthManager.instance.PostResultAsync(data);
        }
    }

    public static void MoveToScene(string sceneName)
    {
        if (MoveSceneManager.instance != null)
        {
            MoveSceneManager.instance.MoveScene(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    public static void MoveToCurrentScene()
    {
        MoveToScene(SceneManager.GetActiveScene().name);
    }
}
