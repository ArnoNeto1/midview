package io.github.arnoneto1.midview.agent.find

import io.github.arnoneto1.midview.agent.protocol.PhoneFind

// What "Tocar para encontrar" does on the device (AndroidRingOutput; a test's
// fake): the alarm volume, the sound, the vibration and the screen with
// "Parar".
interface RingOutput {
    fun alarmVolume(): Int

    fun maxAlarmVolume(): Int

    fun setAlarmVolume(level: Int)

    // False when no sound could be played at all.
    fun startSound(): Boolean

    fun stopSound()

    fun startVibration()

    fun stopVibration()

    fun showScreen()

    fun hideScreen()
}

// Where the alarm volume from before the ring is kept on the device while it
// rings (a file; a test's fake): a ring cut short by the agent's process
// being killed still gives it back on the next start (FindRinger.recover).
interface SavedVolume {
    fun read(): Int?

    // null: nothing to give back.
    fun write(level: Int?)
}

// Runs task after delayMillis; the answer cancels it.
fun interface RingTimer {
    fun after(delayMillis: Long, task: () -> Unit): () -> Unit
}

// "Tocar para encontrar", a lost phone or tablet found by its sound. Only the
// teacher starts it (a trusted teacher's FIND_PHONE): the alarm volume goes to
// the top, the alarm sound loops (USAGE_ALARM: also with the device on
// silent or vibrate), the device vibrates and shows "Parar" over the lock
// screen. It stops when the student taps "Parar", when the teacher stops it,
// when the time is up or when the agent stops; however it stops, the alarm
// volume goes back to what it was. Asked again while ringing, it keeps
// ringing for the new time. No Android classes (JVM tests); any thread.
class FindRinger(
    private val output: RingOutput,
    private val timer: RingTimer,
    private val saved: SavedVolume? = null,
    private val log: (String) -> Unit = {},
) {
    private val lock = Any()
    private var ringing = false
    private var savedVolume: Int? = null
    private var cancelTimeout: (() -> Unit)? = null

    val isRinging: Boolean get() = synchronized(lock) { ringing }

    // The FIND_PHONE payload in, the CommandAck's result out.
    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val seconds = PhoneFind.decode(payload) ?: return false to INVALID
        if (seconds == PhoneFind.STOP) return true to if (stop(Stop.TEACHER)) STOPPED else NOT_RINGING
        return start(seconds)
    }

    fun start(seconds: Int): Pair<Boolean, String> = synchronized(lock) {
        if (!ringing) {
            val before = output.alarmVolume()
            savedVolume = before
            saved?.write(before)
            output.setAlarmVolume(output.maxAlarmVolume())
            if (!output.startSound()) {
                output.setAlarmVolume(before)
                savedVolume = null
                saved?.write(null)
                return false to NO_SOUND
            }
            output.startVibration()
            // Ringing before the screen comes up: FindActivity closes itself
            // when it finds nothing ringing.
            ringing = true
            output.showScreen()
            log("Ringing to be found ($seconds s); the alarm volume was $before.")
        }
        cancelTimeout?.invoke()
        cancelTimeout = timer.after(seconds * 1000L) { stop(Stop.TIME_UP) }
        true to ringingFor(seconds)
    }

    // True when it was ringing.
    fun stop(why: Stop): Boolean = synchronized(lock) {
        if (!ringing) return false
        ringing = false
        cancelTimeout?.invoke()
        cancelTimeout = null
        try {
            output.stopSound()
            output.stopVibration()
            output.hideScreen()
        } finally {
            savedVolume?.let { output.setAlarmVolume(it) }
            savedVolume = null
            saved?.write(null)
        }
        log("Stopped ringing (${why.name.lowercase()}).")
        true
    }

    // The agent starts: a ring the process was killed in the middle of
    // leaves the alarm volume at the top; it goes back to what it was.
    fun recover() = synchronized(lock) {
        if (ringing) return
        val level = saved?.read() ?: return
        output.setAlarmVolume(level)
        saved.write(null)
        log("The alarm volume of a ring cut short went back to $level.")
    }

    enum class Stop { STUDENT, TEACHER, TIME_UP, AGENT }

    companion object {
        const val STOPPED = "toque parado"
        const val NOT_RINGING = "não estava tocando"
        const val INVALID = "pedido inválido"
        const val NO_SOUND = "não foi possível tocar nenhum som"

        fun ringingFor(seconds: Int): String =
            if (seconds % 60 == 0) "tocando por ${seconds / 60} min" else "tocando por $seconds s"
    }
}
