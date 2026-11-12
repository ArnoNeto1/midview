package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.apps.PhoneAppsAnswer
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NetworkUpdateCodec
import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.screen.ConsentState
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.update.ApkUpload
import io.github.arnoneto1.midview.agent.update.PhoneUpdater
import java.io.EOFException

// The commands port (28900), as the Windows AgentServer: anyone may ask to
// pair (PairingFlow decides who may); anything else needs a teacher this
// phone trusts, and from anyone else ends the connection unanswered. Any
// certificate gets through the handshake, so this is what keeps strangers
// out. A trusted teacher may send several messages on one connection.
// appBlock holds the phone's app block; sites, the sites and internet the
// teacher blocked; optimizer, "Otimizar" (which also catches up at a trusted
// teacher's command: the first optimization after pairing, a DNS to try
// again); updater, "Atualizar celulares" (the agent's next version over the
// network); listApps reads the apps the student can open (for the teacher's
// list).
class CommandHandler(
    private val trusted: TrustedMasters,
    private val pairing: PairingFlow,
    private val consent: ScreenConsent,
    private val appBlock: AppBlockStore,
    private val sites: SiteBlockStore,
    private val optimizer: PhoneOptimizer,
    private val updater: PhoneUpdater,
    private val listApps: () -> List<PhoneApp>,
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
            if (message.type != MessageType.OPTIMIZE_PHONE) optimizer.teacherSeen()
            // More than one message each way (the offer, the APK in chunks),
            // all answered by the updater; the connection ends with it, as
            // on a PC.
            if (message.type == MessageType.UPDATE_OFFER) {
                updater.offer(message.payload, channel)
                return
            }
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
        MessageType.SET_PHONE_APP_BLOCK -> {
            val (applied, detail) = appBlock.handle(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(applied, detail))
        }
        MessageType.GET_PHONE_APP_BLOCK -> Message(MessageType.PHONE_APP_BLOCK_STATUS, appBlock.current.encode())
        MessageType.GET_PHONE_APPS -> Message(MessageType.PHONE_APPS, PhoneAppsAnswer.encode(listApps()))
        // On this connection's own thread: up to ~30 s checking the network.
        MessageType.OPTIMIZE_PHONE -> Message(MessageType.PHONE_OPTIMIZE_REPORT, optimizer.run().encode())
        // The payload (the attempt's id, from a Master following one) is
        // for a PC's trial run of a new build; a phone has none.
        MessageType.GET_UPDATE_STATUS -> Message(MessageType.UPDATE_STATUS, NetworkUpdateCodec.encodeStatus(updater.status()))
        // A chunk with no update offered before it.
        MessageType.FILE_CHUNK -> Message(MessageType.COMMAND_ACK, CommandResult.encode(false, ApkUpload.INVALID_TRANSFER))
        // As the Windows AgentServer: the answer is the single byte 1 or 0.
        MessageType.BLOCK_INTERNET, MessageType.UNBLOCK_INTERNET, MessageType.BLOCK_DOMAIN, MessageType.UNBLOCK_DOMAIN -> {
            val done = sites.handle(message.type, message.payload)
            Message(MessageType.COMMAND_ACK, byteArrayOf(if (done) 1 else 0))
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
