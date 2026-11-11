package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.screen.ConsentState
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import java.io.EOFException

// The commands port (28900), as the Windows AgentServer: anyone may ask to
// pair (PairingFlow decides who may); anything else needs a teacher this
// phone trusts, and from anyone else ends the connection unanswered. Any
// certificate gets through the handshake, so this is what keeps strangers
// out. A trusted teacher may send several messages on one connection.
class CommandHandler(
    private val trusted: TrustedMasters,
    private val pairing: PairingFlow,
    private val consent: ScreenConsent,
) {
    fun handle(channel: Channel) {
        val isTrusted = trusted.isTrusted(channel.remoteThumbprint)
        var message = channel.receive(if (isTrusted) Int.MAX_VALUE else UNTRUSTED_MAX_LENGTH)
        if (message.type == MessageType.PAIR_REQUEST) {
            pairing.run(channel, message)
            return
        }
        if (!isTrusted) {
            return
        }

        channel.setTimeout(IDLE_TIMEOUT_MILLIS)
        while (true) {
            channel.send(answer(message))
            message = try {
                channel.receive()
            } catch (e: EOFException) {
                // The teacher's program has what it wanted and hung up.
                return
            }
            if (message.type == MessageType.PAIR_REQUEST) {
                pairing.run(channel, message)
                return
            }
        }
    }

    private fun answer(message: Message): Message = when (message.type) {
        MessageType.HEARTBEAT -> Message(MessageType.HEARTBEAT, ByteArray(0))
        MessageType.ALLOW_NEW_TEACHER -> {
            val (allowed, detail) = pairing.allowNewTeacher()
            Message(MessageType.COMMAND_ACK, CommandResult.encode(allowed, detail))
        }
        MessageType.REQUEST_SCREEN_CONSENT -> {
            val (shown, detail) = askForScreen()
            Message(MessageType.COMMAND_ACK, CommandResult.encode(shown, detail))
        }
        // Everything a PC does and a phone doesn't yet: the teacher reads why.
        else -> Message(MessageType.COMMAND_ACK, CommandResult.encode(false, CommandResult.NOT_ON_PHONE))
    }

    // "Pedir de novo para ver a tela": the student's request comes up again.
    private fun askForScreen(): Pair<Boolean, String> {
        if (consent.state == ConsentState.Capturing) return true to SCREEN_ALREADY_ALLOWED
        consent.ask()
        return if (consent.isLocked) false to SCREEN_LOCKED else true to REQUEST_SHOWN
    }

    companion object {
        const val REQUEST_SHOWN = "pedido mostrado"
        const val SCREEN_ALREADY_ALLOWED = "a tela já está liberada"
        const val SCREEN_LOCKED = "a tela está bloqueada: o pedido aparece quando o celular for desbloqueado"

        // The first message of a teacher not trusted yet: only a PairRequest
        // gets anywhere, and it is a computer name.
        const val UNTRUSTED_MAX_LENGTH = PairingFlow.UNTRUSTED_REQUEST_LENGTH

        // A trusted teacher's connection kept open with nothing said.
        const val IDLE_TIMEOUT_MILLIS = 60_000
    }
}
