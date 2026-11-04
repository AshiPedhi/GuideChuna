using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Source comparison on a disposable anatomical-rig patient. No training assets are written.</summary>
public sealed class Rib2AnatomicalHandsReviewWindow : EditorWindow
{
    private const string OpenMenu = "GuideChuna/시험/제2늑골/해부학 리그 양손 재구성 V03";
    private const string CaptureMenu = "GuideChuna/시험/제2늑골/V03 원본 비교 검증 및 캡처";
    private const string ReportPath = "Temp/model-audit/rib2-anatomical-hands.json";
    private static readonly string[] ViewNames = { "3인칭 원본 시점", "상체 사선", "위에서", "시술자 1인칭" };

    private Rib2AnatomicalHandsReviewSession session;
    private Texture previewTexture;
    private Texture2D sourceTexture;
    private int sourceFrame = -1, sourceView = -1, view;
    private bool running, halfSpeed, showHands = true, needsRender = true, hasMeasurement;
    private float seconds;
    private double lastUpdate, lastRender;
    private int renderWidth = 720;
    private string error;
    private Rib2AnatomicalHandsReviewSession.SampleResult measured;

    [MenuItem(OpenMenu)]
    public static void Open()
    {
        var window = GetWindow<Rib2AnatomicalHandsReviewWindow>();
        window.Release(); window.error = null; window.seconds = 0f;
        window.titleContent = new GUIContent("제2늑골 해부학 V03");
        window.minSize = new Vector2(940f, 640f);
        window.Show();
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 1인칭 원본 비교")]
    public static void OpenFirstPerson()
    {
        Open();
        GetWindow<Rib2AnatomicalHandsReviewWindow>().view = 3;
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
        measured = default;
        hasMeasurement = false;
        if (session != null) { session.Dispose(); session = null; }
        if (sourceTexture != null) DestroyImmediate(sourceTexture);
        sourceTexture = null;
        sourceFrame = -1;
        sourceView = -1;
        needsRender = true;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("해부학 리그의 환자 팔을 영상에 맞춘 뒤 양손 파지를 배치한 별도 재구성입니다. 원본의 숨은 깊이·손가락은 가정이 포함되며, 실제 접촉 압력·장력은 측정하지 않습니다.", MessageType.Info);
        EditorGUILayout.LabelField("청록: 팔을 유도하는 시술자 손     주황: 흉부를 지지하는 시술자 손", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(session != null && session.VideoMatchedHands ?
            "동기화한 두 영상으로 맞춘 손목·전체 파지 · 부드러운 궤적 · 전완 피부 회전 분산" :
            "움직이는 환자 손: 손바닥은 몸 바깥쪽 · 손목은 뒤로 30° · 부드러운 궤적 재생", EditorStyles.miniLabel);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Unity Play 중에는 독립 검토를 멈춥니다. Play 종료 후 다시 사용할 수 있습니다.", MessageType.Info);
            return;
        }
        if (!string.IsNullOrEmpty(error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("재구성 다시 불러오기")) { Release(); error = null; }
            return;
        }
        float duration = session != null ? session.Duration : 247f / 30f;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("시작 파지", GUILayout.Width(85f))) Seek(0f);
            if (GUILayout.Button("전체 재생", GUILayout.Width(85f))) StartAt(0f);
            if (GUILayout.Button("팔 한 주기", GUILayout.Width(85f))) StartAt(3.75f);
            if (GUILayout.Button("자료 다시 읽기", GUILayout.Width(100f))) { Release(); error = null; seconds = 0f; }
            if (GUILayout.Button(running ? "일시정지" : "이어 재생", GUILayout.Width(85f)))
            {
                if (!running && seconds >= duration) seconds = 0f;
                running = !running;
                lastUpdate = EditorApplication.timeSinceStartup;
                needsRender = true;
            }
            halfSpeed = GUILayout.Toggle(halfSpeed, "0.5배", "Button", GUILayout.Width(65f));
            GUILayout.Label(halfSpeed ? "느리게 확인" : "1배", GUILayout.Width(85f));
        }
        EditorGUI.BeginChangeCheck();
        seconds = EditorGUILayout.Slider("시간(초)", seconds, 0f, duration);
        if (EditorGUI.EndChangeCheck()) { running = false; needsRender = true; }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            view = EditorGUILayout.Popup("검토 시점", view, ViewNames);
            showHands = !GUILayout.Toggle(!showHands, "환자만 보기", GUILayout.Width(110f));
            if (EditorGUI.EndChangeCheck()) needsRender = true;
        }
        EditorGUILayout.LabelField("원본 " + (Rib2AnatomicalHandsReviewSession.SourceStart + seconds).ToString("F2") + "초  /  재구성 " + seconds.ToString("F2") + "초");
        Rect area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(320f));
        Rect left = new Rect(area.x, area.y, area.width * .5f - 4f, area.height);
        Rect right = new Rect(area.x + area.width * .5f + 4f, area.y, area.width * .5f - 4f, area.height);
        if (Event.current.type == EventType.Repaint)
        {
            int width = Mathf.Clamp(Mathf.RoundToInt(right.width), 360, 1440);
            if (width != renderWidth) { renderWidth = width; needsRender = true; }
            DrawPanel(left, sourceTexture, view == 3 ? "1인칭 원본 · 3인칭보다 2.430초 앞선 시각" : "3인칭 원본 영상");
            DrawPanel(right, previewTexture, "해부학 리그 재구성");
        }
        if (hasMeasurement)
            EditorGUILayout.LabelField("원본 표지와 화면상 거리: 팔꿈치 " + measured.elbowErrorPixels.ToString("F1") + "px / 손목 " + measured.wristErrorPixels.ToString("F1") +
                "px · 카메라 가정 아래의 비교이며 실제 3D 정확도 수치가 아닙니다.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("시작 파지와 팔 한 주기를 검토합니다. 실습 씬·원본 손 데이터·판정 조건은 수정하지 않습니다.", EditorStyles.wordWrappedMiniLabel);
    }

    private static void DrawPanel(Rect rect, Texture texture, string label)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 22f), label, EditorStyles.boldLabel);
        rect.y += 24f; rect.height -= 24f;
        EditorGUI.DrawRect(rect, new Color(.035f, .05f, .075f));
        if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        else GUI.Label(rect, "불러오는 중…", EditorStyles.centeredGreyMiniLabel);
    }

    private void Seek(float time) { seconds = time; running = false; needsRender = true; }
    private void StartAt(float time) { seconds = time; running = true; needsRender = true; lastUpdate = EditorApplication.timeSinceStartup; }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float elapsed = (float)(now - lastUpdate);
        lastUpdate = now;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(error)) return;
        try
        {
            if (session == null) { session = new Rib2AnatomicalHandsReviewSession(); needsRender = true; }
            if (running)
            {
                seconds += elapsed * (halfSpeed ? .5f : 1f);
                if (seconds >= session.Duration) { seconds = session.Duration; running = false; }
                needsRender = true;
            }
            if (!needsRender || now - lastRender < 1d / 60d) return;
            session.Sample(seconds, false);
            int height = Mathf.RoundToInt(renderWidth * 9f / 16f);
            previewTexture = session.Render(renderWidth, height, view, showHands);
            measured = session.Measure(false);
            hasMeasurement = true;
            // Removed: indices beyond the explicitly sampled source interval, not motion depth or a joint component.
            int index = Mathf.Clamp(Mathf.RoundToInt(seconds * Rib2AnatomicalHandsReviewSession.Fps), 0, Rib2AnatomicalHandsReviewSession.FrameCount - 1);
            if (index != sourceFrame || sourceView != view)
            {
                if (sourceTexture == null) sourceTexture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                string sourcePath = view == 3 ? Rib2AnatomicalHandsReviewSession.BodycamSourcePath(index) : Rib2AnatomicalHandsReviewSession.SourcePath(index);
                if (!sourceTexture.LoadImage(File.ReadAllBytes(sourcePath), false)) throw new InvalidDataException("원본 프레임을 읽지 못했습니다.");
                sourceFrame = index;
                sourceView = view;
            }
            needsRender = false;
            lastRender = now;
            Repaint();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Release();
            Debug.LogError("[Rib2AnatomicalHandsV03] Preview failed: " + exception);
            Repaint();
        }
    }

    [Serializable] private sealed class SceneState
    {
        public int handle;
        public string path;
        public bool dirty;
    }

    [Serializable] private sealed class SceneCollection { public SceneState[] scenes; }
    [Serializable] private sealed class WithheldObservation
    {
        public float actualSourceSeconds = 0f;
        public float[] patientElbow = null, patientWrist = null, leftWrist = null, leftPalm = null, rightWrist = null, rightPalm = null;
    }
    [Serializable] private sealed class WithheldSet { public WithheldObservation[] keys = null; }
    [Serializable] private sealed class GloveShapeObservation
    {
        public int frameIndex = 0;
        public float[] wristPixel = null, envelopeTipPixel = null;
    }
    [Serializable] private sealed class GloveShapeSet { public GloveShapeObservation[] observations = null; }
    [Serializable] private sealed class GloveShapeResult
    {
        public int frameIndex;
        public Vector2 sourceExtent, actualExtent;
        public float angleErrorDegrees, extentVectorErrorPixels;
    }

    [Serializable] private sealed class Receipt
    {
        public bool success, completedAllSamples, measurementsFinite = true, patientProjectionPassed, guideProjectionPassed, originalSceneStateUnchanged;
        public bool clinicalOrPlayValidation = false, sourceContactAccuracyValidated = false;
        public bool detailedFingerCorrectionEnabled = false;
        public bool originalVideoHandPoseRequired = false, requestedHandDirectionPassed = true, heldoutPatientTrajectoryPassed;
        public string requestedPoseScope = "User requested moving patient palm away from chest toward hand and wrist extension30deg. This overrides the original video hand pose. Source guide projection/whole-glove differences remain diagnostics and are not required matches.";
        public int motionSmoothingPasses;
        public float minimumPalmOutwardDot = 1f, maximumAppliedWristExtensionErrorDegrees;
        public string smoothMotionSha256, handDirectionSha256;
        public string forearmSkinSha256;
        public bool previewForearmSkinCorrectionEnabled = true;
        public bool heldoutProjectionPassed;
        public string error = "", utc, outputFolder, implementationSha256, engineSha256, sourceObservationsSha256;
        public string patientFitSha256, contactHelperSha256, heldoutObservationsSha256;
        public string handSolutionSha256, nativeGeometryCalibrationSha256, solutionLoaderSha256, calibrationExporterSha256;
        public string nativeGeometryCalibrationFile;
        public string gloveShapeObservationsSha256;
        public bool gloveEnvelopeDirectionPassed;
        public float maximumGloveEnvelopeAngleErrorDegrees, maximumGloveEnvelopeVectorErrorPixels;
        public float gloveEnvelopeAngleToleranceDegrees = 25f, gloveEnvelopeVectorTolerancePixels = 30f;
        public List<GloveShapeResult> gloveShapeSamples = new List<GloveShapeResult>();
        public string sourceCameraScope = "Projection under the reconstruction camera assumptions, not recovered unique 3D truth. Shoulder alignment may be imposed by camera registration and is not an independent pass result.";
        public string contactScope = "Contact gap is a geometric model check, not source-observed pressure, force or clinical surface-contact accuracy.";
        public float fps, durationSeconds, maximumElbowErrorPixels, maximumWristErrorPixels;
        public float maximumMovingWristErrorPixels, maximumMovingPalmErrorPixels, maximumSupportWristErrorPixels, maximumSupportPalmErrorPixels, maximumContactGapMetres;
        public float patientTolerancePixels = 20f, guideTolerancePixels = 25f;
        public float heldoutTolerancePixels = 30f, maximumHeldoutPatientErrorPixels, maximumHeldoutGuideErrorPixels;
        public float maximumGuideJointInsetMetres;
        public float maximumPatientHandTipCueErrorPixels;
        public float maximumUpperLengthErrorMetres, maximumLowerLengthErrorMetres;
        public float maximumFingerSkinInsetBeforeMetres, maximumFingerSkinInsetAfterMetres, maximumFingerCorrectionRootDisplacementMetres, maximumFingerCorrectionWristDisplacementMetres, guideSkinSamplingErrorMetres;
        public int framesWithRemainingFingerSampleIntrusion;
        public float maximumAllGuideSkinInsetMetres;
        public float maximumReciprocalSkinInsetMetres;
        public string fingerSkinScope = "Detailed finger correction disabled by user scope. Fixed coarse hand pose only; no individual finger-motion or finger-collision validation is claimed.";
        public string guideComparisonScope = "Cuff markers plus approximate projected glove regions; Palm0 distances are diagnostics only because the source central palm is occluded/unidentified. Projected glove region does not infer occlusion.";
        public float maximumMovingGloveRegionErrorPixels, maximumSupportGloveRegionErrorPixels;
        public string intrusionScope = "Guide finger joint centers vs nearest patient surface normal; approximation only, not triangle collision or full mesh nonpenetration proof.";
        public int framesWritten, expectedFrames, sourceFramesFound, activeSceneBefore, activeSceneAfter;
        public SceneState[] before, after;
        public List<Rib2AnatomicalHandsReviewSession.SampleResult> samples = new List<Rib2AnatomicalHandsReviewSession.SampleResult>();
        public List<Rib2AnatomicalHandsReviewSession.SampleResult> heldoutSamples = new List<Rib2AnatomicalHandsReviewSession.SampleResult>();
    }

    private sealed class CaptureJob : IDisposable
    {
        private Rib2AnatomicalHandsReviewSession session;
        private Texture2D image;
        private readonly Receipt report;
        private bool finished;

        public CaptureJob()
        {
            report = new Receipt { utc = DateTimeOffset.UtcNow.ToString("o"), before = SceneSnapshot(), activeSceneBefore = SceneManager.GetActiveScene().handle };
            try
            {
                session = new Rib2AnatomicalHandsReviewSession();
                session.SaveCurveCache();
                report.expectedFrames = Rib2AnatomicalHandsReviewSession.FrameCount;
                report.fps = Rib2AnatomicalHandsReviewSession.Fps;
                report.durationSeconds = session.Duration;
                report.implementationSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewWindow.cs"));
                report.engineSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.cs"));
                report.sourceObservationsSha256 = Hash(Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/source_observations.json"));
                report.patientFitSha256 = Hash(Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/patient_fit.json"));
                report.contactHelperSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsContact.cs"));
                report.handSolutionSha256 = session.HandSolutionHash;
                report.motionSmoothingPasses = session.MotionSmoothingPasses;
                report.smoothMotionSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.SmoothMotion.cs"));
                report.handDirectionSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.HandDirection.cs"));
                report.forearmSkinSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalForearmSkin.cs"));
                report.nativeGeometryCalibrationFile = Path.GetFileName(session.NativeGeometryCalibrationPath);
                report.nativeGeometryCalibrationSha256 = Hash(session.NativeGeometryCalibrationPath);
                report.solutionLoaderSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.Solution.cs"));
                report.calibrationExporterSha256 = Hash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.Calibration.cs"));
                for (int index = 0; index < report.expectedFrames; index++)
                {
                    if (!File.Exists(Rib2AnatomicalHandsReviewSession.SourcePath(index))) throw new FileNotFoundException("Original comparison frame is missing.", Rib2AnatomicalHandsReviewSession.SourcePath(index));
                    report.sourceFramesFound++;
                }
                report.outputFolder = Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/render_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
                if (Directory.Exists(report.outputFolder)) throw new IOException("Capture folder already exists; refusing to overwrite.");
                Directory.CreateDirectory(report.outputFolder);
                image = new Texture2D(960, 540, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                Debug.Log("[Rib2AnatomicalHandsV03] Source-comparison capture started: " + report.outputFolder);
            }
            catch (Exception exception) { Finish(exception); throw; }
        }

        public bool Next()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Unity Play interrupted the independent capture.");
                float time = report.framesWritten / report.fps;
                session.Sample(time);
                Texture texture = session.Render(960, 540, 0, true);
                Rib2AnatomicalHandsReviewSession.SampleResult sample = session.Measure();
                report.samples.Add(sample);
                report.minimumPalmOutwardDot = Mathf.Min(report.minimumPalmOutwardDot, sample.patientPalmOutwardDot);
                report.maximumAppliedWristExtensionErrorDegrees = Mathf.Max(report.maximumAppliedWristExtensionErrorDegrees, Mathf.Abs(sample.appliedWristFlexionDegrees + Rib2AnatomicalHandsReviewSession.RequestedWristExtensionDegrees));
                bool finite = sample.finite && Finite(sample.elbowErrorPixels) && Finite(sample.wristErrorPixels) &&
                    Finite(sample.movingWristErrorPixels) && Finite(sample.movingPalmErrorPixels) && Finite(sample.supportWristErrorPixels) &&
                    Finite(sample.supportPalmErrorPixels) && Finite(sample.measuredElbowDegrees) && Finite(sample.contactGapMetres);
                report.measurementsFinite &= finite;
                if (!finite) throw new InvalidDataException("An actual output measurement is missing or nonfinite.");
                report.maximumElbowErrorPixels = Mathf.Max(report.maximumElbowErrorPixels, sample.elbowErrorPixels);
                report.maximumWristErrorPixels = Mathf.Max(report.maximumWristErrorPixels, sample.wristErrorPixels);
                report.maximumMovingWristErrorPixels = Mathf.Max(report.maximumMovingWristErrorPixels, sample.movingWristErrorPixels);
                report.maximumMovingPalmErrorPixels = Mathf.Max(report.maximumMovingPalmErrorPixels, sample.movingPalmErrorPixels);
                report.maximumSupportWristErrorPixels = Mathf.Max(report.maximumSupportWristErrorPixels, sample.supportWristErrorPixels);
                report.maximumSupportPalmErrorPixels = Mathf.Max(report.maximumSupportPalmErrorPixels, sample.supportPalmErrorPixels);
                report.maximumContactGapMetres = Mathf.Max(report.maximumContactGapMetres, sample.contactGapMetres);
                report.maximumGuideJointInsetMetres = Mathf.Max(report.maximumGuideJointInsetMetres, sample.guideJointMaximumInsetMetres);
                report.maximumPatientHandTipCueErrorPixels = Mathf.Max(report.maximumPatientHandTipCueErrorPixels, sample.patientHandTipErrorPixels);
                report.maximumUpperLengthErrorMetres = Mathf.Max(report.maximumUpperLengthErrorMetres, sample.upperLengthErrorMetres);
                report.maximumLowerLengthErrorMetres = Mathf.Max(report.maximumLowerLengthErrorMetres, sample.lowerLengthErrorMetres);
                report.maximumFingerSkinInsetBeforeMetres = Mathf.Max(report.maximumFingerSkinInsetBeforeMetres, sample.fingerCollision.beforeMaximumInsetMetres);
                report.maximumFingerSkinInsetAfterMetres = Mathf.Max(report.maximumFingerSkinInsetAfterMetres, sample.fingerCollision.afterMaximumInsetMetres);
                report.maximumFingerCorrectionRootDisplacementMetres = Mathf.Max(report.maximumFingerCorrectionRootDisplacementMetres, sample.fingerCollision.rootDisplacementMetres);
                report.maximumFingerCorrectionWristDisplacementMetres = Mathf.Max(report.maximumFingerCorrectionWristDisplacementMetres, sample.fingerCollision.wristDisplacementMetres);
                report.guideSkinSamplingErrorMetres = Mathf.Max(report.guideSkinSamplingErrorMetres, sample.guideSkinSamplingErrorMetres);
                if (sample.fingerCollision.afterIntrudingVertices > 0) report.framesWithRemainingFingerSampleIntrusion++;
                report.maximumAllGuideSkinInsetMetres = Mathf.Max(report.maximumAllGuideSkinInsetMetres, sample.sampledSurfaceContact.maxInsetMetres);
                report.maximumReciprocalSkinInsetMetres = Mathf.Max(report.maximumReciprocalSkinInsetMetres, sample.sampledSurfaceContact.reciprocalMaxInsetMetres);
                report.maximumMovingGloveRegionErrorPixels = Mathf.Max(report.maximumMovingGloveRegionErrorPixels, sample.movingGloveRegionErrorPixels);
                report.maximumSupportGloveRegionErrorPixels = Mathf.Max(report.maximumSupportGloveRegionErrorPixels, sample.supportGloveRegionErrorPixels);
                RenderTexture temporary = RenderTexture.GetTemporary(960, 540, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(texture, temporary);
                    RenderTexture.active = temporary;
                    image.ReadPixels(new Rect(0f, 0f, 960f, 540f), 0, 0, false);
                    image.Apply(false);
                    File.WriteAllBytes(Path.Combine(report.outputFolder, "anatomical_" + report.framesWritten.ToString("D5", CultureInfo.InvariantCulture) + ".jpg"), image.EncodeToJPG(93));
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
                report.framesWritten++;
                if (report.framesWritten >= report.expectedFrames) { Finish(null); return false; }
                return true;
            }
            catch (Exception exception) { Finish(exception); return false; }
        }

        private void Finish(Exception exception)
        {
            if (finished) return;
            finished = true;
            if (exception == null && session != null)
            {
                try
                {
                    string path = Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/heldout_observations.json");
                    report.heldoutObservationsSha256 = Hash(path);
                    var withheld = JsonUtility.FromJson<WithheldSet>(File.ReadAllText(path));
                    foreach (var observation in withheld.keys)
                    {
                        session.Sample(observation.actualSourceSeconds - Rib2AnatomicalHandsReviewSession.SourceStart);
                        var sample = session.Measure();
                        sample.elbowErrorPixels = Vector2.Distance(sample.elbow, Point(observation.patientElbow));
                        sample.wristErrorPixels = Vector2.Distance(sample.wrist, Point(observation.patientWrist));
                        sample.movingWristErrorPixels = Vector2.Distance(sample.movingWrist, Point(observation.leftWrist));
                        sample.movingPalmErrorPixels = Vector2.Distance(sample.movingPalm, Point(observation.leftPalm));
                        sample.supportWristErrorPixels = Vector2.Distance(sample.supportWrist, Point(observation.rightWrist));
                        sample.supportPalmErrorPixels = Vector2.Distance(sample.supportPalm, Point(observation.rightPalm));
                        var regions = session.MeasureGloveRegions(Point(observation.leftPalm), Point(observation.rightPalm));
                        sample.movingGloveRegionErrorPixels = regions.x; sample.supportGloveRegionErrorPixels = regions.y;
                        sample.notes = "Actual anatomical output versus independently annotated withheld source frame; points not used by the patient/hand fitting inputs.";
                        report.heldoutSamples.Add(sample);
                        report.maximumHeldoutPatientErrorPixels = Mathf.Max(report.maximumHeldoutPatientErrorPixels, Mathf.Max(sample.elbowErrorPixels, sample.wristErrorPixels));
                        report.maximumHeldoutGuideErrorPixels = Mathf.Max(report.maximumHeldoutGuideErrorPixels, Mathf.Max(Mathf.Max(sample.movingWristErrorPixels, sample.movingGloveRegionErrorPixels), Mathf.Max(sample.supportWristErrorPixels, sample.supportGloveRegionErrorPixels)));
                    }
                    report.heldoutProjectionPassed = report.heldoutSamples.Count > 0 && report.maximumHeldoutPatientErrorPixels <= report.heldoutTolerancePixels && report.maximumHeldoutGuideErrorPixels <= report.heldoutTolerancePixels;
                    report.heldoutPatientTrajectoryPassed = report.heldoutSamples.Count > 0 && report.maximumHeldoutPatientErrorPixels <= report.heldoutTolerancePixels;
                    string shapePath = Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/guide_shape_observations.json");
                    report.gloveShapeObservationsSha256 = Hash(shapePath);
                    var shapes = JsonUtility.FromJson<GloveShapeSet>(File.ReadAllText(shapePath));
                    foreach (var shape in shapes.observations)
                    {
                        session.Sample(shape.frameIndex / report.fps);
                        Vector2 sourceExtent = Point(shape.envelopeTipPixel) - Point(shape.wristPixel);
                        Vector2 actualExtent = session.MeasureMovingGloveEnvelope();
                        var comparison = new GloveShapeResult { frameIndex = shape.frameIndex, sourceExtent = sourceExtent, actualExtent = actualExtent,
                            angleErrorDegrees = Vector2.Angle(sourceExtent, actualExtent), extentVectorErrorPixels = Vector2.Distance(sourceExtent, actualExtent) };
                        report.gloveShapeSamples.Add(comparison);
                        report.maximumGloveEnvelopeAngleErrorDegrees = Mathf.Max(report.maximumGloveEnvelopeAngleErrorDegrees, comparison.angleErrorDegrees);
                        report.maximumGloveEnvelopeVectorErrorPixels = Mathf.Max(report.maximumGloveEnvelopeVectorErrorPixels, comparison.extentVectorErrorPixels);
                    }
                    report.gloveEnvelopeDirectionPassed = report.gloveShapeSamples.Count == 3 && report.maximumGloveEnvelopeAngleErrorDegrees <= report.gloveEnvelopeAngleToleranceDegrees && report.maximumGloveEnvelopeVectorErrorPixels <= report.gloveEnvelopeVectorTolerancePixels;
                }
                catch (Exception validationError) { exception = validationError; }
            }
            if (exception != null) report.error = exception.ToString();
            Dispose();
            report.after = SceneSnapshot();
            report.activeSceneAfter = SceneManager.GetActiveScene().handle;
            report.originalSceneStateUnchanged = report.activeSceneBefore == report.activeSceneAfter &&
                JsonUtility.ToJson(new SceneCollection { scenes = report.before }) == JsonUtility.ToJson(new SceneCollection { scenes = report.after });
            report.completedAllSamples = report.framesWritten == report.expectedFrames && report.expectedFrames > 0;
            report.patientProjectionPassed = report.maximumElbowErrorPixels <= report.patientTolerancePixels && report.maximumWristErrorPixels <= report.patientTolerancePixels;
            report.guideProjectionPassed = report.maximumMovingWristErrorPixels <= report.guideTolerancePixels && report.maximumMovingGloveRegionErrorPixels <= report.guideTolerancePixels &&
                report.maximumSupportWristErrorPixels <= report.guideTolerancePixels && report.maximumSupportGloveRegionErrorPixels <= report.guideTolerancePixels;
            report.requestedHandDirectionPassed = report.minimumPalmOutwardDot > 0f && report.maximumAppliedWristExtensionErrorDegrees < .01f;
            bool requestedPoseChecks = report.patientProjectionPassed && report.heldoutPatientTrajectoryPassed && report.requestedHandDirectionPassed;
            bool originalHandChecks = !report.originalVideoHandPoseRequired || (report.guideProjectionPassed && report.heldoutProjectionPassed && report.gloveEnvelopeDirectionPassed);
            report.success = exception == null && report.completedAllSamples && report.measurementsFinite && requestedPoseChecks && originalHandChecks && report.originalSceneStateUnchanged;
            if (!report.originalSceneStateUnchanged) report.error += " Normal scene path/handle/dirty/active state changed.";
            if (!report.patientProjectionPassed) report.error += " Patient output projection exceeds the source landmark comparison tolerance.";
            if (report.originalVideoHandPoseRequired && !report.guideProjectionPassed) report.error += " Guide output projection exceeds the source landmark comparison tolerance.";
            if (!report.heldoutPatientTrajectoryPassed) report.error += " Independent held-out patient trajectory comparison did not pass.";
            if (report.originalVideoHandPoseRequired && !report.gloveEnvelopeDirectionPassed) report.error += " Whole-glove source envelope direction/extent comparison did not pass.";
            if (!report.requestedHandDirectionPassed) report.error += " Requested moving-hand outward facing or wrist extension did not pass.";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Absolute(ReportPath)));
            File.WriteAllText(Absolute(ReportPath), json, new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(report.outputFolder) && Directory.Exists(report.outputFolder)) File.WriteAllText(Path.Combine(report.outputFolder, "receipt.json"), json, new UTF8Encoding(false));
            if (report.success) Debug.Log("[Rib2AnatomicalHandsV03] Requested hand pose and patient trajectory checks PASS; original guide-hand image match is diagnostic; frames=" + report.framesWritten + "; report=" + report.outputFolder);
            else Debug.LogError("[Rib2AnatomicalHandsV03] Source-comparison capture failed: " + report.error);
        }
        private static Vector2 Point(float[] value) => new Vector2(value[0], value[1]);

        public void Dispose()
        {
            if (session != null) { session.Dispose(); session = null; }
            if (image != null) { DestroyImmediate(image); image = null; }
        }

        public void Cancel() { Finish(new OperationCanceledException("Editor reload/quit interrupted this independent capture.")); }
    }

    private static CaptureJob capture;

    [MenuItem(CaptureMenu)]
    public static void VerifyAndCapture()
    {
        if (File.Exists(Absolute(Rib2AnatomicalHandsReviewSession.Folder + "/hand_solution_bodycam_v1.json")))
        {
            Rib2AnatomicalHandsReviewSession.CaptureBodycamMenu();
            return;
        }
        if (capture != null) throw new InvalidOperationException("A V03 comparison capture is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Unity Play before private comparison capture.");
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

    private static string Absolute(string relative) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), relative));
    private static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
