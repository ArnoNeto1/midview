package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.optimize.PhoneOptimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.policy.AccountsFree
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.PhoneNetworkFree
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import io.github.arnoneto1.midview.agent.sites.ChromeDataClear
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

// "Contas do aparelho" and "Sair das contas do Chrome" on the commands port:
// a trusted teacher's go to AccountsFree and ChromeDataClear, anyone else's
// go nowhere, and an agent without them says so.
class CommandHandlerAccountsTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)
    private val restrictions = Lockdown.ACCOUNT_RESTRICTIONS.toMutableSet()
    private val cleared = mutableListOf<String>()

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val accounts by lazy {
        AccountsFree(
            File(folder.newFolder(), "accounts-free.txt"), Lockdown.ACCOUNT_RESTRICTIONS,
            isDeviceOwner = { true }, addRestriction = { restrictions += it }, clearRestriction = { restrictions -= it },
            now = { 1_000_000L },
        )
    }
    private val chrome = ChromeDataClear(
        isDeviceOwner = { true }, sdkInt = 34, isChromeInstalled = { true },
        clear = { pkg, done ->
            cleared += pkg
            done(true)
        },
    )

    private fun handler(withThem: Boolean): CommandHandler {
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
        return if (withThem) {
            CommandHandler(
                trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway(),
                freeAccounts = accounts::handle, clearChrome = chrome::handle,
            ) { emptyList() }
        } else {
            CommandHandler(trusted, pairing, consent, appBlock, sites, optimizer, updater, FakeSiteGateway()) { emptyList() }
        }
    }

    @Test fun theNumbersFollowFindPhone() {
        assertEquals(109, MessageType.FREE_PHONE_ACCOUNTS)
        assertEquals(110, MessageType.CLEAR_CHROME_DATA)
    }

    @Test fun aTrustedTeacherFreesTheAccountsAndLocksThemAgain() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.FREE_PHONE_ACCOUNTS, PhoneNetworkFree.encode(10)),
            Message(MessageType.FREE_PHONE_ACCOUNTS, PhoneNetworkFree.encode(PhoneNetworkFree.LOCK_NOW)),
        )
        handler(withThem = true).handle(channel)

        assertEquals(true to "contas do aparelho liberadas por 10 minutos", CommandResult.decode(channel.sent[0].payload))
        assertEquals(true to AccountsFree.LOCKED, CommandResult.decode(channel.sent[1].payload))
        assertEquals(setOf("no_modify_accounts"), restrictions)
    }

    @Test fun aTrustedTeacherClearsChrome() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.CLEAR_CHROME_DATA, ByteArray(0)))
        handler(withThem = true).handle(channel)

        assertEquals(true to ChromeDataClear.CLEARED, CommandResult.decode(channel.sent.single().payload))
        assertEquals(listOf("com.android.chrome"), cleared)
    }

    @Test fun anUntrustedTeacherDoesNeither() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, Message(MessageType.FREE_PHONE_ACCOUNTS, PhoneNetworkFree.encode(10)))
        handler(withThem = true).handle(channel)
        val second = FakeChannel(stranger, Message(MessageType.CLEAR_CHROME_DATA, ByteArray(0)))
        handler(withThem = true).handle(second)

        assertTrue(channel.sent.isEmpty())
        assertTrue(second.sent.isEmpty())
        assertEquals(setOf("no_modify_accounts"), restrictions)
        assertTrue(cleared.isEmpty())
    }

    // What the Master turns into "atualize os celulares".
    @Test fun withoutThemTheDeviceSaysItDoesNotYet() {
        trusted.add(teacher)
        val channel = FakeChannel(
            teacher,
            Message(MessageType.FREE_PHONE_ACCOUNTS, PhoneNetworkFree.encode(10)),
            Message(MessageType.CLEAR_CHROME_DATA, ByteArray(0)),
        )
        handler(withThem = false).handle(channel)
        channel.sent.forEach { assertEquals(false to CommandResult.NOT_ON_PHONE, CommandResult.decode(it.payload)) }
        assertEquals(2, channel.sent.size)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler-accounts").toFile().apply { deleteOnExit() }, "Tab 01")
        }
    }
}
