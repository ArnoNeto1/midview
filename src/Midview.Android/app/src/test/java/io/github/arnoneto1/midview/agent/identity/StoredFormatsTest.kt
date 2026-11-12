package io.github.arnoneto1.midview.agent.identity

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonStore
import io.github.arnoneto1.midview.agent.lab.LabConfigStore
import io.github.arnoneto1.midview.agent.optimize.OptimizeStore
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.LabConfig
import io.github.arnoneto1.midview.agent.protocol.LessonId
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.sites.SiteBlockState
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Instant
import java.time.LocalDate

// What the phones of 0.0.70 keep (files exactly as that agent wrote them,
// some copied off the test Redmi) is read by this version, and kept in the
// phone's own format from the next change on: an update never lifts a block.
class StoredFormatsTest {
    @get:Rule val folder = TemporaryFolder()

    private fun golden(name: String): ByteArray =
        javaClass.getResourceAsStream("/stored-0.0.70/$name")!!.use { it.readBytes() }

    private fun file(name: String, golden: String): File = File(folder.root, name).apply { writeBytes(golden(golden)) }

    // ---- the files of 0.0.70

    @Test fun theRedmisAppBlockIsRead() {
        val store = AppBlockStore(file("app-block.bin", "redmi-app-block.bin"), apply = { null })
        assertEquals(PhoneAppPolicy(0x6AF636A7, AppBlockMode.NONE, emptyList()), store.current)
    }

    @Test fun aBlockListOf0070IsRead() {
        val store = AppBlockStore(file("app-block.bin", "app-block-list.bin"), apply = { null })
        assertEquals(
            PhoneAppPolicy(1762960000, AppBlockMode.BLOCK_LIST, listOf("com.whatsapp", "com.instagram.android", "com.zhiliaoapp.musically")),
            store.current,
        )
    }

    @Test fun anAllowOnlyOf0070IsReadAndKeptInTheNewFormatAfterAChange() {
        val file = file("app-block.bin", "app-allow-only.bin")
        val applied = mutableListOf<PhoneAppPolicy>()
        val store = AppBlockStore(file, apply = { applied += it; null })
        val held = PhoneAppPolicy(1762960100, AppBlockMode.ALLOW_ONLY, listOf("com.android.chrome", "org.khanacademy.android"))
        assertEquals(held, store.current)
        store.reapply()
        assertEquals(listOf(held), applied)

        val newer = held.copy(savedAt = held.savedAt + 1, packages = held.packages + "com.duolingo")
        assertTrue(store.handle(newer.encode()).first)
        assertTrue(String(file.readBytes()).startsWith("midview-app-block 2\n"))
        assertEquals(newer, AppBlockStore(file, apply = { null }).current)
    }

    @Test fun theRedmisLabConfigIsRead() {
        val store = LabConfigStore(file("lab-config.bin", "redmi-lab-config.bin"), { true }, { true }, {})
        assertEquals(LabConfig(0x6AF62910, blockDownloads = false, adblock = true), store.current)
    }

    @Test fun aDownloadBlockOf0070IsRead() {
        val store = LabConfigStore(file("lab-config.bin", "lab-downloads-blocked.bin"), { true }, { true }, {})
        assertEquals(LabConfig(1762960200, blockDownloads = true, adblock = false), store.current)
        assertEquals(
            LabConfig(1762960300, blockDownloads = false, adblock = false),
            LabConfigStore(file("lab-config.bin", "lab-all-off.bin"), { true }, { true }, {}).current,
        )
    }

    @Test fun aLabConfigIsKeptInTheNewFormatAfterAChange() {
        val file = file("lab-config.bin", "lab-downloads-blocked.bin")
        val store = LabConfigStore(file, { true }, { true }, {})
        assertTrue(store.handle(LabConfig(1762960999, blockDownloads = true, adblock = true).encode()).first)
        assertEquals("midview-lab-config 2\nsavedAt=1762960999\nblockDownloads=1\nadblock=1\n", file.readText())
        assertEquals(LabConfig(1762960999, true, true), LabConfigStore(file, { true }, { true }, {}).current)
    }

    @Test fun theRedmisSiteBlockAndOneWithSitesAreRead() {
        assertEquals(SiteBlockState.EMPTY, SiteBlockStore(file("site-block.txt", "redmi-site-block.txt"), apply = { true }).current)
        assertEquals(
            SiteBlockState(setOf("www.g1.com", "youtube.com"), true),
            SiteBlockStore(file("site-block.txt", "site-block-sites.txt"), apply = { true }).current,
        )
    }

