package com.coco.chuna.mirror;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.os.Bundle;
import android.util.Log;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * ★선 실측 관제 미러링 — 영상(H.264) 경로(2026-10-21 사용자 지시: JPEG 대신 하드웨어 압축으로 배터리 · 발열을 줄인다).
 *
 * Unity(RomLineMirror)가 GPU에서 만든 NV12 한 장을 feed()로 넘기면 MediaCodec(하드웨어 AVC)으로 압축하고,
 * 나온 Annex-B 스트림을 HTTP(GET /video)로 붙은 브라우저마다 흘린다. 브라우저는 jmuxer로 MSE에 넣어 &lt;video&gt;로 튼다.
 * 관제 화면(:8090, RomLineMonitor)과 포트가 다르므로 응답에 CORS 머리글을 단다.
 *
 * 브라우저마다 보내는 스레드와 줄(최대 QueueMax장)을 따로 둔다 — 느린 화면 하나가 압축 · 다른 화면을 막지 않는다.
 * 줄이 넘치면 그 화면은 다음 키프레임까지 건너뛴다(깨진 화면 대신 잠깐 멈춤).
 * 키프레임마다 SPS/PPS를 앞에 붙인다(PREPEND_HEADER) — 새로 붙은 화면이 다음 키프레임부터 바로 풀 수 있다.
 * 종전 시험(HwMirrorEncoder · TCP 8091 · 한 클라이언트)은 그대로 두었다.
 */
public class RomVideoEncoder {
    private static final String TAG = "RomVideo";
    private static final int QueueMax = 30;

    private volatile MediaCodec codec;
    private int width, height, fps, bitrate;
    private volatile boolean running;
    private ServerSocket server;
    private byte[] config;
    private final CopyOnWriteArrayList<Client> clients = new CopyOnWriteArrayList<>();

    // 진단(부를 때마다 0으로)
    private volatile int fedFrames, outFrames, droppedInput;
    private volatile long outBytes;

    private class Client {
        final Socket socket;
        final OutputStream os;
        final ArrayBlockingQueue<byte[]> q = new ArrayBlockingQueue<>(QueueMax);
        volatile boolean waitKey = true;
        volatile boolean alive = true;

        Client(Socket s, OutputStream o) { socket = s; os = o; }

        void writeLoop() {
            try {
                while (alive && running) {
                    byte[] d = q.take();
                    if (d.length == 0) break;   // 닫기 신호
                    os.write(d);
                    os.flush();
                }
            } catch (Exception ignored) { }
            close();
        }

        void close() {
            if (!alive) return;
            alive = false;
            clients.remove(this);
            q.offer(new byte[0]);
            try { socket.close(); } catch (Exception ignored) { }
            Log.i(TAG, "화면 끊김 · 남은 화면 " + clients.size());
        }
    }

    /** 서버(포트)를 연다. 압축기는 첫 feed 전에 configure()로 크기를 정해 만든다. */
    public boolean listen(int port) {
        try {
            server = new ServerSocket(port);
            running = true;
            new Thread(this::acceptLoop, "RomVideoAccept").start();
            Log.i(TAG, "열었다 · 포트 " + port);
            return true;
        } catch (Exception e) {
            Log.e(TAG, "포트 " + port + " 열기 실패", e);
            return false;
        }
    }

    /** 압축기를 (다시) 만든다. 크기가 바뀌면 붙어 있던 화면을 끊는다 — 브라우저가 새 SPS로 다시 붙는다. 이름 또는 «실패: …». */
    public synchronized String configure(int w, int h, int fps, int bitrate) {
        if (codec != null && w == width && h == height) return codec.getName();
        stopCodec();
        for (Client c : clients) c.close();
        width = w; height = h; this.fps = fps; this.bitrate = bitrate;
        try {
            MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
            f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            f.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            f.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
            f.setInteger(MediaFormat.KEY_LATENCY, 1);
            f.setInteger(MediaFormat.KEY_PREPEND_HEADER_TO_SYNC_FRAMES, 1);
            codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            codec.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            codec.start();
            final MediaCodec mine = codec;
            new Thread(() -> drainLoop(mine), "RomVideoDrain").start();
            Log.i(TAG, "압축기 " + w + "x" + h + " " + fps + "fps " + bitrate + "bps " + codec.getName());
            return codec.getName();
        } catch (Exception e) {
            Log.e(TAG, "압축기 만들기 실패", e);
            codec = null;
            return "실패: " + e;
        }
    }

    public int clientCount() { return clients.size(); }

