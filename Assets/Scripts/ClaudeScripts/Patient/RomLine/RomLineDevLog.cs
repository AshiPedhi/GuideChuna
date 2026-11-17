using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// XR-ROM 개발용 로그(11-17 사용자 결정 — «개발에 유용한 것만 남기고 나머지는 뺀다»).
///   판 동안 <see cref="Application.logMessageReceived"/>로 들어오는 줄 가운데 정해 둔 것만 메모리에 모았다가,
///   판이 끝날 때(결과 창 · 측정값 초기화 · 앱이 내려감 · 나감) 기기 큐에 txt로 <b>덮어쓴다</b>.
///   전송은 하지 않는다 — 서버(/result/chuna · learnLevel1 = Log) 준비 뒤에 붙인다. 문서 = D:/CoCoChuna_XR-ROM_로그결과_형식정리_V0.2_20261117.
///   ★코드의 Debug.Log 문장은 그대로 둔다 — logcat에는 다 나오고, 이 파일로 모을 때만 거른다.
///   ★켜는 쪽(RomLineSession.Start)과 끄는 쪽(OnDestroy)이 같다(켠 쪽이 끈다).
/// </summary>
public static class RomLineDevLog
{
    private const string Tag = "[XR-ROM 로그]";
    private const int HeadLimit = 20000;     // 앞부분은 이만큼 그대로 둔다
    private const int TotalLimit = 50000;    // 한 판 상한(글자) — 넘으면 가운데를 «…생략 N줄»로
    private const int MaxFiles = 300;        // 큐 상한(RomLineUpload와 같다)
    private const float DiagEvery = 60f;     // 미러링 주기 진단은 60초에 한 줄만
    private const int StackLines = 3;        // 오류의 스택은 앞 세 줄만

    // ── 거르는 규칙 — 접두는 «[선실측] » 뒤의 글이다(RomLine/*.cs의 실제 문장에서 모았다, 11-17) ──
    /// <summary>뺀다 — 결과 JSON · 상세 CSV에 이미 있거나, 늘 같은 내용이거나, 개인정보다. ★보다 먼저 본다.</summary>
    private static readonly string[] Drop =
    {
        "관제 녹화 사용자명",          // ★개인정보
        "★평가 점수",                  // 결과 행에 있다(★여도 뺀다)
        "찍음", "값 —", "비교 —",        // 상세 CSV · 결과 JSON에 있다
        "미러링 영상 — 보는 화면",      // 보는 화면 수 변화
        "컨트롤러 모형", "찌르기 인터랙터", "패스스루", "메인 화면을 세웠다",
        "홈 메뉴 — 커서", "홈 메뉴 — 닫음", "홈 메뉴 — 열 폭",
        "저장된 끝점", "평가 모드", "실습 모드",   // 판 머리에 들어간다
    };

    /// <summary>남긴다(★로 시작하는 줄은 Drop에 없으면 모두 남는다).</summary>
    private static readonly string[] Keep =
    {
        "취소", "이 탭 지우기", "리셋", "받지 않음", "나가기",
        "확인 —",
        "쓰는 컨트롤러", "반대쪽 컨트롤러",
        "끝점 보정", "끝점 저장",
        "미러링 — 붙었다", "미러링 읽기 방식", "미러링 영상 — 압축기", "미러링 영상 — 포트",
        "모니터 명령", "결과 창", "글꼴",
        "폐색",   // ★11-17: 켬 · 지원 안 함 · 깊이 준비됨 · 셰이더 바꿈 — 벽면UI가 통째로 안 보였을 때 이 줄이 없어 원인을 못 갈랐다
    };

    private const string Main = "[선실측] ";
    private const string Gauge = "[선실측·각도기]";
    private const string Upload = "[XR-ROM 전송]";

    // ── 상태 ──
    private static bool on, writing, warned;
    private static string sessionId = "", mode = "", startedAt = "";
    private static readonly StringBuilder head = new StringBuilder(HeadLimit + 512);
    private static readonly Queue<string> tail = new Queue<string>(256);
    private static int tailChars, lines, omitted;
    private static float nextDiag, nextVideoDiag;
    private static string firstBattery, lastStall;

