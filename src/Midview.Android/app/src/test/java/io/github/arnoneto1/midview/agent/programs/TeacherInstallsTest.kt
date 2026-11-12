package io.github.arnoneto1.midview.agent.programs

import io.github.arnoneto1.midview.agent.net.FakeChannel
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.ProgramInstall
import io.github.arnoneto1.midview.agent.update.ApkFacts
import io.github.arnoneto1.midview.agent.update.ApkUpload
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.ByteBuffer

class TeacherInstallsTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val apkBytes = ByteArray(300_000) { (it * 13 + 1).toByte() }
    private val receiveFolder by lazy { File(folder.root, "teacher-app") }

    // What Android would read of the received file, and what is installed.
    private var readAs: ApkFacts? = facts(OLA, 1, "1.0")
    private val installedApps = mutableMapOf<String, PhoneAppFacts>()
    private val suspended = mutableSetOf<String>()
    private var free = 10L * 1024 * 1024 * 1024
    private var deviceOwner = true
    private val events = mutableListOf<String>()
    private var installFailure: String? = null
    private var uninstallFailure: String? = null
    private var allowOnly = false
    private var installedBytes: ByteArray? = null

    private val facts = object : PackageFacts {
        override fun read(apk: File): ApkFacts? = readAs
        override fun installed(pkg: String) = installedApps[pkg]
        override fun isSuspended(pkg: String) = pkg in suspended
        override fun freeBytes() = free
    }

    private val changer = object : PackageChanger {
        override fun install(apk: File, pkg: String): String? {
            events += "install $pkg"
            installedBytes = apk.readBytes()
            if (installFailure == null) installedApps[pkg] = PhoneAppFacts(readAs!!, "Olá", system = false, byMidview = true)
            return installFailure
        }

        override fun uninstall(pkg: String): String? {
            events += "uninstall $pkg"
            if (uninstallFailure == null) installedApps.remove(pkg)
            return uninstallFailure
        }
    }

    private val installs by lazy {
        TeacherInstalls(
            ApkUpload(receiveFolder, "app.apk"), facts, changer, SELF,
            isDeviceOwner = { deviceOwner },
            protectedApps = { setOf(SELF, LAUNCHER) },
            reapplyBlock = {
                events += "reapply"
                if (allowOnly) installedApps.keys.filter { it != SELF }.forEach { suspended += it }
            },
        )
    }

    private fun facts(pkg: String, code: Long, name: String, signer: String = "key") = ApkFacts(pkg, code, name, setOf(signer))

    private fun request(name: String = "ola.apk", size: Long = apkBytes.size.toLong(), arguments: String = ""): ByteArray {
        val args = arguments.toByteArray()
        val fileName = name.toByteArray()
        return ByteBuffer.allocate(10 + args.size + fileName.size)
            .putLong(size).putShort(args.size.toShort()).put(args).put(fileName).array()
    }

    private fun chunks(bytes: ByteArray = apkBytes): Array<Message> =
        (bytes.indices step 65536).map { Message(MessageType.FILE_CHUNK, bytes.copyOfRange(it, minOf(it + 65536, bytes.size))) }
            .toTypedArray()

    // The whole exchange; the channel and its last answer.
    private fun install(payload: ByteArray = request(), incoming: Array<Message> = chunks()): Pair<FakeChannel, Pair<Boolean, String>> {
        val channel = FakeChannel(teacher, *incoming)
        installs.install(payload, channel)
        val last = channel.sent.last()
        assertEquals(MessageType.COMMAND_ACK, last.type)
        return channel to CommandResult.decode(last.payload)
    }

    private fun types(channel: FakeChannel) = channel.sent.map { it.type }

    @Test fun anApkIsReceivedInstalledAndTheTeacherTold() {
        val (channel, result) = install()
        assertEquals(listOf(MessageType.INSTALL_ACCEPTED, MessageType.INSTALL_STARTED, MessageType.COMMAND_ACK), types(channel))
        assertEquals(true to "instalado: Olá (versão 1.0)", result)
        assertArrayEquals(apkBytes, installedBytes)
        assertEquals(listOf("install $OLA", "reapply"), events)
        // Nothing left behind.
        assertTrue(receiveFolder.listFiles().isNullOrEmpty())
    }

    @Test fun underAllowOnlyTheNewAppIsSuspendedBeforeTheTeacherHearsOfIt() {
        allowOnly = true
        val (_, result) = install()
        assertEquals(true to "instalado: Olá (versão 1.0); fica bloqueado pelo \"Liberar só estes\"", result)
    }

    @Test fun onlyAnApkSmallEnoughOnADeviceOwnerWithRoomIsAccepted() {
        assertEquals(false to TeacherInstalls.NOT_APK, install(request("setup.exe")).second)
        assertEquals(false to TeacherInstalls.TOO_BIG, install(request(size = ProgramInstall.MAX_APK_SIZE + 1)).second)
        free = apkBytes.size * 2L
        assertEquals(false to TeacherInstalls.NO_SPACE, install().second)
        free = Long.MAX_VALUE
        deviceOwner = false
        assertEquals(false to TeacherInstalls.NOT_DEVICE_OWNER, install().second)
        assertEquals(false to TeacherInstalls.INVALID, install(byteArrayOf(1, 2, 3)).second)
        assertEquals(false to TeacherInstalls.INVALID, install(request("../x.apk")).second)
        assertTrue(events.isEmpty())
    }

    @Test fun theNameIsCheckedWithoutCaringForItsCase() {
        assertEquals(true, install(request("Ola.APK")).second.first)
    }

    @Test fun somethingThatIsNotAnAppIsRefused() {
        readAs = null
        assertEquals(false to TeacherInstalls.NOT_AN_APP, install().second)
        assertTrue(events.isEmpty())
    }

    @Test fun theAgentItselfIsNeverInstalledHere() {
        readAs = facts(SELF, 99_000_000, "99.0.0")
        assertEquals(false to TeacherInstalls.SELF_INSTALL, install().second)
        assertTrue(events.isEmpty())
    }

    @Test fun anOlderVersionIsRefusedTheSameOneIsAlreadyThere() {
        installedApps[OLA] = PhoneAppFacts(facts(OLA, 3, "1.2"), "Olá", system = false)
        assertEquals(false to "o celular já tem este app numa versão mais nova (1.2): desinstale-o antes para instalar a 1.0", install().second)
        readAs = facts(OLA, 3, "1.2")
        assertEquals(true to "Olá 1.2: esta versão já está instalada (mesmo número de versão); nada foi instalado", install().second)
        assertTrue(events.isEmpty())
        readAs = facts(OLA, 4, "1.3")
        assertEquals(true to "instalado: Olá (versão 1.3)", install().second)
    }

    @Test fun anAppSignedWithAnotherKeyIsRefused() {
        installedApps[OLA] = PhoneAppFacts(facts(OLA, 1, "0.9", signer = "other"), "Olá", system = false)
        readAs = facts(OLA, 2, "1.0")
        assertEquals(false to TeacherInstalls.OTHER_KEY, install().second)
    }

    @Test fun androidRefusingIsSaid() {
        installFailure = "o Android bloqueou a instalação"
        assertEquals(false to "o Android bloqueou a instalação", install().second)
        assertFalse(events.contains("reapply"))
    }

    @Test fun aTransferThatGoesWrongIsRefusedAndLeavesNothing() {
        val (channel, result) = install(request(), arrayOf(Message(MessageType.HEARTBEAT, ByteArray(0))))
        assertEquals(listOf(MessageType.INSTALL_ACCEPTED, MessageType.COMMAND_ACK), types(channel))
        assertEquals(false to "transferência inválida", result)
        assertTrue(receiveFolder.listFiles().isNullOrEmpty())
    }

    @Test fun aDroppedConnectionInstallsNothingAndAnotherCanFollow() {
        val channel = FakeChannel(teacher, *chunks().take(2).toTypedArray())
        installs.install(request(), channel)
        assertEquals(listOf(MessageType.INSTALL_ACCEPTED), types(channel))
        assertTrue(events.isEmpty())
        assertTrue(receiveFolder.listFiles().isNullOrEmpty())
        assertEquals(true, install().second.first)
    }

    @Test fun anAppIsUninstalled() {
        installedApps[OLA] = PhoneAppFacts(facts(OLA, 1, "1.0"), "Olá", system = false, byMidview = true)
        assertEquals(true to "Olá: desinstalado", installs.uninstall(ProgramInstall.encodeList(listOf(OLA))))
        assertEquals(listOf("uninstall $OLA"), events)
    }

    @Test fun systemEssentialAndAgentAppsStayAndAnAbsentOneIsFine() {
        installedApps[CALCULATOR] = PhoneAppFacts(facts(CALCULATOR, 1, "1"), "Calculadora", system = true)
        installedApps[LAUNCHER] = PhoneAppFacts(facts(LAUNCHER, 1, "1"), "Tela inicial", system = true)
        installedApps[SELF] = PhoneAppFacts(facts(SELF, 1, "1"), "Midview", system = false)
        installedApps[OLA] = PhoneAppFacts(facts(OLA, 1, "1.0"), "Olá", system = false, byMidview = true)

        assertEquals(
            false to "Calculadora: ${TeacherInstalls.SYSTEM_APP}",
            installs.uninstall(ProgramInstall.encodeList(listOf(CALCULATOR))),
        )
        assertEquals(false to "Tela inicial: ${TeacherInstalls.ESSENTIAL}", installs.uninstall(ProgramInstall.encodeList(listOf(LAUNCHER))))
        assertEquals(false to "Midview: ${TeacherInstalls.SELF_UNINSTALL}", installs.uninstall(ProgramInstall.encodeList(listOf(SELF))))
        assertEquals(true to "com.nao.existe: ${TeacherInstalls.NOT_HERE}", installs.uninstall(ProgramInstall.encodeList(listOf("com.nao.existe"))))
        assertTrue(events.isEmpty())

        assertEquals(
            false to "Olá: desinstalado; Calculadora: ${TeacherInstalls.SYSTEM_APP}",
            installs.uninstall(ProgramInstall.encodeList(listOf(OLA, CALCULATOR))),
        )
        assertEquals(listOf("uninstall $OLA"), events)
    }

    // Only what the Midview installed goes: not the store's, the maker's nor
    // one installed over adb, even if it is not a system app; never Chrome.
    @Test fun anAppTheMidviewDidNotInstallStays() {
        installedApps[TIKTOK] = PhoneAppFacts(facts(TIKTOK, 1, "1"), "TikTok", system = false, byMidview = false)
        installedApps[NOTES] = PhoneAppFacts(facts(NOTES, 1, "1"), "Anotações", system = false, byMidview = false)
        installedApps[CHROME] = PhoneAppFacts(facts(CHROME, 1, "1"), "Chrome", system = false, byMidview = true)
        // The system's own, even if the Midview put an update of it.
        installedApps[CALCULATOR] = PhoneAppFacts(facts(CALCULATOR, 1, "1"), "Calculadora", system = true, byMidview = true)
        assertEquals(false to "TikTok: ${TeacherInstalls.NOT_BY_MIDVIEW}", installs.uninstall(ProgramInstall.encodeList(listOf(TIKTOK))))
        assertEquals(false to "Anotações: ${TeacherInstalls.NOT_BY_MIDVIEW}", installs.uninstall(ProgramInstall.encodeList(listOf(NOTES))))
        assertEquals(false to "Chrome: ${TeacherInstalls.BROWSER}", installs.uninstall(ProgramInstall.encodeList(listOf(CHROME))))
        assertEquals(false to "Calculadora: ${TeacherInstalls.SYSTEM_APP}", installs.uninstall(ProgramInstall.encodeList(listOf(CALCULATOR))))
        assertTrue(events.isEmpty())
    }

    // An app it installed can be removed again, whatever its install date.
    @Test fun anAppTheMidviewInstalledGoesAgain() {
        install()
        assertEquals(true to "Olá: desinstalado", installs.uninstall(ProgramInstall.encodeList(listOf(OLA))))
    }

    @Test fun androidRefusingAnUninstallIsSaid() {
        installedApps[OLA] = PhoneAppFacts(facts(OLA, 1, "1.0"), "Olá", system = false, byMidview = true)
        uninstallFailure = "o Android não desinstalou (DELETE_FAILED_INTERNAL_ERROR)"
        assertEquals(false to "Olá: o Android não desinstalou (DELETE_FAILED_INTERNAL_ERROR)", installs.uninstall(ProgramInstall.encodeList(listOf(OLA))))
    }

    @Test fun anInvalidUninstallOrOneOffADeviceOwnerDoesNothing() {
        assertEquals(false to TeacherInstalls.INVALID, installs.uninstall(byteArrayOf(1, 0)))
        assertEquals(false to TeacherInstalls.INVALID, installs.uninstall(ProgramInstall.encodeList(emptyList())))
        assertEquals(false to TeacherInstalls.INVALID, installs.uninstall(ProgramInstall.encodeList(listOf("Google Chrome"))))
        deviceOwner = false
        assertEquals(false to TeacherInstalls.NOT_DEVICE_OWNER_UNINSTALL, installs.uninstall(ProgramInstall.encodeList(listOf(OLA))))
        assertTrue(events.isEmpty())
    }

    private companion object {
        const val SELF = "io.github.arnoneto1.midview.agent"
        const val OLA = "io.github.arnoneto1.midview.teste.ola"
        const val CALCULATOR = "com.miui.calculator"
        const val LAUNCHER = "com.miui.home"
        const val TIKTOK = "com.zhiliaoapp.musically"
        const val NOTES = "com.miui.notes"
        const val CHROME = "com.android.chrome"
    }
}
