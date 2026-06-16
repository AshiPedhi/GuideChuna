using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 핸드 데이터 CSV의 기준점(reference point)을 후처리로 변경하는 도구.
///
/// 녹화는 침대 중앙 등 임의의 Transform을 기준으로 저장되어 있음.
/// 이 도구로 원본 기준점 → 새 기준점(환자 몸통/머리/목 등)으로 좌표를 재정렬할 수 있다.
///
/// PracticeHandRecorder 저장 형식 기준 변환식:
///   stored: worldPos = wrist.world - oldRef.pos
///           worldRot = Inverse(oldRef.rot) * wrist.worldRot
///   변환:   newPos = storedPos + (oldRef.pos - newRef.pos)
///           newRot = Inverse(newRef.rot) * oldRef.rot * storedRot
/// </summary>
public class HandPoseRecenterWindow : EditorWindow
{
    private DefaultAsset inputCsvAsset;
    private string inputCsvPath = "";

    private Transform oldReferenceTransform;
    private Transform newReferenceTransform;

    private bool useManualOldReference = false;
    private Vector3 oldRefPosition;
    private Vector3 oldRefEulerRotation;

    private bool useManualNewReference = false;
    private Vector3 newRefPosition;
    private Vector3 newRefEulerRotation;

    private string outputSuffix = "_recentered";
    private bool autoOutputPath = true;
    private string manualOutputPath = "";

    private string statusMessage = "";
    private MessageType statusType = MessageType.Info;

    private Vector2 scrollPos;

