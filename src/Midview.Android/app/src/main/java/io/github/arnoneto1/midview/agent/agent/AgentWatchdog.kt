package io.github.arnoneto1.midview.agent.agent

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.PowerManager
import android.os.SystemClock
import io.github.arnoneto1.midview.agent.policy.LockScreenKeeper
import io.github.arnoneto1.midview.agent.policy.SchoolNetwork

// The watchdog: an alarm every few seconds (WatchdogAlarm) that starts the
// agent again if it is not running, and arms itself again. Android keeps the
// alarm when the app's process is killed, so the agent comes back by itself
// within seconds. Armed at every start of the agent (after the phone starts,
// too); only a force stop, which a school phone does not allow, clears it.
// It wakes the processor every 10 s while the screen is on, every 2 minutes
// while it is off (WatchdogAlarm).
class AgentWatchdog : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        AgentService.ensureRunning(context, "watchdog")
        // The lock screen too: the teacher's PIN put back if Android's
        // "password changed" never came.
        LockScreenKeeper.check(context)
        // The Wi-Fi and airplane mode locked again once their free time is up.
        SchoolNetwork.check(context)
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
            val screenOn = app.getSystemService(PowerManager::class.java)?.isInteractive ?: true
            val at = WatchdogAlarm.nextAt(SystemClock.elapsedRealtime(), screenOn)
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

        // While the agent runs: the screen turned on brings the 10 s check
        // back at once, instead of after the 2 minutes armed with it off.
        // Returns what stops watching.
        fun watchScreen(context: Context): () -> Unit {
            val app = context.applicationContext
            val receiver = object : BroadcastReceiver() {
                override fun onReceive(context: Context, intent: Intent) = arm(context)
            }
            val filter = IntentFilter(Intent.ACTION_SCREEN_ON)
            // The screen broadcast comes only from the system.
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                app.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
            } else {
                app.registerReceiver(receiver, filter)
            }
            return { runCatching { app.unregisterReceiver(receiver) } }
        }
    }
}