    /** NV12 한 장(w*h*3/2). src는 Unity NativeArray를 감싼 direct ByteBuffer(복사 없이 넘어온다). 입력 칸이 없으면 버린다. */
    public boolean feed(ByteBuffer src, long ptsUs) {
        MediaCodec c = codec;
        if (c == null) return false;
        int idx;
        try { idx = c.dequeueInputBuffer(0); } catch (Exception e) { return false; }
        if (idx < 0) { droppedInput++; return false; }
        try {
            src = src.duplicate();
            Image img = c.getInputImage(idx);
            Image.Plane[] p = img.getPlanes();
            ByteBuffer yb = p[0].getBuffer();
            int yStride = p[0].getRowStride();
            for (int r = 0; r < height; r++) { yb.position(r * yStride); yb.put(row(src, r * width, width)); }
            int uvOff = width * height, ch = height / 2, cw = width / 2;
            if (p[1].getPixelStride() == 2) {
                ByteBuffer ub = p[1].getBuffer();
                int uStride = p[1].getRowStride();
                for (int r = 0; r < ch; r++) {
                    int len = Math.min(width, ub.capacity() - r * uStride);   // U 평면 버퍼는 마지막 V 한 바이트가 빠진 길이일 수 있다
                    ub.position(r * uStride); ub.put(row(src, uvOff + r * width, len));
                }
            } else {
                ByteBuffer ub = p[1].getBuffer(), vb = p[2].getBuffer();
                int us = p[1].getRowStride(), vs = p[2].getRowStride();
                for (int r = 0; r < ch; r++)
                    for (int x = 0; x < cw; x++) {
                        ub.put(r * us + x, src.get(uvOff + r * width + x * 2));
                        vb.put(r * vs + x, src.get(uvOff + r * width + x * 2 + 1));
                    }
            }
            c.queueInputBuffer(idx, 0, width * height * 3 / 2, ptsUs, 0);
            fedFrames++;
            return true;
        } catch (Exception e) {
            Log.e(TAG, "feed 실패", e);
            return false;
        }
    }

    private static ByteBuffer row(ByteBuffer s, int off, int len) {
        s.clear();
        s.position(off);
        s.limit(off + len);
        return s;
    }

    private void drainLoop(MediaCodec c) {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        while (running && c == codec) {
            int idx;
            try { idx = c.dequeueOutputBuffer(info, 10000); } catch (Exception e) { break; }
            if (idx < 0) continue;
            byte[] data;
            try {
                ByteBuffer ob = c.getOutputBuffer(idx);
                data = new byte[info.size];
                ob.position(info.offset); ob.get(data);
                c.releaseOutputBuffer(idx, false);
            } catch (Exception e) { break; }
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) { config = data; continue; }
            outFrames++; outBytes += data.length;
            boolean key = (info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0;
            boolean needKey = false;
            for (Client cl : clients) {
                if (cl.waitKey) {
                    if (!key) { needKey = true; continue; }
                    cl.waitKey = false;
                }
                if (!cl.q.offer(data)) { cl.q.clear(); cl.waitKey = true; needKey = true; }   // 넘침 → 다음 키프레임까지 건너뛴다
            }
            if (needKey) requestKey();
        }
    }

    private long lastKeyReq;
    private void requestKey() {
        long now = System.currentTimeMillis();
        if (now - lastKeyReq < 500) return;
        lastKeyReq = now;
        try {
            Bundle b = new Bundle();
            b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0);
            MediaCodec c = codec;
            if (c != null) c.setParameters(b);
        } catch (Exception ignored) { }
    }

    private void acceptLoop() {
        while (running) {
            Socket s;
            try { s = server.accept(); } catch (Exception e) { break; }
            new Thread(() -> serve(s), "RomVideoClient").start();
        }
    }

    private void serve(Socket s) {
        try {
            s.setTcpNoDelay(true);
            s.setSoTimeout(3000);
            InputStream in = s.getInputStream();
            byte[] buf = new byte[4096];
            int n = 0;
            while (n < buf.length) {
                int r = in.read(buf, n, buf.length - n);
                if (r <= 0) break;
                n += r;
                if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n') break;
            }
            s.setSoTimeout(0);
            String req = new String(buf, 0, n, StandardCharsets.US_ASCII);
            OutputStream os = s.getOutputStream();
            if (!req.startsWith("GET /video")) {
                os.write(("HTTP/1.1 404 Not Found\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
                s.close();
                return;
            }
            os.write(("HTTP/1.1 200 OK\r\nContent-Type: video/h264\r\nCache-Control: no-store\r\n" +
                      "Access-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
            os.flush();
            Client c = new Client(s, os);
            clients.add(c);
            requestKey();
            Log.i(TAG, "화면 접속 " + s.getInetAddress() + " · 화면 " + clients.size());
            c.writeLoop();
        } catch (Exception e) {
            try { s.close(); } catch (Exception ignored) { }
        }
    }

    /** 진단(JSON 조각, 앞에 쉼표 없음): 넣은 장수 · 압축된 장수 · 입력 칸 없어 버린 장수 · 압축 바이트 · 화면 수 · 크기. 부를 때마다 0으로. */
    public String stats() {
        String s = "\"vFed\":" + fedFrames + ",\"vOut\":" + outFrames + ",\"vDrop\":" + droppedInput + ",\"vBytes\":" + outBytes +
                   ",\"vClients\":" + clients.size() + ",\"vRes\":\"" + width + "x" + height + "\"";
        fedFrames = outFrames = droppedInput = 0; outBytes = 0;
        return s;
    }

    private void stopCodec() {
        MediaCodec c = codec;
        codec = null;
        if (c == null) return;
        try { c.stop(); } catch (Exception ignored) { }
        try { c.release(); } catch (Exception ignored) { }
    }

    public void stop() {
        running = false;
        for (Client c : clients) c.close();
        try { if (server != null) server.close(); } catch (Exception ignored) { }
        synchronized (this) { stopCodec(); }
    }
}
