package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Test

class AgentServiceTextTest {
    @Test fun theFixedNotificationSaysWhoManagesThePhone() {
        assertEquals("Celular da escola, gerenciado pelo Midview", AgentService.STATUS_TEXT)
    }
}
