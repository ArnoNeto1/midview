package io.github.arnoneto1.midview.agent.pairing

import android.os.SystemClock
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.Channel
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.Pairing
import io.github.arnoneto1.midview.agent.protocol.TeacherStatus
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

// Who may pair with this phone, and the phone's side of the exchange (see
// Pairing), as the Windows Agent's PairingService. A phone with no teacher
// takes the first confirmed pairing; a teacher it already trusts may pair
// again (a lost classroom list); anyone else only while a trusted teacher's
// "Permitir novo professor" lasts, which ends with the first new teacher
// whose pairing reaches the code on the phone's screen (whether or not the
// teacher then confirms it: a classmate who got there first used it up, and
// the real teacher asks again). The window is counted on the clock since the
// phone started (elapsed), which no one can set back in Settings; the wall
// clock (now) only says until when, for the teacher to read.
// One pairing at a time. The code shows on the phone (showCode, with the
// teacher's computer name) before the teacher can have it, and is taken
// down (hideCode) however the pairing ends. showCode says whether the code
// is really on the screen: a code the student can't see is no pairing, and
// the teacher is told so before getting the phone's nonce.
class PairingFlow(
    private val identity: AgentIdentity,
    private val trusted: TrustedMasters,
    private val showCode: (String, String) -> Boolean,
    private val hideCode: () -> Unit,
    private val deviceName: () -> String = { commonName(identity) },
    private val newNonce: () -> ByteArray = Pairing::newNonce,
    private val now: () -> Long = System::currentTimeMillis,
    private val elapsed: () -> Long = SystemClock::elapsedRealtime,
) {
    private val lock = Any()
    private var newTeacherUntil: Long? = null
    private var busy = false

    val newTeacherOpen: Boolean
        get() = synchronized(lock) { windowOpen() }

    // For discovery: whom this phone trusts, as the asking teacher sees it.
    fun teachersSeenBy(masterThumbprint: String): String = when {
        trusted.count() == 0 -> TeacherStatus.NONE
        trusted.isTrusted(masterThumbprint) -> TeacherStatus.YOU
        else -> TeacherStatus.OTHERS
    }

    // AllowNewTeacher, from a trusted teacher (CommandHandler checks that):
    // the CommandResult the teacher sees.
    fun allowNewTeacher(): Pair<Boolean, String> = synchronized(lock) {
        newTeacherUntil = elapsed() + Pairing.NEW_TEACHER_WINDOW_MILLIS
        val until = now() + Pairing.NEW_TEACHER_WINDOW_MILLIS
        true to "liberada até ${SimpleDateFormat("HH:mm", Locale.ROOT).format(Date(until))}"
    }

    // Everything after a PairRequest (read here unless the caller already
    // did), over that connection. A dropped connection surfaces as the
    // receive's exception, after the code has been taken down.
    fun run(channel: Channel, request: Message = channel.receive(UNTRUSTED_REQUEST_LENGTH)) {
        val master = channel.remoteThumbprint
        val teacherName = Pairing.cleanTeacherName(String(request.payload, Charsets.UTF_8))
        val usesWindow: Boolean
        synchronized(lock) {
            val refusal = when {
                busy -> "Outro pareamento está em andamento neste celular."
                trusted.count() > 0 && !trusted.isTrusted(master) && !windowOpen() ->
                    "Este celular já tem professor. Peça para ele usar \"Permitir novo professor\"."
                else -> null
            }
            if (refusal != null) {
                channel.send(Pairing.refused(refusal))
                return
            }
            usesWindow = trusted.count() > 0 && !trusted.isTrusted(master)
            busy = true
        }
        // From here the connection lives as long as a pairing does.
        channel.pairingStarted()

        try {
            exchange(channel, master, teacherName, usesWindow)
        } finally {
            synchronized(lock) { busy = false }
        }
    }

    private fun exchange(channel: Channel, master: String, teacherName: String, usesWindow: Boolean) {
        // The teacher has CONFIRM_TIMEOUT_MILLIS to compare the codes; a
        // little more, so the phone's own deadline is what ends it.
        channel.setTimeout((Pairing.CONFIRM_TIMEOUT_MILLIS + 30_000L).toInt())

        val agentNonce = newNonce()
        channel.send(Message(MessageType.PAIR_COMMIT, Pairing.commit(agentNonce)))

        val nonce = channel.receive(SHORT_MESSAGE_LENGTH)
        if (nonce.type != MessageType.PAIR_NONCE || nonce.payload.size != Pairing.NONCE_LENGTH) {
            return
        }

        val code = Pairing.code(master, identity.thumbprint, agentNonce, nonce.payload)
        try {
            if (!showCode(Pairing.format(code), teacherName)) {
                channel.send(Pairing.refused("Não foi possível mostrar o código na tela do celular. Tente de novo."))
                return
            }
            // The new teacher's window is used up by the first pairing whose
            // code came up, confirmed or not.
            if (usesWindow) {
                synchronized(lock) { newTeacherUntil = null }
            }

            val revealedAt = elapsed()
            channel.send(Message(MessageType.PAIR_REVEAL, agentNonce))

            val answer = channel.receive(SHORT_MESSAGE_LENGTH)
            if (answer.type != MessageType.PAIR_CONFIRM) {
                return
            }
            if (elapsed() - revealedAt > Pairing.CONFIRM_TIMEOUT_MILLIS) {
                channel.send(Pairing.refused("O tempo para conferir o código acabou. Tente de novo."))
                return
            }

            try {
                trusted.add(master)
            } catch (e: IOException) {
                channel.send(Pairing.refused("Não foi possível gravar o professor neste celular. Tente de novo."))
                return
            }

            channel.send(
                Message(MessageType.PAIR_DONE, Pairing.encodeResult(deviceName(), identity.thumbprint, mac = null)),
            )
        } finally {
            hideCode()
        }
    }

    private fun windowOpen() = newTeacherUntil?.let { elapsed() < it } ?: false

    companion object {
        // A teacher not trusted yet opens with a PairRequest: a computer
        // name, never this long.
        const val UNTRUSTED_REQUEST_LENGTH = 4096

        // PairNonce (32 bytes), PairConfirm and PairCancel (none).
        private const val SHORT_MESSAGE_LENGTH = 1 + Pairing.NONCE_LENGTH

        // The CN the identity was made with: the device's name then.
        private fun commonName(identity: AgentIdentity): String =
            identity.certificate.subjectX500Principal.name
                .removePrefix("CN=").replace("\\,", ",").ifBlank { "Celular" }
    }
}
