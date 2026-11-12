package io.github.arnoneto1.midview.agent.net

// Who may hold a connection to a port, so nobody on the network can fill
// the phone's connections and keep the teacher out (a "slowloris": many
// connections that open and then send a byte now and then). A connection is
// a stranger's until its TLS handshake shows a trusted teacher's
// certificate: before that nobody is known. Strangers share a small pool
// (maxStrangers) and each address holds few of them (perAddress), so one
// device on the network can take only a few. An address a trusted teacher
// connected from (within rememberMillis) is a teacher's address: its
// connections waiting for their handshake come from a pool of their own, so
// strangers filling theirs leave the teacher's computer room to come in.
// Once trusted, a connection moves to the teachers' pool (maxTrusted) and
// gives its place back. Times in milliseconds of one clock
// (SystemClock.elapsedRealtime on the phone, which a student cannot set).
class ConnectionAdmission(
    private val maxStrangers: Int = MAX_STRANGERS,
    private val perAddress: Int = PER_ADDRESS,
    private val maxTeacherAddressWaiting: Int = MAX_TEACHER_ADDRESS_WAITING,
    private val maxTrusted: Int = MAX_TRUSTED,
    private val rememberMillis: Long = REMEMBER_TEACHER_ADDRESS_MILLIS,
    private val clock: () -> Long,
) {
    // A connection's place: the pool it holds a seat in.
    enum class Seat { STRANGER, TEACHER_ADDRESS, TRUSTED }

    private val strangersBy = HashMap<String, Int>()
    private var strangers = 0
    private var teacherAddressWaiting = 0
    private var trusted = 0
    private val teacherAddresses = HashMap<String, Long>()

    // A connection just accepted from address: its seat, or null to close it.
    @Synchronized
    fun admit(address: String): Seat? {
        if (isTeacherAddress(address)) {
            if (teacherAddressWaiting < maxTeacherAddressWaiting) {
                teacherAddressWaiting++
                return Seat.TEACHER_ADDRESS
            }
            // Its own pool is full: it may still wait as a stranger.
        }
        val held = strangersBy[address] ?: 0
        if (strangers >= maxStrangers || held >= perAddress) return null
        strangers++
        strangersBy[address] = held + 1
        return Seat.STRANGER
    }

    // The handshake showed a trusted teacher: the seat becomes a teacher's,
    // and the address is remembered. Null when the teachers' pool is full
    // (the connection is closed; its old seat is already given back).
    @Synchronized
    fun trust(address: String, seat: Seat): Seat? {
        if (seat == Seat.TRUSTED) return seat
        free(address, seat)
        teacherAddresses[address] = clock()
        if (trusted >= maxTrusted) return null
        trusted++
        return Seat.TRUSTED
    }

    // The connection ended.
    @Synchronized
    fun release(address: String, seat: Seat) = free(address, seat)

    private fun free(address: String, seat: Seat) {
        when (seat) {
            Seat.TRUSTED -> if (trusted > 0) trusted--
            Seat.TEACHER_ADDRESS -> if (teacherAddressWaiting > 0) teacherAddressWaiting--
            Seat.STRANGER -> {
                if (strangers > 0) strangers--
                val held = (strangersBy[address] ?: 0) - 1
                if (held > 0) strangersBy[address] = held else strangersBy.remove(address)
            }
        }
    }

    private fun isTeacherAddress(address: String): Boolean {
        val seen = teacherAddresses[address] ?: return false
        if (clock() - seen <= rememberMillis) return true
        teacherAddresses.remove(address)
        return false
    }

    companion object {
        // A classroom's teacher opens a handful of connections to a phone at
        // once (the grid, the big window, a command, the lock); a student
        // gets four at most from one device.
        const val MAX_STRANGERS = 16
        const val PER_ADDRESS = 4
        const val MAX_TEACHER_ADDRESS_WAITING = 16
        const val MAX_TRUSTED = 48

        // A teacher's computer keeps its address through a school day.
        const val REMEMBER_TEACHER_ADDRESS_MILLIS = 12 * 60 * 60 * 1000L
    }
}
