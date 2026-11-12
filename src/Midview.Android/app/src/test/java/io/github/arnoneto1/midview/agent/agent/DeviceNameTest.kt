package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Test

class DeviceNameTest {
    // Not the name in the settings: MIUI fills it with the model, and a
    // classroom of the same phone would all answer with one name.
    @Test fun theAndroidIdsEndTellsPhonesApart() {
        assertEquals("Celular 549C", DeviceName.choose(null, "9774d56d682e549c", "Redmi 10C"))
        assertEquals("Celular 549C", DeviceName.choose(null, " 9774d56d682e549c ", "Redmi 10C"))
    }

    @Test fun withoutAnAndroidIdTheModel() {
        assertEquals("Celular Redmi 10C", DeviceName.choose(null, null, "Redmi 10C"))
        assertEquals("Celular Redmi 10C", DeviceName.choose(null, "", "Redmi 10C"))
        assertEquals("Celular Redmi 10C", DeviceName.choose(null, "a1", "Redmi 10C"))
    }

    @Test fun neverEmpty() {
        assertEquals("Celular", DeviceName.choose(null, null, null))
        assertEquals("Celular", DeviceName.choose(null, " ", " "))
    }

    // The number the Master gave while preparing the phone over the cable.
    @Test fun theMastersNumberIsTheName() {
        assertEquals("Celular 07", DeviceName.choose("07", "9774d56d682e549c", "Redmi 10C"))
        assertEquals("Celular 07", DeviceName.choose("07", null, null))
        assertEquals("Celular 549C", DeviceName.choose(" ", "9774d56d682e549c", "Redmi 10C"))
    }
}
