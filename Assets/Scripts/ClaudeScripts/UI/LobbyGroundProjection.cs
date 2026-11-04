using System.Collections;
using UnityEngine;

/// <summary>
/// 로비 바닥 투영 (2026-10-30 사용자 «스카이박스 바닥을 내가 서 있는 기준으로 · 안 되면 바닥은 오브젝트와 섞는 하이브리드» → A안).
/// 실제 바닥(추적 공간 TrackingSpace의 y = 0 · 로비는 FloorLevel)에 판 하나를 깔고, 하늘과 같은 그림을 바닥에 찍는다(셰이더 GuideChuna/LobbyGroundProjection).
/// 판 중심 = 시작할 때 머리의 수평 자리 · 렌더 눈 자리 = 그 위 <see cref="renderEyeHeight"/>(블렌더 카메라 높이 1.6m).
/// 한 번만 놓는다(따라오지 않음) — 추적이 안정될 때까지 <see cref="placeDelay"/>초 기다린다(10-29 «추적 전 머리 값» 사고).
/// ★켜고 끄기는 <see cref="LobbyEnvironmentSky"/>가 한다(하늘 ↔ 패스스루). ★로그는 Debug.Log(릴리스에 남긴다).
/// </summary>
[RequireComponent(typeof(Renderer))]
public class LobbyGroundProjection : MonoBehaviour
{
    [Tooltip("파노라마를 렌더한 눈 높이(m) — 블렌더 pano.py cam.location z = 1.6")]
    [SerializeField] private float renderEyeHeight = 1.6f;
    [Tooltip("비우면 OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Transform headset;
    [Tooltip("비우면 OVRCameraRig/TrackingSpace — 이 y가 바닥")]
    [SerializeField] private Transform floorSpace;
    [SerializeField] private float placeDelay = 1.5f;

    private static readonly int EyeCenterId = Shader.PropertyToID("_EyeCenter");
    private Renderer rend;
    private MaterialPropertyBlock block;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        var rig = GameObject.Find("OVRCameraRig");
        if (headset == null && rig != null) headset = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        if (floorSpace == null && rig != null) floorSpace = rig.transform.Find("TrackingSpace");
    }

    private bool placed;

    // OnEnable — 놓기 전에 ◐로 꺼졌다 다시 켜져도 이어서 놓는다(Start는 다시 안 돈다)
    private void OnEnable()
    {
        if (!placed) StartCoroutine(PlaceLater());
    }

    private IEnumerator PlaceLater()
    {
        rend.enabled = false;
        while (!TrackingReady.Ready() && Time.timeSinceLevelLoad < placeDelay) yield return null;   // 10-30 추적이 들어오면 바로
        Place();
        placed = true;
        rend.enabled = true;
    }

    /// <summary>지금 머리 자리를 중심으로 다시 놓는다.</summary>
    public void Place()
    {
        float floorY = floorSpace != null ? floorSpace.position.y : 0f;
        Vector3 head = headset != null ? headset.position : Vector3.zero;
        transform.position = new Vector3(head.x, floorY + 0.002f, head.z);   // 바닥과 겹쳐 깜빡이지 않게 2mm 띄움
        Vector3 eye = new Vector3(head.x, floorY + renderEyeHeight, head.z);
        rend.GetPropertyBlock(block);
        block.SetVector(EyeCenterId, eye);
        rend.SetPropertyBlock(block);
        Debug.Log($"[LobbyGround] 바닥 투영 놓음 — 바닥 y {floorY:F2} · 머리 {head:F2}(눈높이 {head.y - floorY:F2}m) · 렌더 눈 {eye:F2} · {Time.timeSinceLevelLoad:F1}s");
    }
}
