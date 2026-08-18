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
    // 팔처럼 긴 부위는 표시구가 커서 0.35만 돼도 "통째로 빨간 덩어리"로 보인다 → 더 옅게.
    private static readonly Color IdleColor = new Color(1f, 0.35f, 0.35f, 0.18f);
    // ★닿으면 흐려진다 — 파지점(GripPointTarget.grippedAlpha)과 같은 규칙.
    //   손을 댄 뒤에도 진한 구체가 남아 있으면 손과 시술 부위를 가린다.
    private static readonly Color TouchedColor = new Color(0.25f, 1f, 0.35f, 0.12f);

    private readonly List<GameObject> markers = new List<GameObject>();
    private readonly List<Renderer> markerRenderers = new List<Renderer>();
    private readonly List<PrimitiveType> markerKinds = new List<PrimitiveType>();

    private Transform root;
    private Material sourceMaterial;
    private static bool materialStateLogged;
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
            CreateMarker(PrimitiveType.Sphere);
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

            // ★캡슐(팔 등 길쭉한 부위)을 AABB 기준 구로 그리면 지름 0.5m짜리 공이 되어
            //   실제 판정 범위보다 훨씬 크고 보기 흉하다 → 캡슐은 캡슐 모양 그대로 그린다.
            var capsule = target as CapsuleCollider;
            PrimitiveType want = capsule != null ? PrimitiveType.Capsule : PrimitiveType.Sphere;
            if (markerKinds[i] != want)
                ReplaceMarker(i, want);

            if (capsule != null)
            {
                ApplyCapsuleShape(markers[i].transform, capsule, mul);
            }
            else
            {
                // 판정은 콜라이더의 월드 AABB(bounds)로 하므로 표시도 그 기준을 그대로 쓴다.
                Bounds b = target.bounds;
                float diameter = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 2f * mul;

                markers[i].transform.rotation = Quaternion.identity;
                markers[i].transform.position = b.center;
                markers[i].transform.localScale = new Vector3(diameter, diameter, diameter);
            }

            Renderer r = markerRenderers[i];
            if (r != null)
            {
                // Sprites/Default는 _Color 하나로 색과 알파가 정해진다(발광 속성 없음).
                r.material.color = color;
            }
        }

        anyVisible = true;
    }

    /// <summary>
    /// 캡슐 콜라이더의 실제 모양(중심·방향·반지름·길이)을 표시구에 그대로 옮긴다.
    /// Unity 캡슐 프리미티브는 기본 높이 2·지름 1이라 스케일이 (2r, h/2, 2r)이 된다.
    /// </summary>
    private static void ApplyCapsuleShape(Transform marker, CapsuleCollider capsule, float mul)
    {
        Transform t = capsule.transform;
        Vector3 ls = t.lossyScale;

        Vector3 dirAxis;
        float heightScale, radiusScale;
        switch (capsule.direction)
        {
            case 0:   // X축
                dirAxis = Vector3.right;
                heightScale = Mathf.Abs(ls.x);
                radiusScale = Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                break;
            case 2:   // Z축
                dirAxis = Vector3.forward;
                heightScale = Mathf.Abs(ls.z);
                radiusScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
                break;
            default:  // Y축
                dirAxis = Vector3.up;
                heightScale = Mathf.Abs(ls.y);
                radiusScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
                break;
        }

        float radius = capsule.radius * radiusScale * mul;
        // 캡슐 높이는 양 끝 반구를 포함한 전체 길이이며, 반지름 2배보다 작아질 수 없다.
        float height = Mathf.Max(capsule.height * heightScale, radius * 2f) * mul;

        marker.position = t.TransformPoint(capsule.center);
        marker.rotation = t.rotation * Quaternion.FromToRotation(Vector3.up, dirAxis);
        marker.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
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
        markerKinds.Clear();

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

    /// <summary>대상 콜라이더 모양이 바뀌면(구↔캡슐) 해당 슬롯의 표시구를 새로 만든다.</summary>
    private void ReplaceMarker(int index, PrimitiveType kind)
    {
        if (markers[index] != null)
            Object.Destroy(markers[index]);

        markers.RemoveAt(index);
        markerRenderers.RemoveAt(index);
        markerKinds.RemoveAt(index);

        CreateMarker(kind, index);
        markers[index].SetActive(true);
    }

    private void CreateMarker(PrimitiveType kind, int insertAt = -1)
    {
        var go = GameObject.CreatePrimitive(kind);
        go.name = $"ContactTarget_{(insertAt >= 0 ? insertAt : markers.Count)}";

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

            // ★계측(2026-08-18): "표시구가 불투명하다"가 반복되는데 원인을 정적으로 좁히지 못했다.
            //   실제 런타임 상태를 한 번만 찍어 둔다 — 셰이더가 무엇인지, 알파블렌드가 켜졌는지,
            //   렌더큐가 투명 구간(3000)인지가 여기 다 나온다. 다음 Play 로그로 원인이 확정된다.
            if (!materialStateLogged && r != null && r.sharedMaterial != null)
            {
                materialStateLogged = true;
                Material m = r.sharedMaterial;
                ChunaLogger.Log($"<color=cyan>[ContactIndicator] 표시구 머티리얼 상태 — " +
                                $"shader={m.shader?.name} / renderQueue={m.renderQueue} / " +
                                $"_ALPHABLEND_ON={m.IsKeywordEnabled("_ALPHABLEND_ON")} / " +
                                $"_ZWrite={(m.HasProperty("_ZWrite") ? m.GetInt("_ZWrite").ToString() : "없음")} / " +
                                $"_Mode={(m.HasProperty("_Mode") ? m.GetFloat("_Mode").ToString("0") : "없음")} / " +
                                $"color.a={m.color.a:F2}</color>");
            }
        }

        go.SetActive(false);
        if (insertAt >= 0)
        {
            markers.Insert(insertAt, go);
            markerRenderers.Insert(insertAt, r);
            markerKinds.Insert(insertAt, kind);
        }
        else
        {
            markers.Add(go);
            markerRenderers.Add(r);
            markerKinds.Add(kind);
        }
    }

    /// <summary>
    /// 반투명 머티리얼. Standard 셰이더는 이 프로젝트 머티리얼 267개가 쓰고 있고
    /// 알파블렌드 변형도 38개 머티리얼이 쓰고 있어 빌드에서 스트립되지 않는다
    /// (커스텀 셰이더를 Shader.Find로 찾다가 빌드에서 죽은 xray 전례를 피하려는 선택).
    /// </summary>
    private Material GetSourceMaterial()
    {
        if (sourceMaterial != null) return sourceMaterial;

        // ★2026-08-18 전면 교체 — Standard + Fade 조합을 버린다.
        //   "표시구가 불투명한 덩어리로 보인다"가 반복됐다. Standard를 반투명으로 만들려면
        //   _Mode·_SrcBlend·_DstBlend·_ZWrite·키워드·renderQueue를 <b>전부</b> 맞춰야 하고,
        //   하나라도 어긋나거나 알파블렌드 배리언트가 빌드에서 스트립되면 <b>조용히 불투명</b>이 된다.
        //   런타임에 만든 머티리얼은 어떤 머티리얼 에셋도 참조하지 않아 그 위험이 특히 크다.
        //
        //   Sprites/Default는 셰이더 자체가 알파 블렌드(Blend SrcAlpha OneMinusSrcAlpha,
        //   ZWrite Off)로 고정돼 있어 <b>맞출 상태가 없다</b>. _Color 하나로 색과 알파가 정해지고,
        //   UI가 늘 쓰기 때문에 빌드에서 빠질 일도 없다. 표시구는 위치만 알리면 되므로
        //   조명·발광이 필요 없어 무광(Unlit)인 점도 오히려 알맞다.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            sourceMaterial = new Material(shader) { name = "ContactTargetIndicatorMat" };
            sourceMaterial.renderQueue = 3000;   // 불투명 뒤에 그린다
            return sourceMaterial;
        }

        // 폴백 — Sprites/Default조차 없으면 예전 방식(Standard Fade)을 쓴다.
        ChunaLogger.LogWarning("<color=orange>[ContactIndicator] Sprites/Default를 찾지 못해 " +
                               "Standard Fade로 대체합니다(불투명하게 보일 수 있음).</color>");
        shader = Shader.Find("Standard");
        if (shader == null)
        {
            ChunaLogger.LogWarning("<color=orange>[ContactIndicator] Standard 셰이더도 없어 표시구 머티리얼을 만들 수 없습니다.</color>");
            return null;
        }
        sourceMaterial = new Material(shader) { name = "ContactTargetIndicatorMat" };
        sourceMaterial.SetFloat("_Mode", 3f);
        sourceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        sourceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        sourceMaterial.SetInt("_ZWrite", 0);
        sourceMaterial.DisableKeyword("_ALPHATEST_ON");
        sourceMaterial.EnableKeyword("_ALPHABLEND_ON");
        sourceMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        sourceMaterial.renderQueue = 3000;
        sourceMaterial.SetFloat("_Glossiness", 0f);
        return sourceMaterial;
    }
}
