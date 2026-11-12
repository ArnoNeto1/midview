package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CaptureSourceTest {
    @Test fun accessibilityOnlyFromAndroid11() {
        assertEquals(CaptureSource.Accessibility, CaptureSource.choose(sdkInt = 30, enabled = true, connected = true))
        assertEquals(CaptureSource.Accessibility, CaptureSource.choose(sdkInt = 33, enabled = true, connected = true))
        // Android 10 has no takeScreenshot: "Iniciar agora", as before.
        assertEquals(CaptureSource.MediaProjection, CaptureSource.choose(sdkInt = 29, enabled = true, connected = true))
        assertEquals(CaptureSource.MediaProjection, CaptureSource.choose(sdkInt = 29, enabled = true, connected = false))
    }

    @Test fun withoutAccessibilityTheStudentIsAsked() {
        assertEquals(CaptureSource.MediaProjection, CaptureSource.choose(sdkInt = 33, enabled = false, connected = false))
    }

    // Turned on by cable but not connected yet (the phone or the app just
    // started): waiting for it, not asking the student.
    @Test fun enabledButNotConnectedYetIsStarting() {
        assertEquals(CaptureSource.AccessibilityStarting, CaptureSource.choose(sdkInt = 33, enabled = true, connected = false))
    }

    // Enabled but never connecting: the student is asked after a while
    // rather than never.
    @Test fun startingGivesUpAfterTheLimit() {
        val since = 10_000L
        val limit = CaptureSource.STARTING_LIMIT_MILLIS
        assertEquals(
            CaptureSource.AccessibilityStarting,
            CaptureSource.choose(sdkInt = 33, enabled = true, connected = false, startingSinceMillis = since, nowMillis = since),
        )
        assertEquals(
            CaptureSource.AccessibilityStarting,
            CaptureSource.choose(sdkInt = 33, enabled = true, connected = false, startingSinceMillis = since, nowMillis = since + limit - 1),
        )
        assertEquals(
            CaptureSource.MediaProjection,
            CaptureSource.choose(sdkInt = 33, enabled = true, connected = false, startingSinceMillis = since, nowMillis = since + limit),
        )
        // Connecting late still counts.
        assertEquals(
            CaptureSource.Accessibility,
            CaptureSource.choose(sdkInt = 33, enabled = true, connected = true, startingSinceMillis = since, nowMillis = since + 10 * limit),
        )
    }

    @Test fun theLimitIsAboutHalfAMinute() {
        assertEquals(30_000L, CaptureSource.STARTING_LIMIT_MILLIS)
    }

    // Connected is what counts, whatever the setting said a moment ago.
    @Test fun connectedWinsOverTheSetting() {
        assertEquals(CaptureSource.Accessibility, CaptureSource.choose(sdkInt = 33, enabled = false, connected = true))
    }

    private val service = "io.github.arnoneto1.midview.agent/io.github.arnoneto1.midview.agent.screen.AccessibilityCapture"

    @Test fun theSettingListsTheServiceFullOrShort() {
        assertTrue(CaptureSource.enabledInSettings(service, accessibilityOn = true))
        assertTrue(
            CaptureSource.enabledInSettings(
                "com.other/.Thing:io.github.arnoneto1.midview.agent/.screen.AccessibilityCapture",
                accessibilityOn = true,
            ),
        )
    }

    @Test fun notInTheSettingOrAccessibilityOff() {
        assertFalse(CaptureSource.enabledInSettings(null, accessibilityOn = true))
        assertFalse(CaptureSource.enabledInSettings("", accessibilityOn = true))
        assertFalse(CaptureSource.enabledInSettings("com.other/.Thing", accessibilityOn = true))
        assertFalse(CaptureSource.enabledInSettings("$service.Other", accessibilityOn = true))
        assertFalse(CaptureSource.enabledInSettings(service, accessibilityOn = false))
    }

    // The app's own record of since when the service is starting: the
    // limit counts from the first time it was seen, a check is scheduled
    // once, and the student is asked when it passes.
    @Test fun watchFallsBackToTheRequestAfterTheLimit() {
        var now = 5_000L
        val scheduled = mutableListOf<Long>()
        val watch = CaptureSourceWatch(sdkInt = 33, clock = { now }) { scheduled += it }
        var shown = 0
        val consent = ScreenConsent(clock = { now }, source = { watch.current(enabled = true, connected = false) }) { shown++ }

        consent.teacherConnected()
        consent.ask()
        assertEquals(0, shown)
        assertEquals(listOf(CaptureSource.STARTING_LIMIT_MILLIS), scheduled)

        now += CaptureSource.STARTING_LIMIT_MILLIS - 1
        consent.sourceChanged()
        assertEquals(0, shown)

        // The scheduled check.
        now += 1
        consent.sourceChanged()
        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(1, scheduled.size)
    }

    // Connected (or turned off) starts the count again.
    @Test fun watchStartsAgainAfterConnecting() {
        var now = 0L
        val scheduled = mutableListOf<Long>()
        val watch = CaptureSourceWatch(sdkInt = 33, clock = { now }) { scheduled += it }
        watch.current(enabled = true, connected = false)
        now += CaptureSource.STARTING_LIMIT_MILLIS * 2
        assertEquals(CaptureSource.Accessibility, watch.current(enabled = true, connected = true))
        assertEquals(CaptureSource.AccessibilityStarting, watch.current(enabled = true, connected = false))
        assertEquals(2, scheduled.size)

        // Android 10: nothing to wait for.
        val old = CaptureSourceWatch(sdkInt = 29, clock = { now }) { scheduled += it }
        assertEquals(CaptureSource.MediaProjection, old.current(enabled = true, connected = false))
        assertEquals(2, scheduled.size)
    }
}
