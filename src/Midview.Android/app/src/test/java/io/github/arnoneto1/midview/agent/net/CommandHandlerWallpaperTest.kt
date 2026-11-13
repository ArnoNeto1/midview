package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneWallpaper
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

// "Papel de parede dos celulares e tablets" on the commands port: a trusted
// teacher's picture goes to the wallpaper, anyone else's goes nowhere, and
// an agent without it says so.
class CommandHandlerWallpaperTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val jpeg = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 1)
    private val received = mutableListOf<ByteArray>()

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private fun handler(withWallpaper: Boolean): CommandHandler {
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
        return if (withWallpaper) {
            CommandHandler(
                trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway(),
                wallpaper = { received += it; true to "papel de parede posto na tela inicial e na de bloqueio" },
            ) { emptyList() }
        } else {
            CommandHandler(trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway()) { emptyList() }
        }
    }

    @Test fun aTrustedTeachersPictureGoesToTheWallpaper() {
        trusted.add(teacher)
        val payload = PhoneWallpaper.encode(PhoneWallpaper.BOTH, jpeg)
        val channel = FakeChannel(teacher, Message(MessageType.SET_PHONE_WALLPAPER, payload))
        handler(withWallpaper = true).handle(channel)

        assertEquals(true to "papel de parede posto na tela inicial e na de bloqueio", CommandResult.decode(channel.sent.single().payload))
        assertTrue(payload.contentEquals(received.single()))
    }

    @Test fun anUntrustedTeachersPictureGoesNowhere() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, Message(MessageType.SET_PHONE_WALLPAPER, PhoneWallpaper.encode(PhoneWallpaper.BOTH, jpeg)))
        handler(withWallpaper = true).handle(channel)

        assertTrue(channel.sent.isEmpty())
        assertTrue(received.isEmpty())
    }

    @Test fun withoutItThePhoneSaysItDoesNotYet() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.SET_PHONE_WALLPAPER, PhoneWallpaper.encode(PhoneWallpaper.BOTH, jpeg)))
        handler(withWallpaper = false).handle(channel)
        assertEquals(false to CommandResult.NOT_ON_PHONE, CommandResult.decode(channel.sent.single().payload))
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-wallpaper").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
