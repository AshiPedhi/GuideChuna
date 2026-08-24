using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 파지 접촉점. 트리거 안에 어느 손의 엄지·검지가 들어와 있는지만 센다.
/// 거리 계산 없이 콜라이더가 닿았는지로 판정한다.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class GripContactPoint : MonoBehaviour
{
    private readonly HashSet<GripFingerTip> inside = new HashSet<GripFingerTip>();

    /// <summary>왼손이 엄지·검지로 이 점을 집고 있는가.</summary>
    public bool LeftGripping => Gripping(GripFingerTip.Side.Left);

    /// <summary>오른손이 엄지·검지로 이 점을 집고 있는가.</summary>
    public bool RightGripping => Gripping(GripFingerTip.Side.Right);

    /// <summary>엄지 하나만으로 인정할지. 끄면 엄지와 검지가 둘 다 들어와야 한다.</summary>
    public bool RequireBothFingers { get; set; } = true;

    private void OnEnable() => inside.Clear();
    private void OnDisable() => inside.Clear();

    private void OnTriggerEnter(Collider other)
    {
        GripFingerTip tip = other.GetComponent<GripFingerTip>();
        if (tip != null) inside.Add(tip);
    }

    private void OnTriggerExit(Collider other)
    {
        GripFingerTip tip = other.GetComponent<GripFingerTip>();
        if (tip != null) inside.Remove(tip);
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
