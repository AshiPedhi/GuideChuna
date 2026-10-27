using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Disposable review of the source-authored two-hand draft; never edits a training scene or source asset.</summary>
public sealed class Rib2HandsVideoReviewWindow : EditorWindow
{
    private const string Folder = "Captures/Rib2HandsReview_V02_20261027";
    private const string ManifestPath = Folder + "/draft.json";
    private const string DemoScene = "Assets/Demos/Rib2VideoTextScene/V01/Rib2VideoTextDemo.unity";
    private const string ReportPath = "Temp/model-audit/rib2-hands-review.json";
    internal enum View { SourceOblique, Top, Side }

    [Serializable] internal sealed class Manifest
    {
        public string csvPath = "";
        public string sourceFramesDirectory = "";
        public float sourceStartSeconds = 56.5f;
        public float fps = 30f;
        public int frameCount = 248;
        public float cycleStartSeconds = 3.75f;
        public string sourceVideo = "";
        public string provenance = "";
    }

    [Serializable] private sealed class SceneState
    {
        public int handle;
        public string path;
        public bool dirty;
    }

    [Serializable] private sealed class Receipt
    {
        public bool success;
        public string error = "", utc, outputFolder, csvSha256, manifestSha256, sourceSceneSha256, implementationSha256;
        public string sourceScene = DemoScene;
        public string scope = "Private preview only. Same Rib2VideoTextScenePlayer.Seek and Rib2DemoPatientArm.Evaluate as the separate V01 demonstration. Source-authored depth/fingers; not measured mocap, force, clinical or Play validation.";
        public int framesWritten, expectedFrames, sourceFrameCount, jointsPerHand = 26;
        public int activeSceneBefore, activeSceneAfter;
        public float fps, durationSeconds, maximumPatientWristTargetResidualMetres, maximumReachRetargetMetres;
        public float minimumHandViewportMargin = 1f;
        public Vector3 leftMinimum, leftMaximum, rightMinimum, rightMaximum;
        public bool posesFinite = true, originalSceneStateUnchanged;
        public SceneState[] before, after;
    }

    private Session session;
    private Texture previewTexture;
    private Texture2D sourceTexture;
    private int sourceFrame = -1;
    private bool running, halfSpeed, needsRender = true;
    private float seconds, zoom = 1f;
    private View view;
    private int renderWidth = 760, renderHeight = 480;
    private double lastUpdate, lastRender;
    private string error;

    [MenuItem("GuideChuna/시험/제2늑골/양손 영상 가이드 초안 (독립 검토)")]
    public static void Open()
    {
        var window = GetWindow<Rib2HandsVideoReviewWindow>();
        window.titleContent = new GUIContent("제2늑골 양손 초안");
        window.minSize = new Vector2(920f, 650f);
        window.Show();
    }

    private void OnEnable()
    {
        lastUpdate = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Release;
        EditorApplication.playModeStateChanged += PlayStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        AssemblyReloadEvents.beforeAssemblyReload -= Release;
        EditorApplication.playModeStateChanged -= PlayStateChanged;
        Release();
    }

    private void PlayStateChanged(PlayModeStateChange state) { Release(); }

