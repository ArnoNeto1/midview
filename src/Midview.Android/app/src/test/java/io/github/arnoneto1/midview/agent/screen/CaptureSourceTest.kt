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
}
