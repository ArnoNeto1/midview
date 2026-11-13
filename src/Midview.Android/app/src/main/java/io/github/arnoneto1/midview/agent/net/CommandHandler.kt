package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.apps.PhoneAppsAnswer
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonStore
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.lab.LabConfigStore
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.programs.TeacherInstalls
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NetworkUpdateCodec
import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.protocol.PhoneAppList
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
// list); gateway erases the phone at "Apagar e preparar de novo", runLater
// being how it is put off (the ack has to reach the teacher first).
// showMessage and chat are the teacher's "Mensagem" and "Bate-papo", which a
// PC takes on its Session's port: on a phone the one app is both, and its
// commands port answers everything (an agent from before them says "Ainda
// não funciona em celular.", which the teacher's program can tell apart).
// files is "Enviar arquivo", "Recolher arquivos" and "Apagar as pastas dos
// alunos" (FileHandler), a PC Session's too; without it they get that answer.
// power is "Desligar" and "Reiniciar" (PowerCountdown), a PC Agent's command;
// powerSchedule, "Agendar" (PowerScheduleStore), a PC Agent's too.
// openWebsite and openApp are "Abrir site" and "Abrir programa" (TeacherLaunch),
// a PC Session's; installs is "Instalar programa" and "Desinstalar programa"
// (TeacherInstalls), a PC Agent's. lab is the "Painel do laboratório"
// (LabConfigStore: "Travar downloads" and "Bloqueador de anúncios"), a PC
// Agent's; without it its commands get "Ainda não funciona em celular.".
// classLesson is "Pasta da turma"'s lesson (ClassLessonStore), a PC Agent's;
// the files themselves go on the screen port, as on a PC's Session
// (ClassFolderHandler). The class folders' home (ClaimClassHome,
// GetClassHome, IsTeacherTrusted) stays with the PCs: a phone answers them
// "Ainda não funciona em celular.", as an Agent from before them.
class CommandHandler(
    private val trusted: TrustedMasters,
    private val pairing: PairingFlow,
    private val consent: ScreenConsent,
    private val appBlock: AppBlockStore,
    private val sites: SiteBlockStore,
    private val optimizer: PhoneOptimizer,
    private val updater: PhoneUpdater,
    private val gateway: DevicePolicyGateway,
    private val runLater: (delayMillis: Long, task: () -> Unit) -> Unit = ::onNewThread,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    // The teacher's lock screen PIN (LockScreenKeeper): the payload in, the
    // CommandAck's result out.
    private val screenPin: (ByteArray) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    // "Fechar programas" (CloseCountdown): the warning's time in seconds in,
    // the CommandAck's result out.
    private val closeApps: (delaySeconds: Int) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    // "Desligar" and "Reiniciar" (PowerCountdown): the PC's PowerAction (0
    // shut down, 1 restart, 2 cancel) and the warning's time in seconds in,
    // the CommandAck's result out.
    private val power: (action: Int, delaySeconds: Int) -> Pair<Boolean, String> = { _, _ -> false to CommandResult.NOT_ON_PHONE },
    // "Agendar" (PowerScheduleStore): the SetPowerSchedule payload in, the
    // CommandAck's result out.
    private val powerSchedule: (ByteArray) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    // "Mensagem" (TeacherMessages): the ShowMessage payload in, the
    // CommandAck's result out.
    private val showMessage: (ByteArray) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    // "Bate-papo" (ChatHandler): the connection, for the whole conversation.
    private val chat: (Channel) -> Unit = {
        it.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(false, CommandResult.NOT_ON_PHONE)))
    },
    // "Enviar arquivo", "Recolher arquivos", "Apagar as pastas dos alunos".
    private val files: FileHandler? = null,
    // "Abrir site" and "Abrir programa": the payload in, the CommandAck's result out.
    private val openWebsite: (ByteArray) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    private val openApp: (ByteArray) -> Pair<Boolean, String> = { false to CommandResult.NOT_ON_PHONE },
    // "Instalar programa" and "Desinstalar programa".
    private val installs: TeacherInstalls? = null,
    private val lab: LabConfigStore? = null,
    private val classLesson: ClassLessonStore? = null,
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
            // The conversation keeps the connection until it ends.
            if (message.type == MessageType.CHAT_OPEN) {
                chat(channel)
                return
            }
            // The app to install comes in chunks after the request, as on a
            // PC; the connection ends with the answer.
            if (installs != null && message.type == MessageType.INSTALL_PROGRAM) {
                installs.install(message.payload, channel)
                return
            }
            // A file each way is more than one message, and ends the
            // connection, as on a PC.
            if (files != null && message.type in FILE_TRANSFERS) {
                if (message.type == MessageType.COLLECT_FILES) files.collect(channel) else files.receive(channel, message)
                return
            }
            channel.send(answer(message, channel.remoteThumbprint))
            // After the ack is on the wire, and off this thread: the wipe
            // kills the connection and the process with it.
            if (message.type == MessageType.WIPE_PHONE && gateway.isDeviceOwner()) {
                runLater(WIPE_DELAY_MILLIS) {
                    // A refusal must not take the agent down with it.
                    try {
                        gateway.wipe()
                    } catch (e: RuntimeException) {
                        // The teacher already has the ack; the log is all there is.
                        log("The wipe the teacher asked for failed", e)
                    }
                }
            }
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
            // A teacher another one made this phone forget ("Esquecer os
            // outros professores") gets nothing more on a connection it
            // opened before.
            if (!trusted.isTrusted(channel.remoteThumbprint)) {
                return
            }
        }
    }

    private fun answer(message: Message, sender: String): Message = when (message.type) {
        MessageType.HEARTBEAT -> Message(MessageType.HEARTBEAT, ByteArray(0))
        MessageType.SET_CLASS_LESSON -> {
            val (taken, detail) = classLesson?.handle(message.payload) ?: (false to CommandResult.NOT_ON_PHONE)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(taken, detail))
        }
        MessageType.GET_CLASS_LESSON ->
            classLesson?.let { Message(MessageType.CLASS_LESSON_STATUS, it.current.encode()) }
                ?: Message(MessageType.COMMAND_ACK, CommandResult.encode(false, CommandResult.NOT_ON_PHONE))
        MessageType.ALLOW_NEW_TEACHER -> {
            val (allowed, detail) = pairing.allowNewTeacher(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(allowed, detail))
        }
        MessageType.FORGET_OTHER_TEACHERS -> {
            val (kept, detail) = pairing.forgetOtherTeachers(sender)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(kept, detail))
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
        // With each app's flags for a teacher who asks for them (one byte, 2).
        MessageType.GET_PHONE_APPS ->
            Message(MessageType.PHONE_APPS, PhoneAppsAnswer.encode(listApps(), PhoneAppList.askedFormat(message.payload)))
        MessageType.OPEN_WEBSITE -> {
            val (opened, detail) = openWebsite(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(opened, detail))
        }
        MessageType.OPEN_PROGRAM -> {
            val (opened, detail) = openApp(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(opened, detail))
        }
        // On this connection's thread: Android removes one app after another.
        MessageType.UNINSTALL_PROGRAMS -> {
            val (removed, detail) = installs?.uninstall(message.payload) ?: (false to CommandResult.NOT_ON_PHONE)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(removed, detail))
        }
        MessageType.SET_PHONE_SCREEN_PASSWORD -> {
            val (done, detail) = screenPin(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(done, detail))
        }
        // The PC's payload: the student is warned for the time in it, then
        // the apps close; asking or forcing (the mode) has no meaning on a
        // phone. "Agora" closes on this connection's thread: about a second.
        MessageType.CLOSE_APPS -> {
            val delay = closeAppsDelay(message.payload)
            val (closed, detail) = if (delay == null) false to INVALID_REQUEST else closeApps(delay)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(closed, detail))
        }
        // The PC's payload: the student is warned for the time in it, then
        // the phone restarts, or its screen goes off ("Desligar": Android
        // lets no app turn a phone off). The Windows updates' flag has no
        // meaning on a phone.
        MessageType.POWER -> {
            val request = powerRequest(message.payload)
            val (done, detail) = if (request == null) false to INVALID_REQUEST else power(request.first, request.second)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(done, detail))
        }
        // The PC's payload: the phone keeps the newest and turns its screen
        // off at the shutdown time; the wake time is a PC's only.
        MessageType.SET_POWER_SCHEDULE -> {
            val (taken, detail) = powerSchedule(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(taken, detail))
        }
        MessageType.CLEAR_DELIVERY_FOLDER -> {
            val (cleared, detail) = files?.clear() ?: (false to CommandResult.NOT_ON_PHONE)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(cleared, detail))
        }
        MessageType.SET_LAB_CONFIG -> {
            val (applied, detail) = lab?.handle(message.payload) ?: (false to CommandResult.NOT_ON_PHONE)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(applied, detail))
        }
        MessageType.GET_LAB_CONFIG ->
            lab?.let { Message(MessageType.LAB_CONFIG_STATUS, it.current.encode()) }
                ?: Message(MessageType.COMMAND_ACK, CommandResult.encode(false, CommandResult.NOT_ON_PHONE))
        MessageType.RESET_LAB_CONFIG -> {
            val (reset, detail) = lab?.reset() ?: (false to CommandResult.NOT_ON_PHONE)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(reset, detail))
        }
        MessageType.SHOW_MESSAGE -> {
            val (shown, detail) = showMessage(message.payload)
            Message(MessageType.COMMAND_ACK, CommandResult.encode(shown, detail))
        }
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
        // Only a Device Owner can erase the phone; the wipe itself is started
        // by handle(), once this answer has been sent.
        MessageType.WIPE_PHONE ->
            if (gateway.isDeviceOwner()) {
                Message(MessageType.COMMAND_ACK, CommandResult.encode(true, WIPING))
            } else {
                Message(MessageType.COMMAND_ACK, CommandResult.encode(false, NOT_PREPARED))
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
        const val WIPING = "apagando"
        const val NOT_PREPARED = "Este celular não foi preparado pelo Midview."
        const val INVALID_REQUEST = "pedido inválido"

        private val FILE_TRANSFERS = setOf(MessageType.SEND_FILE, MessageType.SEND_FILE_WITH_OPTIONS, MessageType.COLLECT_FILES)

        // CloseApps' payload, as src/Midview.Protocol/CloseAppsCommand.cs:
        // the mode (0 ask, 1 force), then the warning in seconds (uint16,
        // big-endian, at most 10 minutes); an older Master sends the mode
        // alone, which means now. The warning's seconds, or null if invalid.
        fun closeAppsDelay(payload: ByteArray): Int? {
            if (payload.size != 1 && payload.size != 3) return null
            if (payload[0].toInt() != 0 && payload[0].toInt() != 1) return null
            if (payload.size == 1) return 0
            val seconds = ((payload[1].toInt() and 0xFF) shl 8) or (payload[2].toInt() and 0xFF)
            return if (seconds <= MAX_CLOSE_DELAY_SECONDS) seconds else null
        }

        const val MAX_CLOSE_DELAY_SECONDS = 10 * 60

        // Power's payload, as src/Midview.Protocol/PowerCommand.cs: the
        // action (0 shut down, 1 restart, 2 cancel), the warning in seconds
        // (uint16, big-endian, at most 10 minutes), and, to install the
        // Windows updates on the way, a fourth byte 1 (never on a cancel).
        // The action and the warning's seconds, or null if invalid.
        fun powerRequest(payload: ByteArray): Pair<Int, Int>? {
            if (payload.size != 3 && payload.size != 4) return null
            val action = payload[0].toInt()
            if (action !in 0..2) return null
            if (payload.size == 4 && (payload[3].toInt() != 1 || action == 2)) return null
            val seconds = ((payload[1].toInt() and 0xFF) shl 8) or (payload[2].toInt() and 0xFF)
            return if (seconds <= MAX_CLOSE_DELAY_SECONDS) action to seconds else null
        }

        // The wait between the ack and the wipe, for the ack to reach the Master.
        const val WIPE_DELAY_MILLIS = 2_000L

        private fun onNewThread(delayMillis: Long, task: () -> Unit) {
            Thread {
                try {
                    Thread.sleep(delayMillis)
                } catch (e: InterruptedException) {
                    return@Thread
                }
                task()
            }.apply { isDaemon = true; name = "midview-wipe" }.start()
        }

        const val SCREEN_LOCKED = "a tela está bloqueada: o pedido aparece quando o celular for desbloqueado"

        // The first message of a teacher not trusted yet: only a PairRequest
        // gets anywhere, and it is a computer name.
        const val UNTRUSTED_MAX_LENGTH = PairingFlow.UNTRUSTED_REQUEST_LENGTH

        // A trusted teacher's connection kept open with nothing said.
        const val IDLE_TIMEOUT_MILLIS = 60_000
    }
}
