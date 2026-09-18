// ★[에이전트 브리지 — 씬 편집·캡처·재생] 2026-09-18 신설
//
// 사용자 허가(2026-09-18): "새로 만들게 되는 씬에 대한 권한은 다 허가해 줄게."
// → 브리지가 <b>scene-copy로 새로 만든 씬</b>만 "소유 씬"으로 기록하고,
//   소유 씬에서만 삭제·추가·저장을 허용한다.
// ★★기존 씬(TrainingScene·lobby 등)은 종전 규칙 그대로다 — 값 수정까지만, 저장·삭제 불가.
//   이유: 절대규칙 2(분홍 피부 상태로 저장하면 디스크 손상, 07-27 전례). 사람이 Ctrl+S 하는 단계를 안 없앤다.
// ★소유 목록은 EditorPrefs에 남는다 — 도메인 리로드·에디터 재시작 뒤에도 유지된다.
// ★scene-copy는 <b>대상이 이미 있으면 거부</b>한다. 덮어쓰는 경로가 없다(절대규칙 3 — 파괴성 먼저).
// ★모든 편집은 Undo가 걸린다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class ChunaAgentBridge
{
    private const string OwnedKey = "ChunaAgentBridge.OwnedScenes";

    private static bool TrySceneCommand(string cmd, StringBuilder sb, Dictionary<string, string> a,
                                        out bool ok, out string err)
    {
        ok = true;
        err = null;
        switch (cmd)
        {
            case "scene-list": DoSceneList(sb); return true;
            case "scene-copy": ok = DoSceneCopy(sb, a, out err); return true;
            case "scene-open": ok = DoSceneOpen(sb, a, out err); return true;
            case "scene-save": ok = DoSceneSave(sb, a, out err); return true;
            case "delete": ok = DoDelete(sb, a, out err); return true;
            case "add-go": ok = DoAddGo(sb, a, out err); return true;
            case "add-comp": ok = DoAddComp(sb, a, out err); return true;
            case "build-add": ok = DoBuildAdd(sb, a, out err); return true;
            case "capture": ok = DoCapture(sb, a, out err); return true;
            case "play": ok = DoPlay(sb, true, out err); return true;
            case "stop": ok = DoPlay(sb, false, out err); return true;
        }
        return false;
    }

    // ── 소유 씬 ─────────────────────────────────────────────────────────
    private static HashSet<string> Owned()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in EditorPrefs.GetString(OwnedKey, "").Split(';'))
            if (p.Length > 0) set.Add(p);
        return set;
    }

    private static void AddOwned(string path)
    {
        var set = Owned();
        set.Add(Norm(path));
        EditorPrefs.SetString(OwnedKey, string.Join(";", set));
    }

    private static bool IsOwned(Scene s) => s.IsValid() && Owned().Contains(Norm(s.path));

    private static string Norm(string p) => (p ?? "").Replace('\\', '/').Trim();

    private static bool RequireOwned(GameObject go, out string err)
    {
        err = null;
        if (IsOwned(go.scene)) return true;
        err = $"'{go.scene.name}'은(는) 브리지가 만든 씬이 아니다 — 기존 씬은 삭제·추가·저장을 안 한다. " +
              "값 수정은 set으로 한다.";
        return false;
    }

    // ── scene-list ──────────────────────────────────────────────────────
    private static void DoSceneList(StringBuilder sb)
    {
        var owned = Owned();
        sb.Append("열린 씬\n");
        var active = SceneManager.GetActiveScene();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            sb.Append("  ").Append(s == active ? "▶ " : "  ").Append(s.name).Append("   ").Append(s.path)
              .Append(s.isDirty ? "   ★저장 안 한 변경 있음" : "")
              .Append(owned.Contains(Norm(s.path)) ? "   [브리지 소유]" : "")
              .Append('\n');
        }
        sb.Append("\n브리지 소유 씬: ").Append(owned.Count == 0 ? "없음" : string.Join(", ", owned)).Append('\n');
        sb.Append("\nBuild Settings\n");
        var list = EditorBuildSettings.scenes;
        for (int i = 0; i < list.Length; i++)
            sb.Append("  ").Append(i).Append(". ").Append(list[i].enabled ? "" : "(꺼짐) ").Append(list[i].path).Append('\n');
    }

    // ── scene-copy ──────────────────────────────────────────────────────
    private static bool DoSceneCopy(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string src = Norm(Get(a, "src", ""));
        string dst = Norm(Get(a, "dst", ""));
        if (src.Length == 0 || dst.Length == 0) { err = "src=와 dst=가 둘 다 필요하다."; return false; }
        if (!dst.StartsWith("Assets/Scenes/") || !dst.EndsWith(".unity"))
        {
            err = "dst는 Assets/Scenes/…/이름.unity 여야 한다."; return false;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(src) == null) { err = $"원본 씬이 없다: {src}"; return false; }

        // ★덮어쓰지 않는다. 있으면 무조건 멈춘다.
        string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, dst);
        if (File.Exists(full) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst) != null)
        {
            err = $"대상이 이미 있다 — 덮어쓰지 않는다: {dst}"; return false;
        }

        if (!AssetDatabase.CopyAsset(src, dst)) { err = "CopyAsset이 실패했다."; return false; }
        AddOwned(dst);
        sb.Append($"복사했다: {src} → {dst}\n브리지 소유 씬으로 기록했다 — 이 씬만 삭제·추가·저장을 허용한다.\n");
        return true;
    }

    // ── scene-open ──────────────────────────────────────────────────────
    private static bool DoSceneOpen(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        if (EditorApplication.isPlaying) { err = "Play 중에는 씬을 열지 않는다."; return false; }
        string path = Norm(Get(a, "path", ""));
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { err = $"씬이 없다: {path}"; return false; }
        bool additive = Get(a, "mode", "single") == "additive";

        if (!additive)
        {
            // ★저장 안 한 변경이 있는 씬을 닫지 않는다 — 사람이 한 작업을 날릴 수 있다.
            var dirty = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty) dirty.Add(s.name);
            }
            if (dirty.Count > 0)
            {
                err = "저장 안 한 변경이 있는 씬이 열려 있어 닫지 않는다: " + string.Join(", ", dirty) +
                      " — 사람이 저장하거나 버린 뒤에 다시."; return false;
            }
        }

        var opened = EditorSceneManager.OpenScene(path, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
        if (additive) SceneManager.SetActiveScene(opened);
        sb.Append($"열었다: {opened.name} ({(additive ? "추가" : "단독")})" +
                  (IsOwned(opened) ? " [브리지 소유]" : " — 기존 씬이라 값 수정까지만 한다") + "\n");
        return true;
    }

    // ── scene-save ──────────────────────────────────────────────────────
    private static bool DoSceneSave(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        if (EditorApplication.isPlaying) { err = "Play 중에는 저장하지 않는다."; return false; }
        string path = Norm(Get(a, "path", SceneManager.GetActiveScene().path));
        var s = SceneManager.GetSceneByPath(path);
        if (!s.IsValid() || !s.isLoaded) { err = $"열려 있지 않은 씬이다: {path}"; return false; }
        if (!IsOwned(s)) { err = $"브리지가 만든 씬이 아니라 저장하지 않는다: {path} — 사람이 Ctrl+S 한다."; return false; }
        if (!EditorSceneManager.SaveScene(s)) { err = "SaveScene이 실패했다."; return false; }
        sb.Append($"저장했다: {path}\n");
        return true;
    }

    // ── delete ──────────────────────────────────────────────────────────
    private static bool DoDelete(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string path = Get(a, "path", "");
        var go = FindByPath(path);
        if (go == null) { err = $"그 경로에 오브젝트가 없다: {path}"; return false; }
        if (!RequireOwned(go, out err)) return false;
        if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
        {
            err = $"프리팹 인스턴스 안쪽 오브젝트라 지울 수 없다: {path} — 끄는 것(GameObject|active|false)으로 대신한다.";
            return false;
        }
        var scene = go.scene;
        int count = go.GetComponentsInChildren<Transform>(true).Length;
        Undo.DestroyObjectImmediate(go);
        EditorSceneManager.MarkSceneDirty(scene);
        sb.Append($"지웠다: {path} (하위 포함 {count}개)\n");
        return true;
    }

    // ── add-go ──────────────────────────────────────────────────────────
    private static bool DoAddGo(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string name = Get(a, "name", "");
        if (name.Length == 0) { err = "name=이 필요하다."; return false; }

        string parentPath = Get(a, "parent", "");
        GameObject parent = null;
        Scene scene;
        if (parentPath.Length > 0)
        {
            parent = FindByPath(parentPath);
            if (parent == null) { err = $"부모가 없다: {parentPath}"; return false; }
            scene = parent.scene;
        }
        else scene = SceneManager.GetActiveScene();

        if (!IsOwned(scene)) { err = $"'{scene.name}'은(는) 브리지가 만든 씬이 아니라 오브젝트를 만들지 않는다."; return false; }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "브리지 add-go");
        if (parent != null) Undo.SetTransformParent(go.transform, parent.transform, "브리지 add-go");
        else SceneManager.MoveGameObjectToScene(go, scene);

        string pos = Get(a, "pos", "");
        if (pos.Length > 0)
        {
            if (!Nums(pos, 3, out var v)) { err = "pos는 \"x,y,z\"다."; return false; }
            go.transform.localPosition = new Vector3(v[0], v[1], v[2]);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        sb.Append($"만들었다: {PathOf(go.transform)}\n");
        return true;
    }

    // ── add-comp ────────────────────────────────────────────────────────
    private static bool DoAddComp(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string path = Get(a, "path", "");
        string typeName = Get(a, "type", "");
        var go = FindByPath(path);
        if (go == null) { err = $"그 경로에 오브젝트가 없다: {path}"; return false; }
        if (!RequireOwned(go, out err)) return false;

        var hits = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types)
                if (typeof(Component).IsAssignableFrom(t) && !t.IsAbstract && (t.Name == typeName || t.FullName == typeName))
                    hits.Add(t);
        }
        if (hits.Count == 0) { err = $"그런 컴포넌트 타입이 없다: {typeName} (컴파일이 끝났는지, refresh를 했는지 본다)"; return false; }
        if (hits.Count > 1) { err = "이름이 겹친다 — 전체 이름으로 준다: " + string.Join(", ", hits.Select(t => t.FullName)); return false; }

        if (go.GetComponent(hits[0]) != null) { sb.Append($"이미 붙어 있다: {typeName} @ {path}\n"); return true; }
        var c = Undo.AddComponent(go, hits[0]);
        if (c == null) { err = "AddComponent가 실패했다(RequireComponent 충돌 등)."; return false; }
        EditorSceneManager.MarkSceneDirty(go.scene);
        sb.Append($"붙였다: {hits[0].FullName} @ {path}\n");
        return true;
    }

    // ── build-add ───────────────────────────────────────────────────────
    private static bool DoBuildAdd(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string path = Norm(Get(a, "path", ""));
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { err = $"씬이 없다: {path}"; return false; }
        if (!Owned().Contains(path)) { err = "브리지가 만든 씬만 Build Settings에 넣는다."; return false; }

        var list = EditorBuildSettings.scenes.ToList();
        var hit = list.FirstOrDefault(s => Norm(s.path) == path);
        if (hit != null)
        {
            sb.Append($"이미 들어 있다: {path}" + (hit.enabled ? "" : " (꺼져 있다)") + "\n");
            SaveBuildSettings(sb, path);
            return true;
        }
        list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
        sb.Append($"Build Settings에 넣었다: {path} (#{list.Count - 1})\n");
        SaveBuildSettings(sb, path);
        return true;
    }

    /// <summary>
    /// ★Build Settings는 메모리에만 바뀌고 "프로젝트 저장" 때 파일로 써진다(09-18 실측 — 넣었는데 파일엔 없었다).
    ///   전체 저장(SaveAssets)은 다른 dirty 에셋까지 같이 쓰므로 안 한다. <b>이 파일만</b> 골라 저장한다.
    /// </summary>
    private static void SaveBuildSettings(StringBuilder sb, string path)
    {
        const string file = "ProjectSettings/EditorBuildSettings.asset";
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(file))
            if (o != null) AssetDatabase.SaveAssetIfDirty(o);
        string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, file);
        bool onDisk = File.Exists(full) && File.ReadAllText(full).Contains(path);
        sb.Append(onDisk ? "파일에도 저장됐다: " + file + "\n"
                         : "★파일에는 아직 없다 — 사람이 File > Save Project 해야 한다: " + file + "\n");
    }

    // ── capture ─────────────────────────────────────────────────────────
    // view=scene(기본)|game · label=이름 · frame=오브젝트경로(scene뷰를 그 오브젝트에 맞춘다) · w=,h=
    // ★저장 위치는 <프로젝트>/Captures/yyyy-MM-dd/ — Assets 밖이라 임포트·meta가 안 생긴다.
    private static bool DoCapture(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        string view = Get(a, "view", "scene");
        string label = Get(a, "label", view);
        foreach (char ch in Path.GetInvalidFileNameChars()) label = label.Replace(ch, '_');

        string root = Directory.GetParent(Application.dataPath).FullName;
        string dir = Path.Combine(root, "Captures", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, DateTime.Now.ToString("HHmmss") + "_" + label + ".png");

        if (view == "game" && EditorApplication.isPlaying)
        {
            // ★Play 중에는 Game 뷰를 그대로 찍는다. 프레임 끝에 비동기로 써진다.
            ScreenCapture.CaptureScreenshot(file);
            sb.Append($"Game 뷰를 찍는다(프레임 끝에 저장): {file}\n");
            return true;
        }

        Camera cam;
        if (view == "game")
        {
            cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) { err = "찍을 카메라가 없다."; return false; }
        }
        else
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) { err = "열린 Scene 뷰가 없다."; return false; }
            string frame = Get(a, "frame", "");
            if (frame.Length > 0)
            {
                var go = FindByPath(frame);
                if (go == null) { err = $"frame 대상이 없다: {frame}"; return false; }
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = new Bounds(go.transform.position, Vector3.one * 0.5f);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sv.Frame(b, true);
                sv.Repaint();
            }
            cam = sv.camera;
        }

        int w = GetInt(a, "w", 1600), h = GetInt(a, "h", 900);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
        sb.Append($"찍었다({view}): {file}\n");
        return true;
    }

    // ── play / stop ─────────────────────────────────────────────────────
    private static bool DoPlay(StringBuilder sb, bool on, out string err)
    {
        err = null;
        if (EditorApplication.isPlaying == on)
        {
            sb.Append(on ? "이미 Play 중이다.\n" : "이미 멈춰 있다.\n");
            return true;
        }
        // ★응답을 먼저 쓰고 다음 틱에 전환한다 — 전환 중 도메인 리로드로 응답이 끊기지 않게.
        EditorApplication.delayCall += () => EditorApplication.isPlaying = on;
        sb.Append(on ? "Play를 시작한다. 로그는 Editor.log에서 본다.\n" : "Play를 멈춘다.\n");
        return true;
    }
}
