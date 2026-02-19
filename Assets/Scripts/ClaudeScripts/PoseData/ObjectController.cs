using UnityEngine;

public class ObjectController : MonoBehaviour
{
    // �ν����Ϳ��� �Ҵ��� Ÿ�� ������Ʈ (�̵�/ȸ����)
    public GameObject targetObject;

    // �߰��� ������ ������Ʈ 3�� (On/Off ��ۿ�)
    public GameObject additionalObject1;
    public GameObject additionalObject2;
    public GameObject additionalObject3;

    // �̵� �ӵ� (����/��, �ν����Ϳ��� ���� ����)
    public float moveSpeed = 5f;

    // ȸ�� �ӵ� (��/��, �ν����Ϳ��� ���� ����)
    public float rotateSpeed = 90f;

    // ���� ���õ� ������Ʈ ���� (0: none, 1: Object1, 2: Object2)
    private int selectedAdditional = 0;

    // ������ �ִ� ���� �̵�/ȸ�� �÷��� (�� ���⺰)
    private bool isMovingUp = false;
    private bool isMovingDown = false;
    private bool isMovingForward = false;
    private bool isMovingBackward = false;
    private bool isRotatingPositive = false;
    private bool isRotatingNegative = false;

    // Update �޼���: ������ �ִ� ���� ���� �̵�/ȸ�� ó��
    private void Update()
    {
        if (targetObject == null) return;

        float delta = Time.deltaTime;

        // �̵� ó�� (�۷ι� �� ����)
        if (isMovingUp)
        {
            targetObject.transform.Translate(Vector3.up * moveSpeed * delta, Space.World);
        }
        if (isMovingDown)
        {
            targetObject.transform.Translate(Vector3.down * moveSpeed * delta, Space.World);
        }
        if (isMovingForward)
        {
            targetObject.transform.Translate(Vector3.forward * moveSpeed * delta, Space.World);
        }
        if (isMovingBackward)
        {
            targetObject.transform.Translate(Vector3.back * moveSpeed * delta, Space.World);
        }

        // ȸ�� ó�� (���� X�� ����)
        if (isRotatingPositive)
        {
            targetObject.transform.Rotate(Vector3.right * rotateSpeed * delta);
        }
        if (isRotatingNegative)
        {
            targetObject.transform.Rotate(Vector3.right * -rotateSpeed * delta);
        }
    }

    // ��� �̵�/ȸ�� ���� (Pointer Up �̺�Ʈ���� ȣ��)
    public void StopAllMovement()
    {
        isMovingUp = false;
        isMovingDown = false;
        isMovingForward = false;
        isMovingBackward = false;
        isRotatingPositive = false;
        isRotatingNegative = false;
    }

    // Up ��ư ���� ����
    public void StartMoveUp()
    {
        StopAllMovement(); // �ٸ� ���� ���� (�ɼ�: ���ÿ� ����Ϸ��� ����)
        isMovingUp = true;
    }

    // Down ��ư ���� ����
    public void StartMoveDown()
    {
        StopAllMovement();
        isMovingDown = true;
    }

    // Forward ��ư ���� ����
    public void StartMoveForward()
    {
        StopAllMovement();
        isMovingForward = true;
    }

    // Backward ��ư ���� ����
    public void StartMoveBackward()
    {
        StopAllMovement();
        isMovingBackward = true;
    }

    // Rotate Positive ��ư ���� ����
    public void StartRotatePositive()
    {
        StopAllMovement();
        isRotatingPositive = true;
    }

    // Rotate Negative ��ư ���� ����
    public void StartRotateNegative()
    {
        StopAllMovement();
        isRotatingNegative = true;
    }

    // �߰� ������Ʈ ��� (�ϳ��� ��ư���� ��ȯ: Object1 �� Object2)
    public void ToggleAdditionalObjects()
    {
        if (additionalObject1 == null || additionalObject2 == null || additionalObject3 == null)
        {
            ChunaLogger.LogWarning("One or both additional objects are not assigned!");
            return;
        }

        if (selectedAdditional == 1)
        {
            // ���� Object1 On �� Object2 On���� ��ȯ
            additionalObject1.SetActive(false);
            additionalObject2.SetActive(true);
            selectedAdditional = 2;
            ChunaLogger.Log("Switched to Additional Object 2 (On), Object 1 Off");
        }
        else if (selectedAdditional ==2)
        {
            // ���� Object2 On �Ǵ� none �� Object1 On���� ��ȯ
            additionalObject2.SetActive(false);
            additionalObject3.SetActive(true);
            selectedAdditional = 3;
            ChunaLogger.Log("Switched to Additional Object 3 (On), Object 2 Off");
        }
        else
        {
            // ���� Object2 On �Ǵ� none �� Object1 On���� ��ȯ
            additionalObject1.SetActive(true);
            additionalObject3.SetActive(false);
            selectedAdditional = 1;
            ChunaLogger.Log("Switched to Additional Object 1 (On), Object 3 Off");
        }
    }
}