// ★[미사용 2026-08-31] 씬·프리팹에 배선이 없고, 다른 스크립트에서 부르지도 않는다.
//   지우지 않고 남겨 둔다 — 읽는 사람이 "이게 지금 도는 코드"라고 오해하지 않도록 이 줄을 단다.
//   판정 근거와 재확인 방법: .claude/tools/deadscan.py
using System.Reflection;
using UnityEngine;

/// <summary>
/// 손 접촉 판정이 왜 안 잡히는지 찍는 진단기.
///
/// 2026-08-24: 로그 전체에서 오른손이 <b>한 번도</b> 접촉으로 잡히지 않았다(왼손은 7회).
/// 코드 경로는 좌우가 같으므로 기하가 다르다는 뜻인데, 계산으로 단정하지 않고 실제 값을 찍는다.
///
/// ChunaPathEvaluator의 private 필드를 리플렉션으로 읽어 <b>판정기가 실제로 쓰는 그 값</b>을 본다.
/// 진단용이라 리플렉션과 매 프레임 문자열을 쓴다. 확인이 끝나면 컴포넌트를 빼면 된다.
/// </summary>
public class HandContactProbe : MonoBehaviour
{
    [Tooltip("비우면 씬에서 찾는다.")]
    [SerializeField] private ChunaPathEvaluator evaluator;

    [Tooltip("로그 간격(초)")]
    [SerializeField] private float logInterval = 1f;

    private float nextLogTime;

    private void Awake()
    {
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (evaluator == null)
        {
            Debug.LogWarning("[HandProbe] ChunaPathEvaluator를 찾지 못했습니다.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (Time.time < nextLogTime) return;
        nextLogTime = Time.time + Mathf.Max(0.2f, logInterval);

        Collider head = Field<Collider>("patientHeadCollider");
        if (head == null)
        {
            Debug.Log("[HandProbe] patientHeadCollider가 비어 있습니다.");
            return;
        }

        float forwardOffset = FieldFloat("handCollisionForwardOffset");
        float scale = FieldFloat("handColliderScale");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[HandProbe] 머리 bounds center={head.bounds.center} extents={head.bounds.extents} " +
                      $"(collider={head.name}, enabled={head.enabled}, activeInHierarchy={head.gameObject.activeInHierarchy})");
        sb.AppendLine($"           forwardOffset={forwardOffset:F3} colliderScale={scale:F2}");

        Report(sb, "왼손", ComponentTransform("playerLeftHand"), Field<Collider>("leftHandCollider"),
               head, forwardOffset, scale);
        Report(sb, "오른손", ComponentTransform("playerRightHand"), Field<Collider>("rightHandCollider"),
               head, forwardOffset, scale);

        Debug.Log(sb.ToString());
    }

    private static void Report(System.Text.StringBuilder sb, string label, Transform hand,
                               Collider handCollider, Collider head, float forwardOffset, float scale)
    {
        if (hand == null)
        {
            sb.AppendLine($"  {label}: ★Transform이 없다(필드 미배선).");
            return;
        }

        Vector3 basePos = handCollider != null ? handCollider.bounds.center : hand.position;
        Vector3 offsetPos = basePos + hand.forward * forwardOffset;

        Vector3 toHead = head.bounds.center - basePos;
        float distBase = toHead.magnitude;
        float distOffset = Vector3.Distance(offsetPos, head.bounds.center);
        float angle = Vector3.Angle(hand.forward, toHead.normalized);

        string colliderInfo = handCollider == null
            ? "콜라이더 없음"
            : $"{handCollider.name} enabled={handCollider.enabled} active={handCollider.gameObject.activeInHierarchy} " +
              $"extents={handCollider.bounds.extents}";

        // ★핵심 — forwardOffset을 적용했을 때 머리에서 멀어지면 그 손은 판정에서 밀려난다.
        string verdict = distOffset > distBase
            ? $"★멀어짐 {(distOffset - distBase) * 100f:F1}cm (forward가 머리 반대쪽)"
            : $"가까워짐 {(distBase - distOffset) * 100f:F1}cm";

        sb.AppendLine($"  {label}: obj={hand.name} active={hand.gameObject.activeInHierarchy}");
        sb.AppendLine($"        collider: {colliderInfo}");
        sb.AppendLine($"        머리까지 {distBase * 100f:F1}cm → 오프셋 적용 후 {distOffset * 100f:F1}cm  [{verdict}]");
        sb.AppendLine($"        forward와 머리방향 사이각 {angle:F0}° (90도가 넘으면 반대쪽을 본다)");
    }

    private Transform ComponentTransform(string fieldName)
    {
        object value = FieldRaw(fieldName);
        if (value is Component c) return c.transform;
        return null;
    }

    private T Field<T>(string fieldName) where T : class
    {
        return FieldRaw(fieldName) as T;
    }

    private float FieldFloat(string fieldName)
    {
        object value = FieldRaw(fieldName);
        return value is float f ? f : 0f;
    }

    private object FieldRaw(string fieldName)
    {
        FieldInfo info = typeof(ChunaPathEvaluator).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return info == null ? null : info.GetValue(evaluator);
    }
}
