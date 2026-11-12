package io.github.arnoneto1.midview.agent.agent

// Keeps the agent running in this process. Some phones kill the whole app
// when the student clears the recent apps (MIUI's "clear all"); Android then
// starts the process again, but its own restart of the service can come
// minutes late, or be stopped at once as an "idle" app's service before it
// runs. So every start of the process and every watchdog check asks for the
// agent here, and it is started (in the foreground) unless it runs in this
// process. Only the service's own onCreate says it runs: a start asked for
// can still be stopped before the service is created, with no onDestroy.
class AgentKeeper(private val start: () -> Unit) {
    private var running = false

    // Checks left before a start asked for and not yet created is asked for
    // again (asking again for a live service changes nothing).
    private var waiting = 0

    // True when the agent was started now. A start Android refuses is thrown
    // to the caller and tried again at the next check.
    @Synchronized
    fun ensureRunning(): Boolean {
        if (running) return false
        if (waiting > 0) {
            waiting--
            return false
        }
        start()
        waiting = CHECKS_TO_WAIT
        return true
    }

    // The service came up (also by Android's own restart).
    @Synchronized
    fun created() {
        running = true
        waiting = 0
    }

    // The service ended: the next check starts it again.
    @Synchronized
    fun destroyed() {
        running = false
        waiting = 0
    }

    private companion object {
        // The process's start and the watchdog's first check come together;
        // the service takes well under one interval (10 s) to be created.
        const val CHECKS_TO_WAIT = 1
    }
}

// The watchdog's alarm, which survives the app's process: every few seconds
// it checks that the agent runs (AgentKeeper) and arms itself again.
//
// The trade-off: the phone's processor wakes every 10 s, also with the
// screen off, for a short check. Chosen for a fast return of the agent on a
// school phone that usually sits charging; in deep Doze Android delivers it
// far less often, so the agent then comes back more slowly.
object WatchdogAlarm {
    // Android delivers no alarm sooner than 5 s from now.
    const val INTERVAL_MILLIS = 10_000L

    enum class Kind { EXACT, INEXACT }

    // Before Android 12 every app may set exact alarms; from 12 only with
    // the exact alarm permission or the battery exemption (canScheduleExact).
    // An inexact alarm 10 s ahead comes within about 18 s (an estimate from
    // Android's window of 75% of the delay; not measured).
    fun kind(sdkInt: Int, canScheduleExact: Boolean): Kind =
        if (sdkInt < ANDROID_12 || canScheduleExact) Kind.EXACT else Kind.INEXACT

    fun nextAt(nowElapsed: Long): Long = nowElapsed + INTERVAL_MILLIS

    private const val ANDROID_12 = 31
}
