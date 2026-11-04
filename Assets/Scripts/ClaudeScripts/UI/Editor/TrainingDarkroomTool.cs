using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 실습 배경 = 어두운 암실 · 벽 없는 열린 공간 · 바닥만 (2026-10-30 사용자 지시 · 베드는 «기존 베드만 남기기»).
/// 방 모델(배경/patient9_2020_1)에서 <b>환자 자리 밑 상자 안에 통째로 들어가는 메시</b>만 베드로 남기고 나머지 메시는 렌더러만 끈다(오브젝트 · 계층은 그대로 — 되돌리기 쉽게).
/// 바닥 = 베드 다리 밑 높이에 큰 판 하나(가운데 옅은 회색 → 가장자리 검정 — 벽 없이 어둠으로 녹는다) · 하늘 = 없음(검정).
/// 배경 오브젝트 아래에 두므로 현실 모드(패스스루)에서 같이 꺼진다(PracticeSettingsController.backgroundObject = 배경).
/// ★베드 판정은 이름이 아니라 자리로 한다 — «후보 보기»로 먼저 확인한다(읽기 전용).
/// </summary>
public static class TrainingDarkroomTool
{
    const string RoomPath = "배경/patient9_2020_1";
    const float BedHalfXZ = 1.3f;     // 환자 자리에서 수평으로 이 안
    const float BedTopAbove = 0.4f;   // 환자 자리보다 이만큼 위까지
    const float FloorRadius = 12f;
    const string FloorName = "암실 바닥";
    const string TexPath = "Assets/Art/Darkroom/darkroom_floor_radial.png";
    const string MatPath = "Assets/Art/Darkroom/DarkroomFloor.mat";

    [MenuItem("GuideChuna/UI 개편/10-30 실습 암실 배경 — 베드 후보 보기 (읽기 전용 · TrainingScene)")]
    public static void Preview() => Run(false);

    [MenuItem("GuideChuna/UI 개편/10-30 실습 암실 배경 적용 (TrainingScene)")]
    public static void Apply() => Run(true);

    static void Run(bool apply)
    {
        var room = GameObject.Find(RoomPath);
        var settings = Object.FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);
        var so = settings != null ? new SerializedObject(settings) : null;
        var target = so?.FindProperty("targetObject").objectReferenceValue as Transform;
        var bg = so?.FindProperty("backgroundObject").objectReferenceValue as GameObject;
        if (room == null || target == null || bg == null) { Debug.LogWarning($"[암실] 방 {(room != null)} · 환자 자리 {(target != null)} · 배경 {(bg != null)} — TrainingScene이 아니다"); return; }

        // ★10-30 실측: targetObject(위치초기화 오브젝트)는 (0, −1, 0) 뿌리라 환자 자리가 아니었다 → 켜진 피부 메시 중 가장 큰 것의 중심
        Vector3 p = target.position; float best = 0f; string who = "위치초기화 오브젝트";
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            float v = smr.bounds.size.x * smr.bounds.size.y * smr.bounds.size.z;
            if (v > best && smr.bounds.size.magnitude < 4f) { best = v; p = smr.bounds.center; who = smr.name; }
        }
        // ★10-30 실측: 방 모델의 켜진 메시는 벽(바닥 포함) · 천장 · 기둥 4 · 창 = 7개뿐이고 베드는 방 밖(환자 쪽)에 있다 → 방 메시는 전부 끄고,
        //   베드는 씬 전체에서 «환자 밑 상자 안» 메시로 찾아 바닥 높이만 잰다(베드는 건드리지 않는다)
        var bed = new List<Renderer>(); var other = new List<Renderer>();
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true))
            if (r.gameObject.activeInHierarchy) other.Add(r);
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.transform.IsChildOf(room.transform) || r.transform.IsChildOf(bg.transform)) continue;
            var b = r.bounds;
            bool inside = b.min.x > p.x - BedHalfXZ && b.max.x < p.x + BedHalfXZ && b.min.z > p.z - BedHalfXZ && b.max.z < p.z + BedHalfXZ && b.max.y < p.y + BedTopAbove && b.size.y > 0.05f;
            if (inside) bed.Add(r);
        }
        // 바닥 높이 = 원래 방 바닥(가장 큰 방 메시 = 바닥 포함 «전체벽»의 아래면) — 베드는 다리 메시가 따로 안 잡혀(10-30 실측) 베드 밑면으로는 못 정한다
        float floorY = p.y - 0.8f, bigV = 0f;
        foreach (var r in other)
        {
            var s = r.bounds.size; float v = s.x * s.y * s.z;
            if (v > bigV) { bigV = v; floorY = r.bounds.min.y; }
        }

        var sb = new StringBuilder($"[암실] {(apply ? "적용" : "후보 보기(읽기 전용)")} — 환자 자리 {p:F2} · 베드 {bed.Count}개 · 끌 메시 {other.Count}개 · 바닥 y {floorY:F2}\n");
        sb.Append($"  기준 = {who}\n");
        foreach (var r in bed) sb.Append($"  베드 {r.name} · 중심 {r.bounds.center:F2} · 크기 {r.bounds.size:F2} · 아래 {r.bounds.min.y:F2}\n");
        foreach (var r in other) sb.Append($"  끔 {r.name} · 중심 {r.bounds.center:F2} · 크기 {r.bounds.size:F2}\n");
        if (!apply) { Debug.Log(sb.ToString()); return; }
        if (bed.Count == 0) { Debug.LogWarning(sb.Append("  ★베드를 못 찾아 적용하지 않는다").ToString()); return; }

        foreach (var r in other) { Undo.RecordObject(r, "암실"); r.enabled = false; }

        // 바닥 — 가운데 옅게 · 가장자리 검정(벽 없이 어둠으로 녹는다). 조명 영향 없는 Unlit(어두운 방 느낌을 일정하게 · 연산 적게)
        var mat = FloorMaterial();
        var floor = bg.transform.Find(FloorName)?.gameObject;
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = FloorName;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(floor, "암실 바닥");
            floor.transform.SetParent(bg.transform, true);
        }
        Undo.RecordObject(floor.transform, "암실 바닥");
        floor.transform.SetPositionAndRotation(new Vector3(p.x, floorY - 0.003f, p.z), Quaternion.Euler(90f, 0f, 0f));
        floor.transform.localScale = Vector3.one;
        var ls = floor.transform.lossyScale;
        floor.transform.localScale = new Vector3(FloorRadius * 2f / ls.x, FloorRadius * 2f / ls.y, 1f / ls.z);
        var mr = floor.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;

        // 하늘 없음 → 카메라가 Skybox로 지워도 검정(현실 모드 끔 = Skybox · 검정 배경 — PracticeSettingsController.OnRealityModeToggle)
        RenderSettings.skybox = null;
        EditorSceneManager.MarkSceneDirty(room.scene);
        Debug.Log(sb.Append($"  → 메시 {other.Count}개 렌더러 끔 · «{FloorName}» 반지름 {FloorRadius}m · 하늘 없음 · ★저장은 Ctrl+S(환자 피부가 분홍이면 저장 금지)").ToString());
    }

    static Material FloorMaterial()
    {
        Directory.CreateDirectory("Assets/Art/Darkroom");
        if (!File.Exists(TexPath))
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = Mathf.Lerp(0.16f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / 0.95f)));   // 가운데 #292929 → 가장자리 검정
                    tex.SetPixel(x, y, new Color(v, v, v));
                }
            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport();
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { mat = new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(mat, MatPath); }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        return mat;
    }
}
