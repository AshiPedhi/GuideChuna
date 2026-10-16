using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 모니터로 내보내기(2026-10-07 신설).
///
/// 헤드셋을 쓰지 않고 정해 둔 자리에 놓은 채 컨트롤러만으로 잴 때, 측정자는 헤드셋 안의 판을 못 본다.
/// 그래서 헤드셋 앱이 작은 웹 서버를 열고, 같은 Wi-Fi의 PC·태블릿이 <b>브라우저</b>로 그 주소를 열어 화면을 본다
/// (따로 깔 프로그램이 없다). 영상을 보내는 것이 아니라 <b>값</b>(선·각·추적 상태)을 보낸다 — 가볍고, 미러링 영상 경로와 무관하다.
///
///   GET /        관제 미러링 화면(Resources/RomLine/mirror.html — 10-16부터. 종전 monitor.html은 미사용)
///   GET /state   지금 상태(JSON) — 화면이 0.15초마다 묻는다
///   GET /wall.jpg · /cam.jpg · /mirrorinfo   미러링 이미지 · 진단(RomLineMirror가 넣는다, 10-16)
///   GET /cmd?c=  화면의 버튼이 보낸 명령 — 주 스레드가 꺼내 처리한다
///   GET /csv     이번 실행에서 찍은 기록(CSV)
///
/// ★서버는 따로 도는 스레드다. Unity 것은 건드리지 않는다 — 주 스레드가 만들어 넘긴 글자만 내보내고, 명령은 줄에 세워 둔다.
/// ★인증이 없다. 같은 망의 누구나 열 수 있다 — 시험용이다(포트를 0으로 두면 열지 않는다).
/// </summary>
public class RomLineMonitor
{
    private TcpListener listener;
    private Thread thread;
    private volatile bool running;
    private volatile string stateJson = "{}";
    private volatile string csvText = "";
    private string page = "";
    private readonly ConcurrentQueue<string> commands = new ConcurrentQueue<string>();
    private long lastPollTicks;

    public int Port { get; private set; }
    /// <summary>브라우저에 칠 주소. 못 열었으면 빈 글자.</summary>
    public string Url { get; private set; } = "";

