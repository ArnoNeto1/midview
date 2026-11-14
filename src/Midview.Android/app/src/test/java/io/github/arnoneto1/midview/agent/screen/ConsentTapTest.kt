package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ConsentTapTest {
    // Android 10's dialog on the school's Multilaser M10.
    private val dialog = listOf(
        "O Midview iniciará a captura de tudo que é exibido na tela.",
        "Não mostrar novamente",
        "Cancelar",
        "Iniciar agora",
    )

    @Test fun onlyAFewSecondsAfterTheAgentAsked() {
        assertTrue(ConsentTap.due(requestedAt = 1_000, now = 1_000))
        assertTrue(ConsentTap.due(requestedAt = 1_000, now = 6_000))
        assertFalse(ConsentTap.due(requestedAt = 1_000, now = 6_001))
        assertFalse(ConsentTap.due(requestedAt = null, now = 1_000))
        assertFalse(ConsentTap.due(requestedAt = 2_000, now = 1_000))
    }

    @Test fun theSystemsRequestForTheMidview() {
        assertTrue(ConsentTap.isOwnRequest("com.android.systemui", dialog))
    }

    // Another app's request to capture the screen is never answered.
    @Test fun notAnotherAppsRequest() {
        val other = listOf("O AZ Recorder iniciará a captura de tudo que é exibido na tela.", "Iniciar agora")
        assertFalse(ConsentTap.isOwnRequest("com.android.systemui", other))
    }

    // A window naming the Midview that is not the system's (a page, a message).
    @Test fun notAWindowOfAnotherApp() {
        assertFalse(ConsentTap.isOwnRequest("com.android.chrome", dialog))
        assertFalse(ConsentTap.isOwnRequest(null, dialog))
    }

    // Only the dialog's positive button, never a text that looks like it.
    @Test fun theStartButton() {
        assertTrue(ConsentTap.isStartButton("android:id/button1"))
        assertFalse(ConsentTap.isStartButton("android:id/button2"))
        assertFalse(ConsentTap.isStartButton(null))
        assertFalse(ConsentTap.isStartButton("com.other:id/button1"))
    }

    // The label as a word: an app called "Midview Pro" or "XMidview" is not us.
    @Test fun theLabelMustBeOursAsAWord() {
        assertTrue(ConsentTap.isOwnRequest("com.android.systemui", listOf("Midview iniciará a captura")))
        assertFalse(ConsentTap.isOwnRequest("com.android.systemui", listOf("O XMidview iniciará a captura")))
        assertFalse(ConsentTap.isOwnRequest("com.android.systemui", listOf("O Midviewer iniciará a captura")))
    }
}
