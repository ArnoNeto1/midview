package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.power.PowerScheduleStore
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PowerSchedule
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.update.PhoneUpdater
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files

// "Agendar" on the commands port: a trusted teacher's schedule goes to the
// store, anyone else's goes nowhere, and an agent without it says so.
class CommandHandlerPowerScheduleTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val schedules by lazy { PowerScheduleStore(File(folder.newFolder(), "power-schedule.txt")) }
    private val weekdays18h = PowerSchedule(1000, PowerSchedule.WEEKDAYS, 7 * 60, 18 * 60)

    private fun handler(withSchedule: Boolean): CommandHandler {
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
        return if (withSchedule) {
            CommandHandler(
                trusted, pairing, ScreenConsent(clock = { 0L }) {}, appBlock, sites, optimizer, updater, FakeSiteGateway(),
                powerSchedule = schedules::handle,
            ) { emptyList() }
        } else {
            CommandHandler(trusted, pairing, ScreenConsent(clock = { 0L }) {}, appBlock, sites, optimizer, updater, FakeSiteGateway()) {
                emptyList()
            }
        }
    }

    @Test fun aTrustedTeachersScheduleIsKept() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.SET_POWER_SCHEDULE, weekdays18h.encode()))
        handler(withSchedule = true).handle(channel)
        assertEquals(MessageType.COMMAND_ACK, channel.sent.single().type)
        assertEquals(true to "apaga e trava a tela às 18:00 (seg a sex)", CommandResult.decode(channel.sent.single().payload))
        assertEquals(weekdays18h, schedules.current)
    }

    @Test fun anInvalidScheduleIsRefused() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.SET_POWER_SCHEDULE, byteArrayOf(1)))
        handler(withSchedule = true).handle(channel)
        assertEquals(false to PowerScheduleStore.INVALID, CommandResult.decode(channel.sent.single().payload))
        assertNull(schedules.current)
    }

    @Test fun anUntrustedTeachersScheduleGoesNowhere() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, Message(MessageType.SET_POWER_SCHEDULE, weekdays18h.encode()))
        handler(withSchedule = true).handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertNull(schedules.current)
    }

    @Test fun withoutTheScheduleThePhoneSaysItDoesNotYet() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.SET_POWER_SCHEDULE, weekdays18h.encode()))
        handler(withSchedule = false).handle(channel)
        assertEquals(false to CommandResult.NOT_ON_PHONE, CommandResult.decode(channel.sent.single().payload))
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-schedule").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
