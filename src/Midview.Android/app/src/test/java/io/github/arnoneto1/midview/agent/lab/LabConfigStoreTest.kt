package io.github.arnoneto1.midview.agent.lab

import io.github.arnoneto1.midview.agent.protocol.LabConfig
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class LabConfigStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "lab-config.bin") }
    private var owner = true
    private var chromeTakes = true
    private var chrome: Int? = 131
    private val now = 1_800_000_000L
    private val downloads = mutableListOf<Boolean>()
    private var adblockChanges = 0
    private val store by lazy { newStore() }

    private fun newStore() = LabConfigStore(
        file, { owner }, { downloads += it; chromeTakes }, { adblockChanges++ },
        chromeVersion = { chrome }, nowSeconds = { now },
    )

    private fun set(savedAt: Long, blockDownloads: Boolean, adblock: Boolean) =
        store.handle(LabConfig(savedAt, blockDownloads, adblock).encode())

    @Test fun aNewPhoneHasDownloadsFreeAndTheAdBlockerOn() {
        assertEquals(LabConfig.DEFAULT, store.current)
        assertTrue(store.adblock)
    }

    @Test fun blockingDownloadsTellsChromeAndIsKept() {
        assertEquals(true to "travar downloads, bloqueador de anúncios (DNS)", set(10, blockDownloads = true, adblock = true))
        assertEquals(listOf(true), downloads)
        assertEquals(0, adblockChanges)
        assertEquals(LabConfig(10, true, true), newStore().current)
    }

    @Test fun turningTheAdBlockerOffTellsTheOptimizerAfterItIsKept() {
        var seen: Boolean? = null
        lateinit var store: LabConfigStore
        store = LabConfigStore(file, { true }, { true }, { seen = store.adblock })
        assertEquals(true to "nenhuma configuração aplicada", store.handle(LabConfig(10, false, false).encode()))
        assertEquals(false, seen)
        assertFalse(newStore().adblock)
    }

    @Test fun anOlderConfigurationLosesToTheNewest() {
        set(20, blockDownloads = true, adblock = true)
        val (applied, detail) = set(10, blockDownloads = false, adblock = false)
        assertTrue(applied)
        assertTrue(detail, detail.startsWith("mantida a configuração mais nova"))
        assertEquals(LabConfig(20, true, true), store.current)
        assertEquals(listOf(true), downloads)
    }

    @Test fun theSameSecondAgainIsDoneOnlyWithTheSameFunctions() {
        set(20, blockDownloads = true, adblock = true)
        assertEquals(true to "travar downloads, bloqueador de anúncios (DNS)", set(20, blockDownloads = true, adblock = true))
        assertEquals(false to LabConfigStore.SAME_SECOND, set(20, blockDownloads = false, adblock = true))
    }

    @Test fun resetPutsThePhoneBackToItsDefaultAndAlwaysWins() {
        set(now + 100, blockDownloads = true, adblock = false)
        assertEquals(true to "bloqueador de anúncios (DNS)", store.reset())
        assertEquals(LabConfig(now + 100, blockDownloads = false, adblock = true), store.current)
        assertEquals(listOf(true, false), downloads)
        assertEquals(2, adblockChanges)
        // A later SET still takes over.
        assertTrue(set(now + 101, blockDownloads = true, adblock = true).first)
    }

    @Test fun resetCarriesTheCurrentSecond() {
        set(10, blockDownloads = true, adblock = true)
        store.reset()
        assertEquals(now, store.current.savedAt)
    }

    @Test fun aPhoneThatIsNotTheDeviceOwnerChangesNothing() {
        owner = false
        assertEquals(false to LabConfigStore.NOT_PREPARED, set(10, blockDownloads = true, adblock = true))
        assertEquals(LabConfig.DEFAULT, store.current)
        assertTrue(downloads.isEmpty())
        assertFalse(file.exists())
    }

    @Test fun whatChromeRefusesIsPutBackAndNotKept() {
        chromeTakes = false
        assertEquals(false to LabConfigStore.CHROME_REFUSED, set(10, blockDownloads = true, adblock = false))
        assertEquals(listOf(true, false), downloads)
        assertEquals(LabConfig.DEFAULT, store.current)
        assertEquals(0, adblockChanges)
    }

    @Test fun anOldChromeKeepsTheBlockAndTheTeacherReadsWhy() {
        chrome = 126
        assertEquals(false to LabConfigStore.oldChrome(126), set(10, blockDownloads = true, adblock = true))
        assertEquals(LabConfig(10, true, true), store.current)
        assertEquals(
            "guardado, mas o Chrome deste celular (versão 126) só trava downloads a partir da versão 131: atualize o Chrome",
            LabConfigStore.oldChrome(126),
        )
        // Only the ad blocker changing beside it: not said again.
        assertEquals(true to "travar downloads", set(11, blockDownloads = true, adblock = false))
        // Without Chrome there is nothing to download with.
        chrome = null
        assertTrue(set(12, blockDownloads = true, adblock = true).first)
    }

    @Test fun anInvalidRequestChangesNothing() {
        assertEquals(false to LabConfigStore.INVALID_REQUEST, store.handle(byteArrayOf(1, 2, 3)))
        assertEquals(LabConfig.DEFAULT, store.current)
    }

    @Test fun aDamagedFileIsSetAsideAndTheDefaultHolds() {
        file.writeBytes(byteArrayOf(9, 9, 9))
        assertEquals(LabConfig.DEFAULT, store.current)
        assertTrue(File(file.path + ".corrupt").exists())
    }
}
