using UnityEngine;

public class LocalRotationAnimator : MonoBehaviour
{
    public enum Axis
    {
        Right = 0,
        Up = 1,
        Forward = 2
    }

    [SerializeField] private string ani_name;
    [SerializeField] private Animator animator;
    [SerializeField] private Animator animator2;
    [SerializeField] private Axis rotationAxis = Axis.Up; // Inspector에서 선택할 수 있는 축
    private Quaternion previousLocalRotation; // 이전 로컬 쿼터니언
    public float currentAnimSpeed = 0f; // 현재 애니메이션 속도
    [SerializeField] private float velocity = 0f; // SmoothDamp용 속도 추적
    [SerializeField] private float maxAngularSpeed = 100f; // 최대 각속도 (도/초)
    [SerializeField] private float speedThreshold = 1f; // 최소 변화 임계값
    [SerializeField] private float smoothTime = 0.1f; // 속도 보간 시간 (작을수록 빠르게 반응)
    [SerializeField] private float rotationMultiplier = 1f; // 회전 비율 조절 (Inspector에서 설정 가능, 1:1 비율을 위한 스케일)

    // 애니메이션 재생 위치를 확인하기 위한 변수 (0~1 사이 값, 0: 시작, 1: 끝)
    [SerializeField] private float currentNormalizedTime = 0f;

    void Start()
    {
        previousLocalRotation = transform.localRotation;
    }

    void Update()
    {
        animator.Play(ani_name); // 애니메이션 상태 시작
        animator2.Play(ani_name); // 애니메이션 상태 시작

        Quaternion currentLocalRotation = transform.localRotation;

        // 회전 차이 계산
        Quaternion deltaRotation = Quaternion.Inverse(previousLocalRotation) * currentLocalRotation;
        Vector3 eulerDelta = deltaRotation.eulerAngles;

        // 선택된 축에 따른 변화량 추출
        float delta = 0f;
        if (rotationAxis == Axis.Right)
        {
            delta = eulerDelta.x;
        }
        else if (rotationAxis == Axis.Up)
        {
            delta = eulerDelta.y;
        }
        else if (rotationAxis == Axis.Forward)
        {
            delta = eulerDelta.z;
        }
        delta = Mathf.DeltaAngle(0, delta); // 변화량 정규화

        // 각속도 계산 (multiplier 적용)
        float angularSpeed = (delta * rotationMultiplier) / Time.deltaTime;

        // 목표 애니메이션 속도 계산
        float targetAnimSpeed;
        if (Mathf.Abs(angularSpeed) < speedThreshold)
        {
            // 임계값 이하: 속도를 0으로 부드럽게 감소
            targetAnimSpeed = 0f;
        }
        else
        {
            // 비례 속도: 부호 유지, maxAngularSpeed로 정규화
            targetAnimSpeed = angularSpeed / maxAngularSpeed;
        }

        // 부드러운 속도 전환 (SmoothDamp)
        currentAnimSpeed = Mathf.SmoothDamp(currentAnimSpeed, targetAnimSpeed, ref velocity, smoothTime);

        // Animator에 적용
        animator.SetFloat("AnimSpeed", currentAnimSpeed);
        animator2.SetFloat("AnimSpeed", currentAnimSpeed);

        // 애니메이션 재생 위치 업데이트 (normalizedTime: 0~1 범위)
        currentNormalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        // 다음 프레임 위해 업데이트
        previousLocalRotation = currentLocalRotation;
    }
}