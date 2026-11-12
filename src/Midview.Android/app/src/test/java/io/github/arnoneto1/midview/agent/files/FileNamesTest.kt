package io.github.arnoneto1.midview.agent.files

import org.junit.Assert.assertEquals
import org.junit.Test

// The cases of tests/Midview.Protocol.Tests/FileNamesTests.cs: a phone saves
// a name the way a PC would.
class FileNamesTest {
    @Test
    fun onlyAPlainNameIsKept() {
        val cases = mapOf(
            "notas.pdf" to "notas.pdf",
            """..\..\Windows\System32\evil.exe""" to "evil.exe",
            "../../etc/evil.sh" to "evil.sh",
            """C:\Users\Public\x.txt""" to "x.txt",
            "arquivo.txt:escondido" to "arquivo.txt_escondido",
            "a<b>c|d?e*f\"g.txt" to "a_b_c_d_e_f_g.txt",
            "relatório final.docx. . " to "relatório final.docx",
            "CON" to "_CON",
            "nul.txt" to "_nul.txt",
            "COM1.log" to "_COM1.log",
            "" to "arquivo",
            "..." to "arquivo",
            "pasta\\" to "arquivo",
            "a\u0001b\nc.txt" to "a_b_c.txt",
        )
        for ((sent, saved) in cases) assertEquals(sent, saved, FileNames.safe(sent))
    }

    @Test
    fun aLongNameKeepsItsExtension() {
        val name = FileNames.safe("a".repeat(200) + ".pdf")
        assertEquals(FileNames.MAX_NAME_LENGTH, name.length)
        assertEquals(".pdf", name.takeLast(4))
        assertEquals(FileNames.MAX_NAME_LENGTH, FileNames.safe("b".repeat(200) + "." + "x".repeat(30)).length)
    }

    @Test
    fun aTakenNameGetsTheNextNumber() {
        val taken = setOf("notas.pdf", "notas (2).pdf")
        assertEquals("notas (3).pdf", FileNames.unique("notas.pdf") { it in taken })
        assertEquals("leia-me (2)", FileNames.unique("leia-me") { it == "leia-me" })
        assertEquals("novo.txt", FileNames.unique("novo.txt") { it in taken })
    }
}
