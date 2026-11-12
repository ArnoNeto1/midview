package io.github.arnoneto1.midview.agent.control

// A point on the phone's real screen, in pixels of the current rotation (what
// AccessibilityService.dispatchGesture takes).
data class Spot(val x: Float, val y: Float)

// A width and a height in pixels.
data class Size(val width: Int, val height: Int)

// Android's three buttons, as AccessibilityService's global actions.
enum class GlobalKey { BACK, HOME, RECENTS }

// The teacher's keys that edit the focused text field.
enum class EditKey { BACKSPACE, DELETE, ENTER, LEFT, RIGHT }

// What the phone does for the teacher's input (TouchPlanner), one step at a
// time, each finished before the next (AndroidControlTarget).
sealed interface TouchStep {
    // A finger touches down and stays (a continued stroke).
    data class Down(val at: Spot) : TouchStep

    // The finger that is down moves, in about that time, and stays down.
    data class Move(val from: Spot, val to: Spot, val durationMs: Long) : TouchStep

    // The finger that is down lifts here.
    data class Up(val at: Spot) : TouchStep

    // A whole stroke that stops before lifting, so the list scrolls without a
    // fling: the teacher's mouse wheel.
    data class Swipe(val from: Spot, val to: Spot, val durationMs: Long) : TouchStep

    // A finger held still long enough for Android's long press: the right button.
    data class LongPress(val at: Spot) : TouchStep

    data class Global(val key: GlobalKey) : TouchStep

    // Characters typed into the focused text field, where its cursor is.
    data class Type(val text: String) : TouchStep

    data class Edit(val key: EditKey) : TouchStep
}
