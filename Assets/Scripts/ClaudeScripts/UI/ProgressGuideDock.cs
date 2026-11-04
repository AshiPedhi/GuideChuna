using UnityEngine;

/// <summary>
/// 진행 가이드(진행Root — 단계 · 지시문 · 게이지 · 버튼)를 <b>진행 현황 창 안</b>에 그린다 (2026-10-29 사용자 지시).
///   «처음 켜지는 화면은 수행결과를 진행 현황으로 · 진행 가이드 내용이랑 기능을 진행 현황에 같이 표시» ·
///   «따로 뜨는 판은 숨김»(같은 날 질문 답).
///
/// 방식 = <b>자리만 맞춘다</b>(경추ROM 실측 <c>CervicalRomMeasurementBridge.PlaceProgressAtResultPage</c>와 같은 원리).
///   진행Root는 월드 오브젝트(자체 캔버스)라 창 캔버스 안에 부모로 끼우면 앵커 · 중첩 캔버스가 무너진다(09-03 · 09-04 이력).
///   → 부모는 그대로 두고, 매 프레임 진행 현황 창 캔버스의 <b>위쪽</b>에 같은 방향 · 창 폭에 맞춘 배율로 겹쳐 놓는다.
///   버튼 · 홀드 · 나레이션 등 기능은 진행Root 그대로라 하나도 안 바뀐다.
/// 창이 닫혀 있으면 진행 가이드 캔버스를 그리지 않는다(따로 뜨지 않게). 창을 다시 열면 같이 나온다.
/// ★실측(경추ROM)은 ROM 브리지가 같은 일을 하므로 이 컴포넌트는 손대지 않는다 — 두 쪽이 같은 Transform을 쓰면 싸운다.
/// ★우리가 바꾼 것만 우리가 되돌린다: 꺼질 때 진행Root TRS · 캔버스 켜짐을 원래대로.
/// </summary>
[DefaultExecutionOrder(200)]   // InfoWindowArranger(창 이동) 다음에 돈다
public class ProgressGuideDock : MonoBehaviour
{
    [Tooltip("진행 가이드 루트(진행Root)")]
    [SerializeField] private Transform progressRoot;
    [Tooltip("진행 가이드의 캔버스(진행/CanvasRoot) — 이 캔버스를 창 위쪽에 맞춘다")]
    [SerializeField] private RectTransform progressCanvas;
    [Tooltip("진행 현황 창의 캔버스(정보 창 - 진행 및 결과/…/CanvasRoot)")]
    [SerializeField] private RectTransform windowCanvas;
    [Tooltip("창 폭 대비 진행 가이드 폭(0~1)")]
    [SerializeField] private float widthFraction = 0.94f;
    [Tooltip("창 위 가장자리에서 띄우는 거리(창 높이 대비)")]
    [SerializeField] private float topMarginFraction = 0.03f;
    [Tooltip("창보다 사용자 쪽으로 당기는 거리(m) — 겹친 면이 깜빡이지 않게")]
    [SerializeField] private float towardUser = 0.01f;
    [SerializeField] private bool showDebugLog = true;

    private Canvas[] canvases;
    private bool captured, docked, hidden;
    private Transform homeParent;
    private Vector3 homePos, homeScale;
    private Quaternion homeRot;
    private Camera cam;
    private readonly Vector3[] corners = new Vector3[4];

    private void LateUpdate()
    {
        if (progressRoot == null || progressCanvas == null || windowCanvas == null) return;
        if (Measurement()) { Release(); return; }   // 경추ROM 실측 = ROM 브리지 몫

        bool open = windowCanvas.gameObject.activeInHierarchy;
        if (!open) { SetHidden(true); return; }
        Capture();
        SetHidden(false);
        Place();
    }

    private void OnDisable() => Release();

    private static bool Measurement() =>
        ChunaTraining.DifficultyManager.Instance != null && ChunaTraining.DifficultyManager.Instance.IsMeasurementMode;

    private void Capture()
    {
        if (captured) return;
        captured = true;
        homeParent = progressRoot.parent;
        homePos = progressRoot.localPosition; homeRot = progressRoot.localRotation; homeScale = progressRoot.localScale;
        canvases = progressRoot.GetComponentsInChildren<Canvas>(true);
    }

    private void Place()
    {
        // 창 캔버스의 보이는 위쪽 띠
        windowCanvas.GetWorldCorners(corners);   // 0 왼아래 · 1 왼위 · 2 오른위 · 3 오른아래
        Vector3 right = corners[2] - corners[1];
        Vector3 down = corners[0] - corners[1];
        float winW = right.magnitude, winH = down.magnitude;

        // 진행 캔버스 크기(배율 1일 때) — 지금 배율을 빼서 원래 크기를 구한다
        float rootS = Mathf.Max(1e-6f, progressRoot.lossyScale.x);
        float pW = progressCanvas.rect.width * progressCanvas.lossyScale.x / rootS;
        float pH = progressCanvas.rect.height * progressCanvas.lossyScale.y / rootS;
        float k = winW * widthFraction / Mathf.Max(1e-6f, pW);   // 진행Root에 줄 월드 배율

        // 방향: 진행 캔버스 회전 = 창 캔버스 회전
        Quaternion canvasLocal = Quaternion.Inverse(progressRoot.rotation) * progressCanvas.rotation;
        progressRoot.rotation = windowCanvas.rotation * Quaternion.Inverse(canvasLocal);
        Vector3 parentS = progressRoot.parent != null ? progressRoot.parent.lossyScale : Vector3.one;
        progressRoot.localScale = new Vector3(k / parentS.x, k / parentS.y, k / parentS.z);

        // 자리: 진행 캔버스 가운데 = 창 위 가장자리 아래 (여백 + 진행 높이/2) · 사용자 쪽으로 조금
        Vector3 top = (corners[1] + corners[2]) * 0.5f;
        Vector3 target = top + down.normalized * (winH * topMarginFraction + pH * k * 0.5f);
        if (cam == null) cam = Camera.main;
        if (cam != null) target += (cam.transform.position - target).normalized * towardUser;
        Vector3 center = progressCanvas.TransformPoint(progressCanvas.rect.center);
        progressRoot.position += target - center;

        if (!docked)
        {
            docked = true;
            if (showDebugLog) Debug.Log($"[ProgressGuideDock] 진행 가이드를 진행 현황 창 위쪽에 겹침 — 창 {winW:F2}×{winH:F2}m · 진행 {pW * k:F2}×{pH * k:F2}m (배율 {k:F2})");
        }
    }

    private void SetHidden(bool hide)
    {
        if (!captured && hide) Capture();
        if (hidden == hide || canvases == null) return;
        hidden = hide;
        foreach (var c in canvases) if (c != null) c.enabled = !hide;
        if (showDebugLog) Debug.Log($"[ProgressGuideDock] 진행 가이드 {(hide ? "숨김(진행 현황 창 닫힘)" : "보임")}");
    }

    private void Release()
    {
        if (!captured) return;
        if (hidden && canvases != null) foreach (var c in canvases) if (c != null) c.enabled = true;
        hidden = false;
        if (docked && progressRoot != null && progressRoot.parent == homeParent)
        {
            progressRoot.localPosition = homePos; progressRoot.localRotation = homeRot; progressRoot.localScale = homeScale;
        }
        docked = false; captured = false;
    }
}
