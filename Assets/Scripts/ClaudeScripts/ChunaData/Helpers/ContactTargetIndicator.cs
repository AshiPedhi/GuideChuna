using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 접촉 게이트(PassiveStretch) 구간에서 "환자의 어디를 터치해야 하는지"를 보여주는 런타임 표시구.
///
/// 씬의 접촉 콜라이더(patientHead/Shoulder/ChestCollider)는 렌더러가 없는 SphereCollider라
/// VR에서 전혀 보이지 않는다 → 학습자가 어디에 손을 대야 판정이 걸리는지 알 수 없었다.
/// 여기서는 그 콜라이더의 **실제 판정 범위**를 반투명 구체로 그려준다.
/// 씬 배선이 필요 없도록 런타임에 생성하고, 판정에 끼어들지 않게 콜라이더는 제거한다.
///
/// 색 규칙은 파지점(GripPointTarget)과 동일: 미접촉 = 연한 붉은색 / 접촉 성립 = 초록.
/// </summary>
public class ContactTargetIndicator
{
    private static readonly Color IdleColor = new Color(1f, 0.35f, 0.35f, 0.35f);
    private static readonly Color TouchedColor = new Color(0.25f, 1f, 0.35f, 0.5f);

    private readonly List<GameObject> markers = new List<GameObject>();
    private readonly List<Renderer> markerRenderers = new List<Renderer>();

    private Transform root;
    private Material sourceMaterial;
    private bool anyVisible;

    /// <summary>
    /// 표시구를 현재 접촉 대상 위치·크기에 맞춘다.
    /// </summary>
    /// <param name="targets">활성 접촉 콜라이더들(중복 제거된 상태)</param>
    /// <param name="satisfied">접촉 성립(또는 touchOnce 래치 완료) 여부 → 초록</param>
    /// <param name="scaleMultiplier">표시 크기 배율. 1 = 실제 판정 범위와 동일</param>
    public void UpdateMarkers(List<Collider> targets, bool satisfied, float scaleMultiplier)
    {
        int count = targets != null ? targets.Count : 0;
        if (count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot();
        while (markers.Count < count)
        {
            CreateMarker();
        }

        float mul = Mathf.Max(0.1f, scaleMultiplier);
        Color color = satisfied ? TouchedColor : IdleColor;

        for (int i = 0; i < markers.Count; i++)
        {
            Collider target = i < count ? targets[i] : null;
            bool use = target != null;

            if (markers[i].activeSelf != use)
                markers[i].SetActive(use);
            if (!use) continue;

            // 판정은 콜라이더의 월드 AABB(bounds)로 하므로 표시도 그 기준을 그대로 쓴다.
            Bounds b = target.bounds;
            float diameter = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 2f * mul;

            markers[i].transform.position = b.center;
            markers[i].transform.localScale = new Vector3(diameter, diameter, diameter);

            Renderer r = markerRenderers[i];
            if (r != null)
            {
                r.material.color = color;
                r.material.SetColor("_EmissionColor", color * 0.6f);
            }
        }

        anyVisible = true;
    }

    /// <summary>표시구를 모두 숨긴다(파괴하지 않고 재사용).</summary>
    public void Hide()
    {
        if (!anyVisible) return;
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] != null && markers[i].activeSelf)
                markers[i].SetActive(false);
        }
        anyVisible = false;
    }

    /// <summary>생성한 오브젝트·머티리얼 정리.</summary>
    public void Dispose()
    {
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] != null)
                Object.Destroy(markers[i]);
        }
        markers.Clear();
        markerRenderers.Clear();

        if (root != null)
        {
            Object.Destroy(root.gameObject);
            root = null;
        }
        if (sourceMaterial != null)
        {
            Object.Destroy(sourceMaterial);
            sourceMaterial = null;
        }
        anyVisible = false;
    }

    private void EnsureRoot()
    {
        if (root != null) return;
        var go = new GameObject("[ContactTargetIndicators]");
        root = go.transform;
    }

    private void CreateMarker()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"ContactTarget_{markers.Count}";

        // 표시 전용 — 손 판정이나 물리에 절대 끼어들면 안 된다.
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        go.transform.SetParent(root, false);

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Material src = GetSourceMaterial();
            if (src != null) r.material = src;
        }

        go.SetActive(false);
        markers.Add(go);
        markerRenderers.Add(r);
    }

    /// <summary>
    /// 반투명 머티리얼. Standard 셰이더는 이 프로젝트 머티리얼 267개가 쓰고 있고
    /// 알파블렌드 변형도 38개 머티리얼이 쓰고 있어 빌드에서 스트립되지 않는다
    /// (커스텀 셰이더를 Shader.Find로 찾다가 빌드에서 죽은 xray 전례를 피하려는 선택).
    /// </summary>
    private Material GetSourceMaterial()
    {
        if (sourceMaterial != null) return sourceMaterial;

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            ChunaLogger.LogWarning("<color=orange>[ContactIndicator] Standard 셰이더를 찾지 못해 표시구 머티리얼을 만들 수 없습니다.</color>");
            return null;
        }

        sourceMaterial = new Material(shader) { name = "ContactTargetIndicatorMat" };
        // Standard - Fade(반투명) 설정
        sourceMaterial.SetFloat("_Mode", 3f);
        sourceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        sourceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        sourceMaterial.SetInt("_ZWrite", 0);
        sourceMaterial.DisableKeyword("_ALPHATEST_ON");
        sourceMaterial.EnableKeyword("_ALPHABLEND_ON");
        sourceMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        sourceMaterial.EnableKeyword("_EMISSION");
        sourceMaterial.renderQueue = 3000;
        sourceMaterial.SetFloat("_Glossiness", 0f);

        return sourceMaterial;
    }
}
