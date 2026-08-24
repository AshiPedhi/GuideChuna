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

        // ★음수 스케일이 걸린 부모 밑에서는 BoxCollider가 무효가 된다.
        //   c9 리그 상위에 미러 스케일이 있어 이마·뒤통수가 여기에 걸렸다(2026-08-24).
        Vector3 s = transform.lossyScale;
        if (s.x < 0f || s.y < 0f || s.z < 0f)
        {
            ChunaLogger.LogError($"[GripContactPoint] {name}의 월드 스케일이 음수다 {s} — " +
                                 "콜라이더가 동작하지 않는다. BoneFollower 홀더 밑으로 옮겨야 한다.");
        }
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
