package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.net.ConnectionAdmission.Seat
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ConnectionAdmissionTest {
    private var now = 1_000L
    private val admission = ConnectionAdmission(
        maxStrangers = 6, perAddress = 2, maxTeacherAddressWaiting = 3, maxTrusted = 2, rememberMillis = 60_000, clock = { now },
    )

    private val student = "192.168.15.40"
    private val other = "192.168.15.41"
    private val teacher = "192.168.15.2"

    @Test fun oneAddressHoldsOnlyAFewStrangerConnections() {
        assertEquals(Seat.STRANGER, admission.admit(student))
        assertEquals(Seat.STRANGER, admission.admit(student))
        assertNull(admission.admit(student))
        // Another device still gets in.
        assertEquals(Seat.STRANGER, admission.admit(other))
    }

    @Test fun aClosedConnectionGivesItsPlaceBack() {
        repeat(2) { admission.admit(student) }
        admission.release(student, Seat.STRANGER)
        assertEquals(Seat.STRANGER, admission.admit(student))
    }

    @Test fun strangersShareOnePool() {
        listOf("a", "a", "b", "b", "c", "c").forEach { assertEquals(Seat.STRANGER, admission.admit(it)) }
        assertNull(admission.admit("d"))
    }

    // A teacher's computer is known after its first trusted connection; its
    // next ones wait for their handshake in a pool of their own, so strangers
    // filling theirs never keep it out.
    @Test fun aTeachersAddressGetsInWhileStrangersFillTheirPool() {
        val first = admission.admit(teacher)!!
        assertEquals(Seat.TRUSTED, admission.trust(teacher, first))
        listOf("a", "a", "b", "b", "c", "c").forEach { admission.admit(it) }
        assertNull(admission.admit("d"))

        assertEquals(Seat.TEACHER_ADDRESS, admission.admit(teacher))
        assertEquals(Seat.TEACHER_ADDRESS, admission.admit(teacher))
    }

    @Test fun anUnknownTeacherWaitsAsAStranger() {
        assertEquals(Seat.STRANGER, admission.admit(teacher))
    }

    @Test fun aTeachersOwnPoolFullLetsItWaitAsAStranger() {
        admission.trust(teacher, admission.admit(teacher)!!)
        repeat(3) { assertEquals(Seat.TEACHER_ADDRESS, admission.admit(teacher)) }
        assertEquals(Seat.STRANGER, admission.admit(teacher))
    }

    @Test fun trustingGivesTheWaitingPlaceBack() {
        val seat = admission.admit(student)!!
        admission.admit(student)
        assertNull(admission.admit(student))
        assertEquals(Seat.TRUSTED, admission.trust(student, seat))
        assertEquals(Seat.TEACHER_ADDRESS, admission.admit(student))
    }

    @Test fun trustedConnectionsHaveALimitToo() {
        assertEquals(Seat.TRUSTED, admission.trust(teacher, admission.admit(teacher)!!))
        assertEquals(Seat.TRUSTED, admission.trust(teacher, admission.admit(teacher)!!))
        assertNull(admission.trust(teacher, admission.admit(teacher)!!))
        admission.release(teacher, Seat.TRUSTED)
        assertEquals(Seat.TRUSTED, admission.trust(teacher, admission.admit(teacher)!!))
    }

    @Test fun aTeachersAddressIsForgottenAfterAWhile() {
        admission.release(teacher, admission.trust(teacher, admission.admit(teacher)!!)!!)
        now += 60_001
        assertEquals(Seat.STRANGER, admission.admit(teacher))
    }
}
