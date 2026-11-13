package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import io.github.arnoneto1.midview.agent.policy.NetworkFree
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword
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

// "Liberar Wi-Fi e modo avião" and the admin password on the commands port:
// a trusted teacher's go to NetworkFree and AdminUnlock, anyone else's go
// nowhere, and an agent without them says so.
class CommandHandlerNetworkTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val restrictions = mutableSetOf("no_config_wifi", "no_airplane_mode")

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val network by lazy {
        NetworkFree(
            File(folder.newFolder(), "network-free.txt"), listOf("no_config_wifi", "no_airplane_mode"),
            isDeviceOwner = { true }, addRestriction = { restrictions += it }, clearRestriction = { restrictions -= it },
            now = { 1_000_000L },
        )
    }
    private val admin by lazy {
        val dir = folder.newFolder()
        AdminUnlock(File(dir, "admin-password.bin"), File(dir, "admin-attempts.txt"), network::free, now = { 1_000_000L })
    }

    private fun handler(withNetwork: Boolean): CommandHandler {
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
        return if (withNetwork) {
            CommandHandler(
                trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway(),
                freeNetwork = network::handle, adminPassword = admin::setPassword,
            ) { emptyList() }
        } else {
            CommandHandler(trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway()) { emptyList() }
        }
    }

    @Test fun aTrustedTeacherFreesTheNetworkAndLocksItAgain() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.FREE_PHONE_NETWORK, PhoneNetworkFree.encode(30)),
            Message(MessageType.FREE_PHONE_NETWORK, PhoneNetworkFree.encode(PhoneNetworkFree.LOCK_NOW)),
        )
        handler(withNetwork = true).handle(channel)

        assertEquals(true to "Wi-Fi e modo avião liberados por 30 minutos", CommandResult.decode(channel.sent[0].payload))
        assertEquals(true to NetworkFree.LOCKED, CommandResult.decode(channel.sent[1].payload))
        assertEquals(setOf("no_config_wifi", "no_airplane_mode"), restrictions)
    }

    @Test fun anInvalidTimeIsRefused() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.FREE_PHONE_NETWORK, byteArrayOf(61)))
        handler(withNetwork = true).handle(channel)
        assertEquals(false to NetworkFree.INVALID_REQUEST, CommandResult.decode(channel.sent.single().payload))
        assertEquals(2, restrictions.size)
    }

    @Test fun aTrustedTeachersAdminPasswordIsKept() {
        trusted.add(teacher)
        val payload = PhoneAdminPassword.encode("abc123", ByteArray(16), 10_000)
        val channel = FakeChannel(teacher, Message(MessageType.SET_PHONE_ADMIN_PASSWORD, payload))
        handler(withNetwork = true).handle(channel)

        assertEquals(true to AdminUnlock.SET, CommandResult.decode(channel.sent.single().payload))
        assertEquals(AdminUnlock.State.Ready, admin.state())
    }

    @Test fun anUntrustedTeacherFreesNothingAndSetsNoPassword() {
        trusted.add(teacher)
        val channel = FakeChannel(
            stranger,
            Message(MessageType.FREE_PHONE_NETWORK, PhoneNetworkFree.encode(10)),
        )
        handler(withNetwork = true).handle(channel)
        val second = FakeChannel(
            stranger,
            Message(MessageType.SET_PHONE_ADMIN_PASSWORD, PhoneAdminPassword.encode("abc123", ByteArray(16), 10_000)),
        )
        handler(withNetwork = true).handle(second)

        assertTrue(channel.sent.isEmpty())
        assertTrue(second.sent.isEmpty())
        assertEquals(2, restrictions.size)
        assertEquals(AdminUnlock.State.NoPassword, admin.state())
    }

    @Test fun withoutThemThePhoneSaysItDoesNotYet() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.FREE_PHONE_NETWORK, PhoneNetworkFree.encode(10)),
            Message(MessageType.SET_PHONE_ADMIN_PASSWORD, ByteArray(0)),
        )
        handler(withNetwork = false).handle(channel)
        channel.sent.forEach { assertEquals(false to CommandResult.NOT_ON_PHONE, CommandResult.decode(it.payload)) }
        assertEquals(2, channel.sent.size)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-network").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
