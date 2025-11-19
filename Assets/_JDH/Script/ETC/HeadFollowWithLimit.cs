using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadFollowWithLimit : MonoBehaviour
{
    [Header("기준 본 (Neck)")]
    public Transform neck;

    [Header("X축 회전 (고개 들기/숙이기)")]
    public float maxUpAngle = 30f;     // 위로 들기 (고개 뒤로 젖힘)
    public float maxDownAngle = 20f;   // 아래로 숙이기

    [Header("Y, Z 축 회전 제한 (도)")]
    public float maxYRotation = 45f;   // 좌우 돌림
    public float maxZRotation = 10f;   // 기울임

    [Header("회전 반응 속도")]
    public float rotationLerpSpeed = 5f;

    [Header("머리 거리 제한")]
    public float maxDistanceFromNeck = 0.05f; // 5cm 이상 못 벗어남

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (neck == null) return;

        // ✅ 거리 제한
        Vector3 offset = transform.position - neck.position;
        float distance = offset.magnitude;
        if (distance > maxDistanceFromNeck)
        {
            Vector3 limitedPos = neck.position + offset.normalized * maxDistanceFromNeck;
            rb.MovePosition(limitedPos);
            rb.linearVelocity = Vector3.zero;
        }

        // ✅ 회전 제한
        Quaternion localRotation = Quaternion.Inverse(neck.rotation) * transform.rotation;
        Vector3 euler = NormalizeEuler(localRotation.eulerAngles);

        // --- X축: 고개 들기/숙이기
        float clampedX = Mathf.Clamp(euler.x, -maxDownAngle, maxUpAngle);

        // --- Y, Z축: 좌우/기울임
        float clampedY = Mathf.Clamp(euler.y, -maxYRotation, maxYRotation);
        float clampedZ = Mathf.Clamp(euler.z, -maxZRotation, maxZRotation);

        // 제한된 회전으로 적용
        Quaternion limitedLocalRot = Quaternion.Euler(clampedX, clampedY, clampedZ);
        Quaternion targetWorldRot = neck.rotation * limitedLocalRot;

        Quaternion finalRotation = Quaternion.Slerp(transform.rotation, targetWorldRot, Time.fixedDeltaTime * rotationLerpSpeed);
        rb.MoveRotation(finalRotation);
        rb.angularVelocity = Vector3.zero;
    }

    // 각도 정규화 (-180 ~ 180)
    Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(
            Mathf.Repeat(euler.x + 180f, 360f) - 180f,
            Mathf.Repeat(euler.y + 180f, 360f) - 180f,
            Mathf.Repeat(euler.z + 180f, 360f) - 180f
        );
    }
    void OnDrawGizmosSelected()
    {
        if (neck == null) return;

        // ✅ 거리 제한 구 (회색)
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(neck.position, maxDistanceFromNeck);

        // ✅ 고개 위로 들기 제한 시각화 (파란 원뿔)
        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.5f);
        DrawAngleCone(neck.position, neck.forward, -neck.right, maxUpAngle, 0.1f);

        // ✅ 고개 아래 숙이기 제한 (빨간 원뿔)
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
        DrawAngleCone(neck.position, neck.forward, neck.right, maxDownAngle, 0.1f);

        // ✅ 좌우 회전 제한 (Y축 기준)
        Gizmos.color = Color.green;
        DrawAngleCone(neck.position, neck.forward, neck.up, maxYRotation, 0.1f);

        // ✅ 기울임 제한 (Z축 기준)
        Gizmos.color = Color.magenta;
        DrawAngleCone(neck.position, neck.forward, neck.forward, maxZRotation, 0.1f); // 시각화 용도
    }

    // 🔺 보조 함수: 각도 범위 원뿔 그리기
    void DrawAngleCone(Vector3 origin, Vector3 forward, Vector3 axis, float angle, float length)
    {
        int segments = 20;
        float step = angle / segments;

        for (int i = -segments; i < segments; i++)
        {
            Quaternion rot1 = Quaternion.AngleAxis(step * i, axis);
            Quaternion rot2 = Quaternion.AngleAxis(step * (i + 1), axis);

            Vector3 dir1 = rot1 * forward;
            Vector3 dir2 = rot2 * forward;

            Gizmos.DrawLine(origin, origin + dir1 * length);
            Gizmos.DrawLine(origin + dir1 * length, origin + dir2 * length);
        }
    }
}
