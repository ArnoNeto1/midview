package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.policy.DebuggingFree
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneNetworkFree
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.update.PhoneUpdater
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files

// "Depuração USB" on the commands port: a trusted teacher's goes to
// DebuggingFree (the lock lifted and adb on, then off and locked), anyone
// else's goes nowhere, and an agent without it says so.
class CommandHandlerDebuggingTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val restrictions = Lockdown.DEBUGGING_RESTRICTIONS.toMutableSet()
    private val adb = mutableListOf<Boolean>()

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val debugging by lazy {
        DebuggingFree(
            File(folder.newFolder(), "debugging-free.txt"), Lockdown.DEBUGGING_RESTRICTIONS,
            isDeviceOwner = { true }, addRestriction = { restrictions += it }, clearRestriction = { restrictions -= it },
            usbDebugging = { adb += it }, now = { 1_000_000L },
        )
    }

    private fun handler(withIt: Boolean): CommandHandler {
        val pairing = PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Tab 01" }, elapsed = { 1_000L })
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
        return if (withIt) {
            CommandHandler(
                trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway(),
                freeDebugging = debugging::handle,
            ) { emptyList() }
        } else {
            CommandHandler(trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway()) { emptyList() }
        }
    }

    @Test fun theNumberFollowsClearChromeData() {
        assertEquals(111, MessageType.FREE_PHONE_DEBUGGING)
    }

    @Test fun aTrustedTeacherFreesUsbDebuggingAndLocksItAgain() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.FREE_PHONE_DEBUGGING, PhoneNetworkFree.encode(30)),
            Message(MessageType.FREE_PHONE_DEBUGGING, PhoneNetworkFree.encode(PhoneNetworkFree.LOCK_NOW)),
        )
        handler(withIt = true).handle(channel)

        assertEquals(true to "depuração USB liberada e ligada por 30 minutos", CommandResult.decode(channel.sent[0].payload))
        assertEquals(true to DebuggingFree.LOCKED, CommandResult.decode(channel.sent[1].payload))
        assertEquals(listOf(true, false), adb)
        assertEquals(setOf("no_debugging_features"), restrictions)
    }

    @Test fun anUntrustedTeacherDoesNot() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, Message(MessageType.FREE_PHONE_DEBUGGING, PhoneNetworkFree.encode(10)))
        handler(withIt = true).handle(channel)

        assertTrue(channel.sent.isEmpty())
        assertEquals(setOf("no_debugging_features"), restrictions)
        assertTrue(adb.isEmpty())
    }

    // What the Master turns into "atualize os celulares".
    @Test fun withoutItTheDeviceSaysItDoesNotYet() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.FREE_PHONE_DEBUGGING, PhoneNetworkFree.encode(10)))
        handler(withIt = false).handle(channel)
        assertEquals(false to CommandResult.NOT_ON_PHONE, CommandResult.decode(channel.sent.single().payload))
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-debugging").toFile().apply { deleteOnExit() }, "Tab 01")
        }
    }
}
