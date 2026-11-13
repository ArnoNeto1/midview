package io.github.arnoneto1.midview.agent.pairing

import android.os.SystemClock
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.Channel
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NewTeacherInvite
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
// "Permitir novo professor" lasts, and only with that window's code
// (NewTeacherInvite), proven in PairConfirm. The first valid code ends the
// window (used up on this phone), and so do NewTeacherInvite.MAX_TRIES
// pairings that brought a code to the screen without one (a wrong code, none,
// a cancel or a dropped connection): a classmate can neither guess at it nor
// flash codes on the screen for ten minutes. The window is counted on the
// clock since the phone started (elapsed), which no one can set back in
// Settings; the wall clock (now) only says until when, for the teacher to read.
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
    private var inviteCode: String? = null
    private var inviteTries = 0
    // Which window a pairing was let in by: one opened since is not its to end.
    private var windowGeneration = 0
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
    // its payload is the code the new teacher will type. The CommandResult
    // the teacher sees. A Master from before the code sends none, and opens
    // nothing: anyone on the network could walk in.
    fun allowNewTeacher(payload: ByteArray): Pair<Boolean, String> {
        if (payload.isEmpty()) return false to OLD_TEACHER_PROGRAM
        val code = NewTeacherInvite.decode(payload) ?: return false to "pedido inválido"
        return synchronized(lock) {
            newTeacherUntil = elapsed() + Pairing.NEW_TEACHER_WINDOW_MILLIS
            inviteCode = code
            inviteTries = 0
            windowGeneration++
            val until = now() + Pairing.NEW_TEACHER_WINDOW_MILLIS
            true to NewTeacherInvite.withCode("liberada até ${SimpleDateFormat("HH:mm", Locale.ROOT).format(Date(until))}")
        }
    }

    // ForgetOtherTeachers, from a trusted teacher (CommandHandler checks
    // that): the sender becomes the only teacher, and an open window closes.
    fun forgetOtherTeachers(sender: String): Pair<Boolean, String> {
        synchronized(lock) { closeWindow() }
        val dropped = try {
            trusted.keepOnly(sender)
        } catch (e: IOException) {
            return false to "não foi possível gravar a lista de professores neste celular"
        }
        return true to forgottenText(dropped)
    }

    // Everything after a PairRequest (read here unless the caller already
    // did), over that connection. A dropped connection surfaces as the
    // receive's exception, after the code has been taken down.
    fun run(channel: Channel, request: Message = channel.receive(UNTRUSTED_REQUEST_LENGTH)) {
        val master = channel.remoteThumbprint
        val teacherName = Pairing.cleanTeacherName(String(request.payload, Charsets.UTF_8))
        val window: Window?
        synchronized(lock) {
            val newTeacher = trusted.count() > 0 && !trusted.isTrusted(master)
            val refusal = when {
                busy -> "Outro pareamento está em andamento neste celular."
                newTeacher && (!windowOpen() || inviteCode == null) ->
                    "Este celular já tem professor. Peça para ele usar \"Permitir novo professor\"."
                else -> null
            }
            if (refusal != null) {
                channel.send(Pairing.refused(refusal))
                return
            }
            window = if (newTeacher) Window(inviteCode!!, windowGeneration) else null
            busy = true
        }
        // From here the connection lives as long as a pairing does.
        channel.pairingStarted()

        var paired = false
        try {
            paired = exchange(channel, master, teacherName, window)
        } finally {
            synchronized(lock) {
                busy = false
                if (window != null && window.generation == windowGeneration &&
                    (paired || (window.codeShown && ++inviteTries >= NewTeacherInvite.MAX_TRIES))
                ) {
                    closeWindow()
                }
            }
        }
    }

    // The window a pairing came in by: its code, and whether the pairing
    // brought a code to the phone's screen (a try).
    private class Window(val code: String, val generation: Int) {
        var codeShown = false
    }

    // Whether the teacher is now trusted.
    private fun exchange(channel: Channel, master: String, teacherName: String, window: Window?): Boolean {
        // The teacher has CONFIRM_TIMEOUT_MILLIS to compare the codes; a
        // little more, so the phone's own deadline is what ends it.
        channel.setTimeout((Pairing.CONFIRM_TIMEOUT_MILLIS + 30_000L).toInt())

        val agentNonce = newNonce()
        channel.send(Message(MessageType.PAIR_COMMIT, Pairing.commit(agentNonce)))

        val nonce = channel.receive(SHORT_MESSAGE_LENGTH)
        if (nonce.type != MessageType.PAIR_NONCE || nonce.payload.size != Pairing.NONCE_LENGTH) {
            return false
        }

        val code = Pairing.code(master, identity.thumbprint, agentNonce, nonce.payload)
        try {
            if (!showCode(Pairing.format(code), teacherName)) {
                channel.send(Pairing.refused("Não foi possível mostrar o código na tela do celular. Tente de novo."))
                return false
            }
            window?.codeShown = true

            val revealedAt = elapsed()
            channel.send(Message(MessageType.PAIR_REVEAL, agentNonce))

            val answer = channel.receive(SHORT_MESSAGE_LENGTH)
            if (answer.type != MessageType.PAIR_CONFIRM) {
                return false
            }
            if (elapsed() - revealedAt > Pairing.CONFIRM_TIMEOUT_MILLIS) {
                channel.send(Pairing.refused("O tempo para conferir o código acabou. Tente de novo."))
                return false
            }
            // A new teacher proves the window's code; anyone else's payload
            // means nothing (a first teacher, or one this phone trusts).
            if (window != null) {
                val refusal = when {
                    answer.payload.isEmpty() -> NEEDS_CODE
                    !NewTeacherInvite.verifies(window.code, answer.payload, master, identity.thumbprint, agentNonce, nonce.payload) ->
                        WRONG_CODE
                    else -> null
                }
                if (refusal != null) {
                    channel.send(Pairing.refused(refusal))
                    return false
                }
            }

            try {
                trusted.add(master)
            } catch (e: IOException) {
                channel.send(Pairing.refused("Não foi possível gravar o professor neste celular. Tente de novo."))
                return false
            }

            channel.send(
                Message(MessageType.PAIR_DONE, Pairing.encodeResult(deviceName(), identity.thumbprint, mac = null)),
            )
            return true
        } finally {
            hideCode()
        }
    }

    private fun windowOpen() = newTeacherUntil?.let { elapsed() < it } ?: false

    private fun closeWindow() {
        newTeacherUntil = null
        inviteCode = null
        inviteTries = 0
        windowGeneration++
    }

    companion object {
        // A teacher not trusted yet opens with a PairRequest: a computer
        // name, never this long.
        const val UNTRUSTED_REQUEST_LENGTH = 4096

        // PairNonce (32 bytes), PairConfirm (none, or a 32-byte proof) and PairCancel (none).
        private const val SHORT_MESSAGE_LENGTH = 1 + Pairing.NONCE_LENGTH

        const val OLD_TEACHER_PROGRAM =
            "o Midview deste professor é antigo: atualize-o para liberar outro professor com código"
        const val NEEDS_CODE =
            "Este celular pede o código de \"Permitir novo professor\". Digite o código que apareceu na tela do colega e tente de novo."
        const val WRONG_CODE =
            "O código do colega não confere. Confira o código de \"Permitir novo professor\" e tente de novo."

        // ForgetOtherTeachers' answer, as src/Midview.Protocol/OtherTeachers.cs.
        const val NONE_FORGOTTEN = "nenhum outro professor"

        fun forgottenText(dropped: Int): String = when (dropped) {
            0 -> NONE_FORGOTTEN
            1 -> "1 professor esquecido"
            else -> "$dropped professores esquecidos"
        }

        // The CN the identity was made with: the device's name then.
        private fun commonName(identity: AgentIdentity): String =
            identity.certificate.subjectX500Principal.name
                .removePrefix("CN=").replace("\\,", ",").ifBlank { "Celular" }
    }
}
