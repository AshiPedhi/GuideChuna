package com.coco.chuna.mirror;

import android.app.Activity;
import android.content.Intent;
import android.media.projection.MediaProjectionManager;
import android.os.Bundle;
import android.util.Log;

/**
 * ★시험 전용(2026-10-23) — 안드로이드 화면 녹화 권한(MediaProjection)으로 앱이 시스템 최종 화면
 * (몰입 화면 + 패스스루)을 받을 수 있는지 확인한다. Meta 캐스팅은 1080p를 낸다는데 앱용 카메라 API는 1280급이라
 * 이 통로가 있는지 보는 것이다.
 *
 * Unity 쪽 코드는 없다. PC에서 띄운다:
 *   adb shell am start -n com.CoCo.CHUNA/com.coco.chuna.mirror.ProjectionTestActivity
 * 헤드셋에 동의 창이 뜨면 [시작]을 누른다 → ProjectionTestService가 몇 초 간격으로 PNG를 남긴다.
 */
public class ProjectionTestActivity extends Activity {
    static final String TAG = "ProjTest";
    private static final int REQ = 7301;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        MediaProjectionManager mpm = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        Log.i(TAG, "동의 창 요청");
        startActivityForResult(mpm.createScreenCaptureIntent(), REQ);
    }

    @Override
    protected void onActivityResult(int req, int result, Intent data) {
        Log.i(TAG, "동의 결과 " + result + (result == RESULT_OK ? " (허용)" : " (거절)"));
        if (req == REQ && result == RESULT_OK && data != null) {
            Intent s = new Intent(this, ProjectionTestService.class);
            s.putExtra("code", result);
            s.putExtra("data", data);
            startForegroundService(s);
            return;   // 10-23 1차: 바로 finish하자 이 창(task)이 닫히며 projection이 같이 멈췄다 → 시험 동안 띄워 둔다
        }
        finish();
    }
}
