package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Test

class AgentServiceTextTest {
    @Test fun theTilesStatusSaysTheScreenIsNotShownYet() {
        assertEquals("Online. A tela do celular ainda não aparece aqui.", AgentService.THUMBNAIL_STATUS)
    }

    @Test fun theFixedNotificationSaysWhoManagesThePhone() {
        assertEquals("Celular da escola, gerenciado pelo Midview", AgentService.STATUS_TEXT)
    }
}
