package com.coco.chuna.mirror;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.WindowManager;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;

/**
 * ★시험 전용(2026-10-23) — ProjectionTestActivity가 받은 동의로 화면을 받아 PNG로 남긴다.
 * 안드로이드 10 이상은 mediaProjection 형 포그라운드 서비스 안에서만 받을 수 있어 서비스로 둔다.
 * 기본 화면 크기 그대로 받고, 3 · 6 · 10 · 15초에 한 장씩 앱 외부 파일 폴더(…/files/projtest_N.png)에 쓴다.
 */
public class ProjectionTestService extends Service {
    private static final String TAG = ProjectionTestActivity.TAG;
    private static final long[] SHOTS_MS = { 3000, 6000, 10000, 15000 };

    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private HandlerThread thread;
    private Handler handler;

    @Override public IBinder onBind(Intent i) { return null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel("projtest", "화면 받기 시험", NotificationManager.IMPORTANCE_LOW));
        Notification n = new Notification.Builder(this, "projtest")
                .setContentTitle("화면 받기 시험").setSmallIcon(android.R.drawable.ic_menu_camera).build();
        startForeground(7301, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);

        int code = intent.getIntExtra("code", 0);
        Intent data = intent.getParcelableExtra("data");
        MediaProjectionManager mpm = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        projection = mpm.getMediaProjection(code, data);
        if (projection == null) { Log.e(TAG, "getMediaProjection null"); stopSelf(); return START_NOT_STICKY; }

        thread = new HandlerThread("projtest");
        thread.start();
        handler = new Handler(thread.getLooper());
        projection.registerCallback(new MediaProjection.Callback() {
            @Override public void onStop() { Log.i(TAG, "projection 멈춤"); }
        }, handler);

        DisplayMetrics dm = new DisplayMetrics();
        ((WindowManager) getSystemService(WINDOW_SERVICE)).getDefaultDisplay().getRealMetrics(dm);
        int w = 1920, h = 1080;   // 10-23 1차: 서비스의 기본 화면은 패널 창 크기(500x800)였다 → 크기를 못박는다
        Log.i(TAG, "기본 화면 " + dm.widthPixels + "x" + dm.heightPixels + " dpi " + dm.densityDpi + " — " + w + "x" + h + "로 받는다");

        reader = ImageReader.newInstance(w, h, PixelFormat.RGBA_8888, 2);
        display = projection.createVirtualDisplay("projtest", w, h, dm.densityDpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, reader.getSurface(), null, handler);

        for (int i = 0; i < SHOTS_MS.length; i++) {
            final int idx = i;
            handler.postDelayed(() -> shot(idx), SHOTS_MS[i]);
        }
        handler.postDelayed(this::stopSelf, SHOTS_MS[SHOTS_MS.length - 1] + 1000);
        return START_NOT_STICKY;
    }

    private void shot(int idx) {
        Image img = reader.acquireLatestImage();
        if (img == null) { Log.w(TAG, "장 " + idx + ": 받은 화면 없음"); return; }
        try {
            int w = img.getWidth(), h = img.getHeight();
            Image.Plane p = img.getPlanes()[0];
            ByteBuffer buf = p.getBuffer();
            int pixStride = p.getPixelStride(), rowStride = p.getRowStride();
            Bitmap bmp = Bitmap.createBitmap(rowStride / pixStride, h, Bitmap.Config.ARGB_8888);
            bmp.copyPixelsFromBuffer(buf);
            Bitmap crop = Bitmap.createBitmap(bmp, 0, 0, w, h);

            long sum = 0; int n = 0;
            for (int y = 0; y < h; y += 16)
                for (int x = 0; x < w; x += 16) {
                    int c = crop.getPixel(x, y);
                    sum += ((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255);
                    n++;
                }
            File f = new File(getExternalFilesDir(null), "projtest_" + idx + ".png");
            try (FileOutputStream o = new FileOutputStream(f)) { crop.compress(Bitmap.CompressFormat.PNG, 100, o); }
            Log.i(TAG, "장 " + idx + ": " + w + "x" + h + " 평균 밝기 " + (sum / Math.max(1, n * 3)) + " → " + f.getAbsolutePath());
        } catch (Exception e) {
            Log.e(TAG, "장 " + idx + " 저장 실패", e);
        } finally {
            img.close();
        }
    }

    @Override
    public void onDestroy() {
        if (display != null) display.release();
        if (projection != null) projection.stop();
        if (thread != null) thread.quitSafely();
        Log.i(TAG, "시험 끝");
        super.onDestroy();
    }
}
