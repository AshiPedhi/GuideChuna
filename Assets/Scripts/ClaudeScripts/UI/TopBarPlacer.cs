using UnityEngine;

/// <summary>
/// 실습 상단 버튼 바 — 눈높이에서 위로 <see cref="elevationDeg"/>° · 앞 <see cref="distance"/>m에 한 번 놓고 고정한다
/// (2026-10-27 사용자 요청 5 · 시안 V0.7 그림 4: «플레이어 상단 30도 · 가로로 긴 바 · 위치 고정 · 따라오기 x»).
///
/// 헤드셋 위치가 잡히기 전(첫 몇 프레임은 원점일 수 있다)에는 놓지 않는다 — 추적이 들어온 첫 프레임에 한 번 놓는다.
/// 판은 눈을 향해 기울인다(위에 있으니 아래로 숙는다). <see cref="Recenter"/>로 다시 놓는다.
/// ★10-29 사용자 «상단 메뉴가 머리 뒤쪽으로 생성된다» — 첫 프레임의 머리 방향이 믿을 수 없을 수 있다(원인 미확정 · 릴리스 로그가 없었다).
///   정면 정보 창은 제대로 뜨므로, 정보 창이 펼침 기준을 잡을 때(<see cref="InfoWindowArranger.Anchored"/>) <b>같은 머리 · 정면으로 다시 놓는다</b>.
///   놓을 때마다 Debug.Log를 남긴다(ChunaLogger.Log는 릴리스에서 빠진다).
/// </summary>
public class TopBarPlacer : MonoBehaviour
{
    [Tooltip("비우면 OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Transform headset;
    [Tooltip("수평 거리(m)")]
    [SerializeField] private float distance = 1.0f;
    [SerializeField] private float elevationDeg = 30f;
    [SerializeField] private bool showDebugLog = true;
    [Tooltip("10-29 사용자 «머리 위 상단 메뉴는 헤드셋 높이에 맞춰 쭉 따라가야» — 높이만 계속 따라간다(앞뒤 · 좌우 자리는 고정).\n" +
             "끄면 종전처럼 한 번 놓고 고정.")]
    [SerializeField] private bool followHeadHeight = true;
    [Tooltip("높이를 따라가는 빠르기(클수록 빨리 붙는다) — 고개 끄덕임에 바가 같이 흔들리지 않게 부드럽게")]
    [SerializeField] private float heightFollowSpeed = 4f;

    private bool placed;

    private void Awake()
    {
        if (headset == null)
        {
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) headset = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        }
    }

    private void OnEnable() => InfoWindowArranger.Anchored += OnWindowsAnchored;
    private void OnDisable() => InfoWindowArranger.Anchored -= OnWindowsAnchored;

    private void LateUpdate()
    {
        if (headset == null) return;
        if (!placed)
        {
            if (headset.localPosition.sqrMagnitude < 1e-6f && Time.timeSinceLevelLoad < 3f) return;   // 추적 전
            // ★10-30 사용자 «1.5초 뒤 다시 재설정 거슬린다» — 정보 창과 같이 추적이 안정된 뒤(1.5초) 한 번만 놓는다
            if (!TrackingReady.Ready(InfoWindowArranger.AfterAutoFit)) return;
            Recenter();
            return;
        }
        if (!followHeadHeight) return;

        // 높이만 따라간다 — 눈높이 + 수평 거리 × tan(위 각도). 판은 계속 눈을 향한다(매 프레임 할당 없음)
        Vector3 head = headset.position;
        Vector3 p = transform.position;
        float targetY = head.y + distance * Mathf.Tan(elevationDeg * Mathf.Deg2Rad);
        p.y = Mathf.Lerp(p.y, targetY, 1f - Mathf.Exp(-heightFollowSpeed * Time.unscaledDeltaTime));
        Vector3 look = p - head;
        if (look.sqrMagnitude > 1e-6f) transform.SetPositionAndRotation(p, Quaternion.LookRotation(look, Vector3.up));
        else transform.position = p;
    }

    public void Recenter()
    {
        if (headset == null) return;
        // 10-30 정면 = 내 머리 → 환자(정보 창과 같은 규칙 · 환자를 못 찾으면 머리 방향)
        Vector3 f = PatientLocator.TryGet(out var pp) ? pp.center - headset.position : headset.forward;
        if (Mathf.Abs(f.x) + Mathf.Abs(f.z) < 0.05f) f = headset.forward;
        f.y = 0f;
        Place(headset.position, f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized, "첫 추적");
    }

    private void OnWindowsAnchored(Vector3 head, Vector3 fwd) => Place(head, fwd, "정보 창 기준");

    private void Place(Vector3 head, Vector3 fwd, string why)
    {
        // 시안 그림 4-1과 같은 정의: 수평 앞 distance · 위 distance × tan(elevation) (1.0m면 위 0.58m)
        Vector3 p = head + fwd * distance + Vector3.up * (distance * Mathf.Tan(elevationDeg * Mathf.Deg2Rad));
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head, Vector3.up));
        placed = true;
        SceneReadyGate.Mark("bar");   // 10-30 로딩 가림막이 이걸 기다린다
        if (showDebugLog)
            Debug.Log($"[TopBar] {name} 고정({why}) {p:F2} · 머리 {head:F2} · 정면 {fwd:F2} · 위 {elevationDeg}° · 수평 앞 {distance}m · 시각 {Time.timeSinceLevelLoad:F1}s");
    }
}
