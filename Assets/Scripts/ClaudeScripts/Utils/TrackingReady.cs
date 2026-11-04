using UnityEngine;

/// <summary>
/// 헤드셋 위치 추적이 «믿을 만해졌나» (2026-10-30 사용자 «호출하고 세팅되는 거 너무 오래 걸린다»).
/// 종전엔 씬 로드 뒤 1.5초를 무조건 기다렸다(10-29 «추적 전 머리 값» 사고 대처). 이제 OVR이 머리 위치를 추적 중이라고 알린 뒤
/// <see cref="StableSeconds"/>만 지나면 바로 참 — 신호가 끝내 안 오면(에디터 등) <see cref="FallbackSeconds"/>에 참.
/// 자동 맞춤(InfoPanelController) → 정보 창 · 상단 메뉴(InfoWindowArranger · TopBarPlacer) → 로비 바닥 투영이 같은 기준을 쓴다.
/// </summary>
public static class TrackingReady
{
    public const float StableSeconds = 0.2f;
    public const float FallbackSeconds = 1.5f;
    private static float trackedSince = -1f;
    private static int lastFrame = -1;
    private static bool lastResult;

    /// <param name="extra">그 위에 더 기다릴 초(자동 맞춤 뒤에 놓을 것들)</param>
    public static bool Ready(float extra = 0f)
    {
        float t = Time.timeSinceLevelLoad;
        if (t >= FallbackSeconds + extra) return true;
        if (lastFrame != Time.frameCount)
        {
            lastFrame = Time.frameCount;
            if (trackedSince > t) trackedSince = -1f;   // 씬이 바뀌었다
            bool tracked = OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head);
            if (!tracked) trackedSince = -1f;
            else if (trackedSince < 0f) { trackedSince = t; Debug.Log($"[TrackingReady] 머리 위치 추적 시작 · {t:F2}s"); }
        }
        return trackedSince >= 0f && t - trackedSince >= StableSeconds + extra;
    }
}
