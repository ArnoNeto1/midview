package io.github.arnoneto1.midview.agent.identity

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.security.MessageDigest
import java.security.Signature
import java.security.interfaces.RSAPublicKey
import java.util.Calendar
import java.util.concurrent.Executors

class AgentIdentityTest {
    @get:Rule val folder = TemporaryFolder()

    @Test fun identityIsCreatedOnceAndReused() {
        val dir = folder.newFolder()
        val first = AgentIdentity(dir, "Cel 01")
        val second = AgentIdentity(dir, "Outro nome")
        assertEquals(first.thumbprint, second.thumbprint)
        assertEquals(first.privateKey, second.privateKey)
    }

    @Test fun eachDeviceHasItsOwnIdentity() {
        assertNotEquals(AgentIdentity(folder.newFolder(), "A").thumbprint, AgentIdentity(folder.newFolder(), "A").thumbprint)
    }

    @Test fun thumbprintIsUppercaseSha1OfDer() {
        val identity = AgentIdentity(folder.newFolder(), "Cel 01")
        val expected = MessageDigest.getInstance("SHA-1").digest(identity.certificate.encoded)
            .joinToString("") { "%02X".format(it) }
        assertEquals(expected, identity.thumbprint)
        assertTrue(identity.thumbprint.matches(Regex("[0-9A-F]{40}")))
    }

    @Test fun certificateIsRsa2048Sha256WithTheDeviceName() {
        val cert = AgentIdentity(folder.newFolder(), "Cel, 01").certificate
        assertEquals(2048, (cert.publicKey as RSAPublicKey).modulus.bitLength())
        assertEquals("SHA256withRSA", cert.sigAlgName)
        assertEquals("CN=Cel\\, 01", cert.subjectX500Principal.name)
        assertEquals(cert.subjectX500Principal, cert.issuerX500Principal)
        cert.verify(cert.publicKey)
        cert.checkValidity()
        val years = Calendar.getInstance().apply { time = cert.notAfter }.get(Calendar.YEAR) -
            Calendar.getInstance().get(Calendar.YEAR)
        assertEquals(20, years)
    }

    @Test fun aBlankDeviceNameStillMakesACertificate() {
        assertEquals("CN=Midview", AgentIdentity(folder.newFolder(), "  ").certificate.subjectX500Principal.name)
    }

    @Test fun privateKeyMatchesTheCertificate() {
        val identity = AgentIdentity(folder.newFolder(), "Cel 01")
        val data = "Midview".toByteArray()
        val signature = Signature.getInstance("SHA256withRSA").run {
            initSign(identity.privateKey); update(data); sign()
        }
        assertTrue(
            Signature.getInstance("SHA256withRSA").run {
                initVerify(identity.certificate.publicKey); update(data); verify(signature)
            },
        )
    }

    @Test fun aDamagedStoreIsReplacedOnceAndSaysSo() {
        val dir = folder.newFolder()
        File(dir, "agent-identity.p12").writeBytes(byteArrayOf(1, 2, 3))
        File(dir, "agent-identity.key").writeBytes(byteArrayOf(4, 5, 6))
        val again = AgentIdentity(dir, "Cel 01")
        assertTrue(again.replaced)
        assertEquals(again.thumbprint, AgentIdentity(dir, "Cel 01").thumbprint)
    }

    @Test fun aNewOrAReadableStoreIsNotReplaced() {
        val dir = folder.newFolder()
        assertFalse(AgentIdentity(dir, "Cel 01").replaced)
        assertFalse(AgentIdentity(dir, "Cel 01").replaced)
        assertFalse(AgentIdentity(folder.newFolder(), "Cel 02").replaced)
    }

    @Test fun manyInstancesAtOnceShareOneIdentity() {
        val dir = folder.newFolder()
        val pool = Executors.newFixedThreadPool(8)
        try {
            val thumbprints = (1..8).map { pool.submit<String> { AgentIdentity(dir, "Cel 01").thumbprint } }.map { it.get() }
            assertEquals(1, thumbprints.toSet().size)
        } finally {
            pool.shutdown()
        }
        // No temporary file is left behind.
        assertEquals(setOf("agent-identity.p12", "agent-identity.key"), dir.list()!!.toSet())
    }
}
