package io.github.arnoneto1.midview.agent.files

import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files

// Download/Midview read straight from the storage, as the PC's DeliveryFolder.
class FolderWalkTest {
    @get:Rule val temp = TemporaryFolder()

    private fun file(root: File, path: String, content: String = path): File =
        File(root, path).apply { parentFile!!.mkdirs(); writeText(content) }

    @Test
    fun filesFirstThenSubfoldersInNameOrder() {
        val root = temp.newFolder("Midview")
        file(root, "b.txt")
        file(root, "A.pdf")
        file(root, "sub/z.png")
        file(root, "Outra/x.docx")
        file(root, ".pending-1700000000-notas.pdf")
        file(root, ".trashed-1700000000-velho.pdf")
        file(root, ".oculta/segredo.txt")

        val listed = FolderWalk.list(root, 100)

        assertEquals(listOf("A.pdf", "b.txt", "Outra/x.docx", "sub/z.png"), listed.map { it.relativePath })
        assertEquals(5L, listed[0].size)
        assertEquals("sub/z.png", listed[3].open().use { String(it.readBytes()) })
    }

    // A link is never followed, to a file no more than to a folder.
    @Test
    fun linksAreNotCollected() {
        val root = temp.newFolder("Midview")
        val outside = file(temp.root, "fora/segredo.txt")
        file(root, "a.txt")
        val linked = try {
            Files.createSymbolicLink(File(root, "link.txt").toPath(), outside.toPath())
            Files.createSymbolicLink(File(root, "pasta").toPath(), outside.parentFile!!.toPath())
            true
        } catch (e: Exception) {
            false // Windows without the right to make links
        }
        assumeTrue(linked)
        assertEquals(listOf("a.txt"), FolderWalk.list(root, 100).map { it.relativePath })
    }

    @Test
    fun atMostOneFilePastTheLimit() {
        val root = temp.newFolder("Midview")
        for (i in 1..5) file(root, "f$i.txt")
        assertEquals(3, FolderWalk.list(root, 2).size)
    }

    @Test
    fun noDeeperThanTheProtocolAllows() {
        val root = temp.newFolder("Midview")
        val folders = (1..FileTransfer.MAX_DEPTH).map { "p$it" }
        file(root, folders.dropLast(1).joinToString("/") + "/ok.txt")
        file(root, folders.joinToString("/") + "/fundo.txt")
        assertEquals(listOf("p1/p2/p3/p4/p5/p6/p7/ok.txt"), FolderWalk.list(root, 100).map { it.relativePath })
    }

    @Test
    fun aFolderThatIsNotThereHasNothing() {
        assertTrue(FolderWalk.list(File(temp.root, "nada"), 10).isEmpty())
        assertEquals(0 to emptyList<String>(), FolderWalk.clear(File(temp.root, "nada")))
    }

    @Test
    fun clearingRemovesEverythingButAFileOnItsWayIn() {
        val root = temp.newFolder("Midview")
        file(root, "a.txt")
        file(root, "sub/b.txt")
        file(root, ".pending-1700000000-chegando.pdf")

        val (removed, problems) = FolderWalk.clear(root)

        assertEquals(2, removed)
        assertTrue(problems.isEmpty())
        assertEquals(listOf(".pending-1700000000-chegando.pdf"), root.list()!!.toList())
        assertFalse(File(root, "sub").exists())
    }

    // The class folders are other folders, as on a PC: neither collected nor emptied.
    @Test
    fun theClassFoldersAreLeftAlone() {
        val root = temp.newFolder("Midview")
        file(root, "entrega.txt")
        file(root, "Pasta da turma – 3º A/trabalho.docx")
        file(root, ".midview-turmas/0101/antigo.txt")
        file(root, "Outra/Pasta da turma – nada.txt")

        assertEquals(listOf("entrega.txt", "Outra/Pasta da turma – nada.txt"), FolderWalk.list(root, 100).map { it.relativePath })

        val (removed, problems) = FolderWalk.clear(root)
        assertEquals(2, removed)
        assertTrue(problems.isEmpty())
        assertTrue(File(root, "Pasta da turma – 3º A/trabalho.docx").exists())
        assertTrue(File(root, ".midview-turmas/0101/antigo.txt").exists())
    }
}
