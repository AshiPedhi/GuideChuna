using UnityEngine;

/// <summary>
/// 손끝에 붙는 표식. 어느 손의 어느 손가락인지만 들고 있다.
/// 실제 판정은 <see cref="GripContactPoint"/>가 트리거로 한다.
/// </summary>
public class GripFingerTip : MonoBehaviour
{
    public enum Side { Left, Right }

    /// <summary>
    /// ★<see cref="Middle"/>은 2026-09-17에 <b>맨 끝에</b> 붙였다.
    ///   중간에 끼우면 씬에 int로 직렬화된 기존 표식이 조용히 다른 손가락이 된다(코드 컨벤션).
    /// </summary>
    public enum Finger { Thumb, Index, Middle }

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
