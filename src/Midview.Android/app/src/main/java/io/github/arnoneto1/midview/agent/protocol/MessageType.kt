package io.github.arnoneto1.midview.agent.protocol

// The message types the Android agent speaks, with the numbers of
// src/Midview.Protocol/MessageType.cs (one byte on the wire). Only the ones
// used here; add others from that file with the same number.
object MessageType {
    const val HEARTBEAT = 2
    const val COMMAND_ACK = 11
    const val REQUEST_THUMBNAIL = 12
    const val THUMBNAIL_FRAME = 13

    // Pairing a teacher with this device (see Pairing for the exchange).
    const val PAIR_REQUEST = 37
    const val PAIR_COMMIT = 38
    const val PAIR_NONCE = 39
    const val PAIR_REVEAL = 40
    const val PAIR_CONFIRM = 41
    const val PAIR_CANCEL = 42
    const val PAIR_DONE = 43
    const val PAIR_REFUSED = 44

    // From a trusted teacher only: let one more teacher pair for
    // Pairing.NEW_TEACHER_WINDOW_MILLIS ("Permitir novo professor").
    const val ALLOW_NEW_TEACHER = 45
}
