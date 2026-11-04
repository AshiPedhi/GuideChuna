using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [MenuItem("GuideChuna/시험/제2늑골/V03 환자 손가락 실루엣 비교")]
    private static void AuditPatientGraspMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Patient grasp review requires edit mode.");
        string directory = Absolute(Folder + "/grasp_pose_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var records = new List<object>();
        foreach (float curl in new[] { .45f, .6f, .75f })
            using (var session = new Rib2AnatomicalHandsReviewSession(false, curl))
                foreach (int frame in new[] { 0, 120, 202 })
                {
                    session.Sample(frame / (float)Fps);
                    session.SaveRestArmViews(directory, "curl" + Mathf.RoundToInt(curl * 100f) + "_frame" + frame);
                    var result = session.Measure();
                    records.Add(CalibrationObject("curl", curl, "frameIndex", frame,
                        "actualPatientPipDegrees", new[] { result.measuredPatientFingerFlexion.x, result.measuredPatientFingerFlexion.y, result.measuredPatientFingerFlexion.z, result.measuredPatientFingerFlexion.w },
                        "guideSampleInsetMetres", result.fingerCollision.afterMaximumInsetMetres,
                        "gloveExtent", new[] { session.MeasureMovingGloveEnvelope().x, session.MeasureMovingGloveEnvelope().y }));
                }
        var json = new StringBuilder(); CalibrationWrite(json, CalibrationObject("scope", "Finger-shape hypotheses compared on actual Native FK/skin; no source data or selected solution is overwritten.", "records", records)); json.Append('\n');
        File.WriteAllText(Path.Combine(directory, "audit.json"), json.ToString(), new UTF8Encoding(false));
        Debug.Log("[Rib2PatientGrasp] Native candidate silhouettes: " + directory);
    }
}
