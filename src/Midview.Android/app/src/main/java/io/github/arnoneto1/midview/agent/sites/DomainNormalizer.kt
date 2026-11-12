package io.github.arnoneto1.midview.agent.sites

// What the teacher typed in "Bloquear sites", made a plain site name the same
// way as src/Midview.Agent/DomainNormalizer.cs, so a phone and a PC block the
// same sites: the scheme ("https://") and everything from the first '/', '?',
// '#' or ':' dropped, lower case, and only a valid DNS name kept (else null:
// a pasted address with a line break, a phrase, an IP address). The rules
// below are those of .NET's Uri.CheckHostName, as DomainNormalizerTest checks
// against what the Windows agent answers.
object DomainNormalizer {
    fun normalize(raw: String): String? {
        var value = trimWhiteSpace(raw)
        val scheme = value.indexOf("://")
        if (scheme >= 0) value = value.substring(scheme + 3)
        val cut = value.indexOfAny(charArrayOf('/', '?', '#', ':'))
        if (cut >= 0) value = value.substring(0, cut)
        value = lowerInvariant(value)
        return value.takeIf { isDnsName(it) }
    }

    // String.Trim of .NET: its white space (char.IsWhiteSpace), which is not
    // quite Kotlin's.
    private fun trimWhiteSpace(text: String): String {
        var start = 0
        var end = text.length
        while (start < end && isWhiteSpace(text[start].code)) start++
        while (end > start && isWhiteSpace(text[end - 1].code)) end--
        return text.substring(start, end)
    }

    private fun isWhiteSpace(code: Int): Boolean =
        code in 0x09..0x0D || code == 0x20 || code == 0x85 || code == 0xA0 || code == 0x1680 ||
            code in 0x2000..0x200A || code == 0x2028 || code == 0x2029 || code == 0x202F || code == 0x205F ||
            code == 0x3000

    // String.ToLowerInvariant of .NET, which leaves the dotted capital I
    // (U+0130) as it is.
    private fun lowerInvariant(text: String): String {
        val out = StringBuilder(text.length)
        var i = 0
        while (i < text.length) {
            val codePoint = text.codePointAt(i)
            out.appendCodePoint(if (codePoint == DOTTED_CAPITAL_I) codePoint else Character.toLowerCase(codePoint))
            i += Character.charCount(codePoint)
        }
        return out.toString()
    }

    private fun isDnsName(name: String): Boolean {
        if (name.isEmpty() || isIpv4(name)) return false
        var start = 0
        while (true) {
            var end = start
            while (end < name.length && name[end].code !in SEPARATORS) end++
            if (end == start || !isLabel(name, start, end)) return false
            // The end, or one dot after the last label ("example.org.").
            if (end >= name.length - 1) return true
            start = end + 1
        }
    }

    // A label starts with a letter or digit and holds letters, digits, '-' and
    // '_'; beyond ASCII, any character from U+00A0. A label of ASCII only has
    // at most 63 characters; one with others at most 59 (room for "xn--"),
    // those past U+00FF counting twice.
    private fun isLabel(name: String, start: Int, end: Int): Boolean {
        val first = name[start]
        if (!isAsciiLetterOrDigit(first) && first.code < 0xA0) return false
        var ascii = true
        var weight = 0
        for (i in start until end) {
            val c = name[i]
            weight += when {
                isAsciiLetterOrDigit(c) || c == '-' || c == '_' -> 1
                c.code >= 0x100 -> 2.also { ascii = false }
                c.code >= 0xA0 -> 1.also { ascii = false }
                else -> return false
            }
        }
        return if (ascii) end - start <= MAX_LABEL else weight <= MAX_LABEL - "xn--".length
    }

    private fun isAsciiLetterOrDigit(c: Char) = c in 'a'..'z' || c in 'A'..'Z' || c in '0'..'9'

    // An IPv4 address the way .NET reads one: one to four parts, each decimal,
    // octal (a leading 0) or hexadecimal ("0x"), the last filling the bytes left.
    private fun isIpv4(name: String): Boolean {
        val parts = name.split('.')
        if (parts.size > 4) return false
        for ((index, part) in parts.withIndex()) {
            val value = ipv4Part(part) ?: return false
            val bytes = if (index == parts.size - 1) 4 - index else 1
            if (value >= 1L shl (8 * bytes)) return false
        }
        return true
    }

    private fun ipv4Part(part: String): Long? {
        val (digits, radix) = when {
            part.startsWith("0x") -> part.substring(2) to 16
            part.length > 1 && part.startsWith("0") -> part.substring(1) to 8
            else -> part to 10
        }
        if (digits.isEmpty() || digits.length > 32) return null
        var value = 0L
        for (c in digits) {
            val digit = Character.digit(c, radix)
            if (digit < 0 || c.code > 0x7F) return null
            value = value * radix + digit
            if (value > 0xFFFFFFFFL) return value
        }
        return value
    }

    private const val MAX_LABEL = 63
    private const val DOTTED_CAPITAL_I = 0x130

    // '.' and the dots of Chinese and Japanese text, which .NET also takes
    // between labels.
    private val SEPARATORS = setOf(0x2E, 0x3002, 0xFF0E, 0xFF61)
}
