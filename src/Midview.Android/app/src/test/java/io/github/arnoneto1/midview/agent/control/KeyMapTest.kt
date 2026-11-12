package io.github.arnoneto1.midview.agent.control

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class KeyMapTest {
    @Test fun androidButtons() {
        assertEquals(TouchStep.Global(GlobalKey.BACK), KeyMap.of(0x1B)) // Esc
        assertEquals(TouchStep.Global(GlobalKey.BACK), KeyMap.of(0xA6)) // the viewer's "Voltar"
        assertEquals(TouchStep.Global(GlobalKey.HOME), KeyMap.of(0x24)) // Home
        assertEquals(TouchStep.Global(GlobalKey.HOME), KeyMap.of(0x5B)) // Windows
        assertEquals(TouchStep.Global(GlobalKey.HOME), KeyMap.of(0x5C))
        assertEquals(TouchStep.Global(GlobalKey.HOME), KeyMap.of(0xAC)) // the viewer's "Início"
        assertEquals(TouchStep.Global(GlobalKey.RECENTS), KeyMap.of(0x5D)) // the viewer's "Recentes"
    }

    @Test fun editingKeys() {
        assertEquals(TouchStep.Edit(EditKey.BACKSPACE), KeyMap.of(0x08))
        assertEquals(TouchStep.Edit(EditKey.ENTER), KeyMap.of(0x0D))
        assertEquals(TouchStep.Edit(EditKey.DELETE), KeyMap.of(0x2E))
        assertEquals(TouchStep.Edit(EditKey.LEFT), KeyMap.of(0x25))
        assertEquals(TouchStep.Edit(EditKey.RIGHT), KeyMap.of(0x27))
    }

    @Test fun keysThatTypeOrMeanNothingHereAreIgnored() {
        for (key in listOf('A'.code, '1'.code, 0x20, 0x10, 0x11, 0x12, 0x09, 0x70, 0xBE)) {
            assertNull("key $key", KeyMap.of(key))
        }
    }
}
