package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TouchPlannerTest {
    // The Redmi 10C: 720 x 1650, seen by the teacher at 559 x 1280.
    private var frame: Size? = Size(559, 1280)
    private var screen: Size? = Size(720, 1650)
    private val planner = TouchPlanner({ frame }, { screen })

    private fun event(kind: Int, x: Int = 0, y: Int = 0, key: Int = 0) = InputEvent(kind, x, y, key)

    private fun at(x: Int, y: Int) = ScreenMap.toScreen(x, y, frame!!, screen!!)

    @Test fun framePixelsLandOnTheRealScreen() {
        assertEquals(Spot(0.5f * 720 / 559, 0.5f * 1650 / 1280), ScreenMap.toScreen(0, 0, Size(559, 1280), Size(720, 1650)))
        val middle = ScreenMap.toScreen(279, 640, Size(559, 1280), Size(720, 1650))
        assertEquals(360f, middle.x, 1f)
        assertEquals(825f, middle.y, 1f)
        // The last pixel and beyond (a click on the letterbox edge) stay on the screen.
        assertEquals(Spot(719f, 1649f), ScreenMap.toScreen(600, 2000, Size(559, 1280), Size(720, 1650)))
        assertEquals(Spot(0f, 0f), ScreenMap.toScreen(-30, -5, Size(559, 1280), Size(720, 1650)))
    }

    @Test fun aTurnedPhoneScalesEachAxisOnItsOwn() {
        val spot = ScreenMap.toScreen(1279, 558, Size(1280, 559), Size(1650, 720))
        assertEquals(1649f, spot.x, 1f)
        assertEquals(719f, spot.y, 1f)
    }

    @Test fun aClickIsATapWhereItWas() {
        assertEquals(listOf(TouchStep.Down(at(100, 200))), planner.plan(event(InputEvent.LEFT_DOWN, 100, 200), 0))
        assertEquals(listOf(TouchStep.Up(at(100, 200))), planner.plan(event(InputEvent.LEFT_UP, 100, 200), 90))
    }

    @Test fun aDragIsOneFingerThatFollowsTheMouse() {
        planner.plan(event(InputEvent.MOUSE_MOVE, 100, 900), 0)
        assertEquals(listOf(TouchStep.Down(at(100, 900))), planner.plan(event(InputEvent.LEFT_DOWN, 100, 900), 10))
        assertEquals(
            listOf(TouchStep.Move(at(100, 900), at(100, 700), 30)),
            planner.plan(event(InputEvent.MOUSE_MOVE, 100, 700), 40),
        )
        // A pause longer than a move takes is capped; a burst shorter is stretched.
        assertEquals(
            listOf(TouchStep.Move(at(100, 700), at(100, 500), TouchPlanner.MAX_MOVE_MS)),
            planner.plan(event(InputEvent.MOUSE_MOVE, 100, 500), 1_000),
        )
        assertEquals(
            listOf(TouchStep.Move(at(100, 500), at(100, 480), TouchPlanner.MIN_MOVE_MS)),
            planner.plan(event(InputEvent.MOUSE_MOVE, 100, 480), 1_001),
        )
        // Released somewhere else than the last move: there first, then up.
        assertEquals(
            listOf(TouchStep.Move(at(100, 480), at(100, 470), TouchPlanner.MIN_MOVE_MS), TouchStep.Up(at(100, 470))),
            planner.plan(event(InputEvent.LEFT_UP, 100, 470), 1_002),
        )
    }

    @Test fun movingWithoutAButtonOnlyPlacesThePointer() {
        assertNull(planner.pointer)
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.MOUSE_MOVE, 10, 20), 0))
        assertEquals(at(10, 20), planner.pointer)
    }

    @Test fun nothingIsPlacedBeforeThePictureOrTheScreenIsKnown() {
        frame = null
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.LEFT_DOWN, 10, 20), 0))
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.LEFT_UP, 10, 20), 0))
        frame = Size(559, 1280)
        screen = null
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.MOUSE_MOVE, 10, 20), 0))
        assertNull(planner.pointer)
    }

    @Test fun anUpWithoutADownAndASecondDownDoNothing() {
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.LEFT_UP, 10, 20), 0))
        planner.plan(event(InputEvent.LEFT_DOWN, 10, 20), 0)
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.LEFT_DOWN, 30, 40), 5))
    }

    @Test fun theRightButtonIsALongPress() {
        assertEquals(listOf(TouchStep.LongPress(at(50, 60))), planner.plan(event(InputEvent.RIGHT_DOWN, 50, 60), 0))
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.RIGHT_UP, 50, 60), 50))
    }

    @Test fun theWheelSwipesWhereTheMouseIs() {
        planner.plan(event(InputEvent.MOUSE_MOVE, 279, 640), 0)
        val from = planner.pointer!!
        // Away from the teacher (+120): what is above comes down, the finger goes down.
        val up = planner.plan(event(InputEvent.WHEEL, 120), 10).single() as TouchStep.Swipe
        assertEquals(from, up.from)
        assertEquals(from.y + 165f, up.to.y, 0.01f)
        assertEquals(from.x, up.to.x, 0f)
        val down = planner.plan(event(InputEvent.WHEEL, -240), 20).single() as TouchStep.Swipe
        assertEquals(from.y - 330f, down.to.y, 0.01f)
    }

    @Test fun aBigWheelSwipesAtMostHalfTheScreenAndStaysOnIt() {
        planner.plan(event(InputEvent.MOUSE_MOVE, 279, 1200), 0)
        val swipe = planner.plan(event(InputEvent.WHEEL, 120 * 20), 10).single() as TouchStep.Swipe
        assertEquals(1649f, swipe.to.y, 0f)
        planner.plan(event(InputEvent.MOUSE_MOVE, 279, 640), 20)
        val far = planner.plan(event(InputEvent.WHEEL, -120 * 20), 30).single() as TouchStep.Swipe
        assertEquals(far.from.y - 825f, far.to.y, 0.01f)
    }

    @Test fun theWheelDoesNothingWithoutAPointerOrWhileDragging() {
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.WHEEL, 120), 0))
        planner.plan(event(InputEvent.LEFT_DOWN, 100, 100), 0)
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.WHEEL, 120), 10))
    }

    @Test fun typedCharactersAndKeys() {
        assertEquals(listOf(TouchStep.Type("a")), planner.plan(event(InputEvent.TEXT, key = 'a'.code), 0))
        assertEquals(listOf(TouchStep.Type("ç")), planner.plan(event(InputEvent.TEXT, key = 'ç'.code), 0))
        // Control characters come as keys too: not typed.
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.TEXT, key = 8), 0))
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.TEXT, key = 13), 0))
        assertEquals(listOf(TouchStep.Edit(EditKey.BACKSPACE)), planner.plan(event(InputEvent.KEY_DOWN, key = KeyMap.VK_BACK), 0))
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.KEY_UP, key = KeyMap.VK_BACK), 0))
        // A letter as a key: the character comes as TEXT.
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.KEY_DOWN, key = 'A'.code), 0))
    }

    @Test fun anEmojiArrivesInTwoHalves() {
        val emoji = "😀"
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.TEXT, key = emoji[0].code), 0))
        assertEquals(listOf(TouchStep.Type(emoji)), planner.plan(event(InputEvent.TEXT, key = emoji[1].code), 0))
        // A lone second half is dropped.
        assertEquals(emptyList<TouchStep>(), planner.plan(event(InputEvent.TEXT, key = emoji[1].code), 0))
    }

    @Test fun anUnknownKindDoesNothing() {
        assertEquals(emptyList<TouchStep>(), planner.plan(event(42, 1, 2, 3), 0))
    }

    @Test fun releaseLiftsAFingerStillDown() {
        assertEquals(emptyList<TouchStep>(), planner.release())
        planner.plan(event(InputEvent.LEFT_DOWN, 100, 100), 0)
        planner.plan(event(InputEvent.MOUSE_MOVE, 100, 150), 20)
        assertEquals(listOf(TouchStep.Up(at(100, 150))), planner.release())
        assertEquals(emptyList<TouchStep>(), planner.release())
    }

    @Test fun piledUpStepsAreMerged() {
        val a = Spot(1f, 1f)
        val b = Spot(1f, 5f)
        val c = Spot(1f, 9f)
        val merged = planner.merge(
            listOf(
                TouchStep.Down(a),
                TouchStep.Move(a, b, 80),
                TouchStep.Move(b, c, 80),
                TouchStep.Type("ab"),
                TouchStep.Type("c"),
                TouchStep.Global(GlobalKey.BACK),
                TouchStep.Global(GlobalKey.BACK),
                TouchStep.Up(c),
            ),
        )
        assertEquals(
            listOf(
                TouchStep.Down(a),
                TouchStep.Move(a, c, TouchPlanner.MAX_MERGED_MOVE_MS),
                TouchStep.Type("abc"),
                TouchStep.Global(GlobalKey.BACK),
                TouchStep.Global(GlobalKey.BACK),
                TouchStep.Up(c),
            ),
            merged,
        )
    }

    @Test fun wheelSwipesFromOnePlaceAddUpWithinTheScreen() {
        planner.plan(event(InputEvent.MOUSE_MOVE, 279, 640), 0)
        val one = planner.plan(event(InputEvent.WHEEL, -120), 0)
        val two = planner.plan(event(InputEvent.WHEEL, -120), 0)
        val merged = planner.merge(one + two).single() as TouchStep.Swipe
        assertEquals(merged.from.y - 330f, merged.to.y, 0.01f)
        val many = (1..10).flatMap { planner.plan(event(InputEvent.WHEEL, -120), 0) }
        val capped = planner.merge(many).single() as TouchStep.Swipe
        assertTrue(capped.to.y >= 0f)
        assertEquals(capped.from.y - 825f, capped.to.y, 0.01f)
    }
}
