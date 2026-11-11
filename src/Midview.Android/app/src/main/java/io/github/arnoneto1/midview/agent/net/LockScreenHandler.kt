package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.lock.ScreenLock
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import java.io.IOException

// "Bloquear telas" on the screen port, after its first message, LockScreen:
// as the Session's ServeScreenLock, the phone is locked from the answer
// ("tela bloqueada") until the connection ends, however it ends: closed by
// the teacher ("Desbloquear", or the program closing), broken, or silent for
// silenceMillis because the teacher's PC stopped answering (the Master sends
// a Heartbeat every 3 s) -- the class is never left locked. Only a Device
// Owner can hold the phone in lock task mode; any other phone answers why
// not, and the teacher's ScreenLockKeeper tries again later.
class LockScreenHandler(
    private val channel: Channel,
    private val lock: ScreenLock,
    private val isDeviceOwner: () -> Boolean,
    private val silenceMillis: Int = SILENCE_MILLIS,
) {
    // Returns when the lock is released; the caller closes the connection.
    fun run() {
        if (!isDeviceOwner()) {
            channel.send(ack(false, NO_SCREEN_LOCK))
            return
        }
        val hold = try {
            lock.hold()
        } catch (e: RuntimeException) {
            channel.send(ack(false, NOT_LOCKED))
            return
        }
        try {
            channel.send(ack(true, LOCKED))
            channel.setTimeout(silenceMillis)
            while (true) {
                // Heartbeats; anything else is read and ignored, as the Session does.
                channel.receive(MAX_MESSAGE_LENGTH)
            }
        } catch (e: IOException) {
            // Gone, silent for too long, or nonsense.
        } finally {
            hold.release()
        }
    }

    private fun ack(succeeded: Boolean, detail: String) =
        Message(MessageType.COMMAND_ACK, CommandResult.encode(succeeded, detail))

    companion object {
        const val LOCKED = "tela bloqueada"

        // What the teacher reads when this phone is not the school's Device Owner.
        const val NO_SCREEN_LOCK = "este celular não bloqueia a tela"

        // The lock screen could not come up.
        const val NOT_LOCKED = "a tela não pôde ser bloqueada"

        // The Session's ConnectionTimeoutMs.
        const val SILENCE_MILLIS = 15_000

        // Heartbeats are empty; nothing on this connection needs more.
        private const val MAX_MESSAGE_LENGTH = 1024
    }
}
