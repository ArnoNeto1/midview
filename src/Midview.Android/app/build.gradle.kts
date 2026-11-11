plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "io.github.arnoneto1.midview.agent"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.arnoneto1.midview.agent"
        minSdk = 29
        targetSdk = 35
        versionCode = 1
        versionName = "0.0.1"
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
