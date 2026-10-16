using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ★읽기 전용 — 환자(c9) 리그와 골격(AnatomyV2_3) 리그를 나란히 뜯어 본다(2026-10-16 신설).
///
/// 사용자: «골격 본을 환자 본 밑에 축을 맞춰 넣으면 따라가는데, 둘이 구조가 달라 대응 본을 찾는 게 일이다».
/// .meta의 Humanoid 대응표만으로는 «어느 본이 어느 뼈 메시를 움직이나»를 모른다 → FBX 프리팹을 열어
/// SkinnedMeshRenderer마다 메시 이름 · 그 메시를 움직이는 본 목록 · 본의 모델 공간 자리를 뽑는다.
///
/// 결과: Temp/rig_analysis/ 아래 TSV 넷(골격·환자 × 본·스킨) + 요약 글자. 씬 · 에셋은 고치지 않는다.
/// 부르는 길: 메뉴 GuideChuna/리그 분석 (환자 ↔ 골격) · 브리지 «rig-analysis».
/// </summary>
public static class SkeletonRigAnalysis
{
    private const string SkeletonPath = "Assets/Models/AnatomyV3/AnatomyV2_3.fbx";
    private const string PatientPath = "Assets/MedicXR CPX PhysicalExamination's PatientModel v20221111/Character/c9/c9.fbx";

    // 뼈 메시를 고를 낱말(소문자 비교)
    private static readonly string[] BoneWords = { "vert", "cervic", "thorac", "lumb", "rib", "costa", "stern", "clavic", "scapul", "humer", "atlas", "axis", "spine", "skull", "c1", "c2", "c7", "t1", "t2", "t4", "bone" };

    [MenuItem("GuideChuna/리그 분석 (환자 ↔ 골격 · 읽기 전용)")]
    private static void Menu() { Debug.Log(Run("")); }

    public static string Run(string arg)
    {
        var sb = new StringBuilder();
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "rig_analysis");
        Directory.CreateDirectory(dir);
        Dump("골격", SkeletonPath, dir, "skeleton", sb, true);
        Dump("환자", PatientPath, dir, "patient", sb, false);
        sb.AppendLine("TSV: " + dir);
        return sb.ToString();
    }

    private static void Dump(string label, string path, string dir, string key, StringBuilder sb, bool listBoneMeshes)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (root == null) { sb.AppendLine($"★{label} — {path} 를 못 읽었다"); return; }

        // 본(Transform) 전부: 이름 · 부모 · 모델 공간 위치 · 회전(오일러) · 로컬 스케일
        var bones = new StringBuilder("이름\t부모\t깊이\tx\ty\tz\trx\try\trz\tsx\tsy\tsz\n");
        int n = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            int depth = 0; for (Transform p = t.parent; p != null; p = p.parent) depth++;
            Vector3 w = root.transform.InverseTransformPoint(t.position), e = t.rotation.eulerAngles, s = t.localScale;
            bones.Append(t.name).Append('\t').Append(t.parent ? t.parent.name : "").Append('\t').Append(depth)
                 .Append('\t').Append(w.x.ToString("F4")).Append('\t').Append(w.y.ToString("F4")).Append('\t').Append(w.z.ToString("F4"))
                 .Append('\t').Append(e.x.ToString("F1")).Append('\t').Append(e.y.ToString("F1")).Append('\t').Append(e.z.ToString("F1"))
                 .Append('\t').Append(s.x.ToString("F3")).Append('\t').Append(s.y.ToString("F3")).Append('\t').Append(s.z.ToString("F3")).Append('\n');
            n++;
        }
        File.WriteAllText(Path.Combine(dir, key + "_bones.tsv"), bones.ToString(), new UTF8Encoding(true));

        // 스킨: 렌더러마다 메시 · 루트 본 · 움직이는 본
        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var skin = new StringBuilder("렌더러\t메시\t루트본\t본수\t본들\n");
        var boneUse = new Dictionary<string, List<string>>();
        foreach (var r in smrs)
        {
            var names = r.bones.Where(b => b != null).Select(b => b.name).ToArray();
            skin.Append(r.name).Append('\t').Append(r.sharedMesh ? r.sharedMesh.name : "").Append('\t').Append(r.rootBone ? r.rootBone.name : "")
                .Append('\t').Append(names.Length).Append('\t').Append(string.Join(", ", names)).Append('\n');
            foreach (var b in names) { if (!boneUse.TryGetValue(b, out var l)) boneUse[b] = l = new List<string>(); l.Add(r.name); }
        }
        File.WriteAllText(Path.Combine(dir, key + "_skin.tsv"), skin.ToString(), new UTF8Encoding(true));

        var anim = root.GetComponent<Animator>();
        sb.AppendLine($"== {label} — 본 {n} · 스킨 렌더러 {smrs.Length} · 아바타 {(anim && anim.avatar ? (anim.avatar.isHuman ? "Humanoid" : "Generic") : "없음")}");
        if (!listBoneMeshes) return;

        // 뼈로 보이는 렌더러만 요약(이름 낱말)
        int shown = 0;
        foreach (var r in smrs)
        {
            string nm = (r.name + " " + (r.sharedMesh ? r.sharedMesh.name : "")).ToLowerInvariant();
            if (!BoneWords.Any(w => nm.Contains(w))) continue;
            var names = r.bones.Where(b => b != null).Select(b => b.name).Distinct().ToArray();
            sb.AppendLine($"  {r.name} · 본 {names.Length}: {string.Join(", ", names.Take(8))}{(names.Length > 8 ? " …" : "")}");
            if (++shown >= 80) { sb.AppendLine("  …(이하 TSV)"); break; }
        }
    }
}
