using UnityEngine;

/// <summary>
/// 엄지·검지 끝에 붙는 표식. 어느 손의 어느 손가락인지만 들고 있다.
/// 실제 판정은 <see cref="GripContactPoint"/>가 트리거로 한다.
/// </summary>
public class GripFingerTip : MonoBehaviour
{
    public enum Side { Left, Right }
    public enum Finger { Thumb, Index }

    [SerializeField] private Side side;
    [SerializeField] private Finger finger;

    public Side HandSide => side;
    public Finger FingerKind => finger;

    public void Configure(Side s, Finger f)
    {
        side = s;
        finger = f;
    }
}
