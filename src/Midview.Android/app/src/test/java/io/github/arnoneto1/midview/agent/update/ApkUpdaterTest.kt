package io.github.arnoneto1.midview.agent.update

import io.github.arnoneto1.midview.agent.files.DiskReserve
import io.github.arnoneto1.midview.agent.net.FakeChannel
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NetworkUpdateCodec
import io.github.arnoneto1.midview.agent.protocol.PhoneUpdate
import io.github.arnoneto1.midview.agent.protocol.UpdateStatusReport
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class ApkUpdaterTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val now = Instant.parse("2026-11-12T15:30:45Z")
    private var clock = now
    private val schoolKey = setOf("6a1f0c")
    private val running = ApkFacts("io.github.arnoneto1.midview.agent", 50, "0.0.50", schoolKey)
    private val next = ApkFacts(running.packageName, 51, "0.0.51", schoolKey)

    private val apk = ByteArray(300_000) { (it * 7 + 3).toByte() }
    private val installedApk = ByteArray(1000) { it.toByte() }
    private val attemptId = ByteArray(16) { (0xA0 + it).toByte() }
    private val attemptHex = "A0A1A2A3A4A5A6A7A8A9AAABACADAEAF"
    private val otherId = ByteArray(16) { (0xB0 + it).toByte() }

    private val store by lazy { ApkUpdateStore(File(folder.root, "update-attempt.txt")) }
    private val updateFolder by lazy { File(folder.root, "update") }
    private val reader = FakeReader(running, next)
    private val installer = FakeInstaller()
    private var deviceOwner = true
    private var free = Long.MAX_VALUE
    private var fingerprint = ApkFingerprint { hex(sha256(installedApk)) }
    private val updater by lazy { updater() }

    // A new start of the agent.
    private fun updater() = ApkUpdater(
        store, ApkUpload(updateFolder), fingerprint, reader, installer,
        isDeviceOwner = { deviceOwner }, freeBytes = { free }, clock = { clock }, fingerprintWaitMillis = 2_000,
    )

    private class FakeReader(var installed: ApkFacts, var received: ApkFacts?) : ApkReader {
        val read = mutableListOf<ByteArray>()
        var onRead: () -> Unit = {}
        override fun installed() = installed
        override fun read(apk: File): ApkFacts? {
            onRead()
            read += apk.readBytes()
            return received
        }
    }

    private class FakeInstaller : AgentInstaller {
        val installs = mutableListOf<Pair<ByteArray, String>>()
        var refusal: Exception? = null
        var busy = false
        var cleanups = 0
        override fun install(apk: File, attemptId: String) {
            refusal?.let { throw it }
            installs += apk.readBytes() to attemptId
        }
        override fun installing() = busy
        override fun abandonStale() {
            cleanups++
        }
    }

    private fun sha256(bytes: ByteArray) = MessageDigest.getInstance("SHA-256").digest(bytes)
    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02X".format(it) }

    // UpdateOffer as NetworkUpdate.EncodeOffer writes it. For a phone the
    // fingerprint is the APK's SHA-256 and both sizes are the APK's.
    private fun offer(
        bytes: ByteArray = apk,
        id: ByteArray = attemptId,
        fingerprint: ByteArray = sha256(bytes),
        size: Long = bytes.size.toLong(),
        zipSha256: ByteArray = fingerprint,
        uncompressed: Long = size,
    ): ByteArray = ByteBuffer.allocate(NetworkUpdateCodec.OFFER_LENGTH)
        .put(id).put(fingerprint).putLong(size).put(zipSha256).putLong(uncompressed).array()

    private fun chunks(bytes: ByteArray, size: Int = 256 * 1024) =
        (bytes.indices step size).map { Message(MessageType.FILE_CHUNK, bytes.copyOfRange(it, minOf(it + size, bytes.size))) }

    private fun channelWith(bytes: ByteArray) = FakeChannel(teacher, *chunks(bytes).toTypedArray())

    private fun ack(message: Message): Pair<Boolean, String> {
        assertEquals(MessageType.COMMAND_ACK, message.type)
        return CommandResult.decode(message.payload)
    }

    private fun emptyFolder() = assertTrue(updateFolder.list().isNullOrEmpty())

    private fun ready(updater: ApkUpdater): UpdateStatusReport {
        val until = System.nanoTime() + TimeUnit.SECONDS.toNanos(5)
        while (!updater.status().ready && System.nanoTime() < until) Thread.sleep(5)
        return updater.status()
    }

    @Test fun theNextVersionIsReceivedCheckedAndInstalled() {
        val states = mutableListOf<String>()
        val channel = channelWith(apk)
        channel.onReceive = { states += "receiving: ${store.current!!.state}" }
        reader.onRead = { states += "checking: ${store.current!!.state}" }

        updater.offer(offer(), channel)

        assertEquals(listOf(MessageType.UPDATE_ACCEPTED, MessageType.COMMAND_ACK), channel.sent.map { it.type })
        assertEquals(0, channel.sent[0].payload.size)
        assertEquals(true to "recebido", ack(channel.sent[1]))
        // Accepted first, then every chunk read, then the one answer.
        assertEquals(listOf("send 26", "receive 18", "receive 18", "send 11"), channel.events)
        assertEquals(listOf("receiving: receiving", "receiving: receiving", "checking: verifying"), states)

        assertArrayEquals(apk, reader.read.single())
        assertEquals(attemptHex, installer.installs.single().second)
        assertArrayEquals(apk, installer.installs.single().first)
        assertEquals(ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.51", now, expectedVersionCode = 51), store.current)
        // Android has its own copy.
        emptyFolder()
    }

    @Test fun anApkSignedWithAnotherKeyIsNotInstalled() {
        reader.received = next.copy(signers = setOf("99bb02"))
        val channel = channelWith(apk)
        updater.offer(offer(), channel)

        // It arrived whole: that much is answered, the check comes after.
        assertEquals(true to "recebido", ack(channel.sent.last()))
        assertEquals(ApkAttempt(attemptHex, UpdateStates.FAILED, "assinado com outra chave", now), store.current)
        assertTrue(installer.installs.isEmpty())
        emptyFolder()
    }

    @Test fun anOlderVersionIsNotInstalled() {
        reader.received = next.copy(versionCode = 49, versionName = "0.0.49")
        updater.offer(offer(), channelWith(apk))
        assertEquals(ApkAttempt(attemptHex, UpdateStates.FAILED, "versão igual ou mais antiga (0.0.49)", now), store.current)
        assertTrue(installer.installs.isEmpty())
        emptyFolder()
    }

    @Test fun aCorruptUploadIsRefusedAndFails() {
        val wrong = sha256(apk).also { it[5] = (it[5] + 1).toByte() }
        val channel = channelWith(apk)
        updater.offer(offer(fingerprint = wrong), channel)

        assertEquals(MessageType.UPDATE_ACCEPTED, channel.sent.first().type)
        val detail = "o aplicativo chegou corrompido (o hash não confere)"
        assertEquals(false to detail, ack(channel.sent.last()))
        assertEquals(ApkAttempt(attemptHex, UpdateStates.FAILED, detail, now), store.current)
        assertTrue(reader.read.isEmpty())
        assertTrue(installer.installs.isEmpty())
        emptyFolder()
    }

    // The Wi-Fi drops or the teacher's program is closed midway: the old
    // agent goes on, and the next attempt works.
    @Test fun aDroppedConnectionFailsTheAttemptAndTheNextOneWorks() {
        val channel = FakeChannel(teacher, chunks(apk).first())
        updater.offer(offer(), channel)

        assertEquals(listOf(MessageType.UPDATE_ACCEPTED), channel.sent.map { it.type })
        assertEquals(ApkAttempt(attemptHex, UpdateStates.FAILED, "o envio foi interrompido", now), store.current)
        assertTrue(installer.installs.isEmpty())
        emptyFolder()

        val retry = channelWith(apk)
        updater.offer(offer(id = otherId), retry)
        assertEquals(true to "recebido", ack(retry.sent.last()))
        assertEquals(hex(otherId), installer.installs.single().second)
        assertEquals(UpdateStates.APPLYING, store.current!!.state)
    }

    @Test fun anInstallAndroidWillNotStartFails() {
        installer.refusal = SecurityException("Not allowed to install")
        updater.offer(offer(), channelWith(apk))
        assertEquals(
            ApkAttempt(attemptHex, UpdateStates.FAILED, "o Android não começou a instalação: Not allowed to install", now, 51),
            store.current,
        )
        emptyFolder()
    }

    @Test fun theVersionAlreadyInstalledIsNotSentAgain() {
        val channel = channelWith(installedApk)
        updater.offer(offer(installedApk), channel)
        assertEquals(true to "já está nesta versão", ack(channel.sent.single()))
        assertEquals(listOf("send 11"), channel.events)
        assertNull(store.current)
    }

    // The APK twice (the file, then Android's copy) and the phone's 100 MB.
    @Test fun aPhoneWithoutRoomRefusesBeforeReceiving() {
        free = apk.size * 2L + DiskReserve.SPARE_BYTES - 1
        val channel = channelWith(apk)
        updater.offer(offer(), channel)
        assertEquals(false to ApkUpdater.NO_SPACE, ack(channel.sent.single()))
        assertNull(store.current)
        free = apk.size * 2L + DiskReserve.SPARE_BYTES
        val again = channelWith(apk)
        updater.offer(offer(), again)
        assertEquals(true to "recebido", ack(again.sent[1]))
    }

    @Test fun anotherAttemptBeingInstalledRefuses() {
        val agent = updater
        val installing = ApkAttempt("FF".repeat(16), UpdateStates.APPLYING, "instalando a versão 0.0.51", now.minusSeconds(30), 51)
        store.save(installing)

        val channel = channelWith(apk)
        agent.offer(offer(), channel)
        assertEquals(false to "outra atualização já está em andamento neste celular", ack(channel.sent.single()))
        assertEquals(installing, store.current)
        assertTrue(installer.installs.isEmpty())
    }

    // Android never answered: after three minutes the teacher hears of it
    // and may try again.
    @Test fun anInstallWithNoAnswerFailsAndAnotherMayStart() {
        val agent = updater
        store.save(ApkAttempt("FF".repeat(16), UpdateStates.APPLYING, "instalando a versão 0.0.51", now.minus(Duration.ofMinutes(4)), 51))

        val attempt = agent.status().attempt!!
        assertEquals(UpdateStates.FAILED, attempt.state)
        assertEquals("a instalação não terminou; o celular continua na versão 0.0.50", attempt.detail)

        agent.offer(offer(), channelWith(apk))
        assertEquals(attemptHex, installer.installs.single().second)
    }

    @Test fun anOfferWhileAnotherIsBeingReceivedIsRefused() {
        val inside = CountDownLatch(1)
        val release = CountDownLatch(1)
        val first = channelWith(apk)
        first.onReceive = {
            inside.countDown()
            release.await(5, TimeUnit.SECONDS)
        }
        val thread = Thread { updater.offer(offer(), first) }.apply { start() }
        assertTrue(inside.await(5, TimeUnit.SECONDS))

        val second = FakeChannel(teacher)
        updater.offer(offer(id = otherId), second)
        assertEquals(false to "outra atualização já está em andamento neste celular", ack(second.sent.single()))

        release.countDown()
        thread.join(5_000)
        assertEquals(attemptHex, installer.installs.single().second)
    }

    @Test fun anInvalidOfferIsRefused() {
        val channel = FakeChannel(teacher)
        updater.offer(ByteArray(10), channel)
        assertEquals(false to "pedido inválido", ack(channel.sent.single()))
        assertNull(store.current)
    }

    @Test fun aPhoneThatIsNotDeviceOwnerDoesNotUpdateItself() {
        deviceOwner = false
        val channel = channelWith(apk)
        updater.offer(offer(), channel)
        assertEquals(false to "este celular não se atualiza pela rede", ack(channel.sent.single()))
        assertEquals(listOf("send 11"), channel.events)
        assertNull(store.current)
    }

    // A PC's package (a zip, with the build's own fingerprint) is not the
    // phone's APK.
    @Test fun aPackageThatIsNotTheAppIsRefused() {
        for (payload in listOf(offer(fingerprint = ByteArray(32) { 1 }, zipSha256 = sha256(apk)), offer(uncompressed = apk.size * 3L))) {
            val channel = channelWith(apk)
            updater.offer(payload, channel)
            assertEquals(false to "este pacote não é o aplicativo do celular", ack(channel.sent.single()))
        }
        assertNull(store.current)
    }

    @Test fun anApkLargerThan64MegabytesIsRefused() {
        val channel = FakeChannel(teacher)
        updater.offer(offer(size = PhoneUpdate.MAX_APK_SIZE + 1), channel)
        assertEquals(false to "o aplicativo é grande demais (até 64 MB)", ack(channel.sent.single()))
        assertNull(store.current)

        // 64 MB itself is accepted (and the chunks then never come).
        val limit = FakeChannel(teacher)
        updater.offer(offer(size = PhoneUpdate.MAX_APK_SIZE, id = otherId), limit)
        assertEquals(MessageType.UPDATE_ACCEPTED, limit.sent.first().type)
    }

    @Test fun statusSaysNotReadyUntilTheFingerprintIsKnown() {
        val gate = CountDownLatch(1)
        fingerprint = ApkFingerprint {
            gate.await()
            "ABCD"
        }
        val agent = updater()
        assertEquals(UpdateStatusReport(null, null, false, null), agent.status())
        gate.countDown()
        assertEquals(UpdateStatusReport("ABCD", "0.0.50", true, null), ready(agent))
    }

    // Unreadable is not "not ready": the version is known, its fingerprint
    // is not, so no offer counts as "already this version".
    @Test fun aFingerprintThatCouldNotBeWorkedOutIsNull() {
        fingerprint = ApkFingerprint { throw java.io.IOException("unreadable") }
        val agent = updater()
        assertEquals(UpdateStatusReport(null, "0.0.50", true, null), ready(agent))

        agent.offer(offer(installedApk), channelWith(installedApk))
        assertEquals(UpdateStates.APPLYING, store.current!!.state)
    }

    // What the Master's UpdateStatusReport.TryDecode reads: hex as .NET
    // writes it, the record's own field (the expected versionCode) left out.
    @Test fun statusIsTheJsonTheMasterReads() {
        val agent = updater
        ready(agent)
        store.save(ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.51", now, 51))
        assertEquals(
            "{\"fingerprint\":\"${hex(sha256(installedApk))}\",\"build\":\"0.0.50\",\"ready\":true," +
                "\"attempt\":{\"id\":\"$attemptHex\",\"state\":\"applying\",\"detail\":\"instalando a versão 0.0.51\"," +
                "\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}",
            String(NetworkUpdateCodec.encodeStatus(agent.status()), Charsets.UTF_8),
        )
    }

    @Test fun theSha256OfAFileIsUpperCaseHex() {
        val file = File(folder.root, "x.apk").apply { writeBytes("abc".toByteArray()) }
        assertEquals("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", ApkFingerprint.of(file))
    }

    // At the agent's start: the new version closes the attempt that
    // installed it.
    @Test fun atStartTheNewVersionCommitsTheAttempt() {
        store.save(ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.50", now.minusSeconds(20), 50))
        updater()
        assertEquals(ApkAttempt(attemptHex, UpdateStates.COMMITTED, "atualizado para 0.0.50", now, 50), store.current)
    }

    @Test fun atStartTheOldVersionFailsTheAttempt() {
        store.save(ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.51", now.minusSeconds(20), 51))
        updater()
        assertEquals(
            ApkAttempt(attemptHex, UpdateStates.FAILED, "a instalação não terminou; o celular continua na versão 0.0.50", now, 51),
            store.current,
        )
    }

    @Test fun atStartAnInstallStillInAndroidsHandsIsLeftToFinish() {
        val installing = ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.51", now.minusSeconds(20), 51)
        store.save(installing)
        installer.busy = true
        updater()
        assertEquals(installing, store.current)
    }

    // Android's installer failing to answer must not keep the agent from
    // starting.
    @Test fun atStartAFailureToCloseTheAttemptIsOnlyLogged() {
        store.save(ApkAttempt(attemptHex, UpdateStates.APPLYING, "instalando a versão 0.0.51", now.minusSeconds(20), 51))
        val failing = object : AgentInstaller {
            override fun install(apk: File, attemptId: String) = Unit
            override fun installing(): Boolean = throw SecurityException("no sessions")
            override fun abandonStale() = throw SecurityException("no sessions")
        }
        val agent = ApkUpdater(store, ApkUpload(updateFolder), fingerprint, reader, failing, isDeviceOwner = { true }, clock = { clock })
        assertEquals(UpdateStates.APPLYING, store.current!!.state)
        assertEquals("0.0.50", ready(agent).build)
    }

    // An install session a restart or a failure left behind is abandoned at
    // the start, once, so Android does not keep it (and its copy of the APK).
    @Test fun atStartTheInstallSessionsLeftBehindAreAbandoned() {
        updater()
        assertEquals(1, installer.cleanups)
    }

    // A late success: Android installed the version after the 3-minute
    // timeout had already marked the attempt failed.
    @Test fun atStartTheNewVersionCommitsAnAttemptTheTimeoutFailed() {
        store.save(ApkAttempt(attemptHex, UpdateStates.FAILED, "a instalação não terminou; o celular continua na versão 0.0.49", now.minusSeconds(20), 50))
        updater()
        assertEquals(ApkAttempt(attemptHex, UpdateStates.COMMITTED, "atualizado para 0.0.50", now, 50), store.current)
    }

    @Test fun atStartAnUploadCutShortFailsAndItsFileIsDeleted() {
        updateFolder.mkdirs()
        File(updateFolder, "agent.apk").writeBytes(apk.copyOf(1000))
        store.save(ApkAttempt(attemptHex, UpdateStates.RECEIVING, "recebendo", now.minusSeconds(20)))
        updater()
        assertEquals(ApkAttempt(attemptHex, UpdateStates.FAILED, "interrompida (o Midview reiniciou)", now), store.current)
        emptyFolder()
    }
}
