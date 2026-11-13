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

    @Test fun theStartButton() {
        assertTrue(ConsentTap.isStartButton("android:id/button1", "Iniciar agora"))
        assertTrue(ConsentTap.isStartButton("android:id/button1", null))
        assertTrue(ConsentTap.isStartButton(null, " Start now "))
        assertTrue(ConsentTap.isStartButton(null, "INICIAR AGORA"))
        assertFalse(ConsentTap.isStartButton("android:id/button2", "Cancelar"))
        assertFalse(ConsentTap.isStartButton(null, "Não mostrar novamente"))
        assertFalse(ConsentTap.isStartButton(null, null))
    }
}
