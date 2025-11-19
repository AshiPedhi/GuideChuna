using TMPro;
using UnityEngine;

public class AngleSumDisplay : MonoBehaviour
{
    public GameObject object1;
    public GameObject object2;
    public TextMeshProUGUI angleText;
    public bool useXAxis = true;
    public bool useYAxis = true;
    public bool useZAxis = true;
    public float xMult = 1;

    void Update()
    {
        if (object1 == null || object2 == null || angleText == null)
        {
            Debug.LogWarning("GameObject 또는 Text가 할당되지 않았습니다.");
            return;
        }

        Vector3 rotation1 = object1.transform.localEulerAngles;
        Vector3 rotation2 = object2.transform.localEulerAngles;

        float sum = 0f;
        if (useXAxis) sum += NormalizeAngle(rotation1.x) + NormalizeAngle(rotation2.x);
        if (useYAxis) sum += NormalizeAngle(rotation1.y) + NormalizeAngle(rotation2.y);
        if (useZAxis) sum += NormalizeAngle(rotation1.z) + NormalizeAngle(rotation2.z);

        sum *= xMult; // 배율 적용
        sum = NormalizeAngle(sum); // 최종 합을 -180° ~ 180°로 정규화
        angleText.text = $"{Mathf.Abs(sum):F1}°";
    }

    // 각도를 -180° ~ 180° 범위로 정규화
    private float NormalizeAngle(float angle)
    {
        angle = angle % 360; // 360°로 나눈 나머지
        if (angle > 180) angle -= 360;
        else if (angle < -180) angle += 360;
        return angle;
    }
}