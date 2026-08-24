using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 파지 접촉점. 트리거 안에 어느 손의 엄지·검지가 들어와 있는지만 센다.
/// 거리 계산 없이 콜라이더가 닿았는지로 판정한다.
///
/// ★콜라이더 종류를 가리지 않는다. 이마·뒤통수처럼 넓은 면은 박스가, 곡면은 캡슐이
///   맞을 수 있다. 씬에서 콜라이더를 바꾸거나 크기·회전을 조절하면 그대로 따라간다.
///   트리거로만 되어 있으면 된다.
/// </summary>
public class GripContactPoint : MonoBehaviour
{
    private readonly HashSet<GripFingerTip> inside = new HashSet<GripFingerTip>();

    /// <summary>왼손이 엄지·검지로 이 점을 집고 있는가.</summary>
    public bool LeftGripping => Gripping(GripFingerTip.Side.Left);

    /// <summary>오른손이 엄지·검지로 이 점을 집고 있는가.</summary>
    public bool RightGripping => Gripping(GripFingerTip.Side.Right);

    /// <summary>엄지 하나만으로 인정할지. 끄면 엄지와 검지가 둘 다 들어와야 한다.</summary>
    public bool RequireBothFingers { get; set; } = true;

    private void OnEnable()
    {
        inside.Clear();

        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            ChunaLogger.LogWarning($"[GripContactPoint] {name}에 콜라이더가 없습니다.");
            return;
        }

        // 경고만 띄우면 사람이 4곳을 손으로 켜야 한다. 우리가 만든 오브젝트이므로 켜 준다.
        if (!col.isTrigger)
        {
            col.isTrigger = true;
            ChunaLogger.Log($"[GripContactPoint] {name}의 Is Trigger가 꺼져 있어 켰습니다.");
        }

        // ★c8에 X축 -1 스케일이 걸려 있다. 그 밑에서 BoxCollider는 무효가 된다.
        //   Sphere·Capsule은 멀쩡히 돈다 — 기존 파지점 44개가 전부 그 둘이다(2026-08-24 실측).
        Vector3 s = transform.lossyScale;
        if (col is BoxCollider && (s.x < 0f || s.y < 0f || s.z < 0f))
        {
            ChunaLogger.LogError($"[GripContactPoint] {name}이 음수 스케일 {s} 아래의 BoxCollider다 — " +
                                 "동작하지 않는다. Capsule이나 Sphere로 바꿔야 한다.");
        }
    }

    private void OnDisable() => inside.Clear();

    [Tooltip("트리거에 들어오는 것을 전부 찍는다. 판정이 아예 안 걸릴 때 켜서 원인을 본다 —\n" +
             "아무것도 안 찍히면 트리거 자체가 안 뜨는 것이고(레이어·Rigidbody 문제),\n" +
             "다른 콜라이더만 찍히면 손끝 표식이 안 붙은 것이다.")]
    [SerializeField] private bool logAllTriggers = false;

    private void OnTriggerEnter(Collider other)
    {
        GripFingerTip tip = other.GetComponent<GripFingerTip>();
        if (tip != null) inside.Add(tip);

        if (logAllTriggers)
        {
            ChunaLogger.Log($"<color=cyan>[{name}] 진입: {other.name} " +
                            $"(손끝표식 {(tip != null ? $"{tip.HandSide} {tip.FingerKind}" : "없음")}, " +
                            $"레이어 {LayerMask.LayerToName(other.gameObject.layer)}, " +
                            $"트리거 {other.isTrigger})</color>");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        GripFingerTip tip = other.GetComponent<GripFingerTip>();
        if (tip != null) inside.Remove(tip);

        if (logAllTriggers) ChunaLogger.Log($"[{name}] 이탈: {other.name}");
    }

    private bool Gripping(GripFingerTip.Side side)
    {
        bool thumb = false, index = false;
        foreach (GripFingerTip t in inside)
        {
            if (t == null || t.HandSide != side) continue;
            if (t.FingerKind == GripFingerTip.Finger.Thumb) thumb = true;
            else index = true;
        }
        return RequireBothFingers ? (thumb && index) : (thumb || index);
    }
}
