package io.github.arnoneto1.midview.agent.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ApkCheckTest {
    private val schoolKey = setOf("6a1f0c")
    private val installed = ApkFacts("io.github.arnoneto1.midview.agent", 50, "0.0.50", schoolKey)

    private fun candidate(
        packageName: String = installed.packageName,
        versionCode: Long = 51,
        versionName: String = "0.0.51",
        signers: Set<String> = schoolKey,
    ) = ApkFacts(packageName, versionCode, versionName, signers)

    // What a received APK may be, and what the teacher reads when it may not.
    @Test fun theTable() {
        val rows = listOf(
            candidate() to null,
            candidate(versionCode = 2_000_000, versionName = "2.0.0") to null,
            candidate(packageName = "com.example.other") to "outro aplicativo",
            candidate(versionCode = 50, versionName = "0.0.50") to "versão igual ou mais antiga (0.0.50)",
            candidate(versionCode = 49, versionName = "0.0.49") to "versão igual ou mais antiga (0.0.49)",
            candidate(signers = setOf("99bb02")) to "assinado com outra chave",
        )
        for ((apk, refusal) in rows) {
            assertEquals(apk.toString(), refusal, ApkCheck.refusal(apk, installed))
        }
    }

    // Android signs with every key listed: one more is not the same key.
    @Test fun anExtraSignerIsAnotherKey() {
        assertEquals("assinado com outra chave", ApkCheck.refusal(candidate(signers = schoolKey + "99bb02"), installed))
    }

    // The key says more than the version: an APK from elsewhere is never
    // the agent, whatever its version.
    @Test fun anotherKeyIsSaidBeforeTheVersion() {
        assertEquals(
            "assinado com outra chave",
            ApkCheck.refusal(candidate(versionCode = 49, versionName = "0.0.49", signers = setOf("99bb02")), installed),
        )
    }

    @Test fun anotherAppIsSaidFirst() {
        assertEquals(
            "outro aplicativo",
            ApkCheck.refusal(candidate(packageName = "com.example.other", versionCode = 1, signers = setOf("99bb02")), installed),
        )
    }

    @Test fun aFileAndroidCannotReadIsNotAnApp() {
        assertEquals("o arquivo recebido não é um aplicativo", ApkCheck.refusal(null, installed))
    }

    // Never taken as the same key: no signer read is no proof.
    @Test fun aSignatureThatCouldNotBeReadIsRefused() {
        assertEquals("a assinatura não pôde ser conferida", ApkCheck.refusal(candidate(signers = emptySet()), installed))
        assertEquals(
            "a assinatura não pôde ser conferida",
            ApkCheck.refusal(candidate(signers = emptySet()), installed.copy(signers = emptySet())),
        )
    }

    @Test fun signersAreTheSha256OfEachCertificate() {
        assertEquals(
            setOf("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
            ApkCheck.signers(listOf("abc".toByteArray())),
        )
    }

    @Test fun theNextVersionPassesWithNothingToSay() {
        assertNull(ApkCheck.refusal(candidate(), installed))
    }
}
