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
            ChunaLogger.LogWarning($"[GripContactPoint] {name}에 콜라이더가 없습니다.");
        else if (!col.isTrigger)
            ChunaLogger.LogWarning($"[GripContactPoint] {name}의 콜라이더가 트리거가 아닙니다 — Is Trigger를 켜세요.");
    }

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
