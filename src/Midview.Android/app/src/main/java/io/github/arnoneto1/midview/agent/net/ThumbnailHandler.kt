package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.ThumbnailRequest
import java.io.EOFException

// The screen port (5901), as a Windows Session's: only teachers this phone
// trusts, and the first message says what kind of connection it is
// (SessionServer.HandleClient).
// - RequestThumbnail: the teacher's grid keeps one connection open and asks
//   every few seconds; each request gets one ThumbnailFrame, a JPEG that
//   fits the box asked for (render).
// - ScreenStream: the big window, served by stream (ScreenStreamHandler).
// - LockScreen: not on a phone yet; the teacher reads why.
// Anything else (StartControl and the other control messages among them)
// ends the connection: a phone takes no control. A trusted teacher
// connecting at all is teacherConnected: the moment the student is asked to
// let the screen be seen.
class ThumbnailHandler(
    private val trusted: TrustedMasters,
    private val render: (width: Int, height: Int) -> ByteArray,
    private val teacherConnected: () -> Unit = {},
    private val stream: (Channel) -> Unit = {},
) {
    fun handle(channel: Channel) {
        if (!trusted.isTrusted(channel.remoteThumbprint)) {
            return
        }
        teacherConnected()

        channel.setTimeout(FIRST_MESSAGE_TIMEOUT_MILLIS)
        val first = try {
            channel.receive(MAX_REQUEST_LENGTH)
        } catch (e: EOFException) {
            return
        }
        when (first.type) {
            MessageType.REQUEST_THUMBNAIL -> thumbnails(channel, first)
            MessageType.SCREEN_STREAM -> stream(channel)
            MessageType.LOCK_SCREEN ->
                channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(false, NO_SCREEN_LOCK)))
            else -> return
        }
    }

    private fun thumbnails(channel: Channel, first: Message) {
        channel.setTimeout(IDLE_TIMEOUT_MILLIS)
        var message = first
        while (true) {
            if (message.type != MessageType.REQUEST_THUMBNAIL) {
                return
            }
            val (width, height) = ThumbnailRequest.decode(message.payload)
            channel.send(Message(MessageType.THUMBNAIL_FRAME, render(width, height)))
            message = try {
                channel.receive(MAX_REQUEST_LENGTH)
            } catch (e: EOFException) {
                return
            }
        }
    }

    companion object {
        // What the teacher reads when locking this phone's screen.
        const val NO_SCREEN_LOCK = "este celular não bloqueia a tela"

        // A request is 4 bytes; nothing that opens a connection here needs more.
        private const val MAX_REQUEST_LENGTH = 1024

        // As the Session: 10 s to say what the connection is for.
        private const val FIRST_MESSAGE_TIMEOUT_MILLIS = 10_000

        // The Master asks every 3 s; a Session gives up after 15 s.
        private const val IDLE_TIMEOUT_MILLIS = 15_000
    }
}
