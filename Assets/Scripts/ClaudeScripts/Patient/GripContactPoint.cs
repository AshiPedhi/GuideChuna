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

    /// <summary>왼손이 이 점을 집고 있는가.</summary>
    public bool LeftGripping => Gripping(GripFingerTip.Side.Left);

    /// <summary>오른손이 이 점을 집고 있는가.</summary>
    public bool RightGripping => Gripping(GripFingerTip.Side.Right);

    /// <summary>엄지 하나만으로 인정할지. 끄면 엄지와 검지가 둘 다 들어와야 한다.</summary>
    public bool RequireBothFingers { get; set; } = true;

    /// <summary>엄지만 보고 판정한다. 켜지면 <see cref="RequireBothFingers"/>는 안 본다.</summary>
    public bool ThumbOnly { get; set; } = false;

    /// <summary>
    /// ★엄지 필수 + 검지·중지 중 <b>아무거나 하나 이상</b>(= 닿은 손가락이 둘 이상).
    /// 2026-09-17 사용자 지시: "엄지 필수에 중지 검지 상관없이 둘 이상 닿으면 진행."
    /// ★<see cref="ThumbOnly"/>보다 <b>먼저</b> 본다 — 둘 다 켜지는 일은 없지만, 켜지면 이쪽이 이긴다.
    /// </summary>
    public bool ThumbPlusAny { get; set; } = false;

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
        bool thumb = false, index = false, middle = false;
        foreach (GripFingerTip t in inside)
        {
            if (t == null || t.HandSide != side) continue;
            switch (t.FingerKind)
            {
                case GripFingerTip.Finger.Thumb:  thumb = true;  break;
                case GripFingerTip.Finger.Middle: middle = true; break;
                default:                          index = true;  break;
            }
        }

        // ★엄지 필수 + 검지·중지 아무거나 하나 이상(2026-09-17 사용자 지시).
        //   "엄지 필수에 중지 검지 상관없이 둘 이상 닿으면 진행."
        //   ★엄지가 빠지면 성립하지 않는다 — 검지·중지는 머리 뒤로 넘어가 가려지는 손가락이라
        //     그 둘만으로 선 성립은 믿을 수 없다(09-02에 확인한 사실은 그대로다).
        //   ★엄지 단독보다 까다롭지만 엄지+검지보다는 무르다. 가려지는 손가락이 <b>둘</b>이라
        //     둘 중 하나만 살아 있어도 통과한다 — 그게 이 모드의 전부다.
        if (ThumbPlusAny) return thumb && (index || middle);

        // ★엄지 단독(2026-09-02) — 검지가 닿았는지는 <b>아예 안 본다</b>.
        //   `RequireBothFingers = false`(엄지 또는 검지)와 다르다. 그건 <b>검지만</b> 닿아도
        //   성립시키는데, 검지는 머리 뒤로 넘어가 가려지는 손가락이라 그 성립을 믿을 수 없다.
        if (ThumbOnly) return thumb;

        // 종전 두 모드는 중지를 안 본다 — 그때 판정 그대로 둔다.
        return RequireBothFingers ? (thumb && index) : (thumb || index);
    }
}
