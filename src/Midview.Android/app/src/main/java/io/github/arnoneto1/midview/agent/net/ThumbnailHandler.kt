package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.ThumbnailRequest
import java.io.EOFException

// The thumbnail port (5901), as a Windows Session serves the teacher's grid:
// a trusted teacher keeps one connection open and asks every few seconds;
// each RequestThumbnail gets one ThumbnailFrame, a JPEG that fits the box
// asked for (render). Only teachers this phone trusts; anything other than a
// thumbnail request ends the connection (a phone has no screen to control
// yet).
class ThumbnailHandler(
    private val trusted: TrustedMasters,
    private val render: (width: Int, height: Int) -> ByteArray,
) {
    fun handle(channel: Channel) {
        if (!trusted.isTrusted(channel.remoteThumbprint)) {
            return
        }

        channel.setTimeout(IDLE_TIMEOUT_MILLIS)
        while (true) {
            val message = try {
                channel.receive(MAX_REQUEST_LENGTH)
            } catch (e: EOFException) {
                return
            }
            if (message.type != MessageType.REQUEST_THUMBNAIL) {
                return
            }
            val (width, height) = ThumbnailRequest.decode(message.payload)
            channel.send(Message(MessageType.THUMBNAIL_FRAME, render(width, height)))
        }
    }

    private companion object {
        // A request is 4 bytes; nothing on this port needs more.
        const val MAX_REQUEST_LENGTH = 1024

        // The Master asks every 3 s; a Session gives up after 15 s.
        const val IDLE_TIMEOUT_MILLIS = 15_000
    }
}
