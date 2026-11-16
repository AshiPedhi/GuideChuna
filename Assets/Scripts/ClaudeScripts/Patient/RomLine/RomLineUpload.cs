using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// ★XR-ROM(선 실측) 측정 결과 + 찍은 점 좌표를 회사 서버(AWS · 로그인 · 추나 결과와 같은 서버)로 보낸다(2026-11-16 사용자 지시).
///   범위(사용자 선택): 측정 결과(6동작 능동/압박 · 평가 오차 · 점수) + 점 좌표 원본 + 기기 상세 기록(CSV 줄).
///   서버 쪽 받는 주소는 아직 없다 — 서버 개발자 요청서(D: «XR-ROM 서버 전송 요청서»)의 <see cref="Endpoint"/>로 만들어 달라고 한다.
///
///   흐름: 세션이 바뀔 때마다(확인 · 탭 지우기 · 평가 점수 · 나갈 때) 기기 <c>persistentDataPath/upload_queue/xrrom_세션.json</c>을 덮어쓴다(끊겨도 남는다).
///        나갈 때와 다음 실행 때 큐를 보낸다. 성공(HTTP 2xx)하면 지운다. 실패하면 남겨 두고 다음에 다시.
///   ★전송 스위치는 기본 <b>꺼짐</b>이다 — 서버 주소가 생기기 전에 보내면 매번 404다. 큐 파일은 꺼져 있어도 쌓인다(서버가 생기면 지난 측정도 올라간다).
///     켜기: <see cref="ServerReady"/>를 true로 바꿔 빌드하거나, 시험은 <c>adb shell setprop debug.coco.xrrom.upload 1</c> + 앱 재시작.
///   로그 [XR-ROM 전송].
/// </summary>
public static class RomLineUpload
{
    public const string Endpoint = "/result/xrrom";        // 요청서의 제안 주소 — 서버가 다른 주소로 만들면 여기만 바꾼다
    public const string Schema = "xrrom.v1";
    private static readonly bool ServerReady = false;       // ★서버 주소가 생기면 true(const로 두면 «접근할 수 없는 코드» 경고)
    private const int MaxQueueFiles = 300;                  // 오래된 것부터 버린다(기기 저장 공간)
    private static bool flushing;

    public static string QueueDir => Path.Combine(Application.persistentDataPath, "upload_queue");

