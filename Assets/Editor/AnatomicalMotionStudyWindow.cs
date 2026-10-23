using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Body-relative motion inspection on a disposable generated patient, outside the user's scenes.</summary>
public sealed class AnatomicalMotionStudyWindow : EditorWindow
{
    internal enum CameraView { FrontOblique, Front, Side, Back, SupineOblique, SupineTop }
    private AnatomicalMotionSequence.Motion motion = AnatomicalMotionSequence.Motion.Rib2Study;
    private AnatomicalPatientLab.View view;
    private CameraView cameraView = CameraView.SupineOblique;
    private bool left = true;
    private bool showShirt;
    private bool running;
    private bool loop = true;
    private bool renderRequested = true;
    private bool subscribed;
    private float seconds;
    private float zoom = 1f;
    private double previousUpdate;
    private double previousRender;
    private int renderWidth = 800;
    private int renderHeight = 640;
    private string error;
    private string measured = "";
    private Texture previewTexture;
    private Session session;

    [MenuItem("GuideChuna/시험/해부학 환자/연속 동작실 (독립 미리보기)")]
    private static void Open()
    {
        var window = GetWindow<AnatomicalMotionStudyWindow>();
        window.titleContent = new GUIContent("해부학 연속 동작실");
        window.minSize = new Vector2(680f, 680f);
        window.Show();
    }

    private void OnEnable()
    {
        previousUpdate = EditorApplication.timeSinceStartup;
        renderRequested = true;
        Subscribe();
        AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
    }

