package io.github.arnoneto1.midview.agent.update

import io.github.arnoneto1.midview.agent.files.DiskReserve
import io.github.arnoneto1.midview.agent.net.Channel
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NetworkUpdateCodec
import io.github.arnoneto1.midview.agent.protocol.PhoneUpdate
import io.github.arnoneto1.midview.agent.protocol.UpdateOffer
import io.github.arnoneto1.midview.agent.protocol.UpdateStatusReport
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import java.time.Instant
import java.util.concurrent.FutureTask
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicBoolean

// What the commands port needs of the network update (CommandHandler).
interface PhoneUpdater {
    // Everything after an UPDATE_OFFER, on the teacher's connection and
    // answered here; returns when the connection is no longer needed.
    fun offer(payload: ByteArray, channel: Channel)

    // GET_UPDATE_STATUS's answer.
    fun status(): UpdateStatusReport
}

// The running APK's SHA-256 in hex (the "fingerprint" the Master compares
// with the APK it has), worked out once per start in the background: neither
// the agent's start nor a status question waits for it. Null when the APK
// could not be read.
class ApkFingerprint(compute: () -> String?) {
    private val task = FutureTask<String?> {
        try {
            compute()
        } catch (e: Exception) {
            null
        }
    }

    init {
        Thread(task, "Midview APK fingerprint").apply { isDaemon = true }.start()
    }

    val isReady: Boolean get() = task.isDone

    fun peek(): String? = if (task.isDone) task.get() else null

    // Null when it is not ready within millis, or unreadable.
    fun await(millis: Long): String? =
        try {
            task.get(millis, TimeUnit.MILLISECONDS)
        } catch (_: TimeoutException) {
            null
        }

    companion object {
        fun of(file: File): String {
            val digest = MessageDigest.getInstance("SHA-256")
            file.inputStream().use { input ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    digest.update(buffer, 0, read)
                }
            }
            return Hex.upper(digest.digest())
        }
    }
}

