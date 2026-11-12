package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

// The teacher's input waiting while the phone carries out a gesture
// (RemoteControl), kept small whatever the teacher's side sends:
// - characters in a row are one text, a key pressed again and again one key
//   with a count, wheel notches in a row one wheel turn;
// - past capacity, the oldest mouse moves go first (the finger's path loses
//   a point, nothing else);
// - only past twice the capacity, with no move left to drop, is a new event
//   refused, and so is text past TEXT_LIMIT: counted in refused, never
//   silently;
// - a key's release and the right button's carry nothing on a phone
//   (TouchPlanner): they are not kept.
// Any thread may put; one thread takes.
class InputQueue(private val capacity: Int = CAPACITY) {
    sealed interface Item

    class Event(val event: InputEvent, val atMs: Long) : Item

    class Text(val atMs: Long) : Item {
        val text = StringBuilder()
    }

    class Key(val key: Int, val atMs: Long) : Item {
        var count = 1
            internal set
    }

    class Wheel(var delta: Int, val atMs: Long) : Item

    object Stop : Item

    private val lock = ReentrantLock()
    private val arrived = lock.newCondition()
    private val items = ArrayDeque<Item>()
    private var stopped = false

    // Mouse moves dropped past capacity.
    var droppedMoves = 0
        private set

    // Events refused (the queue full of what can't be dropped, or text past
    // TEXT_LIMIT): for the log.
    var refused = 0
        private set

    val size: Int get() = lock.withLock { items.size }

    fun put(event: InputEvent, atMs: Long) {
        lock.withLock { putLocked(event, atMs) }
    }

    private fun putLocked(event: InputEvent, atMs: Long) {
        if (stopped) return
        val last = items.lastOrNull()
        when (event.kind) {
            InputEvent.KEY_UP, InputEvent.RIGHT_UP -> return
            InputEvent.TEXT -> {
                val text = (last as? Text) ?: Text(atMs).also { if (!add(it)) return }
                if (text.text.length >= TEXT_LIMIT) {
                    refused++
                    return
                }
                text.text.append(event.key.toChar())
            }
            InputEvent.KEY_DOWN -> if (last is Key && last.key == event.key) last.count++ else add(Key(event.key, atMs))
            InputEvent.WHEEL -> if (last is Wheel) {
                last.delta = (last.delta.toLong() + event.x).coerceIn(Int.MIN_VALUE.toLong(), Int.MAX_VALUE.toLong()).toInt()
            } else {
                add(Wheel(event.x, atMs))
            }
            else -> add(Event(event, atMs))
        }
        arrived.signalAll()
    }

    // Ends the queue: what is in it is still taken, then Stop; nothing more goes in.
    fun stop() {
        lock.withLock {
            if (stopped) return
            stopped = true
            items.addLast(Stop)
            arrived.signalAll()
        }
    }

    // Everything waiting, in order; waits for at least one.
    fun takeAll(): List<Item> = lock.withLock {
        while (items.isEmpty()) arrived.await()
        val all = items.toList()
        items.clear()
        all
    }

    private fun add(item: Item): Boolean {
        if (items.size >= capacity) {
            val oldestMove = items.indexOfFirst { it is Event && it.event.kind == InputEvent.MOUSE_MOVE }
            if (oldestMove >= 0) {
                items.removeAt(oldestMove)
                droppedMoves++
            } else if (items.size >= capacity * 2) {
                refused++
                return false
            }
        }
        items.addLast(item)
        return true
    }

    companion object {
        const val CAPACITY = 1024
        const val TEXT_LIMIT = 65_536
    }
}
