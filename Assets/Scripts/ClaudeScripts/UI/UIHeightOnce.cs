using UnityEngine;

/// <summary>
/// 로비 UI 높이를 시작할 때 한 번만 머리 높이에 맞춘다 (2026-10-30 사용자 «로비 메뉴 높이 따라오는 기능 빼 줘»).
/// 옛 `UIHight`(_JDH · 매 프레임 머리 높이 − 0.1m로 따라감)를 로비 씬에서 끄고 이것을 붙인다 — 같은 식(머리 로컬 높이 − <see cref="below"/>)을 한 번만.
/// 추적이 들어온 뒤(TrackingReady)에 맞춘다(추적 전 머리 값이면 높이가 틀어진다 — 10-29 사고). ★Update 할당 없음.
/// </summary>
public class UIHeightOnce : MonoBehaviour
{
    [Tooltip("비우면 OVRCameraRig/TrackingSpace/CenterEyeAnchor")]
    [SerializeField] private Transform centerEyeAnchor;
    [Tooltip("머리보다 이만큼 아래(m) — 옛 UIHight와 같은 0.1")]
    [SerializeField] private float below = 0.1f;
    private bool done;

    private void Awake()
    {
        if (centerEyeAnchor == null)
        {
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) centerEyeAnchor = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        }
    }

    private void LateUpdate()
    {
        if (done || centerEyeAnchor == null || !TrackingReady.Ready()) return;
        var p = transform.localPosition;
        p.y = centerEyeAnchor.localPosition.y - below;
        transform.localPosition = p;
        done = true;
        Debug.Log($"[UIHeightOnce] {name} 높이 한 번 맞춤 — 로컬 y {p.y:F2}(머리 {centerEyeAnchor.localPosition.y:F2} − {below}) · {Time.timeSinceLevelLoad:F2}s");
    }
}
