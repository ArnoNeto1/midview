package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneApp
import io.github.arnoneto1.midview.agent.protocol.PhoneAppList
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import io.github.arnoneto1.midview.agent.protocol.UpdateAttemptReport
import io.github.arnoneto1.midview.agent.protocol.UpdateStatusReport
import io.github.arnoneto1.midview.agent.screen.ConsentState
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.SiteBlockState
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.update.PhoneUpdater
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.io.IOException
import java.nio.file.Files
import java.time.Instant

class CommandHandlerTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val pairing by lazy { PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Cel 01" }) }
    private var shown = 0
    private val consent = ScreenConsent(clock = { 0L }) { shown++ }
    private val applied = mutableListOf<PhoneAppPolicy>()
    private val appBlock by lazy { AppBlockStore(File(folder.newFolder(), "app-block.bin"), apply = { applied += it; null }) }
    private var phoneApps = listOf(PhoneApp("com.android.chrome", "Chrome", byteArrayOf(1, 2, 3)))
    private val sitesApplied = mutableListOf<SiteBlockState>()
    private var sitesWork = true
    private val sites by lazy { SiteBlockStore(File(folder.newFolder(), "site-block.txt"), apply = { sitesApplied += it; sitesWork }) }
    private val optimizer = FakeOptimizer()
    private val updater = FakeUpdater()
    private val gateway = FakeSiteGateway()

    // "Apagar e preparar de novo": the delays asked for, and the task run at
    // once (so the tests do not wait the two seconds).
    private val logged = mutableListOf<Pair<String, Throwable?>>()
    private val wipeDelays = mutableListOf<Long>()
    private val handler by lazy {
        CommandHandler(
            trusted, pairing, consent, appBlock, sites, optimizer, updater, gateway,
            runLater = { delay, task -> wipeDelays += delay; task() },
            log = { text, e -> logged += text to e },
        ) { phoneApps }
    }

    // "Atualizar celulares": the offers it got (answered as accepted) and
    // the status it reports.
    private class FakeUpdater : PhoneUpdater {
        val offers = mutableListOf<ByteArray>()
        var report = UpdateStatusReport(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "0.0.50", true,
            UpdateAttemptReport("A0A1A2A3A4A5A6A7A8A9AAABACADAEAF", "applying", "instalando a versão 0.0.51", Instant.parse("2026-11-12T15:30:45Z")),
        )
        var statusAsked = 0
        override fun offer(payload: ByteArray, channel: Channel) {
            offers += payload
            channel.send(Message(MessageType.UPDATE_ACCEPTED, ByteArray(0)))
        }
        override fun status(): UpdateStatusReport {
            statusAsked++
            return report
        }
    }

    // "Otimizar": what the phone answers, and how often a trusted command
    // let it catch up (the first optimization, a DNS retry).
    private class FakeOptimizer : PhoneOptimizer {
        val report = PhoneOptimizeReport(
            Instant.parse("2026-11-12T13:00:00Z"), listOf("com.miui.msa.global"), listOf("com.facebook.system"),
            PhoneDnsStates.ON, "dns.adguard-dns.com",
        )
        var runs = 0
        var seen = 0
        override fun run(): PhoneOptimizeReport {
            runs++
            return report
        }
        override fun teacherSeen() {
            seen++
        }
    }

    private fun message(type: Int, payload: ByteArray = ByteArray(0)) = Message(type, payload)