    /// <summary>최근 3초 안에 화면이 상태를 물어 갔는가 — 보는 화면이 없으면 주 스레드가 상태 글자를 만들지 않는다.</summary>
    public bool Watched => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPollTicks) < TimeSpan.TicksPerSecond * 3;

    public bool Start(int port, string pageHtml)
    {
        if (running) return true;
        page = pageHtml ?? "";
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[선실측] ★모니터 — 포트 {port}를 열지 못했다({e.Message}). 모니터 화면 없이 계속한다.");
            listener = null;
            return false;
        }
        Port = port;
        string ip = LocalIp();
        Url = ip.Length > 0 ? $"http://{ip}:{port}" : $"http://(헤드셋 IP):{port}";
        running = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "RomLineMonitor" };
        thread.Start();
        Debug.Log($"[선실측] 모니터 — 열었다 · 같은 Wi-Fi의 브라우저에서 {Url} · 화면 {(page.Length > 0 ? page.Length + "자" : "★없음(Resources/RomLine/mirror.html을 못 읽었다)")}");
        return true;
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        try { listener.Stop(); } catch (Exception) { }   // Accept에서 기다리던 스레드가 예외로 빠져나온다
        listener = null;
        Debug.Log("[선실측] 모니터 — 닫았다");
    }

    // ── 관제 미러링 이미지(10-16, RomLineMirror가 넣는다) ──
    private volatile byte[] wallJpg, camJpg;
    private volatile string mirrorInfo = "{}";
    private long wallPollTicks, camPollTicks;

    /// <summary>최근 3초 안에 화면이 그 이미지(«wall» · «cam»)를 물어 갔는가 — 아무도 안 보면 찍지 않는다.</summary>
    public bool ImageWatched(string name)
    {
        long t = name == "wall" ? Interlocked.Read(ref wallPollTicks) : Interlocked.Read(ref camPollTicks);
        return DateTime.UtcNow.Ticks - t < TimeSpan.TicksPerSecond * 3;
    }
    /// <summary>JPEG 한 장을 건넨다(아무 스레드에서나).</summary>
    public void SetImage(string name, byte[] jpg) { if (name == "wall") wallJpg = jpg; else camJpg = jpg; }
    public void SetMirrorInfo(string json) { mirrorInfo = json; }

    /// <summary>주 스레드가 만든 상태(JSON)를 건넨다.</summary>
    public void SetState(string json) { stateJson = json; }
    public void SetCsv(string text) { csvText = text; }
    public bool TryDequeue(out string command) { return commands.TryDequeue(out command); }

    private void Loop()
    {
        while (running)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception) { break; }   // Stop이 닫았다
            ThreadPool.QueueUserWorkItem(Handle, client);   // 느린 화면 하나가 다른 화면을 막지 않게
        }
    }

    private void Handle(object o)
    {
        var client = (TcpClient)o;
        try
        {
            using (client)
            {
                client.ReceiveTimeout = client.SendTimeout = 2000;
                NetworkStream st = client.GetStream();
                var buf = new byte[2048];
                int n = 0;
                while (n < buf.Length)
                {
                    int r = st.Read(buf, n, buf.Length - n);
                    if (r <= 0) break;
                    n += r;
                    if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n') break;   // 머리글 끝
                }
                string req = Encoding.ASCII.GetString(buf, 0, n);
                int a = req.IndexOf(' '), b = a < 0 ? -1 : req.IndexOf(' ', a + 1);
                if (b < 0) return;
                string path = req.Substring(a + 1, b - a - 1);

                if (path == "/" || path.StartsWith("/index") || path.StartsWith("/#"))
                    Send(st, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page), null);
                else if (path.StartsWith("/state"))
                {
                    Interlocked.Exchange(ref lastPollTicks, DateTime.UtcNow.Ticks);
                    Send(st, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(stateJson), null);
                }
                else if (path.StartsWith("/cmd?c="))
                {
                    string c = Uri.UnescapeDataString(path.Substring(7));
                    if (c.Length > 0 && c.Length <= 24) commands.Enqueue(c);
                    Send(st, "200 OK", "application/json; charset=utf-8", Encoding.ASCII.GetBytes("{\"ok\":true}"), null);
                }
                else if (path.StartsWith("/wall.jpg") || path.StartsWith("/cam.jpg"))
                {
                    bool wall = path.StartsWith("/wall");
                    Interlocked.Exchange(ref wall ? ref wallPollTicks : ref camPollTicks, DateTime.UtcNow.Ticks);
                    byte[] jpg = wall ? wallJpg : camJpg;
                    if (jpg != null) Send(st, "200 OK", "image/jpeg", jpg, null);
                    else Send(st, "503 Service Unavailable", "text/plain; charset=utf-8", Encoding.ASCII.GetBytes("no image yet"), null);   // 첫 장 전 · 지원 안 함
                }
                else if (path.StartsWith("/mirrorinfo"))
                    Send(st, "200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(mirrorInfo), null);
                else if (path.StartsWith("/csv"))
                {
                    byte[] body = Encoding.UTF8.GetBytes(csvText);
                    var withBom = new byte[body.Length + 3];   // 엑셀이 한글을 읽게 BOM을 붙인다
                    withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
                    Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
                    Send(st, "200 OK", "text/csv; charset=utf-8", withBom, "Content-Disposition: attachment; filename=\"romline_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv\"\r\n");
                }
                else Send(st, "404 Not Found", "text/plain; charset=utf-8", Encoding.ASCII.GetBytes("not found"), null);
            }
        }
        catch (Exception) { }   // 화면이 도중에 닫혔다 — 다음 요청이 다시 온다
    }

    private static void Send(NetworkStream st, string status, string type, byte[] body, string extraHeader)
    {
        string head = "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                      "\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n" + (extraHeader ?? "") + "\r\n";
        byte[] h = Encoding.ASCII.GetBytes(head);
        st.Write(h, 0, h.Length);
        st.Write(body, 0, body.Length);
        st.Flush();
    }

    /// <summary>이 기기가 망에서 쓰는 주소(IPv4). 못 찾으면 빈 글자.</summary>
    private static string LocalIp()
    {
        // 바깥 주소로 «연결»만 해 보면(UDP라 실제로 보내는 것은 없다) 그쪽으로 나가는 내 주소가 나온다.
        try
        {
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect("8.8.8.8", 65530);
                if (s.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
            }
        }
        catch (Exception) { }
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address)) return ua.Address.ToString();
            }
        }
        catch (Exception) { }
        return "";
    }
}
