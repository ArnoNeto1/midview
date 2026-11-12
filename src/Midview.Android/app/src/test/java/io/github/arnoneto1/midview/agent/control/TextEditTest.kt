package io.github.arnoneto1.midview.agent.control

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TextEditTest {
    @Test fun typingGoesAtTheCursorOrAtTheEnd() {
        assertEquals(EditedText("google.com", 10), TextEdit.insert("google.co", -1, -1, "m"))
        assertEquals(EditedText("abXc", 3), TextEdit.insert("abc", 2, 2, "X"))
        assertEquals(EditedText("12+30=", 6), TextEdit.insert("", 0, 0, "12+30="))
    }

    @Test fun typingReplacesTheSelectionWhicheverWayItWasMade() {
        assertEquals(EditedText("aXd", 2), TextEdit.insert("abcd", 1, 3, "X"))
        assertEquals(EditedText("aXd", 2), TextEdit.insert("abcd", 3, 1, "X"))
        // A selection past the end (a field that changed) is held to the text.
        assertEquals(EditedText("abX", 3), TextEdit.insert("ab", 5, 9, "X"))
    }

    @Test fun backspaceErasesBeforeTheCursorOrTheSelection() {
        assertEquals(EditedText("ab", 2), TextEdit.backspace("abc", -1, -1))
        assertEquals(EditedText("ac", 1), TextEdit.backspace("abc", 2, 2))
        assertEquals(EditedText("ad", 1), TextEdit.backspace("abcd", 1, 3))
        assertNull(TextEdit.backspace("abc", 0, 0))
        assertNull(TextEdit.backspace("", -1, -1))
    }

    @Test fun backspaceTakesAWholeEmoji() {
        assertEquals(EditedText("a", 1), TextEdit.backspace("a😀", -1, -1))
    }

    @Test fun deleteErasesAfterTheCursor() {
        assertEquals(EditedText("ac", 1), TextEdit.delete("abc", 1, 1))
        assertEquals(EditedText("a", 1), TextEdit.delete("a😀", 1, 1))
        assertNull(TextEdit.delete("abc", -1, -1))
        assertEquals(EditedText("ad", 1), TextEdit.delete("abcd", 1, 3))
    }

    @Test fun arrowsMoveTheCursor() {
        assertEquals(1, TextEdit.move("abc", 2, 2, right = false))
        assertEquals(3, TextEdit.move("abc", 2, 2, right = true))
        assertEquals(3, TextEdit.move("abc", -1, -1, right = true))
        assertEquals(0, TextEdit.move("abc", 0, 0, right = false))
        assertEquals(1, TextEdit.move("abcd", 1, 3, right = false))
        assertEquals(3, TextEdit.move("abcd", 1, 3, right = true))
        assertEquals(1, TextEdit.move("a😀", 3, 3, right = false))
        assertEquals(3, TextEdit.move("a😀", 1, 1, right = true))
    }
}