    @Test fun aLessonOf0070IsRead() {
        val store = ClassLessonStore(file("class-lesson.bin", "class-lesson-on.bin"), today = { LocalDate.of(2025, 11, 12) })
        val lesson = store.current
        assertEquals(1762960400, lesson.savedAt)
        assertEquals(1762960000, lesson.startedAt)
        assertEquals("3º A", lesson.turma)
        assertEquals(LessonId(ByteArray(16) { (it + 1).toByte() }), lesson.lessonId)
        assertTrue(lesson.isOn)
    }

    @Test fun aLessonIsKeptInTheNewFormat() {
        val lesson = ClassLessonStore.decodeStored(golden("class-lesson-on.bin"))!!
        val stored = ClassLessonStore.encodeStored(lesson)
        assertTrue(String(stored).startsWith("midview-class-lesson 2\n"))
        assertEquals(lesson, ClassLessonStore.decodeStored(stored))
        // Ended, with a turma that has a line break (never checked when ended).
        val ended = lesson.copy(turma = "A\nB", ended = true)
        assertEquals(ended, ClassLessonStore.decodeStored(ClassLessonStore.encodeStored(ended)))
    }

    @Test fun theRedmisOptimizationIsRead() {
        val state = OptimizeStore(file("optimize.txt", "redmi-optimize.txt")).current
        assertEquals(Instant.parse("2026-11-12T20:58:03.392254Z"), state.report.optimizedAt)
        assertEquals(Instant.parse("2026-11-12T21:01:10.944603Z"), state.lastDnsAttempt)
        assertEquals(15, state.report.hidden.size)
        assertEquals(PhoneDnsStates.ON, state.report.dns)
        assertEquals(state, OptimizeStore.decode(OptimizeStore.encode(state)))
    }

    // ---- tolerance of the new format

    @Test fun aNewerVersionIsReadForWhatThisOneKnows() {
        val bytes = "midview-site-block 3\ninternet=1\nsite=youtube.com\nschedule=08:00-12:00\nsite=g1.com\n".toByteArray()
        assertEquals(SiteBlockState(setOf("youtube.com", "g1.com"), true), SiteBlockState.decode(bytes))
    }

    @Test fun anAppNameThatIsNotValidIsLeftOutAlone() {
        val dropped = mutableListOf<String>()
        val bytes = "midview-app-block 2\nsavedAt=5\nmode=block-list\napp=com.whatsapp\napp=not a package\napp=com.whatsapp\n".toByteArray()
        assertEquals(PhoneAppPolicy(5, AppBlockMode.BLOCK_LIST, listOf("com.whatsapp")), AppBlockStore.decodeStored(bytes) { dropped += it })
        assertTrue("not a package" in dropped)
    }

    // A mode this version does not know still blocks the listed apps.
    @Test fun anUnknownModeBlocksTheListedApps() {
        val bytes = "midview-app-block 3\nsavedAt=5\nmode=schedule\napp=com.whatsapp\n".toByteArray()
        assertEquals(PhoneAppPolicy(5, AppBlockMode.BLOCK_LIST, listOf("com.whatsapp")), AppBlockStore.decodeStored(bytes))
    }

    @Test fun aLabFunctionLeftOutIsTheDefault() {
        val bytes = "midview-lab-config 2\nsavedAt=9\nblockDownloads=1\n".toByteArray()
        assertEquals(LabConfig(9, blockDownloads = true, adblock = true), LabConfigStore.decodeStored(bytes))
    }

    @Test fun anotherKindOrVersion1IsNotThisFormat() {
        assertNull(StoredText.decode("site-block", "midview-app-block 2\n".toByteArray()))
        assertNull(StoredText.decode("site-block", "midview-site-block 1\n".toByteArray()))
        assertNull(StoredText.decode("site-block", "internet=0\n".toByteArray()))
    }

    @Test fun valuesKeepLineBreaksAndPercents() {
        val bytes = StoredText.encode("x", listOf("a" to "50%\n%0A\r!"))
        assertFalse(String(bytes).removePrefix("midview-x 2\n").dropLast(1).contains('\n'))
        assertEquals("50%\n%0A\r!", StoredText.decode("x", bytes)!!.first("a"))
    }
}