    [MenuItem("Tools/HandPose/기준점 재정렬 도구")]
    public static void ShowWindow()
    {
        var window = GetWindow<HandPoseRecenterWindow>("HandPose Recenter");
        window.minSize = new Vector2(440, 540);
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("핸드 데이터 기준점 재정렬", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "녹화된 CSV의 손목 World 좌표를 다른 기준점 기준으로 변환합니다.\n" +
            "PracticeHandRecorder/HandDataRecorder 포맷 호환.",
            MessageType.None);

        EditorGUILayout.Space(8);
        DrawInputSection();

        EditorGUILayout.Space(8);
        DrawReferenceSection("원본 기준점 (녹화 시 사용된 기준점)",
            ref oldReferenceTransform, ref useManualOldReference,
            ref oldRefPosition, ref oldRefEulerRotation);

        EditorGUILayout.Space(8);
        DrawReferenceSection("새 기준점 (변환 후 기준점)",
            ref newReferenceTransform, ref useManualNewReference,
            ref newRefPosition, ref newRefEulerRotation);

        EditorGUILayout.Space(8);
        DrawOutputSection();

        EditorGUILayout.Space(12);
        DrawActionButtons();

        EditorGUILayout.Space(8);
        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, statusType);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawInputSection()
    {
        EditorGUILayout.LabelField("입력 CSV", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        inputCsvAsset = (DefaultAsset)EditorGUILayout.ObjectField(
            "Asset 드래그", inputCsvAsset, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck() && inputCsvAsset != null)
        {
            inputCsvPath = AssetDatabase.GetAssetPath(inputCsvAsset);
        }

        EditorGUILayout.BeginHorizontal();
        inputCsvPath = EditorGUILayout.TextField("경로", inputCsvPath);
        if (GUILayout.Button("...", GUILayout.Width(32)))
        {
            string picked = EditorUtility.OpenFilePanel("입력 CSV 선택",
                Application.dataPath, "csv");
            if (!string.IsNullOrEmpty(picked))
            {
                inputCsvPath = picked;
                inputCsvAsset = null;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawReferenceSection(string title, ref Transform tr, ref bool manual,
        ref Vector3 pos, ref Vector3 euler)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        manual = EditorGUILayout.Toggle("직접 입력 모드", manual);

        if (manual)
        {
            pos = EditorGUILayout.Vector3Field("Position", pos);
            euler = EditorGUILayout.Vector3Field("Rotation (Euler)", euler);
        }
        else
        {
            tr = (Transform)EditorGUILayout.ObjectField("Transform", tr, typeof(Transform), true);
            if (tr != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Vector3Field("(읽기 전용) Position", tr.position);
                EditorGUILayout.Vector3Field("(읽기 전용) Rotation", tr.eulerAngles);
                EditorGUI.EndDisabledGroup();
            }
        }
    }

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("출력 CSV", EditorStyles.boldLabel);
        autoOutputPath = EditorGUILayout.Toggle("자동 경로", autoOutputPath);

        if (autoOutputPath)
        {
            outputSuffix = EditorGUILayout.TextField("파일명 접미사", outputSuffix);
            if (!string.IsNullOrEmpty(inputCsvPath))
            {
                EditorGUILayout.LabelField("미리보기", BuildAutoOutputPath(), EditorStyles.miniLabel);
            }
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            manualOutputPath = EditorGUILayout.TextField("경로", manualOutputPath);
            if (GUILayout.Button("...", GUILayout.Width(32)))
            {
                string picked = EditorUtility.SaveFilePanel("출력 CSV 저장 위치",
                    Application.dataPath, "recentered", "csv");
                if (!string.IsNullOrEmpty(picked))
                {
                    manualOutputPath = picked;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawActionButtons()
    {
        bool canRun = !string.IsNullOrEmpty(inputCsvPath) && File.Exists(inputCsvPath);

        EditorGUI.BeginDisabledGroup(!canRun);
        if (GUILayout.Button("변환 실행", GUILayout.Height(32)))
        {
            RunConversion();
        }
        EditorGUI.EndDisabledGroup();
    }

    private string BuildAutoOutputPath()
    {
        if (string.IsNullOrEmpty(inputCsvPath)) return "";

        string dir = Path.GetDirectoryName(inputCsvPath);
        string name = Path.GetFileNameWithoutExtension(inputCsvPath);
        return Path.Combine(dir, name + outputSuffix + ".csv");
    }

    private void RunConversion()
    {
        try
        {
            if (!File.Exists(inputCsvPath))
            {
                SetStatus($"입력 CSV가 존재하지 않습니다: {inputCsvPath}", MessageType.Error);
                return;
            }

            (Vector3 oldPos, Quaternion oldRot) = ResolveReference(
                oldReferenceTransform, useManualOldReference, oldRefPosition, oldRefEulerRotation);
            (Vector3 newPos, Quaternion newRot) = ResolveReference(
                newReferenceTransform, useManualNewReference, newRefPosition, newRefEulerRotation);

            string outputPath = autoOutputPath ? BuildAutoOutputPath() : manualOutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                SetStatus("출력 경로가 비어있습니다.", MessageType.Error);
                return;
            }

            int converted = TransformCsv(inputCsvPath, outputPath, oldPos, oldRot, newPos, newRot);
            AssetDatabase.Refresh();

            SetStatus(
                $"변환 완료!\n" +
                $"  입력: {inputCsvPath}\n" +
                $"  출력: {outputPath}\n" +
                $"  변환된 손목 프레임: {converted}",
                MessageType.Info);
        }
        catch (Exception e)
        {
            SetStatus($"변환 실패: {e.Message}\n{e.StackTrace}", MessageType.Error);
        }
    }

    private (Vector3 pos, Quaternion rot) ResolveReference(Transform tr, bool manual,
        Vector3 manualPos, Vector3 manualEuler)
    {
        if (manual)
        {
            return (manualPos, Quaternion.Euler(manualEuler));
        }
        if (tr == null)
        {
            throw new InvalidOperationException("기준점 Transform이 할당되지 않았습니다 (또는 직접 입력 모드 켜기).");
        }
        return (tr.position, tr.rotation);
    }

    /// <summary>
    /// CSV를 한 줄씩 처리. wrist root 행(World 컬럼 채워진 행)만 좌표 변환,
    /// 다른 조인트 행과 헤더는 그대로 복사.
    /// </summary>
    private int TransformCsv(string inputPath, string outputPath,
        Vector3 oldRefPos, Quaternion oldRefRot,
        Vector3 newRefPos, Quaternion newRefRot)
    {
        string[] lines = File.ReadAllLines(inputPath, Encoding.UTF8);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("CSV 파일이 비어있습니다.");
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        StringBuilder sb = new StringBuilder(lines.Length * 80);

        sb.AppendLine(lines[0]);

        Quaternion rotDelta = Quaternion.Inverse(newRefRot) * oldRefRot;
        Vector3 posDelta = oldRefPos - newRefPos;

        int convertedCount = 0;

        for (int li = 1; li < lines.Length; li++)
        {
            string line = lines[li];
            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine(line);
                continue;
            }

            string[] cols = line.Split(',');
            if (cols.Length < 18)
            {
                sb.AppendLine(line);
                continue;
            }

            bool hasWorld = !string.IsNullOrEmpty(cols[11]) && !string.IsNullOrEmpty(cols[14]);
            if (!hasWorld)
            {
                sb.AppendLine(line);
                continue;
            }

            if (!TryParseFloat(cols[11], out float wx) ||
                !TryParseFloat(cols[12], out float wy) ||
                !TryParseFloat(cols[13], out float wz) ||
                !TryParseFloat(cols[14], out float rx) ||
                !TryParseFloat(cols[15], out float ry) ||
                !TryParseFloat(cols[16], out float rz) ||
                !TryParseFloat(cols[17], out float rw))
            {
                sb.AppendLine(line);
                continue;
            }

            Vector3 storedPos = new Vector3(wx, wy, wz);
            Quaternion storedRot = new Quaternion(rx, ry, rz, rw);

            Vector3 newStoredPos = storedPos + posDelta;
            Quaternion newStoredRot = rotDelta * storedRot;

            cols[11] = newStoredPos.x.ToString("F4", inv);
            cols[12] = newStoredPos.y.ToString("F4", inv);
            cols[13] = newStoredPos.z.ToString("F4", inv);
            cols[14] = newStoredRot.x.ToString("F4", inv);
            cols[15] = newStoredRot.y.ToString("F4", inv);
            cols[16] = newStoredRot.z.ToString("F4", inv);
            cols[17] = newStoredRot.w.ToString("F4", inv);

            sb.AppendLine(string.Join(",", cols));
            convertedCount++;
        }

        string outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

        return convertedCount;
    }

    private static bool TryParseFloat(string s, out float v)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private void SetStatus(string msg, MessageType type)
    {
        statusMessage = msg;
        statusType = type;
        Debug.Log($"[HandPoseRecenter] {msg}");
    }
}
