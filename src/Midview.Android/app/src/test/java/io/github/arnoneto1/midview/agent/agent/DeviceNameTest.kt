package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Test

class DeviceNameTest {
    @Test fun theNameGivenInTheSettingsWins() {
        assertEquals("Redmi do João", DeviceName.choose(" Redmi do João ", "9774d56d682e549c", "Redmi 10C"))
    }

    @Test fun withoutOneTheAndroidIdsEndTellsPhonesApart() {
        assertEquals("Celular 549C", DeviceName.choose(null, "9774d56d682e549c", "Redmi 10C"))
        assertEquals("Celular 549C", DeviceName.choose("  ", "9774d56d682e549c", "Redmi 10C"))
    }

    @Test fun thenTheModel() {
        assertEquals("Celular Redmi 10C", DeviceName.choose(null, "", "Redmi 10C"))
        assertEquals("Celular Redmi 10C", DeviceName.choose(null, "a1", "Redmi 10C"))
    }

    @Test fun neverEmpty() {
        assertEquals("Celular", DeviceName.choose(null, null, null))
        assertEquals("Celular", DeviceName.choose("", " ", " "))
    }
}
