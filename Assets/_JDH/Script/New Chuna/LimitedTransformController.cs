using UnityEngine;

public class LimitedTransformController : MonoBehaviour
{
    public float maxRotationDegrees = 30f;
    public float maxYMovement = 0.02f;
    public float restoreSpeed = 2f;
    public Transform parentToFollow; // �θ� �ڽ� �������� ���󰡱� ���� ����

    private Quaternion originalRotation;
    private Vector3 originalPosition;

    void Start()
    {
        originalRotation = transform.localRotation;
        originalPosition = transform.localPosition;
    }

    void Update()
    {
        if (parentToFollow != null && parentToFollow != transform)  // �ڽ� �� �θ�� ���� ����
        {
            Vector3 offsetPos = parentToFollow.localPosition - originalPosition;
            Vector3 offsetRot = (parentToFollow.localRotation * Quaternion.Inverse(originalRotation)).eulerAngles;

            offsetRot.x = NormalizeAngle(offsetRot.x);
            offsetRot.y = NormalizeAngle(offsetRot.y);
            offsetRot.z = NormalizeAngle(offsetRot.z);

            // �θ��� ��ġ/ȸ�� ���� (���� ����)
            Vector3 newPos = originalPosition + new Vector3(0, Mathf.Clamp(offsetPos.y, -maxYMovement, maxYMovement), 0);
            Vector3 newEuler = ClampEulerAngles((originalRotation.eulerAngles + offsetRot));

            transform.localPosition = Vector3.Lerp(transform.localPosition, newPos, Time.deltaTime * restoreSpeed);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.Euler(newEuler), Time.deltaTime * restoreSpeed);
        }
        else
        {
            // ȸ�� ����
            Vector3 currentEuler = NormalizeEuler(transform.localRotation.eulerAngles);
            currentEuler.x = Mathf.Clamp(currentEuler.x, -maxRotationDegrees, maxRotationDegrees);
            currentEuler.y = Mathf.Clamp(currentEuler.y, -maxRotationDegrees, maxRotationDegrees);
            currentEuler.z = Mathf.Clamp(currentEuler.z, -maxRotationDegrees, maxRotationDegrees);
            transform.localRotation = Quaternion.Euler(currentEuler);

            // ��ġ ���� (y�ุ)
            Vector3 currentLocalPos = transform.localPosition;
            currentLocalPos.y = Mathf.Clamp(currentLocalPos.y, originalPosition.y - maxYMovement, originalPosition.y + maxYMovement);
            transform.localPosition = new Vector3(originalPosition.x, currentLocalPos.y, originalPosition.z);

            // ���� ���·� ����
            transform.localRotation = Quaternion.Slerp(transform.localRotation, originalRotation, Time.deltaTime * restoreSpeed);
            transform.localPosition = Vector3.Lerp(transform.localPosition, originalPosition, Time.deltaTime * restoreSpeed);
        }
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
    }

    Vector3 ClampEulerAngles(Vector3 euler)
    {
        return new Vector3(
            Mathf.Clamp(NormalizeAngle(euler.x), -maxRotationDegrees, maxRotationDegrees),
            Mathf.Clamp(NormalizeAngle(euler.y), -maxRotationDegrees, maxRotationDegrees),
            Mathf.Clamp(NormalizeAngle(euler.z), -maxRotationDegrees, maxRotationDegrees)
        );
    }
}