    public static bool Enabled
    {
        get
        {
            if (ServerReady) return true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using (var sp = new AndroidJavaClass("android.os.SystemProperties")) return sp.CallStatic<string>("get", "debug.coco.xrrom.upload", "") == "1"; }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>세션 한 판의 현재 상태를 큐 파일로 덮어쓴다. 이벤트 때만 부른다(매 프레임 아님).</summary>
    public static void Save(Payload p)
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            string path = Path.Combine(QueueDir, "xrrom_" + p.sessionId + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(p), new UTF8Encoding(false));
        }
        catch (Exception e) { Debug.LogWarning($"[XR-ROM 전송] ★큐 파일을 못 썼다({e.Message})"); }
    }

    /// <summary>큐를 보낸다. skipSessionId = 지금 진행 중인 판(아직 끝나지 않아 보내지 않는다). 동시에 두 번 돌지 않는다.</summary>
    public static async UniTaskVoid Flush(string skipSessionId, string why)
    {
        if (flushing) return;
        string[] files;
        try { files = Directory.Exists(QueueDir) ? Directory.GetFiles(QueueDir, "xrrom_*.json") : Array.Empty<string>(); }
        catch (Exception e) { Debug.LogWarning($"[XR-ROM 전송] 큐를 못 읽었다({e.Message})"); return; }
        if (files.Length == 0) return;
        Array.Sort(files, StringComparer.Ordinal);

        // 너무 쌓이면 오래된 것부터 버린다
        for (int i = 0; i < files.Length - MaxQueueFiles; i++)
        {
            try { File.Delete(files[i]); Debug.LogWarning($"[XR-ROM 전송] ★큐가 {MaxQueueFiles}개를 넘어 버렸다 — {Path.GetFileName(files[i])}"); } catch (Exception) { }
        }

        if (!Enabled) { Debug.Log($"[XR-ROM 전송] 꺼짐(서버 주소 준비 전) — 큐 {files.Length}개는 기기에 남긴다({why})"); return; }
        if (AuthenticationService.Instance == null) { Debug.LogWarning("[XR-ROM 전송] AuthenticationService 없음 — 다음에 보낸다"); return; }

        flushing = true;
        int sent = 0, failed = 0;
        try
        {
            foreach (string f in files)
            {
                if (!File.Exists(f)) continue;
                if (!string.IsNullOrEmpty(skipSessionId) && f.EndsWith("xrrom_" + skipSessionId + ".json", StringComparison.Ordinal)) continue;
                string json;
                try { json = File.ReadAllText(f, Encoding.UTF8); } catch (Exception) { failed++; continue; }
                try
                {
                    string reply = await AuthenticationService.Instance.PostJsonRawAsync(Endpoint, json);
                    File.Delete(f);
                    sent++;
                    Debug.Log($"[XR-ROM 전송] 보냄 {Path.GetFileName(f)} · 응답 {(reply.Length > 120 ? reply.Substring(0, 120) + "…" : reply)}");
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning($"[XR-ROM 전송] ★실패 {Path.GetFileName(f)} — {e.Message} · 남겨 두고 다음에 다시");
                    break;   // 서버 · 망 문제면 나머지도 실패한다 — 다음 기회에
                }
            }
        }
        finally { flushing = false; }
        Debug.Log($"[XR-ROM 전송] {why} — 보냄 {sent} · 실패 {failed} · 큐 {files.Length}");
    }

    // ── 보내는 형식(xrrom.v1) — 요청서와 같은 이름이다. 이름을 바꾸면 서버도 바꿔야 한다. ──
    [Serializable]
    public class Payload
    {
        public string schema = Schema;
        public string sessionId;            // 판 하나(앱이 만든 GUID)
        public string status;               // in_progress · completed(6동작 확인) · exited(다 못 재고 나감)
        public string orgID;
        public int userId;
        public string username;
        public string deviceSN;
        public string appVersion;
        public string mode;                 // practice(실습) · evaluation(평가 = 재현성)
        public string startedAt, updatedAt; // ISO 8601 · 기기 시각 · 시간대 포함
        public Vec3 tipOffsetMm;            // 측정팁 끝 보정값(컨트롤러 기준 · mm)
        public Frame frame;                 // 환자 기준 축(양쪽 Tr로 세움) — 좌표를 다시 계산할 때 쓴다
        public List<Motion> motions = new List<Motion>(6);
        public Evaluation evaluation;       // 평가 모드에서 점수가 났을 때만 · 아니면 null
        public List<string> rawCsv;         // 기기 상세 기록(romline_*.csv와 같은 줄 · 첫 줄 = 머리글)
    }

    [Serializable] public class Vec3 { public float x, y, z; public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; } public Vec3() { } }

    [Serializable]
    public class Frame
    {
        public bool valid;
        public Vec3 right, forward, up;     // 단위벡터 · 헤드셋 추적 공간(m)
        public bool sidesSwapped;
        public List<Point> baseline = new List<Point>(2);   // 양쪽 Tr
    }

    [Serializable]
    public class Point
    {
        public string phase;                // baseline · neutral · active · pressure · compare
        public string label;                // TrRight · TrLeft · Gl · Gn
        public Vec3 pos;                    // 헤드셋 추적 공간 좌표(m) — 판마다 원점이 다르다(기기가 정함). 판 안의 상대값만 뜻이 있다
    }

    [Serializable]
    public class Motion
    {
        public string code;                 // flexion · extension · lateral_right · lateral_left · rotation_right · rotation_left
        public string name;                 // 굴곡 · 신전 · 우측굴 · 좌측굴 · 우회전 · 좌회전
        public bool confirmed;              // 사용자가 트리거로 확인했다
        public Angle active, pressure;
        public List<Point> points = new List<Point>(7);
    }

    [Serializable]
    public class Angle
    {
        public bool valid;
        public float angle;                 // 표시값(°) — 결과표와 같은 값
        public float signedAngle;           // 부호 포함(면 법선 기준)
        public bool directionMatches;       // 동작 방향과 맞게 움직였다(false = 반대쪽으로 움직인 값)
        public float directAngle;           // 비교용 — 면으로 옮기지 않고 3차원에서 잰 각(°) · 없으면 NaN 대신 -1
    }

    [Serializable]
    public class Evaluation
    {
        public string baselineAt;           // 비교한 실습값을 저장한 때
        public float score;                 // 0~100
        public float meanError;             // 평균 오차(°)
        public List<Slot> slots = new List<Slot>(12);
    }

    [Serializable]
    public class Slot
    {
        public string code, kind;           // kind = active · pressure
        public float? practice, evaluation, error, score;   // 없으면 null
    }

    public static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
}
