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

    // Android 10 lists the incoming pending file under its own name: it is
    // not taken by itself, so the first "notas.pdf" stays "notas.pdf".
    @Test
    fun onAndroid10TheIncomingFileDoesNotTakeItsOwnName() {
        val taken = FileNames.taken(listOf("Notas.pdf", "outro.txt"), incoming = "notas.pdf", sdk = 29)
        assertEquals(setOf("outro.txt"), taken)
        assertEquals("notas.pdf", FileNames.unique("notas.pdf") { it in taken })
    }

    // A file already there: Android 10 names the pending one "notas (1).pdf",
    // and the new file still gets a name of its own.
    @Test
    fun onAndroid10AnOlderFileStillCounts() {
        val taken = FileNames.taken(listOf("notas.pdf", "notas (1).pdf"), incoming = "notas (1).pdf", sdk = 29)
        assertEquals(setOf("notas.pdf"), taken)
        assertEquals("notas (2).pdf", FileNames.unique("notas.pdf") { it in taken })
    }

    // Android 11 hides the pending file (".pending-..."): its name is never dropped.
    @Test
    fun fromAndroid11EveryListedNameCounts() {
        assertEquals(setOf("notas.pdf"), FileNames.taken(listOf("notas.pdf"), incoming = "notas.pdf", sdk = 30))
        assertEquals(setOf("notas.pdf"), FileNames.taken(listOf("Notas.pdf"), incoming = null, sdk = 29))
    }
}