    /// <summary>판을 시작한다(RomLineSession.Start). 지난 판의 로그 파일이 너무 많으면 오래된 것부터 지운다.</summary>
    public static void Begin(string session, bool evaluation, string started)
    {
        End();
        sessionId = session ?? "";
        mode = evaluation ? "평가" : "실습";
        startedAt = started ?? "";
        head.Length = 0;
        tail.Clear();
        tailChars = lines = omitted = 0;
        nextDiag = nextVideoDiag = 0f;
        firstBattery = lastStall = null;
        warned = false;
        Trim();
        Application.logMessageReceived += OnLog;
        on = true;
    }

    /// <summary>판을 끝낸다(RomLineSession.OnDestroy) — 켠 쪽이 끈다.</summary>
    public static void End()
    {
        if (!on) return;
        Application.logMessageReceived -= OnLog;
        on = false;
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (writing || msg == null) return;   // 재진입 막기(파일 쓰기 실패 경고 등)
        if (msg.StartsWith(Tag, StringComparison.Ordinal)) return;

        bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (!error && !Wanted(msg)) return;

        char kind = error ? 'E' : type == LogType.Warning ? 'W' : 'I';
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + kind + " " + msg;
        if (error && !string.IsNullOrEmpty(stack)) line += StackHead(stack);
        Add(line);
    }

    /// <summary>남길 줄인가 — 접두 비교만(정규식 · LINQ 없음 · 할당 없음).</summary>
    private static bool Wanted(string msg)
    {
        if (msg.StartsWith(Upload, StringComparison.Ordinal))
            return msg.IndexOf('★') >= 0 || msg.IndexOf(" — 보냄 ", StringComparison.Ordinal) >= 0;   // 실패 · 요약만
        if (msg.StartsWith(Gauge, StringComparison.Ordinal))
            return msg.IndexOf('★') >= 0;                                                              // 띄움 · 숨김은 뺀다
        if (!msg.StartsWith(Main, StringComparison.Ordinal)) return false;                               // 다른 출처는 오류만

        int o = Main.Length;
        for (int i = 0; i < Drop.Length; i++)
            if (string.CompareOrdinal(msg, o, Drop[i], 0, Drop[i].Length) == 0) return false;

        // 미러링 주기 진단 — 60초에 한 줄만
        if (At(msg, o, "미러링 진단")) return Throttle(ref nextDiag);
        if (At(msg, o, "미러링 영상 진단")) return Throttle(ref nextVideoDiag);

        // 컨트롤러 추적 — 끊김(★) · 복귀(잡힘)만. 맨손 상태 줄(«컨트롤러 O · 왼손 …»)은 뺀다
        if (At(msg, o, "추적 — "))
        {
            bool back = msg.IndexOf("잡힘", StringComparison.Ordinal) >= 0;
            if (back) lastStall = null;   // 상태가 바뀌었다 — 다음 «멈춰 있다»는 다시 남긴다
            return back || msg.IndexOf('★') >= 0;
        }
        // ★11-17 기기 시험 분석 개선 1: «멈춰 있다»는 2초마다 반복된다(26초 끊김에 14줄) — 같은 문장이 이어지면 처음 한 줄만
        if (At(msg, o, "멈춰 있다"))
        {
            if (msg == lastStall) return false;
            lastStall = msg;
            return true;
        }

        // 배터리 — 처음 한 줄만 바로 남기고, 끝 값은 저장할 때 붙인다
        if (At(msg, o, "배터리"))
        {
            if (firstBattery == null) { firstBattery = msg; return true; }
            return false;   // 끝 값은 저장할 때 지금 값을 읽어 붙인다(Save)
        }

        if (msg.Length > o && msg[o] == '★') return true;
        // 홈 메뉴(11-17 V0.4) — 실행 · 취소(B)만. 커서 이동 · 확인 안내 · 설정 탭 이동 · 열 폭은 뺀다
        if (At(msg, o, "홈 메뉴 — "))
            return msg.IndexOf(" 실행(", StringComparison.Ordinal) >= 0 || msg.IndexOf(" 취소(B)", StringComparison.Ordinal) >= 0;
        for (int i = 0; i < Keep.Length; i++)
            if (string.CompareOrdinal(msg, o, Keep[i], 0, Keep[i].Length) == 0) return true;
        return false;
    }

    private static bool At(string msg, int o, string prefix) => string.CompareOrdinal(msg, o, prefix, 0, prefix.Length) == 0;

    private static bool Throttle(ref float next)
    {
        float now = Time.unscaledTime;
        if (now < next) return false;
        next = now + DiagEvery;
        return true;
    }

