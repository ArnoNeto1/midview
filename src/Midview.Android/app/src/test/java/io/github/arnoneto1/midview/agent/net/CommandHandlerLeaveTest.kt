package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneLeaveRequest
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.update.PhoneUpdater
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files

// "Desfazer e tirar o Midview" on the commands port: a trusted teacher's
// confirmed command undoes the locks, is answered, and only then is the rest
// done (off the connection); anyone else's goes nowhere; once leaving, the
// phone takes no command and pairs with nobody.
class CommandHandlerLeaveTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val events = mutableListOf<String>()
    private var leaving = false
    private var ownerGivenUp = false
    private var deviceOwner = true
    private val later = mutableListOf<Pair<Long, () -> Unit>>()

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private fun handler(): CommandHandler {
        val pairing = PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Cel 01" }, elapsed = { 1_000L })
        val optimizer = object : PhoneOptimizer {
            override fun run() = throw UnsupportedOperationException()
            override fun teacherSeen() = Unit
        }
        val updater = object : PhoneUpdater {
            override fun offer(payload: ByteArray, channel: Channel) = throw UnsupportedOperationException()
            override fun status() = throw UnsupportedOperationException()
        }
        val appBlock = AppBlockStore(File(folder.newFolder(), "app-block.bin"), apply = { null })
        val sites = SiteBlockStore(File(folder.newFolder(), "site-block.txt"), apply = { true })
        val consent = ScreenConsent(clock = { 0L }) {}
        return CommandHandler(
            trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway(owner = deviceOwner),
            runLater = { delay, task -> later += delay to task },
            isLeaving = { leaving },
            ownerGivenUp = { ownerGivenUp },
            leave = { payload ->
                events += "leave"
                if (PhoneLeaveRequest.isConfirmed(payload)) {
                    leaving = true
                    true to "Midview saindo"
                } else {
                    false to "pedido inválido"
                }
            },
            finishLeaving = { events += "finish" },
        ) { emptyList() }
    }

    @Test fun aConfirmedCommandIsAnsweredThenFinishedOffTheConnection() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.REMOVE_MIDVIEW_FROM_PHONE, PhoneLeaveRequest.encode()),
            Message(MessageType.HEARTBEAT, ByteArray(0)),
            events = events,
        )

        handler().handle(channel)

        assertEquals(true to "Midview saindo", CommandResult.decode(channel.sent.single().payload))
        // The connection ends with the answer: the heartbeat after it is never read.
        assertEquals(listOf("receive ${MessageType.REMOVE_MIDVIEW_FROM_PHONE}", "leave", "send ${MessageType.COMMAND_ACK}"), events)
        assertEquals(CommandHandler.WIPE_DELAY_MILLIS, later.single().first)
        later.single().second()
        assertEquals("finish", events.last())
    }

    @Test fun anyOtherWordLeavesNothing() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.REMOVE_MIDVIEW_FROM_PHONE, "remover".toByteArray()))

        handler().handle(channel)

        assertEquals(false to "pedido inválido", CommandResult.decode(channel.sent.single().payload))
        assertTrue(later.isEmpty())
        assertFalse(leaving)
    }

    @Test fun aStrangersCommandGoesNowhere() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, Message(MessageType.REMOVE_MIDVIEW_FROM_PHONE, PhoneLeaveRequest.encode()))

        handler().handle(channel)

        assertTrue(channel.sent.isEmpty())
        assertTrue(events.isEmpty())
        assertTrue(later.isEmpty())
    }

    @Test fun whileLeavingNoCommandIsTaken() {
        trusted.add(teacher)
        leaving = true
        val channel = FakeChannel(teacher, Message(MessageType.HEARTBEAT, ByteArray(0)))

        handler().handle(channel)

        assertEquals(false to CommandHandler.LEAVING, CommandResult.decode(channel.sent.single().payload))
    }

    // A lock the leaving could not lift: the teacher can still erase the phone.
    @Test fun whileLeavingAndStillTheOwnerTheWipeIsTaken() {
        trusted.add(teacher)
        leaving = true
        val channel = FakeChannel(teacher, Message(MessageType.WIPE_PHONE, ByteArray(0)))

        handler().handle(channel)

        assertEquals(true to CommandHandler.WIPING, CommandResult.decode(channel.sent.single().payload))
        assertEquals(CommandHandler.WIPE_DELAY_MILLIS, later.single().first)
    }

    @Test fun whileLeavingAndStillTheOwnerTheLeavingIsTriedAgain() {
        trusted.add(teacher)
        leaving = true
        val channel = FakeChannel(teacher, Message(MessageType.REMOVE_MIDVIEW_FROM_PHONE, PhoneLeaveRequest.encode()))

        handler().handle(channel)

        assertEquals(true to "Midview saindo", CommandResult.decode(channel.sent.single().payload))
        assertEquals(listOf("leave"), events)
    }

    // An undo that left a lock: no finish follows, the phone stays the school's.
    @Test fun anIncompleteUndoIsNotFinished() {
        trusted.add(teacher)
        val handler = CommandHandler(
            trusted, PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Cel 01" }, elapsed = { 1_000L }),
            ScreenConsent(clock = { 0L }) {}, AppBlockStore(File(folder.newFolder(), "app-block.bin"), apply = { null }),
            SiteBlockStore(File(folder.newFolder(), "site-block.txt"), apply = { true }),
            object : PhoneOptimizer {
                override fun run() = throw UnsupportedOperationException()
                override fun teacherSeen() = Unit
            },
            object : PhoneUpdater {
                override fun offer(payload: ByteArray, channel: Channel) = throw UnsupportedOperationException()
                override fun status() = throw UnsupportedOperationException()
            },
            FakeSiteGateway(),
            runLater = { delay, task -> later += delay to task },
            leave = { false to "Não deu para desfazer o PIN da tela." },
            finishLeaving = { events += "finish" },
        ) { emptyList() }
        val channel = FakeChannel(teacher, Message(MessageType.REMOVE_MIDVIEW_FROM_PHONE, PhoneLeaveRequest.encode()))

        handler.handle(channel)

        assertEquals(false to "Não deu para desfazer o PIN da tela.", CommandResult.decode(channel.sent.single().payload))
        assertTrue(later.isEmpty())
    }

    // Once the owner is given up, nothing more: the uninstall is all that is left.
    @Test fun onceTheOwnerIsGivenUpNotEvenTheWipe() {
        trusted.add(teacher)
        leaving = true
        ownerGivenUp = true
        val channel = FakeChannel(teacher, Message(MessageType.WIPE_PHONE, ByteArray(0)))

        handler().handle(channel)

        assertEquals(false to CommandHandler.LEAVING, CommandResult.decode(channel.sent.single().payload))
        assertTrue(later.isEmpty())
    }

    @Test fun whileLeavingNobodyPairs() {
        leaving = true
        val channel = FakeChannel(stranger, Message(MessageType.PAIR_REQUEST, "PC-PROF".toByteArray()))

        handler().handle(channel)

        assertTrue(channel.sent.isEmpty())
        assertFalse(channel.pairing)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-leave").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