    private void Subscribe()
    {
        if (subscribed) return;
        EditorApplication.update += Tick;
        subscribed = true;
    }

    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        Release();
    }

    private void BeforeAssemblyReload() { Release(); }

    private void Release()
    {
        if (subscribed) EditorApplication.update -= Tick;
        subscribed = false;
        running = false;
        previewTexture = null;
        if (session != null) { session.Dispose(); session = null; }
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("생성된 c9 복제본을 별도 미리보기에서 움직입니다. 각도는 몸 기준의 운동학적 시험값입니다. " +
            "Rib2Study는 동작 검토용 근사이며, 전문가 영상의 실측 각도·접촉·힘을 재현한 결과가 아닙니다.", MessageType.Info);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Unity Play 중에는 독립 미리보기 갱신을 멈춥니다. Play를 직접 종료하면 이 창에서 다시 살펴볼 수 있습니다.", MessageType.Info);
        if (!string.IsNullOrEmpty(error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("생성 모델 다시 불러오기")) Reload();
            return;
        }

        EditorGUI.BeginChangeCheck();
        motion = (AnatomicalMotionSequence.Motion)EditorGUILayout.EnumPopup("동작", motion);
        left = EditorGUILayout.Toggle("왼팔 (해제하면 오른팔)", left);
        if (EditorGUI.EndChangeCheck()) { seconds = 0f; renderRequested = true; }
        EditorGUILayout.LabelField(AnatomicalMotionSequence.Label(motion), EditorStyles.boldLabel);

        float duration = Mathf.Max(.001f, AnatomicalMotionSequence.Duration(motion));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(running ? "일시정지" : "재생", GUILayout.Width(90f)))
        {
            running = !running;
            if (running && seconds >= duration) seconds = 0f;
            previousUpdate = EditorApplication.timeSinceStartup;
            renderRequested = true;
        }
        if (GUILayout.Button("처음", GUILayout.Width(70f))) { seconds = 0f; running = false; renderRequested = true; }
        loop = GUILayout.Toggle(loop, "반복", GUILayout.Width(65f));
        EditorGUI.BeginChangeCheck();
        seconds = EditorGUILayout.Slider(seconds, 0f, duration);
        if (EditorGUI.EndChangeCheck()) { running = false; renderRequested = true; }
        GUILayout.Label(seconds.ToString("F2") + " / " + duration.ToString("F2") + " s", GUILayout.Width(120f));
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        view = (AnatomicalPatientLab.View)EditorGUILayout.EnumPopup("표시", view);
        showShirt = GUILayout.Toggle(showShirt, "상의 표시", GUILayout.Width(90f));
        EditorGUILayout.EndHorizontal();
        cameraView = (CameraView)EditorGUILayout.EnumPopup("카메라", cameraView);
        zoom = EditorGUILayout.Slider("확대", zoom, .65f, 2.5f);
        if (EditorGUI.EndChangeCheck()) renderRequested = true;

        Rect imageRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(270f));
        if (Event.current.type == EventType.Repaint)
        {
            int width = Mathf.Clamp(Mathf.RoundToInt(imageRect.width), 320, 1400);
            int height = Mathf.Clamp(Mathf.RoundToInt(imageRect.height), 270, 1200);
            if (width != renderWidth || height != renderHeight)
            { renderWidth = width; renderHeight = height; renderRequested = true; }
            EditorGUI.DrawRect(imageRect, new Color(.15f, .17f, .20f));
            if (previewTexture != null) GUI.DrawTexture(imageRect, previewTexture, ScaleMode.ScaleToFit, false);
            else GUI.Label(imageRect, "미리보기 준비 중…", EditorStyles.centeredGreyMiniLabel);
        }
        if (session != null)
        {
            var p = session.Rig.CurrentPose;
            var arm = left ? p.leftArm : p.rightArm;
            EditorGUILayout.LabelField("요청: 어깨 " + arm.shoulderElevation.ToString("F1") + "° / 팔꿈치 " +
                arm.elbowFlexion.ToString("F1") + "° / 전완 " + arm.forearmRotation.ToString("F1") + "° / 손목 굴곡·편위 " +
                arm.wristFlexion.ToString("F1") + "° · " + arm.wristDeviation.ToString("F1") + "°", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("목 요청: " + p.neckDegrees.ToString("F1") + "° (굴곡, 측굴, 회전)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(measured, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("원본 고정: " + session.PrefabPath, EditorStyles.wordWrappedMiniLabel);
        }
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Editor 미리보기 · 최대 12 fps · Play/Quest/임상 검증 아님", EditorStyles.miniLabel);
        if (GUILayout.Button("모델 다시 불러오기", GUILayout.Width(150f))) Reload();
        EditorGUILayout.EndHorizontal();
    }

    private void Reload()
    {
        Release();
        error = null;
        measured = "";
        seconds = 0f;
        renderRequested = true;
        previousUpdate = EditorApplication.timeSinceStartup;
        Subscribe();
        Repaint();
    }

    private void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            running = false;
            previousUpdate = EditorApplication.timeSinceStartup;
            renderRequested = true;
            return;
        }
        if (!string.IsNullOrEmpty(error) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        double now = EditorApplication.timeSinceStartup;
        double elapsed = Math.Max(0d, now - previousUpdate);
        previousUpdate = now;
        try
        {
            if (session == null) session = new Session();
            if (running)
            {
                float duration = Mathf.Max(.001f, AnatomicalMotionSequence.Duration(motion));
                seconds += (float)elapsed;
                if (seconds >= duration)
                {
                    if (loop) seconds %= duration;
                    else { seconds = duration; running = false; }
                }
                renderRequested = true;
            }
            if (!renderRequested || now - previousRender < 1d / 12d) return;
            var pose = AnatomicalMotionSequence.Sample(session.Rig.RestPose, motion, seconds, left);
            if (!session.Rig.Evaluate(pose)) throw new InvalidOperationException("환자 리그가 자세 평가를 중단했습니다. [AnatomicalRig] 로그를 확인하세요.");
            measured = session.Measure(left);
            previewTexture = session.Render(renderWidth, renderHeight, view, showShirt, cameraView, zoom, left);
            previousRender = EditorApplication.timeSinceStartup;
            renderRequested = false;
            Repaint();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Release();
            Debug.LogException(exception);
            Repaint();
        }
    }

    /// <summary>Owns only a preview-scene clone and reusable CPU-skinned rendering resources.</summary>
    internal sealed class Session : IDisposable
    {
        [Serializable] private sealed class Receipt { public string prefab = ""; }
        private sealed class Snapshot
        {
            public SkinnedMeshRenderer Source;
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool SourceWasEnabled;
        }
        private sealed class Arm
        {
            public bool Left;
            public Transform Upper, Lower, Hand, Index, Middle, Little;
        }

        public readonly string PrefabPath;
        public readonly GameObject Root;
        public readonly AnatomicalPatientRig Rig;
        public readonly AnatomicalPatientLab Lab;
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly Arm[] arms = new Arm[2];
        private PreviewRenderUtility preview;
        private bool disposed;

        public Session()
        {
            try
            {
                string project = Path.GetDirectoryName(Application.dataPath);
                const string generated = "Assets/Generated/AnatomicalPatient";
                string receiptPath = Path.Combine(project, generated, "LatestBuild.json");
                if (!File.Exists(receiptPath)) throw new FileNotFoundException("생성된 환자의 LatestBuild.json이 없습니다.", receiptPath);
                var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath));
                if (receipt == null || string.IsNullOrEmpty(receipt.prefab)) throw new InvalidDataException("생성 모델의 prefab 경로가 비어 있습니다.");
                string absolute = Path.GetFullPath(Path.Combine(project, receipt.prefab));
                string allowed = Path.GetFullPath(Path.Combine(project, generated)) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(absolute), "AnatomicalPatient_c9.prefab", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("해부학 환자 생성 폴더 안의 복제본만 미리볼 수 있습니다.");
                PrefabPath = receipt.prefab.Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (asset == null) throw new FileNotFoundException("생성된 환자 prefab을 읽을 수 없습니다.", PrefabPath);

                preview = new PreviewRenderUtility();
                // Instantiate directly in the private preview scene, never in an
                // active/dirty training scene, even transiently.
                Root = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview.camera.gameObject.scene);
                if (Root == null) throw new InvalidOperationException("미리보기 환자 복제를 만들지 못했습니다.");
                Root.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(Root);
                Rig = Root.GetComponent<AnatomicalPatientRig>();
                Lab = Root.GetComponent<AnatomicalPatientLab>();
                var animator = Root.GetComponentInChildren<Animator>(true);
                if (Rig == null || Lab == null || animator == null || !animator.isHuman)
                    throw new InvalidDataException("생성 모델의 리그·실험실·Humanoid 참조가 없습니다.");
                animator.enabled = false;
                Lab.enabled = false;
                Rig.enabled = false;
                Rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
                Rig.SetAutomaticEvaluation(false);
                if (!Rig.Evaluate(Rig.RestPose)) throw new InvalidOperationException("생성 모델의 저장된 기준 자세를 평가하지 못했습니다.");
                ReadArms(animator);
                foreach (var source in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var snapshot = new Snapshot { Source = source };
                    snapshots.Add(snapshot); // Own partial allocations before the next API can throw.
                    snapshot.Mesh = new Mesh { name = "Motion study CPU skin" };
                    snapshot.Mesh.MarkDynamic();
                    snapshot.Object = new GameObject("Motion study CPU snapshot") { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
                    snapshot.Object.transform.SetParent(source.transform, false);
                    snapshot.Object.AddComponent<MeshFilter>().sharedMesh = snapshot.Mesh;
                    snapshot.Renderer = snapshot.Object.AddComponent<MeshRenderer>();
                    snapshot.Renderer.sharedMaterials = source.sharedMaterials;
                    snapshot.Renderer.shadowCastingMode = source.shadowCastingMode;
                    snapshot.Renderer.receiveShadows = source.receiveShadows;
                    snapshot.Renderer.enabled = false;
                }
                preview.camera.enabled = false;
                preview.camera.stereoTargetEye = StereoTargetEyeMask.None;
                preview.camera.fieldOfView = 35f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 20f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.15f, .17f, .20f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                preview.lights[1].intensity = 1f;
                preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);
                Debug.Log("[AnatomicalMotionStudy] Detached preview ready: " + PrefabPath + "; shared scenes/assets are not saved; no Play toggle.");
            }
            catch { Dispose(); throw; }
        }

        private void ReadArms(Animator animator)
        {
            var data = new SerializedObject(Rig);
            var bindings = data.FindProperty("bodyBindings");
            var limbs = data.FindProperty("limbs");
            if (bindings == null || limbs == null || limbs.arraySize != 2) throw new InvalidDataException("저장된 양팔 참조가 없습니다.");
            var outputs = new Dictionary<Transform, Transform>();
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                var source = (Transform)binding.FindPropertyRelative("source").objectReferenceValue;
                var output = (Transform)binding.FindPropertyRelative("output").objectReferenceValue;
                if (source == null || output == null || outputs.ContainsKey(source)) throw new InvalidDataException("중복되거나 빠진 피부 출력 참조가 있습니다.");
                outputs.Add(source, output);
            }
            for (int i = 0; i < limbs.arraySize; i++)
            {
                var limb = limbs.GetArrayElementAtIndex(i);
                bool isLeft = limb.FindPropertyRelative("left").boolValue;
                int side = isLeft ? 0 : 1;
                if (arms[side] != null) throw new InvalidDataException("양팔의 좌우 참조가 중복되어 있습니다.");
                var arm = new Arm
                {
                    Left = isLeft,
                    Upper = (Transform)limb.FindPropertyRelative("humerus").objectReferenceValue,
                    Lower = (Transform)limb.FindPropertyRelative("ulna").objectReferenceValue,
                    Hand = (Transform)limb.FindPropertyRelative("hand").objectReferenceValue,
                    Index = Finger(outputs, animator, isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal),
                    Middle = Finger(outputs, animator, isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal),
                    Little = Finger(outputs, animator, isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal)
                };
                if (arm.Upper == null || arm.Lower == null || arm.Hand == null) throw new InvalidDataException("측정할 팔 출력 본이 없습니다.");
                arms[side] = arm;
            }
        }

        private static Transform Finger(Dictionary<Transform, Transform> outputs, Animator animator, HumanBodyBones id)
        {
            Transform source = animator.GetBoneTransform(id);
            if (source == null || !outputs.TryGetValue(source, out var output)) throw new InvalidDataException("손가락 출력 본이 없습니다: " + id);
            return output;
        }

        public string Measure(bool isLeft)
        {
            var arm = arms[isLeft ? 0 : 1];
            Vector3 upper = arm.Lower.position - arm.Upper.position;
            Vector3 lower = arm.Hand.position - arm.Lower.position;
            Vector3 finger = (arm.Middle.position - arm.Hand.position).normalized;
            Vector3 radial = (arm.Index.position - arm.Little.position).normalized;
            Vector3 palm = (arm.Left ? Vector3.Cross(radial, finger) : Vector3.Cross(finger, radial)).normalized;
            return "출력본 측정: 팔꿈치 " + Vector3.Angle(upper, lower).ToString("F1") +
                "° / 손 " + arm.Hand.position.ToString("F3") + " m / 손바닥 기준 " + palm.ToString("F3") +
                " (손가락 본에서 계산한 방향)";
        }

        public Texture Render(int width, int height, AnatomicalPatientLab.View requestedView, bool shirt, CameraView direction, float magnification, bool isLeft)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Session));
            Lab.SetUpperClothingVisible(shirt);
            Lab.SetView(requestedView);
            bool began = false;
            Vector3 rootPosition = Root.transform.position;
            Quaternion rootRotation = Root.transform.rotation;
            try
            {
                bool supine = direction == CameraView.SupineOblique || direction == CameraView.SupineTop;
                if (supine)
                {
                    // Rotate only the disposable presentation, never its joint
                    // data. c9 +Y (cranial) becomes -Z, +Z (anterior) becomes +Y.
                    Root.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1.3f), Quaternion.Euler(-90f, 0f, 0f));
                }
                foreach (var snapshot in snapshots) snapshot.SourceWasEnabled = snapshot.Source.enabled;
                foreach (var snapshot in snapshots)
                {
                    var source = snapshot.Source;
                    bool visible = source.enabled && source.gameObject.activeInHierarchy && !source.forceRenderingOff;
                    snapshot.Renderer.enabled = visible;
                    if (!visible) continue;
                    // BakeMesh evaluates the current rig now; native skinned
                    // preview was previously measured one pose behind in one tick.
                    source.BakeMesh(snapshot.Mesh);
                    source.enabled = false;
                }
                Vector3 target = new Vector3(0f, 1.3f, 0f);
                Vector3 position = new Vector3(2.2f, 1.55f, 3.4f);
                if (direction == CameraView.Front) position = new Vector3(0f, 1.55f, 4f);
                else if (direction == CameraView.Side) position = new Vector3(isLeft ? -4f : 4f, 1.55f, 0f);
                else if (direction == CameraView.Back) position = new Vector3(0f, 1.55f, -4f);
                else if (direction == CameraView.SupineOblique) { target = Vector3.zero; position = new Vector3(1.8f, 2.1f, 2.4f); }
                else if (direction == CameraView.SupineTop) { target = Vector3.zero; position = new Vector3(0f, 3.2f, 0f); }
                preview.camera.transform.position = target + (position - target) / magnification;
                preview.camera.transform.LookAt(target, direction == CameraView.SupineTop ? Vector3.back : Vector3.up);
                preview.BeginPreview(new Rect(0f, 0f, width, height), GUIStyle.none);
                began = true;
                preview.Render();
                Texture texture = preview.EndPreview();
                began = false;
                return texture;
            }
            finally
            {
                try { if (began) preview.EndPreview(); }
                finally
                {
                    foreach (var snapshot in snapshots)
                    {
                        snapshot.Source.enabled = snapshot.SourceWasEnabled;
                        snapshot.Renderer.enabled = false;
                    }
                    Root.transform.SetPositionAndRotation(rootPosition, rootRotation);
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var snapshot in snapshots)
            {
                if (snapshot.Object != null) UnityEngine.Object.DestroyImmediate(snapshot.Object);
                if (snapshot.Mesh != null) UnityEngine.Object.DestroyImmediate(snapshot.Mesh);
            }
            snapshots.Clear();
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            if (preview != null) { preview.Cleanup(); preview = null; }
        }
    }
}

