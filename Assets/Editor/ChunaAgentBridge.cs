// ★[에이전트 브리지] 2026-09-09 신설
//
// 열려 있는 Unity 에디터의 씬 배선·인스펙터 값을 <b>읽고 고친다.</b>
// 씬 YAML을 정적으로 파싱하는 `unity-scene-audit`과 달리 <b>지금 에디터가 들고 있는 실제 값</b>을
// 보고, `SerializedProperty`로 그 자리에서 바꾼다.
//   (Unity 공식 `com.unity.pipeline`을 시험했다가 그 패키지의 IL 인터프리터가
//    System.Reflection.Metadata를 요구해 컴파일 에러 117건이 났다 — 2026-09-09.
//    필요한 건 임의 C# 실행이 아니라 인스펙터 조회·수정이라, 그 부분만 직접 만들었다.)
//
// ★★저장은 <b>절대 안 한다</b>(사용자 지시 2026-09-09). 값만 바꾸고 dirty 표시까지만 한다.
//   이유: 절대규칙 2 — 환자 피부가 분홍이 된 상태로 저장하면 디스크가 손상된다(07-27 전례).
//   사람이 눈으로 보고 Ctrl+S 하는 그 한 단계가 그 사고의 유일한 안전판이다. 그걸 안 없앤다.
// ★모든 수정은 `Undo.RecordObject`를 건다 — 에디터에서 Ctrl+Z로 되돌아간다.
// ★모든 수정은 before→after를 응답에 적는다. 조용히 바꾸지 않는다.
//
// 통신은 파일 큐다. 소켓을 안 쓰므로 포트·방화벽이 없다.
//   요청  <프로젝트루트>/Temp/chuna-bridge/req.txt    (key=value 한 줄씩)
//   응답  <프로젝트루트>/Temp/chuna-bridge/resp.json
// ★Temp는 Assets 밖이라 파일을 써도 에셋 리임포트가 안 걸린다.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ChunaAgentBridge
{
    private const string PrefKey = "ChunaAgentBridge.Enabled";
    // ★0.2 → 0.05 (2026-09-09). 매 틱에 하는 일은 File.Exists 하나뿐이라 20Hz도 부담이 없다.
    //   ★이 값보다 <b>에디터 프레임이 더 느리면</b> 낮춰도 안 빨라진다 — 거기가 바닥이다.
    private const double PollInterval = 0.05;
    private const int MaxLines = 600;          // 응답이 무한정 길어지지 않게 자른다.

    private static double nextPoll;
    private static string dirCache;

    static ChunaAgentBridge()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static bool Enabled => EditorPrefs.GetBool(PrefKey, true);

    private static string Dir
    {
        get
        {
            if (string.IsNullOrEmpty(dirCache))
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                dirCache = Path.Combine(root, "Temp", "chuna-bridge");
            }
            return dirCache;
        }
    }

    private static string ReqPath => Path.Combine(Dir, "req.txt");
    private static string RespPath => Path.Combine(Dir, "resp.json");

    // ── 폴링 ────────────────────────────────────────────────────────────
    private static void Tick()
    {
        if (!Enabled) return;
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + PollInterval;

        // ★컴파일·에셋 갱신 중에는 건드리지 않는다. 요청은 파일에 남으므로 다음 틱에 처리된다.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        string req = ReqPath;
        if (!File.Exists(req)) return;

        string body;
        try
        {
            body = File.ReadAllText(req, Encoding.UTF8);
            File.Delete(req);   // 두 번 처리하지 않는다
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[에이전트브리지] 요청을 못 읽었다 — {e.Message}");
            return;
        }

        var args = ParseArgs(body, out var items);
        string id = Get(args, "id", "");
        string cmd = Get(args, "cmd", "");
        var sb = new StringBuilder();
        bool ok = true;
        string err = null;

        try
        {
            switch (cmd)
            {
                case "ping": DoPing(sb); break;
                case "hierarchy": DoHierarchy(sb, args); break;
                case "get": DoGet(sb, args); break;
                case "find": DoFind(sb, args); break;
                case "set": ok = DoSet(sb, args, items, out err); break;
                case "refresh": DoRefresh(sb); break;
                case "missing": DoMissing(sb, args); break;
                default:
                    ok = false;
                    err = $"모르는 명령이다: '{cmd}' (ping · hierarchy · get · find · set)";
                    break;
            }
        }
        catch (Exception e)
        {
            ok = false;
            err = e.GetType().Name + ": " + e.Message;
        }

        WriteResponse(id, cmd, ok, err, sb.ToString());
    }

    private static void WriteResponse(string id, string cmd, bool ok, string err, string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var j = new StringBuilder();
            j.Append('{');
            j.Append("\"id\":").Append(Q(id)).Append(',');
            j.Append("\"cmd\":").Append(Q(cmd)).Append(',');
            j.Append("\"ok\":").Append(ok ? "true" : "false").Append(',');
            j.Append("\"playing\":").Append(EditorApplication.isPlaying ? "true" : "false").Append(',');
            j.Append("\"error\":").Append(err == null ? "null" : Q(err)).Append(',');
            j.Append("\"text\":").Append(Q(text));
            j.Append('}');

            // ★임시 파일에 쓰고 옮긴다. 클라이언트가 반쯤 쓰인 파일을 읽지 않게.
            string tmp = RespPath + ".tmp";
            File.WriteAllText(tmp, j.ToString(), new UTF8Encoding(false));
            if (File.Exists(RespPath)) File.Delete(RespPath);
            File.Move(tmp, RespPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[에이전트브리지] 응답을 못 썼다 — {e.Message}");
        }
    }

    // ── 명령 ────────────────────────────────────────────────────────────
    private static void DoPing(StringBuilder sb)
    {
        sb.Append("Unity ").Append(Application.unityVersion)
          .Append(" · 재생중 ").Append(EditorApplication.isPlaying ? "예" : "아니오")
          .Append(" · 컴파일중 ").Append(EditorApplication.isCompiling ? "예" : "아니오").Append('\n');
        int n = SceneManager.sceneCount;
        sb.Append("열린 씬 ").Append(n).Append("개\n");
        for (int i = 0; i < n; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            sb.Append("  ").Append(s.name)
              .Append(s.isDirty ? "  ★저장 안 된 변경 있음" : "")
              .Append("   ").Append(s.path).Append('\n');
        }
    }

    private static void DoHierarchy(StringBuilder sb, Dictionary<string, string> a)
    {
        string rootPath = Get(a, "path", null);
        string filter = Get(a, "filter", null);
        string comp = Get(a, "comp", null);
        int depth = GetInt(a, "depth", 2);

        int count = 0;
        foreach (var go in Roots(rootPath))
        {
            Walk(go.transform, 0, depth, filter, comp, sb, ref count);
            if (count >= MaxLines) break;
        }
        if (count == 0) sb.Append("(해당 없음)\n");
        else if (count >= MaxLines) sb.Append($"… {MaxLines}줄에서 잘랐다. path·filter·depth로 좁혀라.\n");
    }

    private static void Walk(Transform t, int d, int maxDepth, string filter, string comp,
                             StringBuilder sb, ref int count)
    {
        if (count >= MaxLines) return;

        bool nameOk = filter == null || t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        bool compOk = comp == null || HasComponent(t.gameObject, comp);
        if (nameOk && compOk)
        {
            sb.Append(new string(' ', d * 2)).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append("  [비활성]");
            var comps = t.GetComponents<Component>();
            if (comps.Length > 1)
            {
                sb.Append("   <");
                bool first = true;
                foreach (var c in comps)
                {
                    if (c == null || c is Transform) continue;
                    if (!first) sb.Append(", ");
                    sb.Append(c.GetType().Name);
                    first = false;
                }
                sb.Append('>');
            }
            sb.Append('\n');
            count++;
        }

        // ★filter·comp가 걸려 있으면 <b>깊이 제한을 무시하고</b> 끝까지 찾는다.
        //   안 그러면 "그 오브젝트 어디 있냐"에 답을 못 한다.
        bool searching = filter != null || comp != null;
        if (!searching && d >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++)
        {
            Walk(t.GetChild(i), d + 1, maxDepth, filter, comp, sb, ref count);
            if (count >= MaxLines) return;
        }
    }

    private static void DoFind(StringBuilder sb, Dictionary<string, string> a)
    {
        string comp = Get(a, "comp", null);
        if (string.IsNullOrEmpty(comp)) throw new Exception("find 에는 comp= 가 필요하다");

        int count = 0;
        foreach (var go in Roots(null))
            FindIn(go.transform, comp, sb, ref count);

        sb.Append(count == 0 ? "(씬에 0개다)\n" : $"— {comp} {count}개\n");
    }

    private static void FindIn(Transform t, string comp, StringBuilder sb, ref int count)
    {
        if (count >= MaxLines) return;
        if (HasComponent(t.gameObject, comp))
        {
            sb.Append(PathOf(t));
            if (!t.gameObject.activeInHierarchy) sb.Append("   [비활성]");
            sb.Append('\n');
            count++;
        }
        for (int i = 0; i < t.childCount; i++) FindIn(t.GetChild(i), comp, sb, ref count);
    }

    private static void DoGet(StringBuilder sb, Dictionary<string, string> a)
    {
        string path = Get(a, "path", null);
        string comp = Get(a, "comp", null);
        int maxDepth = GetInt(a, "depth", 1);

        var targets = new List<GameObject>();
        if (!string.IsNullOrEmpty(path))
        {
            var go = FindByPath(path);
            if (go == null) throw new Exception($"그 경로에 오브젝트가 없다: {path}");
            targets.Add(go);
        }
        else if (!string.IsNullOrEmpty(comp))
        {
            foreach (var r in Roots(null)) CollectWith(r.transform, comp, targets);
            if (targets.Count == 0) throw new Exception($"{comp} 가 씬에 0개다");
        }
        else throw new Exception("get 에는 path= 또는 comp= 가 필요하다");

        int lines = 0;
        foreach (var go in targets)
        {
            sb.Append("■ ").Append(PathOf(go.transform));
            if (!go.activeInHierarchy) sb.Append("   [비활성]");
            sb.Append('\n');

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) { sb.Append("  ★깨진 컴포넌트(Missing Script)\n"); continue; }
                if (c is Transform) continue;
                if (comp != null && c.GetType().Name != comp) continue;

                sb.Append("  [").Append(c.GetType().Name).Append("]\n");
                DumpProps(c, sb, maxDepth, ref lines);
                if (lines >= MaxLines) { sb.Append("  … 잘랐다\n"); return; }
            }
        }
    }

    private static void DumpProps(Component c, StringBuilder sb, int maxDepth, ref int lines)
    {
        var so = new SerializedObject(c);
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = it.depth < maxDepth && it.hasVisibleChildren
                    && it.propertyType == SerializedPropertyType.Generic;
            if (it.name == "m_Script") continue;
            if (it.depth > maxDepth) continue;

            sb.Append("    ").Append(new string(' ', it.depth * 2))
              .Append(it.displayName).Append(" (").Append(it.name).Append(") = ")
              .Append(ValueOf(it)).Append('\n');
            if (++lines >= MaxLines) return;
        }
    }

    private static string ValueOf(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            case SerializedPropertyType.ArraySize: return p.intValue + " (배열 크기)";
            case SerializedPropertyType.Float: return p.floatValue.ToString("0.#####");
            case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
            case SerializedPropertyType.Enum:
                return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                    ? $"{p.enumDisplayNames[p.enumValueIndex]} ({p.enumValueIndex})"
                    : p.enumValueIndex.ToString();
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue == null
                    ? "★None"
                    : $"{p.objectReferenceValue.name} <{p.objectReferenceValue.GetType().Name}>";
            case SerializedPropertyType.Vector2: return p.vector2Value.ToString("0.###");
            case SerializedPropertyType.Vector3: return p.vector3Value.ToString("0.###");
            case SerializedPropertyType.Vector4: return p.vector4Value.ToString("0.###");
            case SerializedPropertyType.Quaternion: return p.quaternionValue.eulerAngles.ToString("0.#") + " (오일러)";
            case SerializedPropertyType.Color: return p.colorValue.ToString();
            case SerializedPropertyType.Bounds: return p.boundsValue.ToString();
            case SerializedPropertyType.Rect: return p.rectValue.ToString();
            case SerializedPropertyType.LayerMask: return "mask " + p.intValue;
            case SerializedPropertyType.AnimationCurve: return "(커브)";
            case SerializedPropertyType.Generic:
                return p.isArray ? $"[{p.arraySize}개]" : "(중첩)";
            default:
                return "(" + p.propertyType + ")";
        }
    }

    /// <summary>
    /// 에셋을 다시 읽게 한다 — 밖에서 <c>.cs</c>·CSV·mp3를 고쳤을 때 쓴다.
    /// ★2026-09-09에 이게 없어서 물렸다: 스크립트에서 필드를 지웠는데 Unity가 파일을 다시
    ///   안 읽어, 인스펙터에 <b>없는 필드가 계속 보였다</b>. 창을 클릭해야만 반영됐다.
    /// ★스크립트가 바뀌었으면 이 요청 직후 <b>도메인 리로드</b>가 돈다. 응답은 그 전에 나가므로
    ///   다음 명령은 리로드가 끝날 때까지 기다려야 한다(요청 파일은 남아 있으니 저절로 처리된다).
    /// </summary>
    private static void DoRefresh(StringBuilder sb)
    {
        sb.Append("에셋을 다시 읽는다. 스크립트가 바뀌었으면 컴파일·도메인 리로드가 이어진다.\n");
        sb.Append("★다음 명령은 리로드가 끝난 뒤에 처리된다 — 조금 기다려라.\n");
        EditorApplication.delayCall += () => AssetDatabase.Refresh();
    }

    /// <summary>
    /// 스크립트가 사라진 컴포넌트(Missing Script)를 찾는다. <c>apply=1</c>이면 지운다.
    /// ★<b>기본은 조회다</b>(절대규칙 3 — 파괴적 도구는 파괴성을 먼저 보인다).
    /// ★지우기 전에 <c>Undo.RegisterCompleteObjectUndo</c>를 걸어 Ctrl+Z로 되돌아가게 한다.
    /// ★저장은 안 한다 — 언제나처럼 사람이 보고 Ctrl+S 한다.
    /// </summary>
    private static void DoMissing(StringBuilder sb, Dictionary<string, string> a)
    {
        bool apply = Get(a, "apply", "0") == "1";
        var hits = new List<GameObject>();

        foreach (var root in Roots(Get(a, "path", null)))
            CollectMissing(root.transform, hits);

        if (hits.Count == 0)
        {
            sb.Append("Missing Script 인 컴포넌트가 없다.\n");
            return;
        }

        int total = 0;
        foreach (var go in hits)
        {
            int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            total += n;
            sb.Append(n).Append("개  ").Append(PathOf(go.transform));
            if (!go.activeInHierarchy) sb.Append("   [비활성]");
            sb.Append('\n');
        }

        sb.Append('\n').Append($"— 오브젝트 {hits.Count}개 · 컴포넌트 {total}개\n");

        if (!apply)
        {
            sb.Append("※ 조회만 했다. 지우려면 apply=1 을 붙인다.\n");
            return;
        }

        int removed = 0;
        foreach (var go in hits)
        {
            Undo.RegisterCompleteObjectUndo(go, "에이전트 브리지 — Missing Script 제거");
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            EditorUtility.SetDirty(go);
            MarkDirty(go);
        }
        sb.Append($"★{removed}개를 지웠다. <b>저장은 안 했다</b> — 확인하고 Ctrl+S, 되돌리려면 Ctrl+Z.\n");
    }

    private static void CollectMissing(Transform t, List<GameObject> into)
    {
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            into.Add(t.gameObject);
        for (int i = 0; i < t.childCount; i++) CollectMissing(t.GetChild(i), into);
    }

    // ── 쓰기 ────────────────────────────────────────────────────────────
    /// <summary>
    /// 인스펙터 값을 바꾼다. 요청은 `item=경로|컴포넌트|프로퍼티|값` 한 줄에 하나씩, 여러 줄 가능.
    /// ★`dry=1` 이면 무엇이 바뀔지만 보여 주고 안 바꾼다.
    /// ★저장은 안 한다. dirty 표시까지만 하고 사람이 Ctrl+S 한다.
    /// </summary>
    private static bool DoSet(StringBuilder sb, Dictionary<string, string> a,
                              List<string> items, out string err)
    {
        err = null;
        bool dry = Get(a, "dry", "0") == "1";

        // 한 줄짜리 형태도 받는다: cmd=set / path= / comp= / prop= / value=
        if (items.Count == 0)
        {
            string p = Get(a, "path", null), c = Get(a, "comp", null);
            string pr = Get(a, "prop", null), v = Get(a, "value", null);
            if (p == null || c == null || pr == null || v == null)
            {
                err = "set 에는 item=경로|컴포넌트|프로퍼티|값 이 필요하다 "
                      + "(또는 path=·comp=·prop=·value= 넷 다)";
                return false;
            }
            items.Add(string.Join("|", new[] { p, c, pr, v }));
        }

        if (EditorApplication.isPlaying)
        {
            err = "★Play 중이다. 지금 바꾸면 Play를 멈출 때 통째로 사라진다. 멈추고 다시 해라.";
            return false;
        }

        int done = 0, failed = 0;
        if (dry) sb.Append("※ dry — 실제로 바꾸지 않았다\n\n");

        foreach (var item in items)
        {
            var f = item.Split('|');
            if (f.Length < 4)
            {
                sb.Append("★형식 오류: ").Append(item).Append('\n');
                failed++;
                continue;
            }
            string path = f[0].Trim(), comp = f[1].Trim(), prop = f[2].Trim();
            string val = string.Join("|", f, 3, f.Length - 3).Trim();   // 값에 | 가 있어도 살린다

            string line;
            if (ApplyOne(path, comp, prop, val, dry, out line)) done++;
            else failed++;
            sb.Append(line).Append('\n');
        }

        sb.Append('\n');
        if (dry)
        {
            sb.Append($"— 바꿀 것 {done}건 · 못 할 것 {failed}건. 실제로 바꾸려면 dry 를 빼라.\n");
        }
        else
        {
            sb.Append($"— 바꾼 것 {done}건 · 실패 {failed}건\n");
            if (done > 0)
            {
                sb.Append("★씬이 dirty 상태다. <b>저장은 안 했다</b> — 눈으로 확인하고 Ctrl+S 해라.\n");
                sb.Append("★되돌리려면 에디터에서 Ctrl+Z.\n");
            }
        }
        return failed == 0;
    }

    private static bool ApplyOne(string path, string comp, string prop, string val,
                                 bool dry, out string line)
    {
        var go = FindByPath(path);
        if (go == null) { line = $"★없는 경로: {path}"; return false; }

        // GameObject 활성 토글은 SerializedProperty 가 아니라 따로 다룬다.
        if (comp == "GameObject" && (prop == "active" || prop == "activeSelf"))
        {
            bool want = ParseBool(val);
            bool cur = go.activeSelf;
            line = $"{path}  GameObject.active  {cur} → {want}" + (cur == want ? "   (이미 같다)" : "");
            if (!dry && cur != want)
            {
                Undo.RecordObject(go, "에이전트 브리지 — active");
                go.SetActive(want);
                EditorUtility.SetDirty(go);
                MarkDirty(go);
            }
            return true;
        }

        Component target = null;
        foreach (var c in go.GetComponents<Component>())
            if (c != null && c.GetType().Name == comp) { target = c; break; }
        if (target == null)
        {
            var have = new List<string>();
            foreach (var c in go.GetComponents<Component>())
                if (c != null) have.Add(c.GetType().Name);
            line = $"★{path} 에 {comp} 가 없다. 붙어 있는 것: {string.Join(", ", have.ToArray())}";
            return false;
        }

        var so = new SerializedObject(target);
        var p = so.FindProperty(prop);
        if (p == null)
        {
            line = $"★{comp} 에 '{prop}' 프로퍼티가 없다. 가까운 이름: {NearNames(so, prop)}";
            return false;
        }

        string before = ValueOf(p);
        if (!AssignValue(p, val, out string why))
        {
            line = $"★{path}  {comp}.{prop}  값을 못 넣었다 — {why}";
            return false;
        }
        string after = ValueOf(p);

        if (dry)
        {
            line = $"{path}  {comp}.{prop}  {before} → {after}";
            return true;
        }

        Undo.RecordObject(target, $"에이전트 브리지 — {comp}.{prop}");
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        MarkDirty(go);

        // ★넣은 값이 실제로 먹었는지 다시 읽는다. 클램프·검증 로직이 되돌릴 수 있다.
        var again = new SerializedObject(target).FindProperty(prop);
        string real = again != null ? ValueOf(again) : after;
        line = $"{path}  {comp}.{prop}  {before} → {real}"
               + (real != after ? $"   ★요청은 {after} 였다(에디터가 되돌렸다)" : "");
        return true;
    }

    private static void MarkDirty(GameObject go)
    {
        var s = go.scene;
        if (s.IsValid()) EditorSceneManager.MarkSceneDirty(s);
    }

    private static string NearNames(SerializedObject so, string want)
    {
        var hits = new List<string>();
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            if (it.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0
                || want.IndexOf(it.name, StringComparison.OrdinalIgnoreCase) >= 0)
                hits.Add(it.name);
            if (hits.Count >= 8) break;
        }
        return hits.Count > 0 ? string.Join(", ", hits.ToArray()) : "(비슷한 게 없다. get 으로 목록을 봐라)";
    }

    private static bool ParseBool(string v)
        => v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase)
           || v.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static bool AssignValue(SerializedProperty p, string v, out string why)
    {
        why = null;
        var ic = CultureInfo.InvariantCulture;
        try
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    p.boolValue = ParseBool(v); return true;

                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                // ★배열 크기(`프로퍼티.Array.size`)도 정수로 다룬다.
                //   ★줄이면 <b>뒤에서부터 지워진다</b> — 앞 원소의 인덱스는 안 밀린다.
                case SerializedPropertyType.ArraySize:
                    if (!int.TryParse(v, NumberStyles.Integer, ic, out int i))
                    { why = $"정수가 아니다: '{v}'"; return false; }
                    p.intValue = i; return true;

                case SerializedPropertyType.Float:
                    if (!float.TryParse(v, NumberStyles.Float, ic, out float f))
                    { why = $"실수가 아니다: '{v}'"; return false; }
                    p.floatValue = f; return true;

                case SerializedPropertyType.String:
                    p.stringValue = v; return true;

                case SerializedPropertyType.Enum:
                {
                    if (int.TryParse(v, NumberStyles.Integer, ic, out int idx))
                    {
                        if (idx < 0 || idx >= p.enumDisplayNames.Length)
                        { why = $"enum 범위 밖이다(0~{p.enumDisplayNames.Length - 1})"; return false; }
                        p.enumValueIndex = idx; return true;
                    }
                    for (int k = 0; k < p.enumNames.Length; k++)
                        if (string.Equals(p.enumNames[k], v, StringComparison.OrdinalIgnoreCase))
                        { p.enumValueIndex = k; return true; }
                    for (int k = 0; k < p.enumDisplayNames.Length; k++)
                        if (string.Equals(p.enumDisplayNames[k], v, StringComparison.OrdinalIgnoreCase))
                        { p.enumValueIndex = k; return true; }
                    why = $"그런 enum 값이 없다. 있는 것: {string.Join(", ", p.enumNames)}";
                    return false;
                }

                case SerializedPropertyType.Vector2:
                {
                    if (!Nums(v, 2, out float[] n)) { why = "x,y 로 줘라"; return false; }
                    p.vector2Value = new Vector2(n[0], n[1]); return true;
                }
                case SerializedPropertyType.Vector3:
                {
                    if (!Nums(v, 3, out float[] n)) { why = "x,y,z 로 줘라"; return false; }
                    p.vector3Value = new Vector3(n[0], n[1], n[2]); return true;
                }
                case SerializedPropertyType.Vector4:
                {
                    if (!Nums(v, 4, out float[] n)) { why = "x,y,z,w 로 줘라"; return false; }
                    p.vector4Value = new Vector4(n[0], n[1], n[2], n[3]); return true;
                }
                case SerializedPropertyType.Color:
                {
                    if (Nums(v, 4, out float[] n4)) { p.colorValue = new Color(n4[0], n4[1], n4[2], n4[3]); return true; }
                    if (Nums(v, 3, out float[] n3)) { p.colorValue = new Color(n3[0], n3[1], n3[2], 1f); return true; }
                    if (ColorUtility.TryParseHtmlString(v.StartsWith("#") ? v : "#" + v, out Color hc))
                    { p.colorValue = hc; return true; }
                    why = "r,g,b[,a] 또는 #RRGGBB 로 줘라"; return false;
                }

                case SerializedPropertyType.ObjectReference:
                {
                    if (v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("null", StringComparison.OrdinalIgnoreCase))
                    { p.objectReferenceValue = null; return true; }

                    // 에셋 경로면 에셋을, 아니면 씬 오브젝트 경로로 본다.
                    if (v.StartsWith("Assets/"))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(v);
                        if (asset == null) { why = $"그 에셋이 없다: {v}"; return false; }
                        p.objectReferenceValue = asset; return true;
                    }

                    // "경로" 또는 "경로|컴포넌트"
                    string op = v, oc = null;
                    int bar = v.IndexOf('|');
                    if (bar > 0) { op = v.Substring(0, bar); oc = v.Substring(bar + 1); }
                    var ogo = FindByPath(op);
                    if (ogo == null) { why = $"그 경로에 오브젝트가 없다: {op}"; return false; }
                    if (oc == null) { p.objectReferenceValue = ogo; return true; }
                    foreach (var c in ogo.GetComponents<Component>())
                        if (c != null && c.GetType().Name == oc) { p.objectReferenceValue = c; return true; }
                    why = $"{op} 에 {oc} 가 없다"; return false;
                }

                default:
                    why = $"{p.propertyType} 형은 아직 못 넣는다";
                    return false;
            }
        }
        catch (Exception e)
        {
            why = e.Message;
            return false;
        }
    }

    private static bool Nums(string v, int want, out float[] outv)
    {
        outv = null;
        var parts = v.Replace("(", "").Replace(")", "").Split(',');
        if (parts.Length != want) return false;
        var r = new float[want];
        for (int i = 0; i < want; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]))
                return false;
        outv = r;
        return true;
    }

    // ── 공용 ────────────────────────────────────────────────────────────
    private static IEnumerable<GameObject> Roots(string rootPath)
    {
        if (!string.IsNullOrEmpty(rootPath))
        {
            var go = FindByPath(rootPath);
            if (go == null) throw new Exception($"그 경로에 오브젝트가 없다: {rootPath}");
            yield return go;
            yield break;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            foreach (var go in s.GetRootGameObjects()) yield return go;
        }
    }

    private static void CollectWith(Transform t, string comp, List<GameObject> into)
    {
        if (HasComponent(t.gameObject, comp)) into.Add(t.gameObject);
        for (int i = 0; i < t.childCount; i++) CollectWith(t.GetChild(i), comp, into);
    }

    private static bool HasComponent(GameObject go, string typeName)
    {
        foreach (var c in go.GetComponents<Component>())
            if (c != null && c.GetType().Name == typeName) return true;
        return false;
    }

    /// <summary>"Root/Child/Obj" 로 찾는다. 비활성 오브젝트도 찾는다(GameObject.Find는 못 찾는다).</summary>
    private static GameObject FindByPath(string path)
    {
        var parts = path.Split('/');
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            foreach (var root in s.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                Transform cur = root.transform;
                bool ok = true;
                for (int k = 1; k < parts.Length && ok; k++)
                {
                    Transform next = null;
                    for (int j = 0; j < cur.childCount; j++)
                        if (cur.GetChild(j).name == parts[k]) { next = cur.GetChild(j); break; }
                    if (next == null) ok = false; else cur = next;
                }
                if (ok) return cur.gameObject;
            }
        }
        return null;
    }

    private static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
        return sb.ToString();
    }

    /// <summary>key=value 를 읽는다. ★`item=` 만은 여러 줄이라 따로 모은다(한 번에 여러 개 고치려고).</summary>
    private static Dictionary<string, string> ParseArgs(string body, out List<string> items)
    {
        var d = new Dictionary<string, string>();
        items = new List<string>();
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v = line.Substring(eq + 1).Trim();
            if (k == "item") items.Add(v);
            else d[k] = v;
        }
        return d;
    }

    private static string Get(Dictionary<string, string> d, string k, string def)
        => d.TryGetValue(k, out var v) && v.Length > 0 ? v : def;

    private static int GetInt(Dictionary<string, string> d, string k, int def)
        => int.TryParse(Get(d, k, null), out var v) ? v : def;

    private static string Q(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 16);
        sb.Append('"');
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ── 메뉴 ────────────────────────────────────────────────────────────
    [MenuItem("GuideChuna/에이전트 브리지/상태 보기")]
    private static void MenuStatus()
    {
        Debug.Log($"[에이전트브리지] {(Enabled ? "켜짐" : "꺼짐")} · 폴더 {Dir}");
    }

    [MenuItem("GuideChuna/에이전트 브리지/켜기")]
    private static void MenuOn()
    {
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[에이전트브리지] 켰다. 읽기 전용이다 — 씬을 바꾸거나 저장하지 않는다.");
    }

    [MenuItem("GuideChuna/에이전트 브리지/끄기")]
    private static void MenuOff()
    {
        EditorPrefs.SetBool(PrefKey, false);
        Debug.Log("[에이전트브리지] 껐다.");
    }
}
