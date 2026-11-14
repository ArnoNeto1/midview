plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// The version is the repository's one place to change it: <Version> in
// Directory.Build.props. versionCode follows from it (major * 1_000_000 +
// minor * 1_000 + patch), so it only grows when the version does.
// -PmidviewVersion=0.0.51 builds the same code as another version, only to
// try "Atualizar celulares" on a test phone; releases never pass it.
val midviewVersion: String = (findProperty("midviewVersion") as String?)?.let { asked ->
    asked.takeIf { Regex("""[0-9]+\.[0-9]+\.[0-9]+""").matches(it) }
        ?: throw GradleException("-PmidviewVersion must be major.minor.patch, not \"$asked\".")
} ?: run {
    val props = rootDir.resolve("../../Directory.Build.props").readText()
    Regex("""<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>""").find(props)?.groupValues?.get(1)
        ?: throw GradleException("Directory.Build.props has no <Version> as major.minor.patch.")
}
val midviewVersionCode: Int = midviewVersion.split('.').map { it.toInt() }.let { (major, minor, patch) ->
    major * 1_000_000 + minor * 1_000 + patch
}

// The release key comes from the environment (the CI secrets), never from a
// committed file. Without all four values the release APK is built unsigned.
// Spaces and line breaks around a secret (pasted from a text file) are not
// part of it: the key's password is letters and digits only.
val keystorePath: String? = System.getenv("MIDVIEW_KEYSTORE_PATH")?.takeIf { it.isNotBlank() }
val keystorePassword: String? = System.getenv("MIDVIEW_KEYSTORE_PASSWORD")?.trim()?.takeIf { it.isNotEmpty() }
val signingKeyAlias: String? = System.getenv("MIDVIEW_KEY_ALIAS")?.trim()?.takeIf { it.isNotBlank() }
val signingKeyPassword: String? = System.getenv("MIDVIEW_KEY_PASSWORD")?.trim()?.takeIf { it.isNotEmpty() } ?: keystorePassword
val canSignRelease = keystorePath != null && keystorePassword != null && signingKeyAlias != null

android {
    namespace = "io.github.arnoneto1.midview.agent"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.arnoneto1.midview.agent"
        minSdk = 29
        targetSdk = 35
        versionCode = midviewVersionCode
        versionName = midviewVersion
    }

    signingConfigs {
        if (canSignRelease) {
            create("release") {
                storeFile = file(keystorePath!!)
                storePassword = keystorePassword
                keyAlias = signingKeyAlias
                keyPassword = signingKeyPassword
            }
        }
    }

    buildTypes {
        release {
            isDebuggable = false
            if (canSignRelease) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        resources {
            // bcprov, bcutil and bcpkix each carry the same OSGi manifest and
            // the same license; one copy of the license is kept.
            excludes += "META-INF/versions/9/OSGI-INF/MANIFEST.MF"
            pickFirsts += "META-INF/LICENSE.md"
        }
    }
}

kotlin {
    jvmToolchain(17)
}

dependencies {
    // The agent's self-signed TLS certificate (AgentIdentity).
    implementation("org.bouncycastle:bcpkix-jdk18on:1.87")

    testImplementation("junit:junit:4.13.2")
    // org.json is part of Android; plain JVM unit tests need the real one.
    testImplementation("org.json:json:20240303")
}
