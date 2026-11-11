package io.github.arnoneto1.midview.agent.screen

// The ScreenFrame updates on their way to the viewer that it has not
// acknowledged yet (FrameAck); mirrors src/Midview.Session/FrameCredit.cs. The
// capture loop waits for room before capturing the next one, so a slow network
// gets fewer, newer updates instead of a queue of old ones.
//
// An update is numbered nextId() onward (one id per message, so a split update
// takes several) and registered with sent(lastId): it is acknowledged once the
// viewer has drawn its last message.
class FrameCredit(private val maxUnacked: Int = 2) {
    private val lock = Object()
    private val pending = ArrayDeque<Int>()
    private var lastId = 0

    // The id the next message gets.
    fun nextId(): Int = synchronized(lock) { lastId + 1 }

    // An update went out whose last message has this id (ids consecutive from nextId()).
    fun sent(id: Int) = synchronized(lock) {
        if (id > lastId) lastId = id
        pending.addLast(id)
    }

    // The viewer has drawn every message up to id.
    fun ack(id: Int) = synchronized(lock) {
        while (pending.isNotEmpty() && pending.first() <= id) pending.removeFirst()
        lock.notifyAll()
    }

    fun hasRoom(): Boolean = synchronized(lock) { pending.size < maxUnacked }

    fun unacked(): Int = synchronized(lock) { pending.size }

    // Gives up on the oldest unacknowledged update (a viewer that stopped acknowledging).
    fun dropOldest() = synchronized(lock) {
        if (pending.isNotEmpty()) pending.removeFirst()
        lock.notifyAll()
    }

    // Returns once fewer than maxUnacked updates are waiting. After timeoutMs
    // without room it drops the oldest and returns true all the same, so a
    // viewer that stops acknowledging cannot freeze the picture. False only
    // when the thread is interrupted (the connection is closing).
    fun awaitRoom(timeoutMs: Long): Boolean = synchronized(lock) {
        val deadline = System.nanoTime() + timeoutMs * 1_000_000
        while (pending.size >= maxUnacked) {
            val leftMs = (deadline - System.nanoTime()) / 1_000_000
            if (leftMs <= 0) {
                pending.removeFirst()
                break
            }
            try {
                lock.wait(leftMs)
            } catch (e: InterruptedException) {
                Thread.currentThread().interrupt()
                return false
            }
        }
        true
    }
}