/// <summary>Asynchronous CPU-skinned capture of the same motion and preview as the interactive window.</summary>
internal static class AnatomicalMotionStudyExport
{
    private static Job active;
    private const string Menu = "GuideChuna/시험/해부학 환자/영상 참고 동작 캡처 (피부·골격)";

    [MenuItem(Menu)]
    private static void Start()
    {
        if (active != null) { Debug.LogWarning("[AnatomicalMotionExport] Capture already running."); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before isolated capture.");
        active = new Job();
    }

    [MenuItem(Menu, true)]
    private static bool Available() => active == null && !EditorApplication.isPlayingOrWillChangePlaymode;

    [Serializable]
    private sealed class Receipt
    {
        public string status, folder, prefab, failure;
        public string scope = "Editor CPU BakeMesh of shared AnatomicalMotionSequence and Evaluate(Pose). Left panel=skin, right panel=bones. Video-reference authoring estimates; clinical accuracy, contact, force and human Play remain unverified.";
        public int frames, total, fps = 8, width = 1024, height = 512;
    }

    private sealed class Job : IDisposable
    {
        private readonly Receipt receipt = new Receipt();
        private AnatomicalMotionStudyWindow.Session session;
        private Texture2D pixels, combined;
        private StreamWriter times;
        private bool disposed;

        public Job()
        {
            try
            {
                receipt.folder = Path.GetFullPath("Captures/AnatomicalRib2_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(receipt.folder);
                session = new AnatomicalMotionStudyWindow.Session();
                receipt.prefab = session.PrefabPath;
                receipt.total = Mathf.RoundToInt(AnatomicalMotionSequence.Duration(AnatomicalMotionSequence.Motion.Rib2Study) * receipt.fps) + 1;
                receipt.status = "running";
                pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
                combined = new Texture2D(1024, 512, TextureFormat.RGB24, false);
                times = new StreamWriter(Path.Combine(receipt.folder, "timeline.csv"), false, new UTF8Encoding(false));
                times.WriteLine("frame,seconds,phase");
                WriteReceipt();
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel;
                Debug.Log("[AnatomicalMotionExport] Start " + receipt.total + " frames; separate preview; " + receipt.folder);
            }
            catch { Dispose(); throw; }
        }

        private void Tick()
        {
            if (disposed) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Cancel(); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                float t = receipt.frames / (float)receipt.fps;
                var motion = AnatomicalMotionSequence.Motion.Rib2Study;
                if (!session.Rig.Evaluate(AnatomicalMotionSequence.Sample(session.Rig.RestPose, motion, t, false)))
                    throw new InvalidOperationException("Runtime pose evaluation failed.");
                ReadPanel(AnatomicalPatientLab.View.Skin, 0);
                ReadPanel(AnatomicalPatientLab.View.Skeleton, 512);
                combined.Apply(false);
                File.WriteAllBytes(Path.Combine(receipt.folder, "frame_" + receipt.frames.ToString("D5", CultureInfo.InvariantCulture) + ".jpg"), combined.EncodeToJPG(90));
                times.WriteLine(receipt.frames + "," + t.ToString("R", CultureInfo.InvariantCulture) + ",\"" + AnatomicalMotionSequence.PhaseLabel(motion, t) + "\"");
                receipt.frames++;
                if (receipt.frames % 24 == 0) { times.Flush(); WriteReceipt(); }
                if (receipt.frames >= receipt.total)
                {
                    receipt.status = "complete";
                    WriteReceipt();
                    Debug.Log("[AnatomicalMotionExport] Complete: " + receipt.folder + "; 8fps; left skin/right skeleton; no scene/assets saved.");
                    Dispose();
                }
            }
            catch (Exception exception)
            {
                receipt.status = "failed"; receipt.failure = exception.ToString();
                Debug.LogException(exception);
                // The failure itself may be an I/O error. A second failed
                // receipt write must not leave an update callback/preview alive.
                try { WriteReceipt(); }
                finally { Dispose(); }
            }
        }

        private void ReadPanel(AnatomicalPatientLab.View view, int offset)
        {
            Texture rendered = session.Render(512, 512, view, false, AnatomicalMotionStudyWindow.CameraView.SupineOblique, 1.1f, false);
            RenderTexture buffer = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(rendered, buffer);
                RenderTexture.active = buffer;
                pixels.ReadPixels(new Rect(0f, 0f, 512f, 512f), 0, 0, false);
                pixels.Apply(false);
                combined.SetPixels(offset, 0, 512, 512, pixels.GetPixels());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(buffer); }
        }

        private void Cancel()
        {
            receipt.status = "cancelled";
            try { WriteReceipt(); }
            finally { Dispose(); }
        }

        private void WriteReceipt()
        {
            string json = JsonUtility.ToJson(receipt, true);
            File.WriteAllText(Path.Combine(receipt.folder, "export.json"), json, new UTF8Encoding(false));
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/motion-export.json", json, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            try { if (times != null) times.Dispose(); }
            finally
            {
                times = null;
                try
                {
                    if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                    if (combined != null) UnityEngine.Object.DestroyImmediate(combined);
                }
                finally
                {
                    try { if (session != null) session.Dispose(); }
                    finally { session = null; active = null; }
                }
            }
        }
    }
}
