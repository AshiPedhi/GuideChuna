using UnityEngine;

[CreateAssetMenu(fileName = "ApiConfig", menuName = "Config/ApiConfig")]
public class ApiConfig : ScriptableObject
{
    public string BaseApiUrl = "https://qpqjpivcg1.execute-api.ap-northeast-2.amazonaws.com";
    public int TimeoutSeconds = 15; // 네트워크 지연 튕김 방지
}