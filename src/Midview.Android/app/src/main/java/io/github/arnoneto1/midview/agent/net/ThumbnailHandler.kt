package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
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
// - LockScreen: "Bloquear telas", held by lockScreen (LockScreenHandler).
// - The class folder's messages (ClassFolderHandler.MESSAGES): "Pasta da
//   turma", served by classFolder, as a PC's Session serves them. One of
//   them can carry a long list (ClearSetAside), so the first message of a
//   trusted teacher may be as long as any message.
// Anything else ends the connection: control (StartControl) comes only on a
// ScreenStream connection, as the big window's. A trusted teacher asking
// for the screen (a thumbnail or the big window) is teacherConnected: the
// moment the student is asked to let the screen be seen. A lock does not
// ask: the request would only wait under the padlock.
class ThumbnailHandler(
    private val trusted: TrustedMasters,
    private val render: (width: Int, height: Int) -> ByteArray,
    private val teacherConnected: () -> Unit = {},
    private val stream: (Channel) -> Unit = {},
    private val lockScreen: (Channel) -> Unit = {},
    private val classFolder: ((Channel, Message) -> Unit)? = null,
) {
    fun handle(channel: Channel) {
        if (!trusted.isTrusted(channel.remoteThumbprint)) {
            return
        }

        channel.setTimeout(FIRST_MESSAGE_TIMEOUT_MILLIS)
        val first = try {
            channel.receive(MessageFramer.MAX)
        } catch (e: EOFException) {
            return
        }
        if (first.type in ClassFolderHandler.MESSAGES) {
            // Without the class folder this phone hangs up, as one from before it did.
            classFolder?.let { channel.setTimeout(CLASS_FOLDER_TIMEOUT_MILLIS); it(channel, first) }
            return
        }
        when (first.type) {
            MessageType.REQUEST_THUMBNAIL -> {
                teacherConnected()
                thumbnails(channel, first)
            }
            MessageType.SCREEN_STREAM -> {
                teacherConnected()
                stream(channel)
            }
            MessageType.LOCK_SCREEN -> lockScreen(channel)
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
        // A thumbnail request is 4 bytes: the grid's connection never needs more.
        private const val MAX_REQUEST_LENGTH = 1024

        // As the Session: 10 s to say what the connection is for.
        private const val FIRST_MESSAGE_TIMEOUT_MILLIS = 10_000

        // As the Master's class folder client: per read, not for a whole transfer.
        private const val CLASS_FOLDER_TIMEOUT_MILLIS = 30_000

        // The Master asks every 3 s; a Session gives up after 15 s.
        private const val IDLE_TIMEOUT_MILLIS = 15_000
    }
}