// The phone's side of the network update ("Atualizar celulares"): the
// messages of NetworkUpdate, as UpdateReceiver on a PC, with the APK in the
// zip's place (PhoneUpdate). One update at a time; the APK received whole
// and as offered, and only then is the teacher's program answered; then,
// with no connection needed, it is checked as the agent's own next version
// (ApkCheck) and handed to Android (AgentInstaller). How it ends is in the
// attempt's record (ApkUpdateStore), which GET_UPDATE_STATUS reports: the new
// version says "committed" once it runs.
class ApkUpdater(
    private val store: ApkUpdateStore,
    private val upload: ApkUpload,
    private val fingerprint: ApkFingerprint,
    private val apks: ApkReader,
    private val installer: AgentInstaller,
    private val isDeviceOwner: () -> Boolean,
    // Free space where the APK is received.
    private val freeBytes: () -> Long = { Long.MAX_VALUE },
    private val clock: () -> Instant = Instant::now,
    private val fingerprintWaitMillis: Long = FINGERPRINT_WAIT_MILLIS,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) : PhoneUpdater {
    private val busy = AtomicBoolean(false)
    private val running: ApkFacts = apks.installed()

    // At the agent's start: the attempt a restart cut short, or the one that
    // installed this version, is closed, what an upload left is deleted, and
    // so are the install sessions nobody uses any more. Never stops the
    // agent's start: what fails here is tried at the next.
    init {
        try {
            store.update { ApkUpdateStore.atStart(it, running.versionCode, running.versionName, installer.installing(), clock()) }
                ?.let { log("Last network update ${it.id}: ${it.state} (${it.detail}).", null) }
            upload.delete()
        } catch (e: RuntimeException) {
            log("Couldn't close the last network update", e)
        }
        try {
            installer.abandonStale()
        } catch (e: RuntimeException) {
            log("Couldn't abandon the old install sessions", e)
        }
    }

    override fun offer(payload: ByteArray, channel: Channel) {
        val offer = NetworkUpdateCodec.decodeOffer(payload) ?: return answer(channel, false, INVALID)
        if (!busy.compareAndSet(false, true)) return answer(channel, false, BUSY)
        try {
            receiveAndInstall(offer, channel)
        } finally {
            busy.set(false)
        }
    }

    // As a PC's: not ready (no fingerprint, no build) until the APK's
    // SHA-256 is worked out.
    override fun status(): UpdateStatusReport {
        val attempt = settled()?.report()
        return if (fingerprint.isReady) {
            UpdateStatusReport(fingerprint.peek(), running.versionName, true, attempt)
        } else {
            UpdateStatusReport(null, null, false, attempt)
        }
    }

    // The record, with an install Android never answered for failed first.
    private fun settled(): ApkAttempt? = store.update { ApkUpdateStore.expired(it, clock(), running.versionName) }

    // The answer to an offer that is not taken, as a PC's (a success for the
    // version already installed), or null to take it.
    private fun refusal(offer: UpdateOffer): Pair<Boolean, String>? = when {
        !isDeviceOwner() -> false to NOT_DEVICE_OWNER
        !offer.fingerprint.contentEquals(offer.zipSha256) || offer.zipSize != offer.uncompressedSize -> false to NOT_THE_APP
        offer.zipSize > PhoneUpdate.MAX_APK_SIZE -> false to TOO_BIG
        // The APK, then Android's copy of it, and the phone's spare room.
        !DiskReserve.fits(freeBytes(), offer.zipSize * 2) -> false to NO_SPACE
        settled()?.state == UpdateStates.APPLYING -> false to BUSY
        fingerprint.await(fingerprintWaitMillis).equals(Hex.upper(offer.fingerprint), ignoreCase = true) -> true to ALREADY
        else -> null
    }

    private fun receiveAndInstall(offer: UpdateOffer, channel: Channel) {
        refusal(offer)?.let { (succeeded, detail) -> return answer(channel, succeeded, detail) }
        val id = Hex.upper(offer.attemptId)
        var attempt = ApkAttempt(id, UpdateStates.RECEIVING, RECEIVING_TEXT, clock())
        if (!store.save(attempt)) return answer(channel, false, NOT_SAVED)
        try {
            channel.send(Message(MessageType.UPDATE_ACCEPTED, ByteArray(0)))
            when (val received = upload.receive(offer.zipSize, offer.zipSha256) { channel.receive() }) {
                is UploadResult.Refused -> {
                    fail(attempt, received.detail)
                    answer(channel, false, received.detail)
                    return
                }
                is UploadResult.Interrupted -> {
                    fail(attempt, received.detail)
                    return
                }
                UploadResult.Received -> answer(channel, true, RECEIVED)
            }

            attempt = advance(attempt.copy(state = UpdateStates.VERIFYING, detail = VERIFYING_TEXT, updatedAt = clock()))
            val apk = apks.read(upload.file)
            val refusal = ApkCheck.refusal(apk, running)
            if (apk == null || refusal != null) {
                fail(attempt, refusal ?: ApkCheck.NOT_AN_APP)
                return
            }

            attempt = advance(
                attempt.copy(
                    state = UpdateStates.APPLYING, detail = "instalando a versão ${apk.versionName}",
                    updatedAt = clock(), expectedVersionCode = apk.versionCode,
                ),
            )
            try {
                installer.install(upload.file, id)
            } catch (e: Exception) {
                fail(attempt, "o Android não começou a instalação: ${describe(e)}")
                return
            }
            log("Network update $id: version ${apk.versionName} handed to Android.", null)
        } catch (e: Exception) {
            log("Network update $id stopped", e)
            fail(attempt, "interrompida: ${describe(e)}")
        } finally {
            upload.delete()
        }
    }

    // A step that cannot be recorded stops the attempt: the next start only
    // knows what the record says.
    private fun advance(next: ApkAttempt): ApkAttempt {
        if (!store.save(next)) throw IOException(NOT_SAVED)
        return next
    }

    // Best effort: the failure is logged anyway.
    private fun fail(attempt: ApkAttempt, detail: String) {
        log("Network update ${attempt.id} failed: $detail", null)
        if (!UpdateStates.isFinal(attempt.state)) {
            store.save(attempt.copy(state = UpdateStates.FAILED, detail = detail, updatedAt = clock()))
        }
    }

    private fun answer(channel: Channel, succeeded: Boolean, detail: String) {
        try {
            channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(succeeded, detail)))
        } catch (e: IOException) {
            // The teacher's program is gone; the record says how it went.
        }
    }

    companion object {
        // In the app's files: the attempt's record, and the folder the APK
        // is received in.
        const val ATTEMPT_FILE = "update-attempt.txt"
        const val UPLOAD_FOLDER = "update"

        // As long as a PC's Agent waits for its own fingerprint.
        const val FINGERPRINT_WAIT_MILLIS = 30_000L

        const val INVALID = "pedido inválido"
        const val NOT_DEVICE_OWNER = "este celular não se atualiza pela rede"
        const val NOT_THE_APP = "este pacote não é o aplicativo do celular"
        const val TOO_BIG = "o aplicativo é grande demais (até 64 MB)"
        const val BUSY = "outra atualização já está em andamento neste celular"
        const val ALREADY = "já está nesta versão"
        const val NO_SPACE = "sem espaço no celular para a atualização (é preciso deixar pelo menos 100 MB livres)"
        const val RECEIVED = "recebido"
        private const val RECEIVING_TEXT = "recebendo"
        private const val VERIFYING_TEXT = "conferindo o aplicativo"
        private const val NOT_SAVED = "a atualização não pôde ser gravada no celular"

        private fun describe(e: Exception): String = e.message?.takeIf { it.isNotBlank() } ?: e.javaClass.simpleName
    }
}
