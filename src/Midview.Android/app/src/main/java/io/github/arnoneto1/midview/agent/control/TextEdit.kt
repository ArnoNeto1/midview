package io.github.arnoneto1.midview.agent.control

// A text field's text after the teacher's key, and where its cursor goes.
data class EditedText(val text: String, val cursor: Int)

// The teacher's typing in the phone's focused text field, on its text and
// selection as accessibility reads them (start and end -1 when the field
// tells none: the cursor is then at the end). Plain string work.
object TextEdit {
    // The typed text in place of the selection, or at the cursor.
    fun insert(text: String, start: Int, end: Int, typed: String): EditedText {
        val (from, to) = selection(text, start, end)
        return EditedText(text.substring(0, from) + typed + text.substring(to), from + typed.length)
    }

    // The selection, or the character before the cursor (both halves of an
    // emoji); null when there is nothing to erase.
    fun backspace(text: String, start: Int, end: Int): EditedText? {
        val (from, to) = selection(text, start, end)
        if (from != to) return EditedText(text.substring(0, from) + text.substring(to), from)
        if (from == 0) return null
        val cut = if (from >= 2 && text[from - 1].isLowSurrogate() && text[from - 2].isHighSurrogate()) from - 2 else from - 1
        return EditedText(text.substring(0, cut) + text.substring(from), cut)
    }

    // The selection, or the character after the cursor.
    fun delete(text: String, start: Int, end: Int): EditedText? {
        val (from, to) = selection(text, start, end)
        if (from != to) return EditedText(text.substring(0, from) + text.substring(to), from)
        if (from == text.length) return null
        val cut = if (from + 1 < text.length && text[from].isHighSurrogate() && text[from + 1].isLowSurrogate()) from + 2 else from + 1
        return EditedText(text.substring(0, from) + text.substring(cut), from)
    }

    // Where the cursor goes for the arrow keys: a selection collapses to its
    // side, otherwise one character over.
    fun move(text: String, start: Int, end: Int, right: Boolean): Int {
        val (from, to) = selection(text, start, end)
        if (from != to) return if (right) to else from
        return if (right) {
            if (from + 1 < text.length && text[from].isHighSurrogate()) minOf(text.length, from + 2) else minOf(text.length, from + 1)
        } else {
            if (from >= 2 && text[from - 1].isLowSurrogate()) from - 2 else maxOf(0, from - 1)
        }
    }

    private fun selection(text: String, start: Int, end: Int): Pair<Int, Int> {
        if (start < 0 || end < 0) return text.length to text.length
        val a = start.coerceIn(0, text.length)
        val b = end.coerceIn(0, text.length)
        return minOf(a, b) to maxOf(a, b)
    }
}
