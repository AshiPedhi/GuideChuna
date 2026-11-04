using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 리모컨 방향 버튼 하나 — 누르는 순간 한 칸, 누르고 있으면 계속(<see cref="PatientPositionRemote"/>).
/// ISDK PointableCanvasModule이 uGUI 포인터 이벤트로 넘겨 주므로 손 · 레이 둘 다 같은 경로다.
/// 손가락이 버튼 밖으로 벗어나도 멈춘다(떼는 이벤트를 놓쳐 계속 미끄러지는 것을 막는다).
/// </summary>
public class PatientPositionRemoteButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] private PatientPositionRemote remote;
    [SerializeField] private PatientPositionRemote.Direction direction;

    public void OnPointerDown(PointerEventData e) { if (remote != null) remote.Press(direction); }
    public void OnPointerUp(PointerEventData e) { if (remote != null) remote.Release(direction); }
    public void OnPointerExit(PointerEventData e) { if (remote != null) remote.Release(direction); }
    private void OnDisable() { if (remote != null) remote.Release(direction); }
}
