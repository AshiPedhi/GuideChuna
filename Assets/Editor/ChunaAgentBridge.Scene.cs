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
            case "gameview": ok = DoGameView(sb, a, out err); return true;
            case "resolve": ok = DoResolvePackages(sb, out err); return true;
            case "build-run": ok = DoBuildRun(sb, a, out err); return true;
            case "gfx-api": ok = DoGfxApi(sb, a, out err); return true;
            case "romline-ctrl": sb.Append(RomLineVerify.ControllerFront()); return true;   // 컨트롤러 모형의 맨 앞 자리를 잰다(10-01)
            case "romline-verify": sb.Append(RomLineVerify.Run()); return true;   // 선 실측 계산 검증(10-01) — 런타임과 같은 함수를 탄다
            case "romline-model": sb.Append(RomLineModelSim.Run(a)); return true;   // 환자 모델로 «직접 ↔ 면» 시뮬레이션(10-02) — 씬은 미리보기로 읽기만 한다
        }
        return false;
    }

    // ── Android 그래픽 API·폐색 기능 전환(2026-10-01 — 미러링 검은 화면이 Vulkan 탓인지 대조 빌드로 가르려고) ──
    // ★인자 없이 부르면 <b>지금 값만</b> 읽는다. --api=gles3|vulkan, --occlusion=on|off 를 준 것만 바꾼다.
    // ★폐색(Meta Depth API)은 Vulkan 전용이다 — GLES3로 내릴 땐 --occlusion=off 를 같이 줘야 OpenXR 빌드 검증에 안 걸린다.
    private static bool DoGfxApi(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;

        if (a.TryGetValue("api", out var api))
        {
            UnityEngine.Rendering.GraphicsDeviceType t;
            switch (api.ToLowerInvariant())
            {
                case "gles3": t = UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3; break;
                case "vulkan": t = UnityEngine.Rendering.GraphicsDeviceType.Vulkan; break;
                default: err = $"--api 는 gles3 또는 vulkan 이다(받은 값: {api})"; return false;
            }
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { t });
        }

        var xr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        bool wantOcc = a.TryGetValue("occlusion", out var occ);
        bool occOn = wantOcc && (occ == "on" || occ == "1" || occ == "true");

        sb.Append("Android 그래픽 API: ")
          .AppendLine(string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(x => x.ToString())));

        if (xr == null) { sb.AppendLine("★Android OpenXR 설정을 못 찾았다 — 폐색 기능은 안 건드렸다."); }
        else
        {
            foreach (var f in xr.GetFeatures())
            {
                if (f == null || f.GetType().Name != "AROcclusionFeature") continue;
                if (wantOcc && f.enabled != occOn)
                {
                    f.enabled = occOn;
                    EditorUtility.SetDirty(f);
                    EditorUtility.SetDirty(xr);
                }
                sb.Append("OpenXR 폐색 기능(AROcclusionFeature Android): ").AppendLine(f.enabled ? "켜짐" : "꺼짐");
            }
        }

        if (a.ContainsKey("api") || wantOcc) AssetDatabase.SaveAssets();
        return true;
    }

    // ── 빌드 후 기기 실행(2026-09-29 사용자: "빌드해서 디바이스에 RUN까지 해 줘") ──────────
    // ★에디터가 열려 있으면 batchmode 빌드는 프로젝트 락으로 실패한다(CLAUDE.md) — 그래서 열린 에디터 안에서 돌린다.
    // ★Build Settings의 <b>켜진 씬</b>·마지막 빌드 경로·Development 여부를 <b>그대로</b> 쓴다(File > Build And Run과 같다).
    //   빌드는 몇 분 걸려 에디터를 붙잡으므로 응답은 즉시 «시작»으로 내고, 결과는 로그와 build-result.txt에 남긴다.
    private static string BuildResultPath => Path.Combine(Dir, "build-result.txt");

    // ★--scenes="A.unity;B.unity"로 <b>이번 빌드에만</b> 씬 목록을 준다(Build Settings 파일은 안 건드린다, 09-29).
    //   --path=로 출력 apk 경로를 준다(주면 마지막 빌드 경로를 안 바꾼다 — BuildPipeline은 그 설정을 고치지 않는다).
    private static bool DoBuildRun(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        if (EditorApplication.isPlaying) { err = "Play 중이다 — 멈춘 뒤 빌드한다."; return false; }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        { err = $"빌드 대상이 Android가 아니다({EditorUserBuildSettings.activeBuildTarget})."; return false; }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            { err = $"저장 안 된 씬이 있다({SceneManager.GetSceneAt(i).name}) — 빌드는 디스크의 씬을 쓴다. 먼저 저장한다."; return false; }
        string sceneArg = Get(a, "scenes", "");
        string[] scenes = string.IsNullOrWhiteSpace(sceneArg)
            ? EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray()
            : sceneArg.Split(';').Select(s => Norm(s.Trim())).Where(s => s.Length > 0).ToArray();
        if (scenes.Length == 0) { err = "빌드할 씬이 없다(Build Settings에 켜진 씬도, --scenes도 없다)."; return false; }
        foreach (var sp in scenes)
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sp) == null) { err = $"씬이 없다: {sp}"; return false; }
        string path = Get(a, "path", "");
        if (string.IsNullOrEmpty(path)) path = EditorUserBuildSettings.GetBuildLocation(BuildTarget.Android);
        if (string.IsNullOrEmpty(path)) { err = "마지막 빌드 경로가 없다 — 에디터에서 한 번 Build 해 경로를 정한다."; return false; }

        var opt = BuildOptions.AutoRunPlayer;
        if (EditorUserBuildSettings.development) opt |= BuildOptions.Development;
        try { File.Delete(BuildResultPath); } catch (Exception) { }

        sb.Append("빌드를 시작한다(끝나면 기기에서 실행) — ").Append(path).Append('\n');
        sb.Append("  씬 ").Append(scenes.Length).Append("개: ").Append(string.Join(", ", scenes)).Append('\n');
        sb.Append("  Development ").Append(EditorUserBuildSettings.development ? "켬" : "끔").Append('\n');
        sb.Append("  결과는 Temp/chuna-bridge/build-result.txt 와 Editor.log «[에이전트브리지] 빌드 끝»\n");

        // ★응답을 먼저 쓰게 한 틱 미룬다.
        EditorApplication.delayCall += () =>
        {
            string line;
            try
            {
                Debug.Log($"[에이전트브리지] 빌드 시작 — {path} · 씬 {string.Join(", ", scenes)}");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = path, target = BuildTarget.Android, options = opt,
                });
                var s = report.summary;
                line = $"{s.result} · {s.totalTime.TotalSeconds:F0}초 · 오류 {s.totalErrors} · 경고 {s.totalWarnings} · {s.totalSize / 1048576.0:F0}MB · {path}";
            }
            catch (Exception e)
            {
                line = "예외 — " + e.GetType().Name + ": " + e.Message;
            }
            Debug.Log("[에이전트브리지] 빌드 끝 — " + line);
            try { File.WriteAllText(BuildResultPath, line, new UTF8Encoding(false)); } catch (Exception) { }
        };
        return true;
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
    /// <summary>
    /// Game 뷰를 앞으로 가져오고 <b>화면 좌표</b>를 알려 준다(2026-09-21 신설).
    /// ★녹화가 Unity 창을 통째로 긁던 탓에 Scene 뷰가 찍혔다 — 사용자는 Game 화면을 원한다.
    ///   그래서 ①Game 뷰를 활성 탭으로 만들고 ②그 그림 영역만 잘라낼 좌표를 돌려준다.
    /// ★<c>position</c>은 스크린 좌표다. Game 뷰 위쪽 툴바만큼은 빼야 그림만 남는다.
    /// </summary>
    private static bool DoGameView(StringBuilder sb, Dictionary<string, string> a, out string err)
    {
        err = null;
        var t = Type.GetType("UnityEditor.GameView,UnityEditor");
        if (t == null) { err = "GameView 타입을 못 찾았다"; return false; }

        var w = EditorWindow.GetWindow(t, false, null, true);   // focus=true — 탭을 앞으로
        if (w == null) { err = "Game 뷰를 못 열었다"; return false; }
        w.Focus();

        float tb = 0f;
        float.TryParse(Get(a, "toolbar", "21"), out tb);
        Rect r = w.position;
        sb.Append("Game 뷰를 앞으로 가져왔다.\n");
        sb.Append(string.Format("창 x{0:F0} y{1:F0} w{2:F0} h{3:F0} · 툴바 {4:F0}px\n",
                                r.x, r.y, r.width, r.height, tb));
        // ★record.py가 이 줄을 읽는다 — 형식을 바꾸면 녹화의 잘라내기가 깨진다.
        sb.Append(string.Format("rect {0:F0},{1:F0},{2:F0},{3:F0}\n",
                                r.x, r.y + tb, r.width, Mathf.Max(2f, r.height - tb)));
        return true;
    }

    /// <summary>
    /// <c>Packages/manifest.json</c>을 다시 풀어 패키지를 내려받게 한다(2026-09-21 신설).
    /// ★<c>refresh</c>(AssetDatabase.Refresh)로는 안 된다 — manifest는 Assets 밖이라 에셋 임포트 대상이 아니다.
    ///   백그라운드 Unity는 파일 감시도 안 돌아서, 밖에서 manifest를 고쳐도 아무 일이 안 일어난다.
    /// ★비동기다. 내려받기·컴파일이 끝날 때까지 기다렸다가 <c>ping</c>으로 확인한다.
    /// </summary>
    private static bool DoResolvePackages(StringBuilder sb, out string err)
    {
        err = null;
        UnityEditor.PackageManager.Client.Resolve();
        sb.Append("패키지를 다시 푼다(Client.Resolve).\n");
        sb.Append("★내려받기와 컴파일이 이어진다 — 끝날 때까지 기다렸다가 ping으로 확인해라.\n");
        return true;
    }

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
