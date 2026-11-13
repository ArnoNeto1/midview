package io.github.arnoneto1.midview.agent.ui

import io.github.arnoneto1.midview.agent.policy.AdminUnlock

// What "Tirar o Midview deste celular" (LeaveActivity) says, kept apart so
// it can be tested.
object LeaveTexts {
    const val TITLE = "Tirar o Midview deste celular"

    // The button in "Ajustes da escola".
    const val OPEN = "Tirar o Midview deste celular..."

    // "Ajustes da escola" without the admin password: nothing to offer.
    const val NEEDS_PASSWORD =
        "Para tirar o Midview deste celular aqui, o professor precisa definir a senha de administrador dos celulares " +
            "no Midview (Telas, Liberar Wi-Fi e modo avião). O professor também pode tirá-lo pelo Midview " +
            "(Telas, Tirar o Midview dos celulares e tablets)."

    const val ASK = "Só quem sabe a senha de administrador dos celulares pode tirar o Midview deste celular. Digite a senha."

    const val CHECKING = "Conferindo a senha..."

    const val CHOOSE = "Escolha como tirar o Midview deste celular:"

    const val KEEP_BUTTON = "Desfazer e tirar o Midview"

    const val KEEP_TEXT =
        "Mantém as fotos e os arquivos do aluno. Tira todas as travas e ajustes da escola (Wi-Fi, apps e sites " +
            "bloqueados, PIN, papel de parede), devolve os apps do sistema que foram tirados e desinstala o Midview. " +
            "No fim o celular pede para confirmar a desinstalação."

    const val WIPE_BUTTON = "Restaurar de fábrica"

    const val WIPE_TEXT =
        "Apaga tudo: fotos, arquivos, contas e apps. O celular volta como saiu da loja e pede para ser configurado de novo."

    const val CONFIRM_KEEP =
        "Tirar o Midview deste celular? As travas da escola saem e o Midview é desinstalado. As fotos e os arquivos ficam."

    const val CONFIRM_WIPE = "Apagar tudo deste celular? Fotos, arquivos, contas e apps somem e não dá para desfazer."

    const val CONFIRM_KEEP_YES = "Tirar o Midview"
    const val CONFIRM_WIPE_YES = "Apagar tudo"
    const val CANCEL = "Cancelar"

    const val WORKING = "Tirando as travas da escola... Pode levar um minuto."

    const val WIPING = "Apagando o celular. Ele vai reiniciar sozinho."

    const val WIPE_FAILED = "O Android não apagou o celular."

    // The password's answer, as a line under its box (null: the screen goes on).
    fun after(attempt: AdminUnlock.Attempt): String? = when (attempt) {
        is AdminUnlock.Attempt.Wrong -> SchoolSettingsTexts.wrong(attempt.triesLeft)
        is AdminUnlock.Attempt.LockedOut -> SchoolSettingsTexts.lockedOut(SchoolSettingsTexts.clock(attempt.untilMillis))
        AdminUnlock.Attempt.NoPassword -> NEEDS_PASSWORD
        else -> null
    }
}
