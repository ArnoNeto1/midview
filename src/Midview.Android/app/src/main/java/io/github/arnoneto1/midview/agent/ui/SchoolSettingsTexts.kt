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
        "O Wi-Fi, o modo avião, a depuração USB e as contas deste celular estão travados pela escola. " +
            "Digite a senha de administrador para liberar por 10 minutos."

    const val NO_PASSWORD =
        "O Wi-Fi, o modo avião, a depuração USB e as contas deste celular estão travados pela escola. Para liberá-los aqui, " +
            "o professor precisa definir a senha de administrador dos celulares no Midview (Telas, Liberar Wi-Fi e modo avião)."

    const val CHECKING = "Conferindo a senha..."

    // The button under the password box.
    const val UNLOCK = "Continuar"

    // After the right password, above the choices.
    const val CHOOSE = "Senha certa. O que liberar por 10 minutos?"

    // Locks again all that is free now.
    const val LOCK_NOW = "Travar agora"

    // The choices' buttons.
    fun choice(what: AdminUnlock.Freeable): String = when (what) {
        AdminUnlock.Freeable.NETWORK -> "Wi-Fi e modo avião"
        AdminUnlock.Freeable.DEBUGGING -> "Depuração USB"
        AdminUnlock.Freeable.ACCOUNTS -> "Contas do aparelho"
    }

    fun freeUntil(time: String) = "Wi-Fi e modo avião liberados até $time. Depois eles travam de novo sozinhos."

    // One line for each lock free now (null: locked), in the choices' order;
    // empty when none is.
    fun freed(network: String?, debugging: String?, accounts: String?): String = listOfNotNull(
        network?.let { "Wi-Fi e modo avião liberados até $it." },
        debugging?.let { "Depuração USB liberada e ligada até $it." },
        accounts?.let { "Contas do aparelho liberadas até $it." },
    ).let { lines ->
        if (lines.isEmpty()) "" else (lines + "Depois travam de novo sozinhos.").joinToString("\n")
    }

    fun lockedOut(time: String) = "Muitas senhas erradas. Tente de novo às $time."

    fun wrong(triesLeft: Int) =
        if (triesLeft == 1) "Senha errada. Resta 1 tentativa." else "Senha errada. Restam $triesLeft tentativas."

    // The line under the password box after an attempt (null when the
    // screen's state already says it all).
    fun after(attempt: AdminUnlock.Attempt): String? = when (attempt) {
        is AdminUnlock.Attempt.Wrong -> wrong(attempt.triesLeft)
        else -> null
    }

    // The line under the choices after one (null when the freed lines say
    // it): why Android, or the unlock's end, did not let it.
    fun afterFree(result: Pair<Boolean, String>): String? =
        if (result.first) null else result.second.replaceFirstChar { it.uppercase() }.let { if (it.endsWith(".")) it else "$it." }

    fun clock(millis: Long, zone: ZoneId = ZoneId.systemDefault()): String =
        DateTimeFormatter.ofPattern("HH:mm").withZone(zone).format(Instant.ofEpochMilli(millis))
}
