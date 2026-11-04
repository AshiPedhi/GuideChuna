using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V04 «당겨 펴기 / 굽혀 돌아오기»를 실습에 붙일 자료(2026-11-03 사용자 «연결해서 플레이 해볼 수 있게»).
/// ① 컨트롤러 `제2늑골`에 V04 상태 2개 추가(기존 상태 · 클립은 그대로)
/// ② 가이드손 녹화: 기존 `제2늑골 교정1`의 «환자 손 기준 시술자 왼손 손목» 파지를 V04 팔 위에 얹어 계산(V03 연결과 같은 방식)
///    ★파지 자세는 임시다 — 영상의 악수 잡기에 맞추는 건 Play 확인 뒤(사용자와 합의 11-03)
/// CSV는 이 도구가 고치지 않는다.
/// </summary>
public static partial class Rib2V03ClipBaker
{
    const string V04RecordingPath = "Assets/Resources/HandPoseData/제2늑골 교정 V04.csv";
    const string V04ReverseRecordingPath = "Assets/Resources/HandPoseData/제2늑골 교정 V04 역재생.csv";

    [MenuItem("GuideChuna/시험/제2늑골/V04 앱 연결 자료 만들기 (녹화 · 컨트롤러 상태)")]
    public static void BuildConnectionV04()
    {
        if (Application.isPlaying) { Debug.LogWarning("[V04연결] Play 중에는 만들지 않는다."); return; }
        var sceneC9 = GameObject.Find(C9Path);
        var pull = AssetDatabase.LoadAssetAtPath<AnimationClip>(PullClipPath);
        var back = AssetDatabase.LoadAssetAtPath<AnimationClip>(ReturnClipPath);
        var oldClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        var evaluator = Object.FindFirstObjectByType<ChunaPathEvaluator>(FindObjectsInactive.Include);
        Transform sceneRef = evaluator != null ? new SerializedObject(evaluator).FindProperty("referenceTransform").objectReferenceValue as Transform : null;
        if (sceneC9 == null || pull == null || back == null || oldClip == null || sceneRef == null || !File.Exists(OldRecordingPath))
        { Debug.LogWarning($"[V04연결] 준비물 — c9 {sceneC9 != null} · 당겨펴기 {pull != null} · 돌아오기 {back != null} · 기존 클립 {oldClip != null} · HandGuideAxis {sceneRef != null}"); return; }

        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<GameObject>();
        var log = new StringBuilder();
        try
        {
            var holder = sceneC9.transform.parent; var parent = holder.parent;
            var rootGo = new GameObject("부모 대역") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(rootGo, scene); made.Add(rootGo);
            rootGo.transform.SetPositionAndRotation(parent.position, parent.rotation); rootGo.transform.localScale = parent.lossyScale;
            var holderCopy = Object.Instantiate(holder.gameObject, rootGo.transform, false);
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var b in rootGo.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var c9 = holderCopy.transform.Find(sceneC9.name).gameObject;
            var animator = c9.GetComponent<Animator>();
            Transform reference = sceneRef.IsChildOf(holder) ? FindByPath(holderCopy.transform, RelativePath(holder, sceneRef)) : null;
            bool refMoves = reference != null;
            Vector3 RefPos() => refMoves ? reference.position : sceneRef.position;
            Quaternion RefRot() => refMoves ? reference.rotation : sceneRef.rotation;
            var arm = FindArm(animator, false);   // 이름 Right = 앱에서 움직이는 팔(V03 연결 · V04 굽기에서 잰 값)

            // 기존 녹화의 파지(환자 손 기준) — 첫 프레임
            var rows = ReadRecording(OldRecordingPath, out string header);
            int frames = rows.Max(r => r.frame) + 1;
            // ★11-03 사용자 «가이드핸드 위치도 수정» — 첫 프레임 파지는 환자 손에서 159mm 떨어져 있었다.
            //   기존 녹화 전 프레임을 기존 클립의 같은 진행도에 맞춰 재고, 시술자 손목이 환자 손에 «가장 가까운» 프레임의 파지를 쓴다.
            Vector3 gripPos = Vector3.zero; Quaternion gripRot = Quaternion.identity; float bestD = float.MaxValue; int bestF = 0;
            for (int f = 0; f < frames; f++)
            {
                oldClip.SampleAnimation(c9, f / (float)(frames - 1) * oldClip.length);
                var w = rows.First(r => r.frame == f && r.hand == "Left" && r.joint == WristJoint);
                Vector3 gp = RefPos() + RefRot() * V3(w.cells, 11); Quaternion gr = RefRot() * Q(w.cells, 14);
                Quaternion hf = HandFrame(arm);
                Vector3 rel = Quaternion.Inverse(hf) * (gp - arm.hand.position);
                if (rel.magnitude < bestD) { bestD = rel.magnitude; bestF = f; gripPos = rel; gripRot = Quaternion.Inverse(hf) * gr; }
            }
            log.Append($"파지(기존 교정1 · 환자 손 기준) = 가장 가까운 프레임 {bestF} · 손목 거리 {bestD * 1000f:F0}mm (첫 프레임은 159mm였다)\n");

            float span = rows.Where(r => r.frame == frames - 1).Select(r => float.Parse(r.cells[10], CultureInfo.InvariantCulture)).First();
            var blocks = new List<string>();
            for (int f = 0; f < frames; f++)
            {
                pull.SampleAnimation(c9, f / (float)(frames - 1) * pull.length);
                Quaternion hf = HandFrame(arm);
                Vector3 gp = arm.hand.position + hf * gripPos; Quaternion gr = hf * gripRot;
                blocks.Add(FrameBlock(rows, f, Quaternion.Inverse(RefRot()) * (gp - RefPos()), Quaternion.Inverse(RefRot()) * gr));
            }
            var times = Enumerable.Range(0, frames).Select(f => f / (float)(frames - 1) * span).ToArray();
            WriteRecording(V04RecordingPath, header, blocks, times, false);
            WriteRecording(V04ReverseRecordingPath, header, blocks, times, true);
            log.Append($"녹화 {frames}프레임 · {span:F2}초 → {V04RecordingPath} · 역재생\n");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var sm = controller.layers[0].stateMachine;
            foreach (var clip in new[] { pull, back })
            {
                var existing = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name);
                if (existing == null) { var st = sm.AddState(clip.name); st.motion = clip; st.writeDefaultValues = sm.states.First().state.writeDefaultValues; log.Append($"상태 추가 «{clip.name}»\n"); }
                else { existing.motion = clip; log.Append($"상태 있음 «{clip.name}» — 클립만 다시 연결\n"); }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(V04RecordingPath); AssetDatabase.ImportAsset(V04ReverseRecordingPath);
            Directory.CreateDirectory(Path.GetDirectoryName(PullReportPath));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(PullReportPath), "connect.txt"), log.ToString(), new UTF8Encoding(false));
            Debug.Log("[V04연결] " + log);
        }
        finally
        {
            foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
