using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 우측 상단 패스스루 켬/끔 (2026-10-27 사용자 요청 1 · 시안 V0.7 그림 1 ③).
/// 기본 = 패스스루 켬(사용자 지시 «배경 디폴트는 패스쓰루»). 끄면 방 모델(RoomEnvironment)을 켜고 카메라 배경을 불투명으로 한다.
///
/// ★켠 쪽이 끈다 — 이 컴포넌트가 바꾼 세 가지(패스스루 층 숨김 · 방 모델 · 카메라 배경 알파)만 바꾸고, 꺼질 때 손대지 않는다.
/// 버튼은 모멘터리 Toggle(로비의 다른 버튼과 같은 꼴)이라 누를 때마다 상태를 뒤집는다.
/// </summary>
[RequireComponent(typeof(Toggle))]
public class LobbyPassthroughToggle : MonoBehaviour
{
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [Tooltip("패스스루를 끄면 보일 방(로비의 RoomEnvironment)")]
    [SerializeField] private GameObject roomEnvironment;
    [SerializeField] private Camera centerEyeCamera;
    [Tooltip("켜짐 표시(◐ 글자 등) — 켜짐이면 밝게")]
    [SerializeField] private Graphic stateGraphic;

    private bool passthroughOn = true;
    private Toggle toggle;
    private LobbyEnvironmentSky sky;   // 10-30 사용자 «패스스루 끄라니까» A안 — 파노라마 로비면 하늘 ↔ 패스스루 · 패스스루 층은 sky 한 곳만 만진다

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.group = null;
        toggle.onValueChanged.AddListener(OnChanged);
        sky = FindFirstObjectByType<LobbyEnvironmentSky>();
        if (sky != null && !sky.UsesSky) sky = null;
        if (passthroughLayer == null) passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
        if (centerEyeCamera == null)
        {
            var rig = GameObject.Find("OVRCameraRig");
            var eye = rig != null ? rig.transform.Find("TrackingSpace/CenterEyeAnchor") : null;
            if (eye != null) centerEyeCamera = eye.GetComponent<Camera>();
        }
    }

    // 시작 = 파노라마 로비면 하늘(패스스루 끔 · 하늘은 sky.Start가 이미 깔았다) · 아니면 종전대로 패스스루
    private void Start()
    {
        if (sky != null) { passthroughOn = false; SetStateGraphic(false); return; }
        Apply(true);
    }

    private void OnChanged(bool isOn)
    {
        if (!isOn) return;
        toggle.SetIsOnWithoutNotify(false);   // 모멘터리
        Apply(!passthroughOn);
    }

    private void Apply(bool on)
    {
        passthroughOn = on;
        if (sky != null)
        {
            sky.SetSkyVisible(!on);
            SetStateGraphic(on);
            Debug.Log($"[LobbyPassthrough] 패스스루 {(on ? "켬" : "끔 → 하늘")}");
            return;
        }
        if (passthroughLayer != null) passthroughLayer.hidden = !on;
        if (roomEnvironment != null) roomEnvironment.SetActive(!on);
        if (centerEyeCamera != null)
        {
            var c = centerEyeCamera.backgroundColor;
            c.a = on ? 0f : 1f;
            centerEyeCamera.backgroundColor = c;
        }
        SetStateGraphic(on);
        Debug.Log($"[LobbyPassthrough] 패스스루 {(on ? "켬" : "끔")} · 층 {(passthroughLayer != null)} · 방 {(roomEnvironment != null)} · 카메라 {(centerEyeCamera != null)}");
    }

    private void SetStateGraphic(bool on)
    {
        if (stateGraphic != null) stateGraphic.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
    }
}
