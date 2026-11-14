package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// "Sair das contas do Chrome": Chrome's data erased by the Device Owner,
// Chrome's policies written again after, and each reason it could not be
// said to the teacher.
class ChromeDataClearTest {
    private var owner = true
    private var chrome = true
    private val cleared = mutableListOf<String>()
    private var reapplied = 0

    private fun clear(
        sdkInt: Int = 34,
        android: (String, (Boolean) -> Unit) -> Unit = { pkg, done ->
            cleared += pkg
            done(true)
        },
    ) = ChromeDataClear(
        isDeviceOwner = { owner },
        sdkInt = sdkInt,
        isChromeInstalled = { chrome },
        clear = android,
        reapplyPolicy = { reapplied++ },
        waitMillis = 200,
    )

    @Test fun chromesDataIsClearedAndItsPoliciesWrittenAgain() {
        assertEquals(true to ChromeDataClear.CLEARED, clear().handle(ByteArray(0)))
        assertEquals(listOf("com.android.chrome"), cleared)
        assertEquals(1, reapplied)
    }

    // Android answers on its own thread, a moment later.
    @Test fun theAnswerMayComeFromAnotherThread() {
        val later = clear(android = { _, done -> Thread { Thread.sleep(20); done(true) }.start() })
        assertEquals(true to ChromeDataClear.CLEARED, later.run())
    }

    @Test fun eachReasonItCouldNotIsSaid() {
        assertEquals(false to ChromeDataClear.NOT_CLEARED, clear(android = { _, done -> done(false) }).run())
        assertEquals(false to ChromeDataClear.NO_ANSWER, clear(android = { _, _ -> }).run())
        assertEquals(false to ChromeDataClear.REFUSED, clear(android = { _, _ -> throw SecurityException("no") }).run())
        assertEquals(false to ChromeDataClear.TOO_OLD, clear(sdkInt = 27).run())
        chrome = false
        assertEquals(false to ChromeDataClear.NO_CHROME, clear().run())
        owner = false
        assertEquals(false to ChromeDataClear.NOT_PREPARED, clear().run())
        assertTrue(cleared.isEmpty())
        assertEquals(0, reapplied)
    }

    @Test fun aPayloadIsInvalid() {
        assertEquals(false to ChromeDataClear.INVALID_REQUEST, clear().handle(byteArrayOf(1)))
        assertTrue(cleared.isEmpty())
    }
}
