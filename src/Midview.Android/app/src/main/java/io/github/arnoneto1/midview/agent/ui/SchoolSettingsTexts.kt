package io.github.arnoneto1.midview.agent.ui

import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

// What "Ajustes da escola" (SchoolSettingsActivity) says, kept apart so it
// can be tested.
object SchoolSettingsTexts {
    const val TITLE = "Ajustes da escola"

    const val ASK =
        "O Wi-Fi e o modo avião deste celular estão travados pela escola. Digite a senha de administrador para liberá-los por 10 minutos."

    const val NO_PASSWORD =
        "O Wi-Fi e o modo avião deste celular estão travados pela escola. Para liberá-los aqui, o professor precisa " +
            "definir a senha de administrador dos celulares no Midview (Telas, Liberar Wi-Fi e modo avião)."

    const val CHECKING = "Conferindo a senha..."

    fun freeUntil(time: String) = "Wi-Fi e modo avião liberados até $time. Depois eles travam de novo sozinhos."

    fun lockedOut(time: String) = "Muitas senhas erradas. Tente de novo às $time."

    fun wrong(triesLeft: Int) =
        if (triesLeft == 1) "Senha errada. Resta 1 tentativa." else "Senha errada. Restam $triesLeft tentativas."

    // The line under the password box after an attempt (null when the
    // screen's state already says it all).
    fun after(attempt: AdminUnlock.Attempt): String? = when (attempt) {
        is AdminUnlock.Attempt.Wrong -> wrong(attempt.triesLeft)
        is AdminUnlock.Attempt.Refused -> attempt.detail
        else -> null
    }

    fun clock(millis: Long, zone: ZoneId = ZoneId.systemDefault()): String =
        DateTimeFormatter.ofPattern("HH:mm").withZone(zone).format(Instant.ofEpochMilli(millis))
}
