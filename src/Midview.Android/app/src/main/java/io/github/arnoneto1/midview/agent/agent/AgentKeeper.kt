package io.github.arnoneto1.midview.agent.agent

// Keeps the agent running in this process. Some phones kill the whole app
// when the student clears the recent apps (MIUI's "clear all"); Android then
// starts the process again, but its own restart of the service can come
// minutes late, or be stopped at once as an "idle" app's service before it
// runs. So every start of the process and every watchdog check asks for the
// agent here, and it is started (in the foreground) unless this process
// already started it or has it running.
class AgentKeeper(private val start: () -> Unit) {
    private var running = false

    // True when the agent was started now. A start Android refuses is thrown
    // to the caller and tried again at the next check.
    @Synchronized
    fun ensureRunning(): Boolean {
        if (running) return false
        start()
        running = true
        return true
    }

    // The service came up (also by Android's own restart).
    @Synchronized
    fun created() {
        running = true
    }

    // The service ended: the next check starts it again.
    @Synchronized
    fun destroyed() {
        running = false
    }
}

// The watchdog's alarm, which survives the app's process: every few seconds
// it checks that the agent runs (AgentKeeper) and arms itself again.
object WatchdogAlarm {
    // Android delivers no alarm sooner than 5 s from now.
    const val INTERVAL_MILLIS = 10_000L

    enum class Kind { EXACT, INEXACT }

    // Before Android 12 every app may set exact alarms; from 12 only with
    // the exact alarm permission or the battery exemption (canScheduleExact).
    // An inexact alarm 10 s ahead comes within about 18 s.
    fun kind(sdkInt: Int, canScheduleExact: Boolean): Kind =
        if (sdkInt < ANDROID_12 || canScheduleExact) Kind.EXACT else Kind.INEXACT

    fun nextAt(nowElapsed: Long): Long = nowElapsed + INTERVAL_MILLIS

    private const val ANDROID_12 = 31
}
