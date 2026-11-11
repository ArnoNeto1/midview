package io.github.arnoneto1.midview.agent.status

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class StatusImageTest {
    // The Redmi 10C: 720 x 1650.
    private val screenWidth = 720
    private val screenHeight = 1650

    @Test fun statusImageFitsTheRequestedBox() {
        val (width, height) = StatusImage.size(320, 180, screenWidth, screenHeight)
        assertTrue(width <= 320 && height <= 180)
        assertTrue("portrait", height > width)
        assertEquals(180, height)
        assertEquals(79, width)
    }

    @Test fun aStandingBoxIsFilledByItsNarrowSide() {
        // What the Master asks for a phone tile (9:19.5).
        assertEquals(85 to 195, StatusImage.size(90, 195, screenWidth, screenHeight))
        assertEquals(85 to 195, StatusImage.size(85, 400, screenWidth, screenHeight))
    }

    @Test fun aSidewaysScreenIsStillShownStanding() {
        assertEquals(StatusImage.size(320, 180, screenWidth, screenHeight), StatusImage.size(320, 180, screenHeight, screenWidth))
    }

    @Test fun anUnknownScreenIsTheTilesShape() {
        val (width, height) = StatusImage.size(960, 540, 0, 0)
        assertEquals(540, height)
        assertEquals(249, width) // 540 * 9 / 19.5
    }

    @Test fun neverEmpty() {
        assertEquals(1 to 1, StatusImage.size(1, 1, screenWidth, screenHeight))
    }

    @Test fun textIsWrappedToTheWidth() {
        val lines = StatusImage.wrap("Online. A tela aparece aqui depois do plano 2.", 12f) { it.length.toFloat() }
        assertEquals(listOf("Online. A", "tela aparece", "aqui depois", "do plano 2."), lines)
    }

    @Test fun aWordLongerThanTheWidthStaysWhole() {
        assertEquals(listOf("a", "paralelepípedo", "b"), StatusImage.wrap("a paralelepípedo b", 5f) { it.length.toFloat() })
    }
}
