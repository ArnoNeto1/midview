package io.github.arnoneto1.midview.agent.protocol

// "Desfazer e tirar o Midview", as src/Midview.Protocol/PhoneLeave.cs: the
// payload of REMOVE_MIDVIEW_FROM_PHONE is the word the teacher typed in the
// Master to confirm, "REMOVER" in UTF-8. Nothing else takes the Midview off
// the phone, so a stray or half-sent message never can.
object PhoneLeaveRequest {
    const val WORD = "REMOVER"

    fun encode(): ByteArray = WORD.toByteArray(Charsets.UTF_8)

    fun isConfirmed(payload: ByteArray): Boolean = payload.contentEquals(encode())
}
