package io.github.arnoneto1.midview.agent.agent

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.PowerManager
import io.github.arnoneto1.midview.agent.power.PowerCountdown
import io.github.arnoneto1.midview.agent.power.PowerScheduleStore
import io.github.arnoneto1.midview.agent.power.ScheduledScreenOff
import java.io.File
import java.time.Instant

// The alarm of the teacher's "Agendar" (ScheduledScreenOff): at the
// schedule's next time, on the wall clock, exact when Android lets the app
// (as the watchdog's, WatchdogAlarm) and even in Doze. Android keeps it
// while the app's process is killed, and clears it when the phone restarts
// or the app is updated: every start of the agent sets it again. A ring
// starts the agent if it is not running, and is acted on by the agent's
// follower, or by the next one to start (the process was killed: the ring is
// held for it, in this process).
class PowerScheduleAlarm : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        AgentService.ensureRunning(context, "power schedule")
        val running = synchronized(Companion) {
            follower.also { if (it == null) heldRing = true }
        }
        running?.rang()
    }

    companion object {
        @Volatile private var follower: ScheduledScreenOff? = null
        private var heldRing = false

        // The agent's follower, until what this returns is called; a ring
        // held for it is acted on now.
        fun follow(scheduled: ScheduledScreenOff): () -> Unit {
            val held = synchronized(this) {
                follower = scheduled
                heldRing.also { heldRing = false }
            }
            if (held) scheduled.rang() else scheduled.rearm()
            return { synchronized(this) { if (follower === scheduled) follower = null } }
        }

        // at: the next time, or null to cancel the alarm.
        fun arm(context: Context, at: Instant?) {
            val app = context.applicationContext
            val alarms = app.getSystemService(AlarmManager::class.java)
            val ring = PendingIntent.getBroadcast(
                app, 0, Intent(app, PowerScheduleAlarm::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            if (at == null) {
                alarms.cancel(ring)
                return
            }
            val millis = at.toEpochMilli()
            val canExact = Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarms.canScheduleExactAlarms()
            try {
                when (WatchdogAlarm.kind(Build.VERSION.SDK_INT, canExact)) {
                    WatchdogAlarm.Kind.EXACT -> alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, ring)
                    WatchdogAlarm.Kind.INEXACT -> alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, ring)
                }
            } catch (e: SecurityException) {
                // The exact alarm permission taken back in between.
                alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, ring)
            }
        }
    }
}

// "Agendar" on this phone: the schedule kept in the app's storage, its alarm
// set now and again whenever the clock or the time zone changes, and the
// screen turned off through power ("Desligar": the student's warning, then
// the screen off and locked). Returns the store, for the teacher's command.
internal fun AgentService.scheduledScreenOff(
    power: PowerCountdown,
    log: (String, Throwable?) -> Unit,
    own: (() -> Unit) -> Unit,
): PowerScheduleStore {
    lateinit var scheduled: ScheduledScreenOff
    val store = PowerScheduleStore(File(filesDir, POWER_SCHEDULE_FILE), changed = { scheduled.rearm() }, log = log)
    scheduled = ScheduledScreenOff(
        store,
        arm = { PowerScheduleAlarm.arm(this, it) },
        screenOff = { power.request(PowerCountdown.SHUTDOWN, it) },
        screenOn = { getSystemService(PowerManager::class.java)?.isInteractive ?: true },
        log = log,
    )
    own(PowerScheduleAlarm.follow(scheduled))
    val clockChanged = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) = scheduled.rearm()
    }
    val filter = IntentFilter().apply {
        addAction(Intent.ACTION_TIME_CHANGED)
        addAction(Intent.ACTION_TIMEZONE_CHANGED)
    }
    // Both come only from the system.
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        applicationContext.registerReceiver(clockChanged, filter, Context.RECEIVER_EXPORTED)
    } else {
        applicationContext.registerReceiver(clockChanged, filter)
    }
    own { runCatching { applicationContext.unregisterReceiver(clockChanged) } }
    return store
}

private const val POWER_SCHEDULE_FILE = "power-schedule.txt"
