package io.github.arnoneto1.midview.agent.protocol

// The message types the Android agent speaks, with the numbers of
// src/Midview.Protocol/MessageType.cs (one byte on the wire). Only the ones
// used here; add others from that file with the same number.
object MessageType {
    const val HEARTBEAT = 2
    const val START_CONTROL = 3
    const val STOP_CONTROL = 4
    const val INPUT_EVENT = 6

    // The teacher's "Bloquear sites" window, as on a PC: the internet as a
    // whole (no payload), or one site (what the teacher typed, UTF-8). Each
    // is acknowledged with a CommandAck of a single byte, 1 or 0.
    const val BLOCK_INTERNET = 7
    const val UNBLOCK_INTERNET = 8
    const val BLOCK_DOMAIN = 9
    const val UNBLOCK_DOMAIN = 10
    const val COMMAND_ACK = 11
    const val REQUEST_THUMBNAIL = 12
    const val THUMBNAIL_FRAME = 13

    // Sending a file in chunks (the update's APK too).
    const val FILE_CHUNK = 18

    // Updating the agent over the network, from a trusted teacher only (see
    // NetworkUpdateCodec): offer -> accepted, the file as FILE_CHUNKs, then
    // GET_UPDATE_STATUS -> UPDATE_STATUS.
    const val UPDATE_OFFER = 25
    const val UPDATE_ACCEPTED = 26
    const val GET_UPDATE_STATUS = 27
    const val UPDATE_STATUS = 28
    const val LOCK_SCREEN = 34

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

    // Screen streaming: a first ScreenStream message makes the agent answer
    // with ScreenFrame updates, and the viewer acknowledges each with FrameAck.
    const val SCREEN_STREAM = 48
    const val SCREEN_FRAME = 49
    const val FRAME_ACK = 50
    const val CLIPBOARD_TEXT = 60
    const val FILL_ADMIN_PASSWORD = 90

    // Master -> Android agent (port 28900): ask this device to show the
    // screen-capture consent dialog.
    const val REQUEST_SCREEN_CONSENT = 92

    // Master -> Android agent (port 28900): the phone-apps block. SET sends a
    // PhoneAppPolicy (CommandAck back), GET asks for the one in force (STATUS
    // carries it), GET_PHONE_APPS asks for the openable apps (PHONE_APPS).
    const val SET_PHONE_APP_BLOCK = 93
    const val GET_PHONE_APP_BLOCK = 94
    const val PHONE_APP_BLOCK_STATUS = 95
    const val GET_PHONE_APPS = 96
    const val PHONE_APPS = 97

    // Master -> Android agent (port 28900), trusted teacher only: "Otimizar"
    // the phone (no payload), answered with one PHONE_OPTIMIZE_REPORT
    // (a PhoneOptimizeReport). An older agent answers COMMAND_ACK.
    const val OPTIMIZE_PHONE = 98
    const val PHONE_OPTIMIZE_REPORT = 99

    // Master -> Android agent (port 28900), trusted teacher only: the
    // teacher's lock screen PIN (a PhoneScreenPassword payload: 4 digits, or
    // empty to remove it), which the agent then keeps on the phone so the
    // student can neither change nor remove it. Answered with a
    // CommandAck (CommandResult).
    const val SET_PHONE_SCREEN_PASSWORD = 100

    // Master -> Android agent (port 28900), trusted teacher only: erase the
    // phone ("Apagar e preparar de novo", no payload). Answered with a
    // COMMAND_ACK before the erasing starts. Number 101 as the C# WipePhone (100 is SetPhoneScreenPassword).
    const val WIPE_PHONE = 101
}