    private static string StackHead(string stack)
    {
        int end = 0;
        for (int n = 0; n < StackLines; n++)
        {
            int nl = stack.IndexOf('\n', end);
            if (nl < 0) { end = stack.Length; break; }
            end = nl + 1;
        }
        string s = stack.Substring(0, end).TrimEnd();
        return s.Length > 0 ? "\n    " + s.Replace("\n", "\n    ") : "";
    }

    private static void Add(string line)
    {
        lines++;
        if (head.Length + line.Length + 1 <= HeadLimit && tail.Count == 0)
        {
            head.Append(line).Append('\n');
            return;
        }
        tail.Enqueue(line);
        tailChars += line.Length + 1;
        while (tailChars > TotalLimit - HeadLimit && tail.Count > 1)
        {
            tailChars -= tail.Dequeue().Length + 1;
            omitted++;
        }
    }

    /// <summary>
    /// 지금까지 모은 것을 큐 파일에 덮어쓴다 — 결과 창이 뜰 때 · 측정값 초기화 · 앱이 내려갈 때 · 나갈 때.
    ///   같은 판이면 같은 파일이다(sessionId = RomLineUpload와 같은 번호).
    /// </summary>
    public static void Save(string why, Vector3 tipOffsetMm)
    {
        if (string.IsNullOrEmpty(sessionId) || lines == 0) return;
        writing = true;
        try
        {
            string sn = "";
            try { sn = new LoginStateStore().LoadDeviceSN(); } catch (Exception) { }
            var sb = new StringBuilder(head.Length + tailChars + 600);
            sb.Append("# XR-ROM 개발용 로그 · 세션 ").Append(sessionId)
              .Append(" · 앱 ").Append(Application.version)
              .Append(" · 기기 ").Append(SystemInfo.deviceModel)
              .Append(" · SN ").Append(string.IsNullOrEmpty(sn) ? "없음" : sn)
              .Append(" · 모드 ").Append(mode)
              .Append(" · 시작 ").Append(startedAt)
              .Append(" · 끝점 ").Append(tipOffsetMm.x.ToString("F1")).Append(", ").Append(tipOffsetMm.y.ToString("F1")).Append(", ").Append(tipOffsetMm.z.ToString("F1")).Append("mm")
              .Append(" · 저장 ").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")).Append(" (").Append(why).Append(")\n");
            sb.Append(head);
            if (omitted > 0) sb.Append("…생략 ").Append(omitted).Append("줄\n");
            foreach (string l in tail) sb.Append(l).Append('\n');
            // ★11-17 기기 시험 분석 개선 2: 배터리 줄은 잔량 구간이 바뀔 때만 찍혀 판 끝 값이 비었다 — 저장할 때 지금 값을 한 줄 붙인다
            int pct = RomLineView.BatteryPercent();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" I ").Append(Main).Append("배터리 — ")
              .Append(pct < 0 ? "읽을 수 없음" : pct + "%").Append(RomLineView.BatteryCharging() ? " · 충전 중" : " · 충전 안 함").Append(" (끝)\n");

            Directory.CreateDirectory(RomLineUpload.QueueDir);
            string path = Path.Combine(RomLineUpload.QueueDir, "xrrom_log_" + sessionId + ".txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"{Tag} 저장({why}) — {lines}줄(생략 {omitted}) · {sb.Length}자 · {path}");
        }
        catch (Exception e)
        {
            if (!warned) { warned = true; Debug.LogWarning($"{Tag} ★파일을 못 썼다({e.Message}) — 이 판은 다음 저장 때 다시 해 본다"); }
        }
        finally { writing = false; }
    }

    /// <summary>로그 파일이 상한을 넘으면 오래된 것부터 지운다(판 시작 때 한 번).</summary>
    private static void Trim()
    {
        try
        {
            if (!Directory.Exists(RomLineUpload.QueueDir)) return;
            string[] files = Directory.GetFiles(RomLineUpload.QueueDir, "xrrom_log_*.txt");
            if (files.Length <= MaxFiles) return;
            Array.Sort(files, StringComparer.Ordinal);   // 이름 앞이 시각이다(yyyyMMdd_HHmmss)
            for (int i = 0; i < files.Length - MaxFiles; i++)
            {
                try { File.Delete(files[i]); } catch (Exception) { }
            }
            Debug.Log($"{Tag} 오래된 로그 {files.Length - MaxFiles}개를 지웠다(상한 {MaxFiles})");
        }
        catch (Exception) { }
    }
}
