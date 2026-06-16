package com.coco.chuna.boot;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.SystemClock;
import android.util.Log;

/**
 * 디바이스 부팅 완료 시 본 앱(메인 런처 액티비티)을 자동 실행한다.
 * 패키지명을 하드코딩하지 않고 getPackageName()으로 해석하므로
 * 메인 빌드(com.CoCo.CHUNA)와 HandRecord 빌드(.handrecord 접미사) 모두에서 동작한다.
 *
 * [동작 방식 — SAW + AlarmManager 늦은 단일 실행]
 * 실측 결과:
 *  - 부팅 직후(+4초)에 띄우면 부팅 리소스 폭주(메모리 고갈/vrruntime 크래시) 중이라
 *    무거운 Unity 앱 메인스레드가 못 버티고 ANR(Input dispatch timeout) → 강제종료.
 *  - 늦게(AlarmManager) 띄우면 부트 면제창 밖이라 BAL_BLOCK.
 * 두 문제를 동시에 풀려면: SYSTEM_ALERT_WINDOW 권한(BAL 면제)을 부여한 상태에서
 *  AlarmManager로 리소스가 안정된 뒤(LAUNCH_DELAY_MS) 단 한 번 실행한다.
 *
 * 전제: 앱에 SYSTEM_ALERT_WINDOW 권한(appop "allow")이 부여돼 있어야 +15초 실행이
 * BAL에 막히지 않는다. manifest 선언(키오스크 빌드 주입)만으론 부족하고
 * 기기마다 `adb shell appops set <pkg> SYSTEM_ALERT_WINDOW allow` 1회가 필요하다.
 *
 * onReceive는 알람만 예약하고 즉시 리턴 → 브로드캐스트 ANR 없음.
 * 단일 실행 → 반복 깜빡임/Unity 초기화 충돌 없음.
 *
 * 한계: 메타 셸/Guardian 자체는 억제 못 함. 완전 제어는 키오스크(HMS)만 가능.
 */
public class BootReceiver extends BroadcastReceiver {

    private static final String TAG = "ChunaBootReceiver";

    // 부팅 후 이 시간(ms) 뒤 실행. 부팅 리소스 폭주가 가라앉은 뒤여야 ANR을 피한다.
    // 실측상 부팅 직후 수 초간 lowmemorykiller가 도는 구간을 넘기도록 충분히 둔다.
    // 너무 짧으면 ANR, 너무 길면 검은/홈 화면 대기가 길어진다. 15초 기준 튜닝.
    private static final long LAUNCH_DELAY_MS = 15000L;

    private static final int REQUEST_CODE = 7301;

    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null || intent == null) {
            return;
        }

        String action = intent.getAction();
        if (action == null) {
            return;
        }

        boolean isBootAction =
                action.equals(Intent.ACTION_BOOT_COMPLETED)
                || action.equals("android.intent.action.QUICKBOOT_POWERON")
                || action.equals("com.htc.intent.action.QUICKBOOT_POWERON");

        if (!isBootAction) {
            return;
        }

        // 브로드캐스트를 붙잡지 않고 즉시 알람만 예약 → onReceive는 밀리초 단위로 리턴(ANR 방지).
        scheduleDelayedLaunch(context.getApplicationContext());
    }

    private void scheduleDelayedLaunch(Context context) {
        try {
            String packageName = context.getPackageName();
            Intent launchIntent = context.getPackageManager()
                    .getLaunchIntentForPackage(packageName);

            if (launchIntent == null) {
                Log.w(TAG, "No launch intent found for " + packageName);
                return;
            }

            launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            launchIntent.addFlags(Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);

            int piFlags = PendingIntent.FLAG_UPDATE_CURRENT;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                piFlags |= PendingIntent.FLAG_IMMUTABLE;
            }

            PendingIntent pendingIntent = PendingIntent.getActivity(
                    context, REQUEST_CODE, launchIntent, piFlags);

            AlarmManager alarmManager =
                    (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
            if (alarmManager == null) {
                Log.w(TAG, "AlarmManager unavailable");
                return;
            }

            long triggerAt = SystemClock.elapsedRealtime() + LAUNCH_DELAY_MS;

            // API 31+(Android 12)부터 setExact*/setExactAndAllowWhileIdle 는 SCHEDULE_EXACT_ALARM
            // 권한을 요구한다(targetSdk 32). 권한이 없으면 SecurityException 으로 알람이 조용히
            // 안 걸려 자동실행이 통째로 실패하므로, 정확알람이 불가하면 inexact(권한 불필요)로 폴백한다.
            // 부팅 +15초는 밀리초 정밀도가 필요 없으므로 inexact 로도 충분히 동작한다.
            boolean canExact = true;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                canExact = alarmManager.canScheduleExactAlarms();
            }

            if (canExact && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                alarmManager.setExactAndAllowWhileIdle(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAt, pendingIntent);
                Log.i(TAG, "Boot auto-launch scheduled (exact) in " + LAUNCH_DELAY_MS + "ms for " + packageName);
            } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                // 정확알람 권한 없음 → Doze 우회 inexact 알람(SCHEDULE_EXACT_ALARM 불필요).
                alarmManager.setAndAllowWhileIdle(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAt, pendingIntent);
                Log.i(TAG, "Boot auto-launch scheduled (inexact fallback) in ~" + LAUNCH_DELAY_MS + "ms for " + packageName);
            } else {
                alarmManager.setExact(
                        AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAt, pendingIntent);
                Log.i(TAG, "Boot auto-launch scheduled (exact, pre-M) in " + LAUNCH_DELAY_MS + "ms for " + packageName);
            }
        } catch (Exception e) {
            Log.e(TAG, "Failed to schedule boot auto-launch", e);
        }
    }
}
