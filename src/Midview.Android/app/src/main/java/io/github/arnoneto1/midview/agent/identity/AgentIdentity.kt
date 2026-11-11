package io.github.arnoneto1.midview.agent.identity

import org.bouncycastle.asn1.x500.X500NameBuilder
import org.bouncycastle.asn1.x500.style.BCStyle
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.math.BigInteger
import java.security.GeneralSecurityException
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.Calendar
import java.util.Date
import java.util.TimeZone

// The device's TLS identity: a self-signed RSA 2048 / SHA-256 certificate
// with CN=<device name>, as src/Midview.Protocol/SelfSignedCertificate.cs
// makes for a Windows Agent. Made the first time and kept in dir (the app's
// private storage) as PKCS12, with its random password in a file beside it;
// every later start reuses it, so the teachers that paired keep trusting the
// same thumbprint. A store that can no longer be read is replaced.
class AgentIdentity(dir: File, deviceName: String) {
    val certificate: X509Certificate
    val privateKey: PrivateKey

    // The .NET X509Certificate2.Thumbprint: SHA-1 of the DER certificate,
    // upper-case hex. What the teachers pin.
    val thumbprint: String

    init {
        val store = File(dir, STORE_FILE)
        val password = File(dir, PASSWORD_FILE)
        val (cert, key) = load(store, password) ?: create(dir, store, password, deviceName)
        certificate = cert
        privateKey = key
        thumbprint = MessageDigest.getInstance("SHA-1").digest(cert.encoded)
            .joinToString("") { "%02X".format(it.toInt() and 0xFF) }
    }

    private companion object {
        const val STORE_FILE = "agent-identity.p12"
        const val PASSWORD_FILE = "agent-identity.key"
        const val ALIAS = "midview-agent"
        const val DEFAULT_NAME = "Midview"
        const val VALID_YEARS = 20

        val random = SecureRandom()

        fun load(store: File, password: File): Pair<X509Certificate, PrivateKey>? {
            if (!store.isFile || !password.isFile) {
                return null
            }

            return try {
                val secret = password.readText(Charsets.US_ASCII).trim().toCharArray()
                val keyStore = KeyStore.getInstance("PKCS12")
                store.inputStream().use { keyStore.load(it, secret) }
                val key = keyStore.getKey(ALIAS, secret) as? PrivateKey
                val cert = keyStore.getCertificate(ALIAS) as? X509Certificate
                if (key != null && cert != null) cert to key else null
            } catch (e: IOException) {
                null
            } catch (e: GeneralSecurityException) {
                null
            }
        }

        fun create(dir: File, store: File, password: File, deviceName: String): Pair<X509Certificate, PrivateKey> {
            val keys = KeyPairGenerator.getInstance("RSA").apply { initialize(2048, random) }.generateKeyPair()
            val subject = X500NameBuilder(BCStyle.INSTANCE)
                .addRDN(BCStyle.CN, deviceName.trim().ifEmpty { DEFAULT_NAME })
                .build()
            val now = System.currentTimeMillis()
            val notAfter = Calendar.getInstance(TimeZone.getTimeZone("UTC")).apply {
                timeInMillis = now
                add(Calendar.YEAR, VALID_YEARS)
            }.time
            val builder = JcaX509v3CertificateBuilder(
                subject,
                BigInteger(64, random).setBit(63),
                // A day back, as on Windows, so a clock a little behind still accepts it.
                Date(now - 24L * 60 * 60 * 1000),
                notAfter,
                subject,
                keys.public,
            )
            val signer = JcaContentSignerBuilder("SHA256withRSA").build(keys.private)
            val cert = JcaX509CertificateConverter().getCertificate(builder.build(signer))

            val secretText = ByteArray(32).also { random.nextBytes(it) }
                .joinToString("") { "%02x".format(it.toInt() and 0xFF) }
            val secret = secretText.toCharArray()
            val keyStore = KeyStore.getInstance("PKCS12").apply {
                load(null, null)
                setKeyEntry(ALIAS, keys.private, secret, arrayOf(cert))
            }
            val bytes = ByteArrayOutputStream().also { keyStore.store(it, secret) }.toByteArray()

            dir.mkdirs()
            // Password first: a crash between the two leaves a pair that
            // doesn't open, which the next start replaces.
            writeAtomically(password, secretText.toByteArray(Charsets.US_ASCII))
            writeAtomically(store, bytes)
            return cert to keys.private
        }
    }
}
