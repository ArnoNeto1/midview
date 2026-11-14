package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.control.ControlTarget
import io.github.arnoneto1.midview.agent.control.RemoteControl
import io.github.arnoneto1.midview.agent.control.Size
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.FrameAck
import io.github.arnoneto1.midview.agent.protocol.InputEvent
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.ScreenFrame
import io.github.arnoneto1.midview.agent.protocol.ScreenRegion
import io.github.arnoneto1.midview.agent.screen.CaptureCadence
import io.github.arnoneto1.midview.agent.screen.Frame
import io.github.arnoneto1.midview.agent.screen.FrameCredit
import io.github.arnoneto1.midview.agent.screen.FrameSource
import io.github.arnoneto1.midview.agent.screen.Rect
import io.github.arnoneto1.midview.agent.screen.TileDiff
import java.io.IOException

// One region of a picture as a JPEG (Android's Bitmap; a test's fake).
fun interface RegionEncoder {
    fun jpeg(frame: Frame, rect: Rect, quality: Int): ByteArray
}

// The teacher's big window ("Ver a tela") on the screen port, after its
// first message, ScreenStream: as the Windows Session's StreamScreenFrames,
// ScreenFrame updates (the whole screen first and whenever its size changes,
// as when the phone turns; then only what changed), never more than
// FrameCredit's two on their way unacknowledged, at CaptureCadence's pace.
// While there is no picture (the student has not allowed it, or the capture
// stopped) the stream stays open and nothing is sent. The teacher's window
// sends a Heartbeat every second and a FrameAck per update; the connection
// ending or silenceMillis without a word end the stream.
// "Controlar", as on a PC's Session: StartControl starts control (RemoteControl
// over control), answered here (a PC answers nothing) with one CommandAck
// carrying a CommandResult: 1, or 0 and why not (the accessibility service
// off; no control at all here: CommandResult.NOT_ON_PHONE); InputEvent then
// carries the teacher's mouse and keys, in pixels of the last picture sent,
// and StopControl goes back to only watching (the window sends it as it
// closes, too). A malformed InputEvent ends the connection, as on a PC;
// FillAdminPassword (the PC helper's) does too. The window's clipboard
// (ClipboardText, sent whenever the teacher copies something) is read and
// dropped.
class ScreenStreamHandler(
    private val channel: Channel,
    private val source: FrameSource,
    private val credit: FrameCredit,
    private val cadence: CaptureCadence,
    private val encoder: RegionEncoder,
    private val silenceMillis: Int = SILENCE_MILLIS,
    private val ackTimeoutMillis: Long = ACK_TIMEOUT_MILLIS,
    control: ControlTarget? = null,
) {
    @Volatile private var running = true

    // The size of the picture the teacher has, which the teacher's mouse is in.
    @Volatile private var sentSize: Size? = null

    private val control = control?.let { RemoteControl(it, frameSize = { sentSize }, log = { text -> System.err.println("Midview control: $text") }) }

    // The sender's frames and the receiver's answers to StartControl go out
    // one whole message at a time.
    private val sending = Any()

    // Returns when the stream has ended, its connection closed.
    fun run() {
        channel.setTimeout(silenceMillis)
        // This thread only receives and the sender only sends: one reader
        // and one writer, which a TLS socket takes without a lock.
        val sender = Thread({ send() }, "Midview screen stream").apply { isDaemon = true }
        sender.start()
        try {
            readUntilTheEnd()
        } finally {
            running = false
            control?.stopAndWait()
            sender.interrupt()
            // A send in progress gives up once the connection is closed.
            channel.close()
            sender.join(JOIN_MILLIS)
        }
    }

    private fun readUntilTheEnd() {
        while (true) {
            val message = try {
                channel.receive(MAX_MESSAGE_LENGTH)
            } catch (e: IOException) {
                // Gone, silent for too long, or nonsense.
                return
            }
            when (message.type) {
                MessageType.HEARTBEAT -> Unit
                MessageType.FRAME_ACK -> FrameAck.decode(message.payload)?.let { credit.ack(it) }
                MessageType.START_CONTROL -> {
                    val refusal = if (control == null) CommandResult.NOT_ON_PHONE else control.start()
                    val answer = Message(MessageType.COMMAND_ACK, CommandResult.encode(refusal == null, refusal.orEmpty()))
                    try {
                        synchronized(sending) { channel.send(answer) }
                    } catch (e: IOException) {
                        return
                    }
                }
                // Without waiting for the gesture under way: the acks keep being read.
                MessageType.STOP_CONTROL -> control?.stop()
                MessageType.LOCK_STUDENT_TOUCH -> control?.lockTouch(message.payload.contentEquals(byteArrayOf(1)))
                MessageType.INPUT_EVENT -> if (control?.controlling == true) {
                    // As the Session: a payload that is no InputEvent ends the connection.
                    val event = InputEvent.decode(message.payload) ?: return
                    control.input(event)
                    cadence.activity(System.nanoTime() / 1_000_000)
                }
                MessageType.FILL_ADMIN_PASSWORD -> return
                // Anything else is ignored, as the Session does.
                else -> Unit
            }
        }
    }

    private fun send() {
        val diff = TileDiff()
        var previous: Frame? = null
        try {
            while (running && credit.awaitRoom(ackTimeoutMillis)) {
                val frame = source.awaitNew(previous?.seq ?: 0, NEW_PICTURE_WAIT_MILLIS)
                if (frame == null) {
                    // Nothing new on the screen; with no capture at all, look
                    // again in a little while.
                    if (source.latest() == null) Thread.sleep(RETRY_MILLIS)
                    continue
                }

                val started = System.nanoTime()
                val regions = try {
                    diff.merge(
                        diff.changed(
                            previous?.pixels, previous?.width ?: 0, previous?.height ?: 0,
                            frame.pixels, frame.width, frame.height,
                        ),
                    ).map { ScreenRegion(it.x, it.y, it.width, it.height, encoder.jpeg(frame, it, JPEG_QUALITY)) }
                } catch (e: RuntimeException) {
                    // The picture could not be made: the next one goes whole.
                    previous = null
                    Thread.sleep(RETRY_MILLIS)
                    continue
                }
                previous = frame

                val messages = if (regions.isEmpty()) {
                    emptyList()
                } else {
                    val groups = ScreenFrame.split(regions)
                    val firstId = credit.nextId()
                    credit.sent(firstId + groups.size - 1)
                    groups.mapIndexed { i, group ->
                        Message(MessageType.SCREEN_FRAME, ScreenFrame.encode(firstId + i, frame.width, frame.height, group))
                    }
                }
                // The work is the capture, compare and encode, as the
                // Session counts it: a slow network is not the phone's work.
                val workMillis = (System.nanoTime() - started) / 1_000_000
                if (messages.isNotEmpty()) sentSize = Size(frame.width, frame.height)
                synchronized(sending) { messages.forEach { channel.send(it) } }

                val rest = cadence.restAfter(workMillis, regions.isNotEmpty(), System.nanoTime() / 1_000_000)
                if (rest > 0) Thread.sleep(rest)
            }
        } catch (e: InterruptedException) {
            // The stream is ending.
        } catch (e: IOException) {
            // A send failed: the connection is dead, and closing it ends the
            // receive above.
            channel.close()
        } catch (e: Throwable) {
            // Anything else (out of memory, say) ends this stream only, never
            // the agent: an exception left on this thread would end the app.
            System.err.println("Midview screen stream stopped: $e")
            channel.close()
        }
    }

    companion object {
        // The Session's ConnectionTimeoutMs; the window sends a Heartbeat every second.
        const val SILENCE_MILLIS = 15_000

        // FrameCredit.cs: no room for 1 s drops the oldest update.
        const val ACK_TIMEOUT_MILLIS = 1_000L

        // As a PC's regions.
        const val JPEG_QUALITY = 90

        // The Session's CaptureRetryDelayMs.
        const val RETRY_MILLIS = 500L

        // How long one wait for a new picture lasts before looking again.
        private const val NEW_PICTURE_WAIT_MILLIS = 500L

        // Any message, as the Session: a ClipboardText can be long and is
        // only dropped; MessageFramer.MAX still holds.
        private const val MAX_MESSAGE_LENGTH = MessageFramer.MAX

        private const val JOIN_MILLIS = 2_000L
    }
}
