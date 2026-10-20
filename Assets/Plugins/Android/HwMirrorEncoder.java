package com.coco.chuna.mirror;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.util.Log;

import java.io.OutputStream;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.ByteBuffer;

/**
 * ★미러링 시험 ③(2026-10-20) — 앱이 그린 눈 화면을 헤드셋 하드웨어 H.264로 압축해 TCP로 내보낸다.
 *
 * Unity가 NV12(Y 평면 + UV 교차 평면) 바이트를 feed()로 넘기면 MediaCodec(c2.qti.avc.encoder 예상)에 넣고,
 * 나온 Annex-B 스트림을 포트에 붙은 클라이언트 한 곳에 그대로 흘린다(PC: ffmpeg -f h264 -i tcp://IP:포트).
 * 클라이언트가 없으면 압축은 계속하되 버린다. 새 클라이언트가 붙으면 SPS/PPS를 먼저 보내고 키프레임을 요청한다.
 *
 * 시험 전용이다 — HwMirrorTest(C#)가 시스템 속성 debug.coco.hwmirror=1일 때만 만든다.
 */
public class HwMirrorEncoder {
    private static final String TAG = "HwMirror";

    private MediaCodec codec;
    private int width, height;
    private volatile boolean running;
    private ServerSocket server;
    private volatile OutputStream client;
    private byte[] config;              // SPS/PPS(새 클라이언트에 먼저 보낸다)
    private volatile boolean wantKey;

    // 진단
    private volatile int fedFrames, outFrames, sentFrames, droppedInput;
    private volatile long outBytes;
    private volatile double feedMsSum;

    public String start(int w, int h, int fps, int bitrate, int port) {
        width = w; height = h;
        try {
            MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
            f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            f.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
            f.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 2);
            f.setInteger(MediaFormat.KEY_LATENCY, 1);
            codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            codec.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            codec.start();
            running = true;
            new Thread(this::drainLoop, "HwMirrorDrain").start();
            new Thread(() -> acceptLoop(port), "HwMirrorAccept").start();
            String name = codec.getName();
            Log.i(TAG, "시작 " + w + "x" + h + " " + fps + "fps " + bitrate + "bps 포트 " + port + " 압축기 " + name);
            return name;
        } catch (Exception e) {
            Log.e(TAG, "시작 실패", e);
            return "실패: " + e;
        }
    }

    /**
     * NV12 한 장(w*h*3/2 바이트). src는 Unity NativeArray를 그대로 감싼 direct ByteBuffer다(복사 없이 넘어온다).
     * 입력 칸이 없으면 버린다(Unity 쪽을 막지 않는다).
     */
    public boolean feed(ByteBuffer src, long ptsUs) {
        if (!running) return false;
        long t0 = System.nanoTime();
        int idx;
        try { idx = codec.dequeueInputBuffer(0); } catch (Exception e) { return false; }
        if (idx < 0) { droppedInput++; return false; }
        try {
            src = src.duplicate();
            Image img = codec.getInputImage(idx);
            Image.Plane[] p = img.getPlanes();
            // Y
            ByteBuffer yb = p[0].getBuffer();
            int yStride = p[0].getRowStride();
            for (int r = 0; r < height; r++) { yb.position(r * yStride); yb.put(row(src, r * width, width)); }
            // UV — 반평면 압축기는 U 평면 버퍼가 UV 교차 배열을 그대로 덮는다(pixelStride 2). 아니면 한 칸씩 나눠 쓴다.
            int uvOff = width * height;
            int ch = height / 2, cw = width / 2;
            if (p[1].getPixelStride() == 2) {
                ByteBuffer ub = p[1].getBuffer();
                int uStride = p[1].getRowStride();
                // U 평면 버퍼는 마지막 V 한 바이트가 빠진 길이일 수 있다 → 줄마다 남은 길이로 자른다.
                for (int r = 0; r < ch; r++) {
                    int len = Math.min(width, ub.capacity() - r * uStride);
                    ub.position(r * uStride); ub.put(row(src, uvOff + r * width, len));
                }
            } else {
                ByteBuffer ub = p[1].getBuffer(), vb = p[2].getBuffer();
                int us = p[1].getRowStride(), vs = p[2].getRowStride();
                for (int r = 0; r < ch; r++)
                    for (int c = 0; c < cw; c++) {
                        ub.put(r * us + c, src.get(uvOff + r * width + c * 2));
                        vb.put(r * vs + c, src.get(uvOff + r * width + c * 2 + 1));
                    }
            }
            codec.queueInputBuffer(idx, 0, width * height * 3 / 2, ptsUs, 0);
            fedFrames++;
            feedMsSum += (System.nanoTime() - t0) / 1e6;
            return true;
        } catch (Exception e) {
            Log.e(TAG, "feed 실패", e);
            return false;
        }
    }

    /** s의 [off, off+len) 구간만 보이게 맞춘다(복사 없음 · s는 feed마다 한 번 만든 duplicate). */
    private static ByteBuffer row(ByteBuffer s, int off, int len) {
        s.clear();
        s.position(off);
        s.limit(off + len);
        return s;
    }

    private void drainLoop() {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        while (running) {
            int idx;
            try { idx = codec.dequeueOutputBuffer(info, 10000); } catch (Exception e) { break; }
            if (idx < 0) continue;
            ByteBuffer ob = codec.getOutputBuffer(idx);
            byte[] data = new byte[info.size];
            ob.position(info.offset); ob.get(data);
            codec.releaseOutputBuffer(idx, false);
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) { config = data; continue; }
            outFrames++; outBytes += data.length;
            OutputStream os = client;
            if (os == null) continue;
            if (wantKey) {
                if ((info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) == 0) continue;   // 키프레임부터 보낸다
                wantKey = false;
            }
            try { os.write(data); sentFrames++; }
            catch (Exception e) { Log.i(TAG, "클라이언트 끊김"); client = null; }
        }
    }

    private void acceptLoop(int port) {
        try {
            server = new ServerSocket(port);
            while (running) {
                Socket s = server.accept();
                s.setTcpNoDelay(true);
                OutputStream os = s.getOutputStream();
                if (config != null) os.write(config);
                wantKey = true;
                android.os.Bundle b = new android.os.Bundle();
                b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0);
                codec.setParameters(b);
                client = os;
                Log.i(TAG, "클라이언트 접속 " + s.getInetAddress());
            }
        } catch (Exception e) {
            if (running) Log.e(TAG, "accept 실패", e);
        }
    }

    /** 진단 한 줄: 넣은 장수 · 압축된 장수 · 보낸 장수 · 입력 칸 없어 버린 장수 · 평균 넣기 ms · 누적 바이트. 부를 때마다 0으로. */
    public String stats() {
        String s = "넣음 " + fedFrames + " · 압축 " + outFrames + " · 보냄 " + sentFrames + " · 버림 " + droppedInput +
                   " · 넣기 " + String.format("%.1f", fedFrames > 0 ? feedMsSum / fedFrames : 0) + "ms · " + (outBytes / 1024) + "KB" +
                   " · 클라이언트 " + (client != null ? "있음" : "없음");
        fedFrames = outFrames = sentFrames = droppedInput = 0; outBytes = 0; feedMsSum = 0;
        return s;
    }

    public void stop() {
        running = false;
        try { if (server != null) server.close(); } catch (Exception ignored) { }
        try { codec.stop(); codec.release(); } catch (Exception ignored) { }
    }
}
