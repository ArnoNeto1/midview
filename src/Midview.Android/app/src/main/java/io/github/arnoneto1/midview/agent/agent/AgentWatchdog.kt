package io.github.arnoneto1.midview.agent.agent

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.SystemClock

// The watchdog: an alarm every few seconds (WatchdogAlarm) that starts the
// agent again if it is not running, and arms itself again. Android keeps the
// alarm when the app's process is killed, so the agent comes back by itself
// within seconds. Armed at every start of the agent (after the phone starts,
// too); only a force stop, which a school phone does not allow, clears it.
// It wakes the processor every 10 s, also with the screen off: the price of
// a fast return on a school phone that usually sits charging (WatchdogAlarm).
class AgentWatchdog : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        AgentService.ensureRunning(context, "watchdog")
        arm(context)
    }

    companion object {
        fun arm(context: Context) {
            val app = context.applicationContext
            val alarms = app.getSystemService(AlarmManager::class.java)
            val check = PendingIntent.getBroadcast(
                app, 0, Intent(app, AgentWatchdog::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val at = WatchdogAlarm.nextAt(SystemClock.elapsedRealtime())
            val canExact = Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarms.canScheduleExactAlarms()
            try {
                when (WatchdogAlarm.kind(Build.VERSION.SDK_INT, canExact)) {
                    WatchdogAlarm.Kind.EXACT ->
                        alarms.setExactAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, at, check)
                    WatchdogAlarm.Kind.INEXACT ->
                        alarms.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, at, check)
                }
            } catch (e: SecurityException) {
                // The exact alarm permission taken back in between.
                alarms.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, at, check)
            }
        }
    }
}