    @Test fun untrustedTeacherCannotSendCommands() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, message(MessageType.HEARTBEAT), message(MessageType.HEARTBEAT))

        handler.handle(channel)

        // Nothing answered, nothing more read: the server closes the connection.
        assertTrue(channel.sent.isEmpty())
        assertEquals(listOf("receive 2"), channel.events)
        assertFalse(pairing.newTeacherOpen)
    }

    @Test fun untrustedTeacherCannotOpenTheWindowEither() {
        trusted.add(teacher)
        handler.handle(FakeChannel(stranger, message(MessageType.ALLOW_NEW_TEACHER)))
        assertFalse(pairing.newTeacherOpen)
    }

    @Test fun untrustedTeacherMaySendOnlyASmallFirstMessage() {
        val channel = FakeChannel(stranger, message(MessageType.PAIR_REQUEST, ByteArray(CommandHandler.UNTRUSTED_MAX_LENGTH)))
        try {
            handler.handle(channel)
        } catch (e: IOException) {
            // Refused before it is read.
        }
        assertTrue(channel.sent.isEmpty())
    }

    @Test fun anyoneMayAskToPair() {
        val channel = FakeChannel(
            stranger,
            message(MessageType.PAIR_REQUEST, "PROF".toByteArray()),
            message(MessageType.PAIR_NONCE, ByteArray(32)),
            message(MessageType.PAIR_CONFIRM),
        )
        handler.handle(channel)
        assertEquals(MessageType.PAIR_DONE, channel.sent.last().type)
        assertTrue(trusted.isTrusted(stranger))
    }

    @Test fun heartbeatIsAnswered() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.HEARTBEAT), message(MessageType.HEARTBEAT))
        handler.handle(channel)
        assertEquals(listOf(MessageType.HEARTBEAT, MessageType.HEARTBEAT), channel.sent.map { it.type })
        assertTrue(channel.sent.all { it.payload.isEmpty() })
    }

    @Test fun unknownCommandGetsNotOnPhoneAck() {
        trusted.add(teacher)
        // GetWakeAddress (22) and ClearChromeProfiles (91): PC commands.
        val channel = FakeChannel(teacher, message(22), message(91, byteArrayOf(1, 2)))
        handler.handle(channel)

        assertEquals(listOf(MessageType.COMMAND_ACK, MessageType.COMMAND_ACK), channel.sent.map { it.type })
        channel.sent.forEach {
            assertEquals(false to "Ainda não funciona em celular.", CommandResult.decode(it.payload))
        }
    }

    @Test fun allowNewTeacherOpensTheWindow() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.ALLOW_NEW_TEACHER))
        handler.handle(channel)

        assertTrue(pairing.newTeacherOpen)
        val (succeeded, detail) = CommandResult.decode(channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }.payload)
        assertTrue(succeeded)
        assertTrue(detail, detail.matches(Regex("liberada até \\d\\d:\\d\\d")))
    }

    @Test fun aTrustedTeacherWaitsLongerThanTheHandshake() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.HEARTBEAT))
        handler.handle(channel)
        assertEquals(CommandHandler.IDLE_TIMEOUT_MILLIS, channel.timeoutMillis)
    }

    @Test fun requestScreenConsentAsksAndAcks() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }.payload))
    }

    @Test fun requestScreenConsentWhileLockedSaysItWaits() {
        trusted.add(teacher)
        consent.lockedChanged(true)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(0, shown)
        assertEquals(
            false to "a tela está bloqueada: o pedido aparece quando o celular for desbloqueado",
            CommandResult.decode(channel.sent.single().payload),
        )
        // It comes up at the unlock.
        consent.lockedChanged(false)
        assertEquals(1, shown)
    }

    @Test fun requestScreenConsentWhileCapturingChangesNothing() {
        trusted.add(teacher)
        consent.granted()
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(0, shown)
        assertEquals(ConsentState.Capturing, consent.state)
        assertEquals(true to "a tela já está liberada", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun requestScreenConsentAfterADenialShowsTheRequest() {
        trusted.add(teacher)
        consent.denied()
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun requestScreenConsentWhileWaitingShowsTheRequestAgain() {
        trusted.add(teacher)
        consent.ask()
        assertEquals(1, shown)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun untrustedTeacherCannotRequestScreenConsent() {
        trusted.add(teacher)
        handler.handle(FakeChannel(stranger, message(MessageType.REQUEST_SCREEN_CONSENT)))
        assertEquals(0, shown)
        assertEquals(ConsentState.NotAsked, consent.state)
    }

    @Test fun setPhoneAppBlockAppliesAndAcks() {
        trusted.add(teacher)
        val policy = PhoneAppPolicy(10, AppBlockMode.BLOCK_LIST, listOf("com.whatsapp"))
        val channel = FakeChannel(teacher, message(MessageType.SET_PHONE_APP_BLOCK, policy.encode()))
        handler.handle(channel)

        assertEquals(listOf(policy), applied)
        val ack = channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }
        assertEquals(true to "bloqueando 1 app(s)", CommandResult.decode(ack.payload))
    }

    @Test fun setPhoneAppBlockRefusesNonsense() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.SET_PHONE_APP_BLOCK, byteArrayOf(1)))
        handler.handle(channel)
        assertEquals(false to "pedido inválido", CommandResult.decode(channel.sent.single().payload))
        assertTrue(applied.isEmpty())
    }

    @Test fun getPhoneAppBlockAnswersTheHeldPolicy() {
        trusted.add(teacher)
        val policy = PhoneAppPolicy(10, AppBlockMode.ALLOW_ONLY, listOf("com.android.chrome"))
        val channel = FakeChannel(
            teacher,
            message(MessageType.GET_PHONE_APP_BLOCK),
            message(MessageType.SET_PHONE_APP_BLOCK, policy.encode()),
            message(MessageType.GET_PHONE_APP_BLOCK),
        )
        handler.handle(channel)

        assertEquals(
            listOf(MessageType.PHONE_APP_BLOCK_STATUS, MessageType.COMMAND_ACK, MessageType.PHONE_APP_BLOCK_STATUS),
            channel.sent.map { it.type },
        )
        assertEquals(PhoneAppPolicy.lifted(0), PhoneAppPolicy.decode(channel.sent[0].payload))
        assertEquals(policy, PhoneAppPolicy.decode(channel.sent[2].payload))
    }

    @Test fun getPhoneAppsAnswersTheList() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.GET_PHONE_APPS))
        handler.handle(channel)

        val answer = channel.sent.single().also { assertEquals(MessageType.PHONE_APPS, it.type) }
        assertEquals(phoneApps, PhoneAppList.decode(answer.payload))
    }

    @Test fun untrustedTeacherCannotBlockApps() {
        trusted.add(teacher)
        val policy = PhoneAppPolicy(10, AppBlockMode.ALLOW_ONLY, emptyList())
        val channel = FakeChannel(stranger, message(MessageType.SET_PHONE_APP_BLOCK, policy.encode()))
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertTrue(applied.isEmpty())
    }

    // As the Windows AgentServer: the site commands are acknowledged with a
    // single byte, 1 or 0.
    @Test fun siteCommandsAreAckedWithOneByte() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            message(MessageType.BLOCK_DOMAIN, "YouTube.com".toByteArray()),
            message(MessageType.BLOCK_DOMAIN, "not a domain".toByteArray()),
            message(MessageType.BLOCK_INTERNET),
            message(MessageType.UNBLOCK_INTERNET),
            message(MessageType.UNBLOCK_DOMAIN, "facebook.com".toByteArray()),
            message(MessageType.UNBLOCK_DOMAIN, "youtube.com".toByteArray()),
        )
        handler.handle(channel)

        assertTrue(channel.sent.all { it.type == MessageType.COMMAND_ACK })
        assertEquals(listOf(1, 0, 1, 1, 1, 1), channel.sent.map { it.payload.single().toInt() })
        assertEquals(SiteBlockState.EMPTY, sites.current)
        assertEquals(
            listOf(
                SiteBlockState(setOf("youtube.com"), false),
                SiteBlockState(setOf("youtube.com"), true),
                SiteBlockState(setOf("youtube.com"), false),
                SiteBlockState(setOf("youtube.com"), false),
                SiteBlockState.EMPTY,
            ),
            sitesApplied,
        )
    }

    @Test fun aSiteBlockThePhoneCannotApplyIsAckedWithZero() {
        trusted.add(teacher)
        sitesWork = false
        val channel = FakeChannel(teacher, message(MessageType.BLOCK_INTERNET))
        handler.handle(channel)
        assertEquals(0, channel.sent.single().payload.single().toInt())
    }

    @Test fun untrustedTeacherCannotBlockSites() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, message(MessageType.BLOCK_INTERNET))
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertTrue(sitesApplied.isEmpty())
    }

    @Test fun optimizePhoneRunsAndAnswersTheReport() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.OPTIMIZE_PHONE))
        handler.handle(channel)

        assertEquals(1, optimizer.runs)
        val answer = channel.sent.single().also { assertEquals(MessageType.PHONE_OPTIMIZE_REPORT, it.type) }
        assertEquals(optimizer.report, PhoneOptimizeReport.decode(answer.payload))
    }

    @Test fun wipeFromTrustedAcksThenWipes() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.WIPE_PHONE))
        gateway.onWipe = { gateway.sentWhenWiped = channel.sent.size }
        handler.handle(channel)

        val ack = channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }
        assertEquals(true to CommandHandler.WIPING, CommandResult.decode(ack.payload))
        assertEquals(1, gateway.wipes)
        assertEquals(1, gateway.sentWhenWiped)
        assertEquals(listOf(CommandHandler.WIPE_DELAY_MILLIS), wipeDelays)
    }

    @Test fun failedWipeIsLoggedAndDoesNotEscape() {
        trusted.add(teacher)
        val refusal = SecurityException("refused")
        gateway.onWipe = { throw refusal }
        val channel = FakeChannel(teacher, message(MessageType.WIPE_PHONE))
        handler.handle(channel)

        assertEquals(1, channel.sent.size)
        assertEquals(1, logged.size)
        assertEquals(refusal, logged.single().second)
    }

    @Test fun wipeFromUntrustedNeverWipes() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, message(MessageType.WIPE_PHONE))
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertEquals(0, gateway.wipes)
    }

    @Test fun wipeWhenNotDeviceOwnerSaysWhy() {
        trusted.add(teacher)
        gateway.owner = false
        val channel = FakeChannel(teacher, message(MessageType.WIPE_PHONE))
        handler.handle(channel)

        val ack = channel.sent.single()
        assertEquals(false to "Este celular não foi preparado pelo Midview.", CommandResult.decode(ack.payload))
        assertEquals(0, gateway.wipes)
    }

    @Test fun untrustedTeacherCannotOptimize() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, message(MessageType.OPTIMIZE_PHONE))
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertEquals(0, optimizer.runs)
        assertEquals(0, optimizer.seen)
    }

    // The first optimization after pairing and the DNS retries start from
    // here; "Otimizar" itself runs anyway.
    @Test fun everyOtherTrustedCommandLetsTheOptimizerCatchUp() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            message(MessageType.GET_PHONE_APP_BLOCK),
            message(MessageType.HEARTBEAT),
            message(MessageType.OPTIMIZE_PHONE),
        )
        handler.handle(channel)
        assertEquals(2, optimizer.seen)
        assertEquals(1, optimizer.runs)
    }

    @Test fun anUntrustedCommandDoesNotStartTheOptimizer() {
        trusted.add(teacher)
        handler.handle(FakeChannel(stranger, message(MessageType.GET_PHONE_APP_BLOCK)))
        assertEquals(0, optimizer.seen)
    }

    @Test fun pairingDoesNotStartTheOptimizer() {
        handler.handle(
            FakeChannel(
                stranger,
                message(MessageType.PAIR_REQUEST, "PROF".toByteArray()),
                message(MessageType.PAIR_NONCE, ByteArray(32)),
                message(MessageType.PAIR_CONFIRM),
            ),
        )
        assertTrue(trusted.isTrusted(stranger))
        assertEquals(0, optimizer.seen)
    }

    // The update takes more than one message each way: the updater answers
    // them all, and the connection ends with it, as on a PC.
    @Test fun anUpdateOfferGoesToTheUpdaterAndEndsTheConnection() {
        trusted.add(teacher)
        val offer = ByteArray(96) { it.toByte() }
        val channel = FakeChannel(teacher, message(MessageType.UPDATE_OFFER, offer), message(MessageType.HEARTBEAT))
        handler.handle(channel)

        assertArrayEquals(offer, updater.offers.single())
        assertEquals(listOf("receive 25", "send 26"), channel.events)
        assertEquals(1, optimizer.seen)
    }

    @Test fun getUpdateStatusAnswersTheUpdatersReport() {
        trusted.add(teacher)
        // With or without the attempt's id, as the Master asks.
        val channel = FakeChannel(
            teacher,
            message(MessageType.GET_UPDATE_STATUS),
            message(MessageType.GET_UPDATE_STATUS, ByteArray(16) { (0xA0 + it).toByte() }),
        )
        handler.handle(channel)

        assertEquals(listOf(MessageType.UPDATE_STATUS, MessageType.UPDATE_STATUS), channel.sent.map { it.type })
        assertEquals(
            "{\"fingerprint\":\"BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD\",\"build\":\"0.0.50\"," +
                "\"ready\":true,\"attempt\":{\"id\":\"A0A1A2A3A4A5A6A7A8A9AAABACADAEAF\",\"state\":\"applying\"," +
                "\"detail\":\"instalando a versão 0.0.51\",\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}",
            String(channel.sent.first().payload, Charsets.UTF_8),
        )
        assertEquals(2, updater.statusAsked)
    }

    // A chunk with no update offered before it.
    @Test fun aStrayFileChunkIsRefused() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.FILE_CHUNK, ByteArray(1000)))
        handler.handle(channel)
        assertEquals(false to "transferência inválida", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun untrustedTeacherCannotUpdate() {
        trusted.add(teacher)
        for (type in listOf(MessageType.UPDATE_OFFER, MessageType.GET_UPDATE_STATUS, MessageType.FILE_CHUNK)) {
            val channel = FakeChannel(stranger, message(type, ByteArray(96)))
            handler.handle(channel)
            assertTrue(channel.sent.isEmpty())
        }
        assertTrue(updater.offers.isEmpty())
        assertEquals(0, updater.statusAsked)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
