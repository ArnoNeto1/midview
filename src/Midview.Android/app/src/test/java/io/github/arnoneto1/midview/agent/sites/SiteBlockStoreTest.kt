package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SiteBlockStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "site-block.txt") }
    private val applied = mutableListOf<SiteBlockState>()
    private var works = true

    private fun store() = SiteBlockStore(file, apply = { applied += it; works })

    private fun SiteBlockStore.send(type: Int, text: String = "") = handle(type, text.toByteArray(Charsets.UTF_8))

    @Test fun startsWithNothingBlocked() {
        assertEquals(SiteBlockState.EMPTY, store().current)
    }

    @Test fun blockingADomainNormalizesAppliesAndKeepsIt() {
        val store = store()
        assertTrue(store.send(MessageType.BLOCK_DOMAIN, "https://YouTube.com/watch?v=x"))
        assertEquals(SiteBlockState(setOf("youtube.com"), false), store.current)
        assertEquals(listOf(store.current), applied)
        assertEquals(store.current, store().current)
    }

    @Test fun blockingTheSameDomainTwiceKeepsOne() {
        val store = store()
        store.send(MessageType.BLOCK_DOMAIN, "youtube.com")
        assertTrue(store.send(MessageType.BLOCK_DOMAIN, "YOUTUBE.COM"))
        assertEquals(setOf("youtube.com"), store.current.domains)
    }

    @Test fun anInvalidDomainIsRefusedAndNothingChanges() {
        val store = store()
        assertFalse(store.send(MessageType.BLOCK_DOMAIN, "not a domain"))
        assertFalse(store.send(MessageType.UNBLOCK_DOMAIN, "youtube.com\nevil.example"))
        assertEquals(SiteBlockState.EMPTY, store.current)
        assertTrue(applied.isEmpty())
        assertFalse(file.exists())
    }

    @Test fun unblockingADomainRemovesIt() {
        val store = store()
        store.send(MessageType.BLOCK_DOMAIN, "youtube.com")
        store.send(MessageType.BLOCK_DOMAIN, "facebook.com")
        assertTrue(store.send(MessageType.UNBLOCK_DOMAIN, "http://youtube.com/"))
        assertEquals(setOf("facebook.com"), store.current.domains)
        assertEquals(setOf("facebook.com"), store().current.domains)
    }

    // As on Windows: freeing a site that was not blocked is done.
    @Test fun unblockingAnAbsentDomainSucceeds() {
        assertTrue(store().send(MessageType.UNBLOCK_DOMAIN, "youtube.com"))
    }

    @Test fun blockingAndFreeingTheInternet() {
        val store = store()
        store.send(MessageType.BLOCK_DOMAIN, "youtube.com")
        assertTrue(store.send(MessageType.BLOCK_INTERNET))
        assertEquals(SiteBlockState(setOf("youtube.com"), true), store.current)
        assertEquals(store.current, store().current)

        assertTrue(store.send(MessageType.UNBLOCK_INTERNET))
        assertEquals(SiteBlockState(setOf("youtube.com"), false), store.current)
        assertEquals(store.current, store().current)
    }

    @Test fun whatTheTeacherSendsAlongWithTheInternetIsIgnored() {
        assertTrue(store().send(MessageType.BLOCK_INTERNET, "anything"))
    }

    @Test fun aRefusedApplyIsAFailureAndTheOldStateStays() {
        val store = store()
        store.send(MessageType.BLOCK_DOMAIN, "youtube.com")
        works = false
        assertFalse(store.send(MessageType.BLOCK_INTERNET))
        assertEquals(SiteBlockState(setOf("youtube.com"), false), store.current)
        assertEquals(store.current, store().current)
        // The old state was put back.
        assertEquals(SiteBlockState(setOf("youtube.com"), false), applied.last())
    }

    @Test fun aStateThatCannotBeSavedIsAFailureAndTheOldOneIsPutBack() {
        val store = SiteBlockStore(File(folder.newFile("not-a-folder"), "site-block.txt"), apply = { applied += it; true })
        assertFalse(store.send(MessageType.BLOCK_DOMAIN, "youtube.com"))
        assertEquals(SiteBlockState.EMPTY, store.current)
        assertEquals(SiteBlockState.EMPTY, applied.last())
    }

    @Test fun reapplyAppliesTheHeldState() {
        store().send(MessageType.BLOCK_INTERNET)
        applied.clear()
        val again = store()
        assertTrue(again.reapply())
        assertEquals(listOf(SiteBlockState(emptySet(), true)), applied)
    }

    @Test fun aCorruptFileMeansNothingBlockedAndIsKeptAside() {
        file.writeText("internet=1\nnot a domain\n")
        val store = store()
        assertEquals(SiteBlockState.EMPTY, store.current)
        assertFalse(file.exists())
        assertTrue(File(file.path + ".corrupt").exists())
    }

    @Test fun theFileFormatIsReadBack() {
        file.writeText("internet=0\nyoutube.com\nwww.g1.com\n")
        assertEquals(SiteBlockState(setOf("youtube.com", "www.g1.com"), false), store().current)
    }

    @Test fun chromeTakesNoMoreDomainsThanItReads() {
        val store = store()
        file.writeText("internet=0\n" + (1..ChromePolicy.MAX_DOMAINS).joinToString("\n") { "s$it.com" } + "\n")
        val full = store()
        assertFalse(full.send(MessageType.BLOCK_DOMAIN, "youtube.com"))
        // One already there is still fine.
        assertTrue(full.send(MessageType.BLOCK_DOMAIN, "s1.com"))
        assertTrue(store.send(MessageType.UNBLOCK_DOMAIN, "youtube.com"))
    }

    @Test fun otherMessagesAreRefused() {
        assertFalse(store().send(MessageType.HEARTBEAT))
    }
}
