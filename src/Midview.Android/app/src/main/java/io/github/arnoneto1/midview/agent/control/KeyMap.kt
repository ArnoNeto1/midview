package io.github.arnoneto1.midview.agent.control

// The teacher's special keys (Windows virtual-key codes) on a phone. The
// characters themselves come as InputEvent.TEXT, not as keys: a key that
// types is ignored here. The keyboard's Home and End move the cursor in the
// text field, as on a PC; the Windows key is the phone's Home. The viewer's
// "Voltar", "Início" and "Recentes" buttons send the browser back, browser
// home and context-menu keys.
object KeyMap {
    const val VK_BACK = 0x08
    const val VK_RETURN = 0x0D
    const val VK_ESCAPE = 0x1B
    const val VK_END = 0x23
    const val VK_HOME = 0x24
    const val VK_LEFT = 0x25
    const val VK_RIGHT = 0x27
    const val VK_DELETE = 0x2E
    const val VK_LWIN = 0x5B
    const val VK_RWIN = 0x5C
    const val VK_APPS = 0x5D
    const val VK_BROWSER_BACK = 0xA6
    const val VK_BROWSER_HOME = 0xAC

    fun of(virtualKey: Int): TouchStep? = when (virtualKey) {
        VK_ESCAPE, VK_BROWSER_BACK -> TouchStep.Global(GlobalKey.BACK)
        VK_LWIN, VK_RWIN, VK_BROWSER_HOME -> TouchStep.Global(GlobalKey.HOME)
        VK_APPS -> TouchStep.Global(GlobalKey.RECENTS)
        VK_BACK -> TouchStep.Edit(EditKey.BACKSPACE)
        VK_DELETE -> TouchStep.Edit(EditKey.DELETE)
        VK_RETURN -> TouchStep.Edit(EditKey.ENTER)
        VK_LEFT -> TouchStep.Edit(EditKey.LEFT)
        VK_RIGHT -> TouchStep.Edit(EditKey.RIGHT)
        VK_HOME -> TouchStep.Edit(EditKey.START)
        VK_END -> TouchStep.Edit(EditKey.END)
        else -> null
    }
}
