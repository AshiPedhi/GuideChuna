using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [MenuItem("GuideChuna/시험/제2늑골/V03 손 방향 및 부드러운 궤적 실측")]
    private static void AuditMotionMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Motion review requires edit mode.");
        string directory = Absolute(Folder + "/motion_review_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        for (int passes = 0; passes <= 2; passes++)
            using (var session = new Rib2AnatomicalHandsReviewSession(false, -1f, passes))
            {
                float wristDeviation = 0f, elbowDeviation = 0f, guideDeviation = 0f, maximumElbowError = 0f, maximumWristError = 0f;
                float peakSpeed = 0f, peakAcceleration = 0f, minOutward = 1f;
                Vector3 previousWrist = default, previousVelocity = default;
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    float time = frame / (float)Fps;
                    session.Sample(time, false, false); SampleResult original = session.Measure(false);
                    Vector3 originalGuideWrist = session.moving.WristWorld;
                    session.Sample(time, false, true); SampleResult actual = session.Measure(false);
                    wristDeviation = Mathf.Max(wristDeviation, Vector3.Distance(original.patientWristWorld, actual.patientWristWorld));
                    elbowDeviation = Mathf.Max(elbowDeviation, Vector3.Distance(original.elbowWorld, actual.elbowWorld));
                    guideDeviation = Mathf.Max(guideDeviation, Vector3.Distance(session.moving.WristWorld, originalGuideWrist));
                    maximumElbowError = Mathf.Max(maximumElbowError, actual.elbowErrorPixels);
                    maximumWristError = Mathf.Max(maximumWristError, actual.wristErrorPixels);
                    Vector3 outward = (session.fingers.Wrist.position - session.supportBodyFrame.position).normalized;
                    minOutward = Mathf.Min(minOutward, Vector3.Dot(session.fingers.PalmarWorld, outward));
                    if (frame > 0)
                    {
                        Vector3 velocity = (actual.patientWristWorld - previousWrist) * Fps;
                        peakSpeed = Mathf.Max(peakSpeed, velocity.magnitude);
                        if (frame > 1) peakAcceleration = Mathf.Max(peakAcceleration, (velocity - previousVelocity).magnitude * Fps);
                        previousVelocity = velocity;
                    }
                    previousWrist = actual.patientWristWorld;
                }
                rows.Add(CalibrationObject("passes", passes, "maximumNativeWristPathDeviationMetres", wristDeviation,
                    "maximumNativeElbowPathDeviationMetres", elbowDeviation, "maximumNativeGuideWristPathDeviationMetres", guideDeviation,
                    "maximumActualElbowSourceErrorPixels", maximumElbowError, "maximumActualWristSourceErrorPixels", maximumWristError,
                    "peakNativeWristSpeedMetresPerSecondAt30Hz", peakSpeed, "peakNativeWristAccelerationMetresPerSecondSquaredAt30Hz", peakAcceleration,
                    "minimumActualPalmOutwardDot", minOutward, "measuredWristDorsalBendDegrees", new[] { session.minimumMeasuredDorsalBend, session.maximumMeasuredDorsalBend }));
                if (passes == DefaultSmoothingPasses)
                    foreach (int frame in new[] { 0, 120, 202, 225 })
                    { session.Sample(frame / (float)Fps, false); session.SaveRestArmViews(directory, "smooth_frame" + frame); }
            }
        object performance;
        using (var session = new Rib2AnatomicalHandsReviewSession()) performance = session.AuditPreviewPerformance();
        var json = new StringBuilder(); CalibrationWrite(json, CalibrationObject("scope", "Actual native FK with requested moving-hand orientation; zero/one/two-pass smoothing compared to the same unsmoothed requested pose. Source17 used for selection; heldout4 not used.",
            "rows", rows, "performance", performance, "selectedDefaultPasses", DefaultSmoothingPasses,
            "limitations", "CPU preview timings are not on-screen FPS or Quest performance. Native30Hz finite differences are not recovered patient kinematics. No Play toggle or regular-scene writes.")); json.Append('\n');
        File.WriteAllText(Path.Combine(directory, "audit.json"), json.ToString(), new UTF8Encoding(false));
        Debug.Log("[Rib2MotionAudit] " + directory);
    }

    private object AuditPreviewPerformance()
    {
        var measurements = new List<object>();
        var clock = new Stopwatch();
        var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try
        {
            foreach (bool full in new[] { true, false })
            {
                double sample = 0, render = 0, measure = 0, decode = 0;
                float largestModePositionError = 0f, largestModeRotationError = 0f;
                const int count = 12;
                for (int i = 0; i < count; i++)
                {
                    int frame = i * 20; float time = frame / (float)Fps;
                    clock.Restart(); Sample(time, full); clock.Stop(); sample += clock.Elapsed.TotalMilliseconds;
                    clock.Restart(); Render(720, 405, 0, true); clock.Stop(); render += clock.Elapsed.TotalMilliseconds;
                    clock.Restart(); SampleResult measured = Measure(full); clock.Stop(); measure += clock.Elapsed.TotalMilliseconds;
                    clock.Restart(); source.LoadImage(File.ReadAllBytes(SourcePath(frame)), false); clock.Stop(); decode += clock.Elapsed.TotalMilliseconds;
                    Sample(time, !full); SampleResult other = Measure(false);
                    largestModePositionError = Mathf.Max(largestModePositionError, Vector3.Distance(measured.patientWristWorld, other.patientWristWorld));
                    largestModeRotationError = Mathf.Max(largestModeRotationError, Quaternion.Angle(measured.patientHandRotation, other.patientHandRotation));
                }
                measurements.Add(CalibrationObject("fullSurfaceDiagnostics", full, "samples", count, "averageSampleMilliseconds", sample / count,
                    "averageRenderMilliseconds", render / count, "averageMeasureMilliseconds", measure / count, "averageSourceDecodeMilliseconds", decode / count,
                    "averageTotalCpuMilliseconds", (sample + render + measure + decode) / count,
                    "maximumFullVsLightWristPositionErrorMetres", largestModePositionError, "maximumFullVsLightHandRotationErrorDegrees", largestModeRotationError));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        return measurements;
    }
}
