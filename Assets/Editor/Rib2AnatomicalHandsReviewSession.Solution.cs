using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [Serializable] private sealed class SolvedHandFrame
    {
        public float sourceSeconds = 0f, forearmRotation = 0f, wristFlexion = 0f, wristDeviation = 0f;
        public Quaternion guideRelativeRotation = Quaternion.identity;
        public Vector3 guideRelativePosition = Vector3.zero;
    }
    [Serializable] private sealed class SolvedHands
    {
        public int version = 0, patientVertex = -1, gripFamily = -1;
        public float mcpOpen = 0f, pipCurl = 0f, dipCurl = 0f, thumbOpposition = 30f;
        public string sourceObservationsSha256 = "", patientFitSha256 = "", calibrationSha256 = "";
        public string calibrationFile = "hand_calibration.json";
        public string patientFitFile = "patient_fit.json";
        public float patientFingerCurl = .24f;
        public string bodycamCameraFile = "", bodycamCameraSha256 = "";
        public string bodycamObservationsFile = "", bodycamObservationsSha256 = "";
        public bool hasSupportChestRotation = false;
        public Quaternion supportChestRelativeRotation = Quaternion.identity;
        public int supportContactVertex = -1;
        public string supportBundleFile = "", supportBundleSha256 = "";
        public string guideFamiliesFile = "", guideFamiliesSha256 = "";
        public SolvedHandFrame[] frames = null;
    }
    private readonly Quaternion[] solvedRelativeGuideRotations = new Quaternion[FrameCount];
    private readonly Vector3[] solvedRelativeGuidePositions = new Vector3[FrameCount];
    private bool solvedHandsLoaded;
    private bool solvedSurfaceGrasp;
    private string loadedSolutionHash = "";
    public string HandSolutionHash => loadedSolutionHash;
    public string NativeGeometryCalibrationPath { get; private set; }
    public string NativePatientFitPath { get; private set; }
    public bool VideoMatchedHands { get; private set; }
    private bool hasSynchronizedSupport;
    private Quaternion synchronizedSupportChestRotation;
    private Vector3 synchronizedSupportHeelLocalMetres;
    private int synchronizedSupportVertex = -1;

    private static string FileHash(string path)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    private bool TryLoadHandSolution()
    {
        string path = Absolute(Folder + "/hand_solution.json");
        string synchronizedPath = Absolute(Folder + "/hand_solution_bodycam_v1.json");
        if (useBodycamSolution && File.Exists(synchronizedPath)) path = synchronizedPath;
        if (comparisonSolutionFile != null) path = Absolute(Folder + "/" + comparisonSolutionFile);
        if (!File.Exists(path)) return false;
        byte[] bytes = File.ReadAllBytes(path);
        using (var hash = SHA256.Create()) loadedSolutionHash = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        string json = Encoding.UTF8.GetString(bytes);
        var solution = JsonUtility.FromJson<SolvedHands>(json);
        if (solution == null || (solution.version != 1 && solution.version != 2 && solution.version != 3) || solution.frames == null || solution.frames.Length != FrameCount || (solution.version == 1 && solution.patientVertex < 0))
            throw new InvalidDataException("V03 solved hand curve is incomplete.");
        if (json.IndexOf("\"calibrationFile\"", StringComparison.Ordinal) < 0) solution.calibrationFile = "hand_calibration.json";
        if (json.IndexOf("\"patientFingerCurl\"", StringComparison.Ordinal) < 0) solution.patientFingerCurl = .24f;
        if (string.IsNullOrEmpty(solution.calibrationFile) || Path.GetFileName(solution.calibrationFile) != solution.calibrationFile)
            throw new InvalidDataException("V03 calibration must be a file inside its separate data folder.");
        NativeGeometryCalibrationPath = Absolute(Folder + "/" + solution.calibrationFile);
        if (solution.sourceObservationsSha256 != FileHash(Absolute(Folder + "/source_observations.json")) ||
            solution.patientFitSha256 != FileHash(NativePatientFitPath) ||
            solution.calibrationSha256 != FileHash(NativeGeometryCalibrationPath))
            throw new InvalidDataException("V03 hand inputs changed after the native geometry fit; export and fit the separate hand solution again.");
        pinnedPatientVertex = solution.patientVertex;
        solvedSurfaceGrasp = solution.version >= 2;
        VideoMatchedHands = solution.version == 3;
        hasSynchronizedSupport = solution.hasSupportChestRotation;
        if (hasSynchronizedSupport)
        {
            float norm = Quaternion.Dot(solution.supportChestRelativeRotation, solution.supportChestRelativeRotation);
            if (float.IsNaN(norm) || float.IsInfinity(norm) || norm < .00001f)
                throw new InvalidDataException("Synchronized support rotation is invalid.");
            synchronizedSupportChestRotation = solution.supportChestRelativeRotation.normalized;
            if (solution.supportContactVertex >= 0)
            {
                var geometry = support.ExportGeometry();
                if (solution.supportContactVertex >= geometry.vertices.Length)
                    throw new InvalidDataException("The synchronized support vertex is absent from the actual coarse SDK mesh.");
                synchronizedSupportHeelLocalMetres = geometry.vertices[solution.supportContactVertex];
                synchronizedSupportVertex = solution.supportContactVertex;
            }
            else synchronizedSupportHeelLocalMetres = Quaternion.Inverse(support.Root.rotation) * (support.UlnarHeelSurfaceWorld - support.Root.position);
            Debug.Log("[Rib2ChestSupport] Two-view support pose loaded; actual coarse-mesh contact vertex=" + solution.supportContactVertex + "; heel-local-metres=" + synchronizedSupportHeelLocalMetres.ToString("F5") + ". No flat-palm/chest-normal orientation constraint.");
        }
        if (VideoMatchedHands)
        {
            foreach (string filename in new[] { solution.bodycamCameraFile, solution.bodycamObservationsFile })
                if (string.IsNullOrEmpty(filename) || Path.GetFileName(filename) != filename)
                    throw new InvalidDataException("Synchronized comparison inputs must stay inside the V03 data folder.");
            if (solution.bodycamCameraSha256 != FileHash(Absolute(Folder + "/" + solution.bodycamCameraFile)) ||
                solution.bodycamObservationsSha256 != FileHash(Absolute(Folder + "/" + solution.bodycamObservationsFile)))
                throw new InvalidDataException("Synchronized source/camera inputs changed after hand fitting.");
            bodycamCameraFilename = solution.bodycamCameraFile;
            if (!string.IsNullOrEmpty(solution.supportBundleFile)) LoadSupportBundle(solution.supportBundleFile, solution.supportBundleSha256);
            if (!string.IsNullOrEmpty(solution.guideFamiliesFile) && (Path.GetFileName(solution.guideFamiliesFile) != solution.guideFamiliesFile ||
                FileHash(Absolute(Folder + "/" + solution.guideFamiliesFile)) != solution.guideFamiliesSha256))
                throw new InvalidDataException("Coarse guide geometry changed after hand fitting.");
        }
        if (float.IsNaN(solution.patientFingerCurl) || solution.patientFingerCurl < 0f || solution.patientFingerCurl > 1f)
            throw new InvalidDataException("Patient grasp hypothesis curl is outside its authoring range.");
        solvedPatientCurl = solution.patientFingerCurl;
        moving.SetGraspFamily(solution.mcpOpen, solution.pipCurl, solution.dipCurl, solution.thumbOpposition);
        for (int i = 0; i < FrameCount; i++)
        {
            var frame = solution.frames[i];
            if (Mathf.Abs(frame.sourceSeconds - (SourceStart + i / (float)Fps)) > .001f)
                throw new InvalidDataException("V03 hand solution source clock does not match the original frames.");
            Observation observation;
            var arm = SourceArm(frame.sourceSeconds, out observation);
            arm.forearmRotation = frame.forearmRotation; arm.wristFlexion = frame.wristFlexion; arm.wristDeviation = frame.wristDeviation;
            sampledArms[i] = arm; sampledGrips[i] = default;
            solvedRelativeGuideRotations[i] = frame.guideRelativeRotation.normalized;
            solvedRelativeGuidePositions[i] = frame.guideRelativePosition;
        }
        solvedHandsLoaded = true;
        if (VideoMatchedHands)
        {
            Array.Copy(sampledArms, presentationArms, FrameCount);
            Debug.Log("[Rib2HandDirection] Synchronized two-view hand solution; fitted forearm/wrist angles retained. Earlier fixed30deg/outward maximization is superseded by source matching. Coarse fingers only.");
        }
        else InitializeRequestedHandDirection();
        InitializeSmoothMotion();
        Debug.Log("[Rib2AnatomicalV03] Native geometry hand solution loaded; fixed patient skin vertex=" + pinnedPatientVertex + ", grip family=" + solution.gripFamily + ". Source projection and skin remain independently measured on actual Unity output.");
        return true;
    }
}
