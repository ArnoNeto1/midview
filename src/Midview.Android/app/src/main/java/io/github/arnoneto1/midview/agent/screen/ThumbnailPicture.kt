package io.github.arnoneto1.midview.agent.screen

// What the teacher's tile gets for a RequestThumbnail: the screen itself,
// scaled into the box asked for, while the capture runs; otherwise the
// phone's status picture saying why not ("Aguardando o aluno permitir",
// "Não permitiu ver a tela"). status and jpeg do the drawing (Android's
// Bitmap); the choice is made here.
class ThumbnailPicture(
    private val consent: ScreenConsent,
    private val frames: FrameSource,
    private val status: (text: String, boxWidth: Int, boxHeight: Int) -> ByteArray,
    private val jpeg: (frame: Frame, width: Int, height: Int, quality: Int) -> ByteArray,
) {
    fun render(boxWidth: Int, boxHeight: Int): ByteArray {
        val state = consent.state
        val frame = if (state == ConsentState.Capturing) frames.latest() else null
        if (frame == null) {
            return status(ScreenConsent.statusText(state) ?: ScreenConsent.WAITING_TEXT, boxWidth, boxHeight)
        }
        val (width, height) = FrameScale.thumbnail(boxWidth, boxHeight, frame.width, frame.height)
        return jpeg(frame, width, height, JPEG_QUALITY)
    }

    companion object {
        // As a PC's thumbnail.
        const val JPEG_QUALITY = 70
    }
}