    private void Release()
    {
        running = false;
        previewTexture = null;
        if (session != null) { session.Dispose(); session = null; }
        if (sourceTexture != null) DestroyImmediate(sourceTexture);
        sourceTexture = null;
        sourceFrame = -1;
        needsRender = true;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("영상 참고 양손 초안 · 시작 파지와 팔 한 주기입니다. 깊이와 가려진 손가락은 제작 가정이며, 실제 압력·장력은 재현하지 않습니다.", MessageType.Info);
        EditorGUILayout.LabelField("청록: 시술자 왼손 · 환자의 손/손목 유도     주황: 시술자 오른손 · 흉부 지지", EditorStyles.boldLabel);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Unity Play 중에는 독립 검토를 멈춥니다. Play 종료 후 이 창을 다시 사용하세요.", MessageType.Info);
            return;
        }
        if (!string.IsNullOrEmpty(error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("초안 다시 불러오기")) { Release(); error = null; }
            return;
        }
        float duration = session != null ? session.Duration : 8.233334f;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("시작 파지", GUILayout.Width(85f))) { seconds = 0f; running = false; needsRender = true; }
            if (GUILayout.Button("전체 재생", GUILayout.Width(85f))) StartAt(0f);
            if (GUILayout.Button("팔 한 주기", GUILayout.Width(85f))) StartAt(session != null ? session.Data.cycleStartSeconds : 3.75f);
            if (GUILayout.Button(running ? "일시정지" : "이어 재생", GUILayout.Width(85f))) { running = !running; lastUpdate = EditorApplication.timeSinceStartup; }
            halfSpeed = GUILayout.Toggle(halfSpeed, "0.5배", "Button", GUILayout.Width(60f));
            GUILayout.Label(halfSpeed ? "느리게 확인" : "1배", GUILayout.Width(90f));
        }
        EditorGUI.BeginChangeCheck();
        seconds = EditorGUILayout.Slider("시간(초)", seconds, 0f, duration);
        if (EditorGUI.EndChangeCheck()) { running = false; needsRender = true; }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            view = (View)EditorGUILayout.EnumPopup("검토 시점", view);
            zoom = EditorGUILayout.Slider("확대", zoom, .75f, 1.6f);
            if (EditorGUI.EndChangeCheck()) needsRender = true;
        }
        EditorGUILayout.LabelField("원본 " + ((session != null ? session.Data.sourceStartSeconds : 56.5f) + seconds).ToString("F2") + "초  /  초안 " + seconds.ToString("F2") + "초");
        Rect area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(350f));
        Rect left = new Rect(area.x, area.y, area.width * .5f - 3f, area.height);
        Rect right = new Rect(area.x + area.width * .5f + 3f, area.y, area.width * .5f - 3f, area.height);
        if (Event.current.type == EventType.Repaint)
        {
            int width = Mathf.Clamp(Mathf.RoundToInt(right.width), 360, 1400);
            int height = Mathf.Clamp(Mathf.RoundToInt(right.height - 24f), 300, 900);
            if (width != renderWidth || height != renderHeight) { renderWidth = width; renderHeight = height; needsRender = true; }
            DrawPanel(left, sourceTexture, "원본 영상 · 관찰 자료");
            DrawPanel(right, previewTexture, "별도 환자와 양손 · 제작 초안");
        }
        EditorGUILayout.LabelField("접근·파지 해제 전체 과정이 아닌 관찰 구간입니다. 실습 씬·원본 가이드 CSV·판정 조건은 변경하지 않습니다.", EditorStyles.wordWrappedMiniLabel);
    }

    private static void DrawPanel(Rect rect, Texture texture, string label)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 22f), label, EditorStyles.boldLabel);
        rect.y += 24f; rect.height -= 24f;
        EditorGUI.DrawRect(rect, new Color(.035f, .05f, .075f));
        if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        else GUI.Label(rect, "불러오는 중…", EditorStyles.centeredGreyMiniLabel);
    }

    private void StartAt(float time) { seconds = time; running = true; needsRender = true; lastUpdate = EditorApplication.timeSinceStartup; }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdate);
        lastUpdate = now;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!string.IsNullOrEmpty(error)) return;
        try
        {
            if (session == null) { session = new Session(); needsRender = true; }
            if (running)
            {
                seconds += dt * (halfSpeed ? .5f : 1f);
                if (seconds >= session.Duration) { seconds = session.Duration; running = false; }
                needsRender = true;
            }
            if (!needsRender || now - lastRender < 1d / 30d) return;
            session.Sample(seconds);
            previewTexture = session.Render(renderWidth, renderHeight, view, zoom);
            int frame = session.SourceIndex(seconds);
            if (frame != sourceFrame)
            {
                if (sourceTexture == null) sourceTexture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!sourceTexture.LoadImage(File.ReadAllBytes(session.SourcePath(frame)), false)) throw new InvalidDataException("원본 영상 프레임을 읽지 못했습니다.");
                sourceFrame = frame;
            }
            needsRender = false;
            lastRender = now;
            Repaint();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Release();
            Debug.LogError("[Rib2HandsReview] Preview failed: " + exception);
            Repaint();
        }
    }

    internal sealed class Session : IDisposable
    {
        private sealed class Snapshot
        {
            public SkinnedMeshRenderer source;
            public GameObject holder;
            public Mesh mesh;
            public MeshRenderer renderer;
            public bool sourceEnabled;
        }

        public readonly Manifest Data;
        public readonly string CsvPath;
        public readonly string CsvHash;
        public float Duration => player.Duration;
        public Vector3 LeftPosition => left.Root.position;
        public Vector3 RightPosition => right.Root.position;
        public float WristResidual => Vector3.Distance(arm.Wrist.position, arm.Target.position);
        public float ReachCorrection => arm.MaximumReachCorrection;
        private PreviewRenderUtility preview;
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly List<GameObject> roots = new List<GameObject>();
        private TextAsset csv;
        private Rib2VideoTextScenePlayer player;
        private Rib2DemoPatientArm arm;
        private HandTransformMapper left, right;
        private readonly Vector3 target = new Vector3(10.2f, 1.1f, -1.38f);
        private bool disposed;

        public Session()
        {
            Scene loaded = default;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Unity Play before the private review.");
                string manifest = Absolute(ManifestPath);
                Data = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifest, Encoding.UTF8));
                if (Data == null || Data.frameCount < 2 || !Finite(Data.fps) || Data.fps <= 0f || !Finite(Data.sourceStartSeconds))
                    throw new InvalidDataException("Invalid two-hand draft manifest.");
                CsvPath = DraftInput(Data.csvPath);
                DraftInput(Data.sourceFramesDirectory);
                if (!File.Exists(CsvPath)) throw new FileNotFoundException("Draft CSV is missing.", CsvPath);
                CsvHash = Hash(CsvPath);
                for (int i = 0; i < Data.frameCount; i++)
                    if (!File.Exists(SourcePath(i))) throw new FileNotFoundException("Source video frame is missing.", SourcePath(i));
                preview = new PreviewRenderUtility();
                loaded = EditorSceneManager.OpenPreviewScene(DemoScene);
                foreach (GameObject root in loaded.GetRootGameObjects())
                {
                    SceneManager.MoveGameObjectToScene(root, preview.camera.gameObject.scene);
                    root.hideFlags = HideFlags.HideAndDontSave;
                    roots.Add(root);
                    preview.AddSingleGO(root);
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                    foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                    foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
                    var found = root.GetComponentInChildren<Rib2VideoTextScenePlayer>(true);
                    if (found != null)
                    {
                        if (player != null) throw new InvalidDataException("Multiple separate demonstration players.");
                        player = found;
                        arm = root.GetComponentInChildren<Rib2DemoPatientArm>(true);
                    }
                }
                EditorSceneManager.ClosePreviewScene(loaded); loaded = default;
                if (player == null || arm == null) throw new InvalidDataException("The separate scene lacks its guide player or patient arm.");
                var data = new SerializedObject(player);
                left = data.FindProperty("leftGuideHand").objectReferenceValue as HandTransformMapper;
                right = data.FindProperty("rightGuideHand").objectReferenceValue as HandTransformMapper;
                if (left == null || right == null || left.Joints.Count != 26 || right.Joints.Count != 26)
                    throw new InvalidDataException("Expected two complete 26-joint hand mappings.");
                csv = new TextAsset(File.ReadAllText(CsvPath, Encoding.UTF8)) { name = "Rib2 V02 private video-authored input", hideFlags = HideFlags.HideAndDontSave };
                data.FindProperty("motionCsv").objectReferenceValue = csv;
                data.ApplyModifiedPropertiesWithoutUndo();
                foreach (GameObject root in roots)
                    foreach (SkinnedMeshRenderer source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var snapshot = new Snapshot { source = source };
                        snapshots.Add(snapshot);
                        snapshot.mesh = new Mesh { name = "Rib2 hands private CPU skin", hideFlags = HideFlags.HideAndDontSave };
                        snapshot.mesh.MarkDynamic();
                        snapshot.holder = new GameObject("Rib2 review CPU snapshot") { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
                        snapshot.holder.transform.SetParent(source.transform, false);
                        snapshot.holder.AddComponent<MeshFilter>().sharedMesh = snapshot.mesh;
                        snapshot.renderer = snapshot.holder.AddComponent<MeshRenderer>();
                        snapshot.renderer.sharedMaterials = source.sharedMaterials;
                        snapshot.renderer.shadowCastingMode = ShadowCastingMode.Off;
                        snapshot.renderer.enabled = false;
                    }
                preview.camera.enabled = false;
                preview.camera.stereoTargetEye = StereoTargetEyeMask.None;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.035f, .05f, .075f);
                preview.camera.fieldOfView = 35f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 30f;
                preview.camera.cullingMask = 1 << 29;
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                preview.lights[1].intensity = .8f;
                preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);
                Sample(0f);
                if (Mathf.Abs(Duration - (Data.frameCount - 1) / Data.fps) > .002f)
                    throw new InvalidDataException("Manifest duration and the loaded guide CSV disagree.");
                Debug.Log("[Rib2HandsReview] Private review ready; L26/R26; source-only draft=" + CsvPath + "; no main scene or source assets saved.");
            }
            catch { Dispose(); throw; }
            finally { if (loaded.IsValid()) EditorSceneManager.ClosePreviewScene(loaded); }
        }

        public int SourceIndex(float time)
        {
            // Removed: sample indices beyond the decoded source interval, never a patient motion component.
            return Mathf.Clamp(Mathf.RoundToInt(time * Data.fps), 0, Data.frameCount - 1);
        }

        public string SourcePath(int index) => Path.Combine(DraftInput(Data.sourceFramesDirectory), "source_" + index.ToString("D5", CultureInfo.InvariantCulture) + ".jpg");

        public void Sample(float time)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Session));
            if (!player.Seek(time) || !arm.Evaluate()) throw new InvalidOperationException("Same-runtime-path guide/patient evaluation failed. Read [Rib2SceneDemo]/[Rib2SceneArm].");
            ValidateHand(left); ValidateHand(right);
            if (!Finite(arm.Wrist.position) || !Finite(arm.Target.position) || !Finite(WristResidual) || WristResidual > .005f)
                throw new InvalidDataException("The displayed patient wrist is not attached within 5 mm of its guide grip target.");
        }

        private static void ValidateHand(HandTransformMapper hand)
        {
            if (hand.Root == null || !Finite(hand.Root.position) || !Finite(hand.Root.rotation)) throw new InvalidDataException("Guide hand root is not finite.");
            foreach (Transform joint in hand.Joints)
                if (joint == null || !Finite(joint.localPosition) || !Finite(joint.localRotation)) throw new InvalidDataException("Guide joint pose is missing or not finite.");
        }

        public Texture Render(int width, int height, View direction, float magnification)
        {
            bool began = false;
            try
            {
                foreach (Snapshot snapshot in snapshots) snapshot.sourceEnabled = snapshot.source.enabled;
                foreach (Snapshot snapshot in snapshots)
                {
                    bool visible = snapshot.sourceEnabled && snapshot.source.gameObject.activeInHierarchy && !snapshot.source.forceRenderingOff;
                    snapshot.renderer.enabled = visible;
                    if (!visible) continue;
                    snapshot.source.BakeMesh(snapshot.mesh);
                    snapshot.source.enabled = false;
                }
                Vector3 offset = new Vector3(1.05f, 1.15f, -.7f);
                Vector3 up = Vector3.up;
                if (direction == View.Top) { offset = new Vector3(0f, 1.85f, 0f); up = Vector3.back; }
                else if (direction == View.Side) offset = new Vector3(1.85f, .5f, 0f);
                preview.camera.transform.position = target + offset / magnification;
                preview.camera.transform.LookAt(target, up);
                preview.BeginPreview(new Rect(0f, 0f, width, height), GUIStyle.none);
                began = true;
                preview.Render();
                Texture texture = preview.EndPreview(); began = false;
                return texture;
            }
            finally
            {
                try { if (began) preview.EndPreview(); }
                finally
                {
                    foreach (Snapshot snapshot in snapshots)
                    {
                        snapshot.source.enabled = snapshot.sourceEnabled;
                        snapshot.renderer.enabled = false;
                    }
                }
            }
        }

        public float HandViewportMargin()
        {
            float margin = 1f;
            foreach (Transform joint in left.Joints) margin = Mathf.Min(margin, JointViewportMargin(joint));
            foreach (Transform joint in right.Joints) margin = Mathf.Min(margin, JointViewportMargin(joint));
            return margin;
        }

        private float JointViewportMargin(Transform joint)
        {
            Vector3 point = preview.camera.WorldToViewportPoint(joint.position);
            if (!Finite(point) || point.z <= preview.camera.nearClipPlane) return -1f;
            return Mathf.Min(point.x, 1f - point.x, point.y, 1f - point.y);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Snapshot snapshot in snapshots)
            {
                if (snapshot.holder != null) DestroyImmediate(snapshot.holder);
                if (snapshot.mesh != null) DestroyImmediate(snapshot.mesh);
            }
            if (preview != null) preview.Cleanup();
            preview = null;
            if (csv != null) DestroyImmediate(csv);
        }
    }

    private sealed class CaptureJob : IDisposable
    {
        private Session session;
        private readonly Receipt report;
        private readonly Texture2D image;
        private bool complete;

        public CaptureJob()
        {
            report = new Receipt { utc = DateTimeOffset.UtcNow.ToString("o"), before = SceneSnapshot(), activeSceneBefore = SceneManager.GetActiveScene().handle };
            try
            {
                session = new Session();
                report.expectedFrames = session.Data.frameCount;
                report.sourceFrameCount = session.Data.frameCount;
                report.fps = session.Data.fps;
                report.durationSeconds = session.Duration;
                report.csvSha256 = session.CsvHash;
                report.manifestSha256 = Hash(Absolute(ManifestPath));
                report.sourceSceneSha256 = Hash(Absolute(DemoScene));
                report.implementationSha256 = Hash(Absolute("Assets/Editor/Rib2HandsVideoReviewWindow.cs"));
                report.outputFolder = Absolute(Folder + "/render_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
                if (Directory.Exists(report.outputFolder)) throw new IOException("Capture output already exists.");
                Directory.CreateDirectory(report.outputFolder);
                image = new Texture2D(960, 600, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                report.leftMinimum = report.leftMaximum = session.LeftPosition;
                report.rightMinimum = report.rightMaximum = session.RightPosition;
                Debug.Log("[Rib2HandsReview] Capture start; original scenes unchanged; " + report.outputFolder);
            }
            catch (Exception exception) { Finish(exception); throw; }
        }

        public bool Next()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Unity Play interrupted the isolated capture.");
                float time = report.framesWritten / report.fps;
                session.Sample(time);
                report.maximumPatientWristTargetResidualMetres = Mathf.Max(report.maximumPatientWristTargetResidualMetres, session.WristResidual);
                report.maximumReachRetargetMetres = Mathf.Max(report.maximumReachRetargetMetres, session.ReachCorrection);
                report.leftMinimum = Vector3.Min(report.leftMinimum, session.LeftPosition);
                report.leftMaximum = Vector3.Max(report.leftMaximum, session.LeftPosition);
                report.rightMinimum = Vector3.Min(report.rightMinimum, session.RightPosition);
                report.rightMaximum = Vector3.Max(report.rightMaximum, session.RightPosition);
                Texture rendered = session.Render(960, 600, View.SourceOblique, 1f);
                report.minimumHandViewportMargin = Mathf.Min(report.minimumHandViewportMargin, session.HandViewportMargin());
                if (report.minimumHandViewportMargin < .01f) throw new InvalidDataException("A guide hand joint leaves the fixed review-camera framing.");
                RenderTexture buffer = RenderTexture.GetTemporary(960, 600, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(rendered, buffer);
                    RenderTexture.active = buffer;
                    image.ReadPixels(new Rect(0f, 0f, 960f, 600f), 0, 0, false);
                    image.Apply(false);
                    File.WriteAllBytes(Path.Combine(report.outputFolder, "guide_" + report.framesWritten.ToString("D5", CultureInfo.InvariantCulture) + ".jpg"), image.EncodeToJPG(93));
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(buffer); }
                report.framesWritten++;
                if (report.framesWritten >= report.expectedFrames) { Finish(null); return false; }
                return true;
            }
            catch (Exception exception) { Finish(exception); return false; }
        }

        private void Finish(Exception exception)
        {
            if (complete) return;
            complete = true;
            if (exception != null) { report.error = exception.ToString(); report.posesFinite = false; }
            Dispose();
            report.after = SceneSnapshot();
            report.activeSceneAfter = SceneManager.GetActiveScene().handle;
            report.originalSceneStateUnchanged = report.activeSceneBefore == report.activeSceneAfter &&
                JsonUtility.ToJson(new SceneCollection { scenes = report.before }) == JsonUtility.ToJson(new SceneCollection { scenes = report.after });
            report.success = exception == null && report.framesWritten == report.expectedFrames && report.originalSceneStateUnchanged;
            if (!report.originalSceneStateUnchanged) report.error += " Original scene paths/count/dirty state changed during review.";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Absolute(ReportPath)));
            File.WriteAllText(Absolute(ReportPath), json, new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(report.outputFolder) && Directory.Exists(report.outputFolder)) File.WriteAllText(Path.Combine(report.outputFolder, "receipt.json"), json, new UTF8Encoding(false));
            if (report.success) Debug.Log("[Rib2HandsReview] Capture PASS; frames=" + report.framesWritten + "; scenes unchanged; " + report.outputFolder);
            else Debug.LogError("[Rib2HandsReview] Capture failed: " + report.error);
        }

        public void Dispose()
        {
            if (session != null) { session.Dispose(); session = null; }
            if (image != null) DestroyImmediate(image);
        }

        public void Cancel() { Finish(new OperationCanceledException("Editor reload/quit interrupted the isolated capture.")); }
    }

    [Serializable] private sealed class SceneCollection { public SceneState[] scenes; }
    private static CaptureJob capture;

    [MenuItem("GuideChuna/시험/제2늑골/양손 초안 검증 및 캡처")]
    public static void VerifyAndCapture()
    {
        if (capture != null) throw new InvalidOperationException("A two-hand review capture is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Unity Play before isolated capture.");
        capture = new CaptureJob();
        EditorApplication.update += CaptureTick;
        AssemblyReloadEvents.beforeAssemblyReload += CancelCapture;
        EditorApplication.quitting += CancelCapture;
    }

    private static void CaptureTick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (capture != null && capture.Next()) return;
        StopCaptureHooks();
        capture = null;
    }

    private static void CancelCapture()
    {
        StopCaptureHooks();
        if (capture != null) capture.Cancel();
        capture = null;
    }

    private static void StopCaptureHooks()
    {
        EditorApplication.update -= CaptureTick;
        AssemblyReloadEvents.beforeAssemblyReload -= CancelCapture;
        EditorApplication.quitting -= CancelCapture;
    }

    private static SceneState[] SceneSnapshot()
    {
        var states = new SceneState[SceneManager.sceneCount];
        for (int i = 0; i < states.Length; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            states[i] = new SceneState { handle = scene.handle, path = scene.path, dirty = scene.isDirty };
        }
        return states;
    }

    private static string DraftInput(string path)
    {
        string absolute = Absolute(path);
        string allowed = Absolute(Folder) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Draft input must stay inside the separate V02 folder.");
        return absolute;
    }

    private static string Absolute(string relative) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), relative));
    private static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .000001f;
}
