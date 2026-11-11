package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Test

class ThumbnailPictureTest {
    private val consent = ScreenConsent(clock = { 0L }) {}
    private val frames = LatestFrame()
    private val picture = ThumbnailPicture(
        consent, frames,
        status = { text, width, height -> "status $text $width x $height".toByteArray() },
        jpeg = { frame, width, height, quality -> "frame ${frame.seq} $width x $height q$quality".toByteArray() },
    )

    private fun render(width: Int, height: Int) = String(picture.render(width, height))

    @Test fun thumbnailShowsStatusUntilCapturing() {
        assertEquals("status Aguardando o aluno permitir 320 x 180", render(320, 180))
        consent.ask()
        assertEquals("status Aguardando o aluno permitir 320 x 180", render(320, 180))
        consent.denied()
        assertEquals("status Não permitiu ver a tela 90 x 195", render(90, 195))

        // Allowed, but no picture yet.
        consent.ask()
        consent.granted()
        assertEquals("status Aguardando o aluno permitir 320 x 180", render(320, 180))

        // The Redmi 10C's screen, in the box asked for, JPEG 70.
        frames.publish(IntArray(720 * 1650), 720, 1650)
        assertEquals("frame 1 79 x 180 q70", render(320, 180))
        // Never enlarged.
        frames.publish(IntArray(72 * 165), 72, 165)
        assertEquals("frame 2 72 x 165 q70", render(960, 540))

        // An old picture is not shown once the capture stopped.
        consent.stopped()
        assertEquals("status Aguardando o aluno permitir 320 x 180", render(320, 180))
    }
}
