using System.Collections;
using UnityEngine;

/// <summary>
/// 로비 배경 = 360° 파노라마 하늘 (2026-10-29 사용자 «로비 배경 시안 V0.3 괜찮다 — 적용해서 같이 테스트»).
/// 블렌더 시안(테라스 · 현대식 한방병원 · 도시)을 눈높이에서 360°로 렌더한 그림 한 장을 스카이박스로 깐다 — 3D 모델이 없어 배터리 · 발열 부담이 거의 없다.
///
/// 켜면(useSky) 시작할 때: 패스스루 층을 숨기고 · 카메라 배경을 스카이박스로 · RenderSettings.skybox = 이 재질.
/// 끄면 아무것도 안 한다(씬 기본 = 패스스루 그대로).
/// ★패스스루 층 · 카메라 배경은 이 컴포넌트 한 곳만 만진다(10-30 — ◐ 버튼 `LobbyPassthroughToggle`은 SetSkyVisible만 부른다).
/// ★우리가 바꾼 것만 되돌린다 — 꺼질 때(OnDisable) 패스스루 층 · 카메라 배경 · 스카이박스를 시작 전 값으로.
/// ★로그는 Debug.Log(ChunaLogger.Log는 릴리스에서 빠진다).
/// </summary>
public class LobbyEnvironmentSky : MonoBehaviour
{
    [Tooltip("켜면 파노라마 배경 · 끄면 패스스루(종전)")]
    [SerializeField] private bool useSky = true;
    [SerializeField] private Material skyMaterial;
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [Tooltip("비우면 OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Camera centerEyeCamera;
    [Tooltip("10-30 바닥 투영 판(LobbyGroundProjection) — 하늘일 때만 켠다 · 비우면 바닥 없이 하늘만")]
    [SerializeField] private GameObject groundProjection;

    private bool applied;
    private bool skyVisible;
    private bool oldHidden;
    private CameraClearFlags oldFlags;
    private Color oldBg;
    private Material oldSky;

    /// <summary>파노라마를 쓰는 로비인가(◐ 버튼이 시작 상태를 정할 때 본다)</summary>
    public bool UsesSky => useSky && skyMaterial != null;
    public bool SkyVisible => skyVisible;

    private IEnumerator Start()
    {
        if (!UsesSky) { Debug.Log($"[LobbySky] 파노라마 배경 안 씀(useSky {useSky} · 재질 {(skyMaterial != null)}) — 패스스루 그대로"); yield break; }
        // ★10-30 기기 로그 실측: 로비의 옛 `EventManager`(UIAxis · realOn true)가 Start에서 패스스루를 켜고 카메라를 투명 단색으로 바꾼다.
        //   Start 순서는 정해져 있지 않아 그쪽이 나중에 돌면 하늘이 덮였다(«하늘 켬» 2초 뒤 hidden False · SolidColor 알파 0).
        //   → 모든 Start가 끝난 다음 프레임에 깐다. EventManager는 다른 씬과 같이 쓰는 옛 코드라 건드리지 않는다
        yield return null;
        if (passthroughLayer == null) passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
        if (centerEyeCamera == null)
        {
            var rig = GameObject.Find("OVRCameraRig");
            var eye = rig != null ? rig.transform.Find("TrackingSpace/CenterEyeAnchor") : null;
            if (eye != null) centerEyeCamera = eye.GetComponent<Camera>();
        }
        oldSky = RenderSettings.skybox;
        if (passthroughLayer != null) oldHidden = passthroughLayer.hidden;
        if (centerEyeCamera != null) { oldFlags = centerEyeCamera.clearFlags; oldBg = centerEyeCamera.backgroundColor; }
        applied = true;
        SetSkyVisible(true);
        StartCoroutine(ReportLater());
    }

    /// <summary>true = 하늘(패스스루 숨김) · false = 패스스루(하늘 걷음)</summary>
    public void SetSkyVisible(bool on)
    {
        if (!applied) return;
        skyVisible = on;
        // ★10-30 씬 자체가 하늘 상태로 저장된다(에디터 확인용) — 시작 전 값이 곧 하늘이라 «되돌리기»로는 패스스루가 안 된다.
        //   패스스루 = 투명 단색(알파 0) + 층 보임으로 명시한다
        RenderSettings.skybox = skyMaterial;
        if (passthroughLayer != null) passthroughLayer.hidden = on;
        if (groundProjection != null) groundProjection.SetActive(on);
        if (centerEyeCamera != null)
        {
            centerEyeCamera.clearFlags = on ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            var c = oldBg; c.a = on ? 1f : 0f; centerEyeCamera.backgroundColor = c;
        }
        Debug.Log($"[LobbySky] {(on ? "하늘 켬" : "패스스루로")} — 재질 {skyMaterial.name} · 패스스루 층 {(passthroughLayer != null ? (passthroughLayer.hidden ? "숨김" : "보임") : "없음")} · 카메라 {(centerEyeCamera != null ? centerEyeCamera.clearFlags.ToString() : "없음")}");
    }

    // 10-30: 시작 로그엔 «숨김»인데 기기에선 패스스루가 보였다 — 2초 뒤 실제 상태를 한 번 더 찍어 무엇이 되살렸는지 가른다
    private IEnumerator ReportLater()
    {
        yield return new WaitForSeconds(2f);
        var mgr = OVRManager.instance;
        Debug.Log($"[LobbySky] 2초 뒤 상태 — 층 {(passthroughLayer != null ? $"hidden {passthroughLayer.hidden} · enabled {passthroughLayer.enabled} · 활성 {passthroughLayer.gameObject.activeInHierarchy}" : "없음")}"
            + $" · insight {(mgr != null ? mgr.isInsightPassthroughEnabled.ToString() : "매니저 없음")}"
            + $" · 카메라 {(centerEyeCamera != null ? $"{centerEyeCamera.clearFlags} 알파 {centerEyeCamera.backgroundColor.a:F2}" : "없음")}"
            + $" · 스카이박스 {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "없음")}");
    }

    private void OnDisable()
    {
        if (!applied) return;
        applied = false;
        RenderSettings.skybox = oldSky;
        if (passthroughLayer != null) passthroughLayer.hidden = oldHidden;
        if (centerEyeCamera != null) { centerEyeCamera.clearFlags = oldFlags; centerEyeCamera.backgroundColor = oldBg; }
    }
}
