package io.github.arnoneto1.midview.agent.agent

// "celular" or "tablet" in what the device says, to the student and to the
// teacher, by the kind the cable setup gave (CableSetup.isTablet; set when
// the agent starts and when the setup is saved). Both words are masculine in
// Portuguese, so only the word itself changes ("deste celular" -> "deste
// tablet"); "celulares", the class's devices, stays. A phone's texts are
// left exactly as they are.
object DeviceWord {
    @Volatile var tablet: Boolean = false

    private val WORD = Regex("\\b([Cc])elular\\b")

    fun say(text: String): String =
        if (!tablet) text else WORD.replace(text) { if (it.groupValues[1] == "C") "Tablet" else "tablet" }
}
