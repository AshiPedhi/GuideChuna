using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [Serializable] private sealed class BodycamCameraKey
    {
        public int frameIndex = 0;
        public float[] position = null, rotation = null;
    }
    [Serializable] private sealed class BodycamCameraData
    {
        public int imageWidth = 960, imageHeight = 540;
        public float verticalFovDegrees = 90f, focalLengthPixels = 270f, principalX = 480f, principalY = 270f;
        public BodycamCameraKey[] poseKeys = null;
    }
    private BodycamCameraData bodycamCamera;
    private string bodycamCameraHash;
    private string bodycamCameraFilename = "bodycam_camera_v1.json";
    public bool BodycamCameraAvailable => File.Exists(Absolute(Folder + "/" + bodycamCameraFilename));

    private void LoadBodycamCamera()
    {
        if (bodycamCamera != null) return;
        string path = Absolute(Folder + "/" + bodycamCameraFilename);
        bodycamCamera = JsonUtility.FromJson<BodycamCameraData>(File.ReadAllText(path));
        if (bodycamCamera == null || bodycamCamera.poseKeys == null || bodycamCamera.poseKeys.Length < 2 ||
            bodycamCamera.imageWidth <= 0 || bodycamCamera.imageHeight <= 0 ||
            !(bodycamCamera.verticalFovDegrees > 20f && bodycamCamera.verticalFovDegrees < 150f))
            throw new InvalidDataException("Bodycam perspective-camera comparison data is incomplete.");
        int previous = -1;
        foreach (var key in bodycamCamera.poseKeys)
        {
            if (key.frameIndex <= previous || key.position == null || key.position.Length != 3 || key.rotation == null || key.rotation.Length != 4)
                throw new InvalidDataException("Bodycam pose keys are not ordered complete rigid transforms.");
            previous = key.frameIndex;
        }
        if (bodycamCamera.poseKeys[0].frameIndex != 0 || previous != FrameCount - 1)
            throw new InvalidDataException("Bodycam camera must cover the entire comparison interval.");
        bodycamCameraHash = FileHash(path);
        Debug.Log("[Rib2FirstPerson] Perspective camera hypothesis loaded: FOV=" + bodycamCamera.verticalFovDegrees.ToString("F2") +
            "; keys=" + bodycamCamera.poseKeys.Length + "; bodycam=MOV-2.430s. Torso registration is not a unique recovered camera or hand-match proof.");
    }

    private void BodycamPose(float time, out Vector3 position, out Quaternion rotation)
    {
        LoadBodycamCamera();
        float frame = time * Fps;
        int lo = 0;
        while (lo + 1 < bodycamCamera.poseKeys.Length - 1 && bodycamCamera.poseKeys[lo + 1].frameIndex < frame) lo++;
        var a = bodycamCamera.poseKeys[lo]; var b = bodycamCamera.poseKeys[lo + 1];
        // Removed: times beyond the observed interval. Camera translation,
        // elevation and roll are retained; camera never tracks the patient hand.
        float t = Mathf.InverseLerp(a.frameIndex, b.frameIndex, frame);
        position = Vector3.Lerp(V3(a.position), V3(b.position), t);
        rotation = Quaternion.Slerp(BodycamRotation(a.rotation), BodycamRotation(b.rotation), t);
    }

    private static Quaternion BodycamRotation(float[] q) => new Quaternion(q[0], q[1], q[2], q[3]).normalized;

    private void ConfigureBodycamCamera(float time)
    {
        BodycamPose(time, out Vector3 position, out Quaternion rotation);
        var camera = preview.camera;
        camera.orthographic = false;
        camera.fieldOfView = bodycamCamera.verticalFovDegrees;
        camera.transform.SetPositionAndRotation(position, rotation);
        Matrix4x4 projection = Matrix4x4.Perspective(camera.fieldOfView, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
        projection.m02 = 1f - 2f * bodycamCamera.principalX / bodycamCamera.imageWidth;
        projection.m12 = 2f * bodycamCamera.principalY / bodycamCamera.imageHeight - 1f;
        camera.projectionMatrix = projection;
    }

    public Vector3 ProjectBodycam(Vector3 world)
    {
        BodycamPose(seconds, out Vector3 position, out Quaternion rotation);
        Vector3 local = Quaternion.Inverse(rotation) * (world - position);
        float focal = bodycamCamera.imageHeight * .5f / Mathf.Tan(bodycamCamera.verticalFovDegrees * .5f * Mathf.Deg2Rad);
        return new Vector3(bodycamCamera.principalX + focal * local.x / local.z,
            bodycamCamera.principalY - focal * local.y / local.z, local.z);
    }

    public static string BodycamSourcePath(int frame) => Absolute(Folder + "/bodycam_frames/bodycam_" + frame.ToString("D5") + ".jpg");

    private static string FirstPersonSceneState()
    {
        var rows = new List<object>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            rows.Add(CalibrationObject("handle", scene.handle, "path", scene.path, "dirty", scene.isDirty));
        }
        var text = new StringBuilder();
        CalibrationWrite(text, CalibrationObject("active", SceneManager.GetActiveScene().handle, "scenes", rows));
        return text.ToString();
    }

    private void SaveBodycamImage(Texture texture, string path, Texture2D image)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(image.width, image.height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, temporary); RenderTexture.active = temporary;
            image.ReadPixels(new Rect(0f, 0f, image.width, image.height), 0, 0, false); image.Apply(false);
            File.WriteAllBytes(path, image.EncodeToJPG(92));
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
    }

    private static BodycamCaptureJob bodycamCapture;
    [MenuItem("GuideChuna/시험/제2늑골/V03 양 시점 비교 및 캡처")]
    public static void CaptureBodycamMenu()
    {
        StartBodycamCapture(false, true);
    }
    [MenuItem("GuideChuna/시험/제2늑골/V03 1인칭 주요자세 비교")]
    private static void CaptureBodycamKeyMenu() { StartBodycamCapture(true, true); }
    [MenuItem("GuideChuna/시험/제2늑골/V03 이전 손자세 1인칭 비교")]
    private static void CapturePreviousBodycamKeyMenu() { StartBodycamCapture(true, false); }
    [MenuItem("GuideChuna/시험/제2늑골/V03 파지기하 임시 비교")]
    private static void CaptureContactSeedMenu() { StartBodycamCapture(true, false, "hand_contact_seed_bodycam_preliminary_v0.json"); }
    [MenuItem("GuideChuna/시험/제2늑골/V03 양 시점 후보 주요자세")]
    private static void CaptureCandidateKeyMenu()
    {
        string latest = null; DateTime modified = DateTime.MinValue;
        foreach (string path in Directory.GetFiles(Absolute(Folder), "hand_solution_bodycam_candidate_v*.json"))
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp > modified) { latest = path; modified = stamp; }
        }
        if (latest == null) throw new FileNotFoundException("No two-view hand candidate is available.");
        StartBodycamCapture(true, false, Path.GetFileName(latest));
    }
    private static void StartBodycamCapture(bool keyOnly, bool useSynchronizedHands, string solutionFile = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || bodycamCapture != null)
            throw new InvalidOperationException("Two-view capture requires edit mode and no active capture.");
        bodycamCapture = new BodycamCaptureJob(keyOnly, useSynchronizedHands, solutionFile);
        EditorApplication.update += BodycamCaptureTick;
        AssemblyReloadEvents.beforeAssemblyReload += CancelBodycamCapture;
        EditorApplication.quitting += CancelBodycamCapture;
    }
    private static void BodycamCaptureTick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (bodycamCapture != null && bodycamCapture.Next()) return;
        CancelBodycamCapture();
    }
    private static void CancelBodycamCapture()
    {
        EditorApplication.update -= BodycamCaptureTick;
        AssemblyReloadEvents.beforeAssemblyReload -= CancelBodycamCapture;
        EditorApplication.quitting -= CancelBodycamCapture;
        if (bodycamCapture != null) bodycamCapture.Dispose();
        bodycamCapture = null;
    }

    private sealed class BodycamCaptureJob : IDisposable
    {
        private readonly Rib2AnatomicalHandsReviewSession session;
        private readonly string directory, before;
        private readonly Texture2D image;
        private readonly List<object> rows = new List<object>();
        private readonly int[] selectedFrames;
        private int frame;
        private bool finished;
        private float projectionAgreement;
        public BodycamCaptureJob(bool keyOnly, bool useSynchronizedHands, string solutionFile)
        {
            if (keyOnly) selectedFrames = new[] { 0, 10, 112, 172, 181, 202, 208, 247 };
            else { selectedFrames = new int[FrameCount]; for (int i = 0; i < FrameCount; i++) selectedFrames[i] = i; }
            before = FirstPersonSceneState();
            directory = Absolute(Folder + (keyOnly ? "/bodycam_keys_" : "/bodycam_render_") + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            if (Directory.Exists(directory)) throw new IOException("Capture directory already exists.");
            Directory.CreateDirectory(directory);
            try
            {
                session = new Rib2AnatomicalHandsReviewSession(false, -1f, DefaultSmoothingPasses, useSynchronizedHands, solutionFile); session.LoadBodycamCamera();
                image = new Texture2D(960, 540, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            catch { session?.Dispose(); throw; }
            Debug.Log("[Rib2FirstPerson] Two-view native capture started: " + directory);
        }
        public bool Next()
        {
            if (finished) return false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play interrupted private capture.");
                int modelFrame = selectedFrames[frame];
                session.Sample(modelFrame / (float)Fps);
                session.SaveBodycamImage(session.Render(960, 540, 0), Path.Combine(directory, "third_" + modelFrame.ToString("D5") + ".jpg"), image);
                session.SaveBodycamImage(session.Render(960, 540, 3), Path.Combine(directory, "first_" + modelFrame.ToString("D5") + ".jpg"), image);
                var points = new Dictionary<string, object>();
                foreach (var pair in new[] {
                    new KeyValuePair<string, Vector3>("patientElbow", session.lower.position),
                    new KeyValuePair<string, Vector3>("patientWrist", session.fingers.Wrist.position),
                    new KeyValuePair<string, Vector3>("patientPalm", session.fingers.PalmCenterWorld),
                    new KeyValuePair<string, Vector3>("patientTip", session.handTip.position + (session.handTip.position - session.handPrevious.position) * .55f),
                    new KeyValuePair<string, Vector3>("patientThumb", session.fingers.Thumb.position),
                    new KeyValuePair<string, Vector3>("movingWrist", session.moving.WristWorld),
                    new KeyValuePair<string, Vector3>("movingPalm", session.moving.Joint(0).position),
                    new KeyValuePair<string, Vector3>("supportWrist", session.support.WristWorld),
                    new KeyValuePair<string, Vector3>("supportPalm", session.support.Joint(0).position) })
                {
                    Vector3 projected = session.ProjectBodycam(pair.Value);
                    // An unobserved hand may leave the bodycam field or pass
                    // behind it. Keep signed depth; visibility-specific source
                    // checks decide validity instead of hiding those samples.
                    if (float.IsNaN(projected.x) || float.IsInfinity(projected.x) || float.IsNaN(projected.y) || float.IsInfinity(projected.y))
                        throw new InvalidDataException("Nonfinite native comparison point: " + pair.Key);
                    Vector3 screen = session.preview.camera.WorldToViewportPoint(pair.Value);
                    Vector2 native = new Vector2(screen.x * session.bodycamCamera.imageWidth, (1f - screen.y) * session.bodycamCamera.imageHeight);
                    projectionAgreement = Mathf.Max(projectionAgreement, Vector2.Distance(native, new Vector2(projected.x, projected.y)));
                    points[pair.Key] = CalibrationArray(projected);
                }
                var third = session.Measure();
                rows.Add(CalibrationObject("frameIndex", modelFrame, "sourceSeconds", SourceStart + modelFrame / (float)Fps,
                    "bodycamSeconds", SourceStart + modelFrame / (float)Fps - 2.430f,
                    "firstPersonPoints", points, "thirdPerson", CalibrationObject(
                        "patientElbow", new[] { third.elbow.x, third.elbow.y }, "patientWrist", new[] { third.wrist.x, third.wrist.y },
                        "patientTip", new[] { third.patientHandTip.x, third.patientHandTip.y },
                        "movingWrist", new[] { third.movingWrist.x, third.movingWrist.y }, "movingPalm", new[] { third.movingPalm.x, third.movingPalm.y },
                        "supportWrist", new[] { third.supportWrist.x, third.supportWrist.y }, "supportPalm", new[] { third.supportPalm.x, third.supportPalm.y },
                        "contactGapMetres", third.contactGapMetres, "upperLengthErrorMetres", third.upperLengthErrorMetres,
                        "lowerLengthErrorMetres", third.lowerLengthErrorMetres,
                        "supportPinnedVertexSamplingErrorMetres", third.supportPinnedVertexSamplingErrorMetres,
                        "nativeGuideTriangleInsetMetres", third.sampledSurfaceContact.maxInsetMetres,
                        "nativeReciprocalTriangleInsetMetres", third.sampledSurfaceContact.reciprocalMaxInsetMetres),
                    "nativeArmPose", CalibrationArm(session.currentPresentedArm),
                    "patientPalmarWorld", CalibrationArray(session.fingers.PalmarWorld), "patientDistalWorld", CalibrationArray(session.fingers.DistalWorld),
                    "movingPalmarWorld", CalibrationArray(session.moving.PalmNormalWorld), "movingDistalWorld", CalibrationArray(session.moving.FingerDirectionWorld)));
                frame++;
                if (frame == selectedFrames.Length) { Finish(null); return false; }
                return true;
            }
            catch (Exception error) { Finish(error); return false; }
        }
        private void Finish(Exception error)
        {
            if (finished) return;
            finished = true;
            bool unchanged = before == FirstPersonSceneState();
            var json = new StringBuilder();
            CalibrationWrite(json, CalibrationObject("engineeringChecksPassed", error == null && unchanged && frame == selectedFrames.Length && projectionAgreement < .05f,
                "sourceMatchIndependentlyValidated", false, "framesWritten", frame, "expectedFrames", selectedFrames.Length,
                "videoMatchedHands", session.VideoMatchedHands, "originalSceneStateUnchanged", unchanged,
                "maximumNativeVsAnalyticalProjectionErrorPixels", projectionAgreement, "error", error?.ToString() ?? "",
                "bodycamCameraSha256", session.bodycamCameraHash, "handSolutionSha256", session.HandSolutionHash,
                "supportBundleSha256", session.SupportBundleHash,
                "firstPersonRendererSha256", FileHash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.BodycamRender.cs")),
                "forearmSkinSha256", FileHash(Absolute("Assets/Editor/Rib2AnatomicalForearmSkin.cs")),
                "scope", "Actual identical FK/hand/skin sample rendered from both cameras. Camera registration and observed source landmarks require independent comparison; engineering pass alone is not source fidelity. No Play or normal-scene writes.", "samples", rows));
            json.Append('\n');
            File.WriteAllText(Path.Combine(directory, "receipt.json"), json.ToString(), new UTF8Encoding(false));
            Debug.Log("[Rib2FirstPerson] Two-view capture finished: frames=" + frame + "; unchangedScene=" + unchanged + "; error=" + (error?.Message ?? "none") + "; " + directory);
        }
        public void Dispose()
        {
            if (!finished) Finish(new OperationCanceledException("Comparison capture interrupted."));
            session?.Dispose();
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
