package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.ZoneOffset

class AppBlockStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "app-block.bin") }
    private val applied = mutableListOf<PhoneAppPolicy>()
    private var warning: String? = null

    private fun store() = AppBlockStore(file, apply = { applied += it; warning }, zone = ZoneOffset.UTC)

    private fun policy(savedAt: Long, mode: AppBlockMode, vararg pkgs: String) = PhoneAppPolicy(savedAt, mode, pkgs.toList())

    @Test fun startsWithNothingBlocked() {
        assertEquals(PhoneAppPolicy.lifted(0), store().current)
    }

    @Test fun describesAsTheWindowsAgentDoes() {
        assertEquals("bloqueando 2 app(s)", AppBlockStore.describe(policy(1, AppBlockMode.BLOCK_LIST, "a.b", "c.d")))
        assertEquals("permitindo só o essencial", AppBlockStore.describe(policy(1, AppBlockMode.ALLOW_ONLY)))
        assertEquals("permitindo só o essencial e 1 app(s)", AppBlockStore.describe(policy(1, AppBlockMode.ALLOW_ONLY, "a.b")))
        assertEquals("nenhum app bloqueado", AppBlockStore.describe(PhoneAppPolicy.lifted(1)))
    }

    @Test fun aNewerPolicyIsSavedAppliedAndKept() {
        val store = store()
        val blocked = policy(1_700_000_000, AppBlockMode.BLOCK_LIST, "com.whatsapp")
        assertEquals(true to "bloqueando 1 app(s)", store.handle(blocked.encode()))
        assertEquals(listOf(blocked), applied)
        assertEquals(blocked, store.current)
        assertArrayEquals(blocked.encode(), file.readBytes())

        // Back after a restart.
        assertEquals(blocked, store().current)
    }

    @Test fun anOlderPolicyKeepsTheNewest() {
        val store = store()
        store.handle(policy(1_700_000_000, AppBlockMode.ALLOW_ONLY, "com.android.chrome").encode())
        applied.clear()

        val answer = store.handle(policy(1_600_000_000, AppBlockMode.BLOCK_LIST, "com.whatsapp").encode())

        // 1_700_000_000 is 14/11/2023 22:13 UTC.
        assertEquals(true to "mantido o bloqueio mais novo, salvo em 14/11 22:13: permitindo só o essencial e 1 app(s)", answer)
        assertTrue(applied.isEmpty())
        assertEquals(AppBlockMode.ALLOW_ONLY, store.current.mode)
    }

    @Test fun theSameSecondWithTheSameRulesIsTheSamePolicy() {
        val store = store()
        store.handle(policy(50, AppBlockMode.BLOCK_LIST, "a.b", "c.d").encode())
        applied.clear()
        assertEquals(true to "bloqueando 2 app(s)", store.handle(policy(50, AppBlockMode.BLOCK_LIST, "c.d", "a.b").encode()))
        assertTrue(applied.isEmpty())
    }

    @Test fun theSameSecondWithOtherRulesAsksToApplyAgain() {
        val store = store()
        store.handle(policy(50, AppBlockMode.BLOCK_LIST, "a.b").encode())
        assertEquals(
            false to "outro bloqueio foi salvo no mesmo segundo; aplique de novo",
            store.handle(policy(50, AppBlockMode.ALLOW_ONLY).encode()),
        )
        assertEquals(AppBlockMode.BLOCK_LIST, store.current.mode)
    }

    @Test fun anInvalidRequestIsRefused() {
        assertEquals(false to "pedido inválido", store().handle(byteArrayOf(9, 9)))
        assertTrue(applied.isEmpty())
    }

    @Test fun theApplyWarningIsSaidWhenBlocking() {
        warning = "este celular não bloqueia apps"
        assertEquals(
            true to "bloqueando 1 app(s) (atenção: este celular não bloqueia apps)",
            store().handle(policy(2, AppBlockMode.BLOCK_LIST, "a.b").encode()),
        )
    }

    @Test fun liftingSaysNoWarning() {
        warning = "este celular não bloqueia apps"
        assertEquals(true to "nenhum app bloqueado", store().handle(PhoneAppPolicy.lifted(2).encode()))
    }

    @Test fun aPolicyThatCannotBeSavedIsNotApplied() {
        // The folder is a file: nothing can be written in it.
        val blocker = folder.newFile("not-a-folder")
        val store = AppBlockStore(File(blocker, "app-block.bin"), apply = { applied += it; null }, zone = ZoneOffset.UTC)
        val (succeeded, detail) = store.handle(policy(3, AppBlockMode.BLOCK_LIST, "a.b").encode())
        assertEquals(false, succeeded)
        assertTrue(detail, detail.startsWith("não foi possível gravar o bloqueio: "))
        assertTrue(applied.isEmpty())
        assertEquals(PhoneAppPolicy.lifted(0), store.current)
    }

    // Unreadable means nothing blocked, and the file is kept aside.
    @Test fun aCorruptFileBlocksNothingAndIsKeptAside() {
        file.writeBytes(byteArrayOf(1, 2, 3))
        assertEquals(PhoneAppPolicy.lifted(0), store().current)
        assertTrue(File(folder.root, "app-block.bin.corrupt").exists())
    }
}
