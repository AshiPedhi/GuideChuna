using UnityEngine;

public class ObjectController : MonoBehaviour
{
    // 인스펙터에서 할당할 타겟 오브젝트 (이동/회전용)
    public GameObject targetObject;

    // 추가로 지정할 오브젝트 3개 (On/Off 토글용)
    public GameObject additionalObject1;
    public GameObject additionalObject2;
    public GameObject additionalObject3;

    // 이동 속도 (유닛/초, 인스펙터에서 조정 가능)
    public float moveSpeed = 5f;

    // 회전 속도 (도/초, 인스펙터에서 조정 가능)
    public float rotateSpeed = 90f;

    // 현재 선택된 오브젝트 추적 (0: none, 1: Object1, 2: Object2)
    private int selectedAdditional = 0;

    // 누르고 있는 동안 이동/회전 플래그 (각 방향별)
    private bool isMovingUp = false;
    private bool isMovingDown = false;
    private bool isMovingForward = false;
    private bool isMovingBackward = false;
    private bool isRotatingPositive = false;
    private bool isRotatingNegative = false;

    // Update 메서드: 누르고 있는 동안 지속 이동/회전 처리
    private void Update()
    {
        if (targetObject == null) return;

        float delta = Time.deltaTime;

        // 이동 처리 (글로벌 축 기준)
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

        // 회전 처리 (로컬 X축 기준)
        if (isRotatingPositive)
        {
            targetObject.transform.Rotate(Vector3.right * rotateSpeed * delta);
        }
        if (isRotatingNegative)
        {
            targetObject.transform.Rotate(Vector3.right * -rotateSpeed * delta);
        }
    }

    // 모든 이동/회전 중지 (Pointer Up 이벤트에서 호출)
    public void StopAllMovement()
    {
        isMovingUp = false;
        isMovingDown = false;
        isMovingForward = false;
        isMovingBackward = false;
        isRotatingPositive = false;
        isRotatingNegative = false;
    }

    // Up 버튼 눌림 시작
    public void StartMoveUp()
    {
        StopAllMovement(); // 다른 동작 중지 (옵션: 동시에 허용하려면 제거)
        isMovingUp = true;
    }

    // Down 버튼 눌림 시작
    public void StartMoveDown()
    {
        StopAllMovement();
        isMovingDown = true;
    }

    // Forward 버튼 눌림 시작
    public void StartMoveForward()
    {
        StopAllMovement();
        isMovingForward = true;
    }

    // Backward 버튼 눌림 시작
    public void StartMoveBackward()
    {
        StopAllMovement();
        isMovingBackward = true;
    }

    // Rotate Positive 버튼 눌림 시작
    public void StartRotatePositive()
    {
        StopAllMovement();
        isRotatingPositive = true;
    }

    // Rotate Negative 버튼 눌림 시작
    public void StartRotateNegative()
    {
        StopAllMovement();
        isRotatingNegative = true;
    }

    // 추가 오브젝트 토글 (하나의 버튼으로 전환: Object1 ↔ Object2)
    public void ToggleAdditionalObjects()
    {
        if (additionalObject1 == null || additionalObject2 == null || additionalObject3 == null)
        {
            Debug.LogWarning("One or both additional objects are not assigned!");
            return;
        }

        if (selectedAdditional == 1)
        {
            // 현재 Object1 On → Object2 On으로 전환
            additionalObject1.SetActive(false);
            additionalObject2.SetActive(true);
            selectedAdditional = 2;
            Debug.Log("Switched to Additional Object 2 (On), Object 1 Off");
        }
        else if (selectedAdditional ==2)
        {
            // 현재 Object2 On 또는 none → Object1 On으로 전환
            additionalObject2.SetActive(false);
            additionalObject3.SetActive(true);
            selectedAdditional = 3;
            Debug.Log("Switched to Additional Object 3 (On), Object 2 Off");
        }
        else
        {
            // 현재 Object2 On 또는 none → Object1 On으로 전환
            additionalObject1.SetActive(true);
            additionalObject3.SetActive(false);
            selectedAdditional = 1;
            Debug.Log("Switched to Additional Object 1 (On), Object 3 Off");
        }
    }
}