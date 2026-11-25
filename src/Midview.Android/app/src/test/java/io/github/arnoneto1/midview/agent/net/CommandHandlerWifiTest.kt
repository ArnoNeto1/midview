package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneWifiNetwork
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

// "Rede Wi-Fi nova da escola" on the commands port: a trusted teacher's
// network is saved by the Device Owner, anyone else's is not answered, and a
// device the Midview does not own, or a payload the Master never sends, is
// refused.
class CommandHandlerWifiTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private fun handler(gateway: FakeSiteGateway): CommandHandler {
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
        return CommandHandler(trusted, pairing, consent, appBlock, sites, optimizer, updater, gateway) { emptyList() }
    }

    private fun send(gateway: FakeSiteGateway, from: String, payload: ByteArray): FakeChannel {
        val channel = FakeChannel(from, Message(MessageType.ADD_PHONE_WIFI, payload))
        handler(gateway).handle(channel)
        return channel
    }

    @Test fun aTrustedTeachersNetworkIsSaved() {
        trusted.add(teacher)
        val channel = send(FakeSiteGateway(), teacher, PhoneWifiNetwork.encode("Escola Nova", "senha1234"))
        assertEquals(true to CommandHandler.WIFI_SAVED, CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun aDeviceTheMidviewDoesNotOwnSavesNothing() {
        trusted.add(teacher)
        val channel = send(FakeSiteGateway(owner = false), teacher, PhoneWifiNetwork.encode("Escola Nova", ""))
        assertEquals(false to CommandHandler.NOT_PREPARED, CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun aBadPayloadIsRefused() {
        trusted.add(teacher)
        val channel = send(FakeSiteGateway(), teacher, byteArrayOf(9, 1))
        assertEquals(false to CommandHandler.INVALID_REQUEST, CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun anUntrustedTeacherGetsNoAnswer() {
        trusted.add(teacher)
        val channel = send(FakeSiteGateway(), stranger, PhoneWifiNetwork.encode("Outra", ""))
        assertTrue(channel.sent.isEmpty())
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-wifi").toFile().apply { deleteOnExit() }, "Tab 01")
        }
    }
}
