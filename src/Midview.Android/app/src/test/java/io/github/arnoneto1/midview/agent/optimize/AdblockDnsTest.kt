package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AdblockDnsTest {
    private val host = "dns.adguard-dns.com"
    private val gw = FakeSiteGateway()
    private val network = FakeDnsNetwork()
    private var blocked = false
    private val dns = AdblockDns(gw, network, internetBlocked = { blocked })

    private val automatic = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
    private val ours = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, host)

    @Test fun theHostIsTheAdFreeDns() {
        assertEquals(host, AdblockDns.HOST)
        assertEquals(20_000L, AdblockDns.VALIDATION_MILLIS)
    }

    // The decision, step by step (the plan's table).

    @Test fun internetBlockedByTheTeacherLeavesTheDnsAlone() {
        for (internetOk in listOf(true, false)) {
            val outcome = AdblockDns.decide(internetOk, internetBlockedByTeacher = true, setResult = null, validated = null)
            assertEquals(DnsOutcome.INTERNET_BLOCKED, outcome)
            assertEquals(PhoneDnsStates.OFF_INTERNET_BLOCKED, outcome.state)
            assertFalse(outcome.backToAutomatic)
        }
    }

    @Test fun noInternetLeavesTheDnsAlone() {
        val outcome = AdblockDns.decide(internetOk = false, internetBlockedByTeacher = false, setResult = null, validated = null)
        assertEquals(DnsOutcome.NO_INTERNET, outcome)
        assertEquals(PhoneDnsStates.OFF_NO_INTERNET, outcome.state)
        assertFalse(outcome.backToAutomatic)
    }

    // Ours on and the internet check, which went through it, failing: the
    // DNS may be what the network keeps out, so back to automatic.
    @Test fun ourDnsOnAndNoInternetGoesBackToAutomatic() {
        val outcome = AdblockDns.decide(internetOk = false, internetBlockedByTeacher = false, setResult = null, validated = null, dnsOn = true)
        assertEquals(DnsOutcome.NOT_VALIDATED, outcome)
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, outcome.state)
        assertTrue(outcome.backToAutomatic)
        // The teacher's block still comes first.
        assertEquals(DnsOutcome.INTERNET_BLOCKED, AdblockDns.decide(false, true, null, null, dnsOn = true))
    }

    @Test fun withInternetTheHostIsSetNext() {
        val outcome = AdblockDns.decide(internetOk = true, internetBlockedByTeacher = false, setResult = null, validated = null)
        assertEquals(DnsOutcome.SET_HOST, outcome)
        assertNull(outcome.state)
    }

    // Android checks the host before taking it and changes nothing.
    @Test fun aHostTheNetworkDoesNotReachIsTheNetworkBlocking() {
        val outcome = AdblockDns.decide(true, false, PrivateDns.SET_ERROR_HOST_NOT_SERVING, null)
        assertEquals(DnsOutcome.HOST_REFUSED, outcome)
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, outcome.state)
        assertFalse(outcome.backToAutomatic)
    }

    @Test fun aSettingAndroidCouldNotSaveIsAnErrorAndGoesBackToAutomatic() {
        val outcome = AdblockDns.decide(true, false, PrivateDns.SET_ERROR_FAILURE_SETTING, null)
        assertEquals(DnsOutcome.ERROR, outcome)
        assertEquals(PhoneDnsStates.OFF_ERROR, outcome.state)
        assertTrue(outcome.backToAutomatic)
    }

    @Test fun aHostTakenWaitsForTheNetworkToValidateIt() {
        val outcome = AdblockDns.decide(true, false, PrivateDns.SET_NO_ERROR, validated = null)
        assertEquals(DnsOutcome.AWAIT_VALIDATION, outcome)
        assertNull(outcome.state)
    }

    @Test fun validatedIsOn() {
        val outcome = AdblockDns.decide(true, false, PrivateDns.SET_NO_ERROR, validated = true)
        assertEquals(DnsOutcome.ON, outcome)
        assertEquals(PhoneDnsStates.ON, outcome.state)
        assertFalse(outcome.backToAutomatic)
    }

    @Test fun notValidatedIn20SecondsGoesBackToAutomatic() {
        val outcome = AdblockDns.decide(true, false, PrivateDns.SET_NO_ERROR, validated = false)
        assertEquals(DnsOutcome.NOT_VALIDATED, outcome)
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, outcome.state)
        assertTrue(outcome.backToAutomatic)
    }

    // The whole step, on the phone's gateway and network.

    @Test fun onWhenTheNetworkValidatesIt() {
        assertEquals(PhoneDnsStates.ON, dns.apply())
        assertEquals(ours, gw.privateDns)
        assertEquals(listOf("host $host"), gw.dnsCalls)
        assertEquals(listOf("check", "check $host", "await $host 20000ms"), network.calls)
    }

    @Test fun internetBlockedByTheTeacherChecksNothingAndChangesNothing() {
        blocked = true
        assertEquals(PhoneDnsStates.OFF_INTERNET_BLOCKED, dns.apply())
        assertTrue(network.calls.isEmpty())
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(automatic, gw.privateDns)
    }

    @Test fun internetBlockedWithTheDnsOnKeepsIt() {
        gw.privateDns = ours
        blocked = true
        assertEquals(PhoneDnsStates.OFF_INTERNET_BLOCKED, dns.apply())
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(ours, gw.privateDns)
    }

    @Test fun noInternetChangesNothing() {
        network.internet = false
        assertEquals(PhoneDnsStates.OFF_NO_INTERNET, dns.apply())
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check"), network.calls)
    }

    @Test fun aHostTheNetworkRefusesChangesNothing() {
        gw.setHostResult = PrivateDns.SET_ERROR_HOST_NOT_SERVING
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("host $host"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check", "check $host"), network.calls)
    }

    // Android's own check of the host has no time limit (minutes on a network
    // that drops the packets): the same check, bounded, comes first, and with
    // no address of the host answering nothing is asked for nor written.
    @Test fun noAddressAnsweringWritesNothing() {
        network.firstAddress = false
        network.ipv4 = false
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check", "check $host"), network.calls)
    }

    // The first address answering, Android's own way is used, even with the
    // IPv4 addresses answering too.
    @Test fun theFirstAddressAnsweringUsesAndroidsOwnWay() {
        network.ipv4 = false
        assertEquals(PhoneDnsStates.ON, dns.apply())
        assertEquals(listOf("host $host"), gw.dnsCalls)
    }

    // A network whose route to the host's IPv6 is broken: Android's own check
    // (the first address, IPv6) would refuse a host the phone can use, since
    // the private DNS uses every address. The settings are written directly.
    @Test fun onlyTheIpv4AnsweringWritesTheSettingsAndValidates() {
        network.firstAddress = false
        assertEquals(PhoneDnsStates.ON, dns.apply())
        assertEquals(listOf("write $host"), gw.dnsCalls)
        assertEquals(ours, gw.privateDns)
        assertEquals(listOf("check", "check $host", "await $host 20000ms"), network.calls)
    }

    @Test fun onlyTheIpv4AnsweringAndNotValidatedGoesBackToAutomatic() {
        network.firstAddress = false
        network.validates = false
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("write $host", "automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
    }

    // A refused write is the network keeping the DNS out, not an error.
    @Test fun aWriteAndroidRefusesIsTheNetworkBlocking() {
        network.firstAddress = false
        gw.refuseWrite = true
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("write $host"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check", "check $host"), network.calls)
    }

    @Test fun aWriteWithNoEffectIsTheNetworkBlocking() {
        network.firstAddress = false
        gw.writeHasNoEffect = true
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("write $host"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check", "check $host"), network.calls)
    }

    // Strict on no host (or another) would leave the phone without names.
    @Test fun aWriteThatLeftTheModeWithoutOurHostGoesBackToAutomatic() {
        network.firstAddress = false
        gw.writeOnlyTheMode = true
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("write $host", "automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
    }

    @Test fun notValidatedGoesBackToAutomatic() {
        network.validates = false
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("host $host", "automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
    }

    // Setting it again would make the network check it from the start.
    @Test fun alreadyOnIsCheckedAgainNotSetAgain() {
        gw.privateDns = ours
        network.firstAddress = false
        network.ipv4 = false
        assertEquals(PhoneDnsStates.ON, dns.apply())
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(listOf("check", "await $host 20000ms"), network.calls)
    }

    @Test fun alreadyOnAndNoLongerValidatedGoesBackToAutomatic() {
        gw.privateDns = ours
        network.validates = false
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
    }

    @Test fun alreadyOnAndTheInternetCheckFailingGoesBackToAutomatic() {
        gw.privateDns = ours
        network.internet = false
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, dns.apply())
        assertEquals(listOf("automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
        assertEquals(listOf("check"), network.calls)
    }

    @Test fun anotherHostIsReplacedByOurs() {
        gw.privateDns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, "dns.example.com")
        assertEquals(PhoneDnsStates.ON, dns.apply())
        assertEquals(listOf("host $host"), gw.dnsCalls)
    }

    @Test fun anErrorGoesBackToAutomatic() {
        gw.refuseDns = true
        assertEquals(PhoneDnsStates.OFF_ERROR, dns.apply())
        assertEquals(listOf("automatic"), gw.dnsCalls)
        assertEquals(automatic, gw.privateDns)
    }

    @Test fun aFailedCheckIsAnErrorAndGoesBackToAutomatic() {
        gw.privateDns = ours
        network.failCheck = IllegalStateException("no thread")
        assertEquals(PhoneDnsStates.OFF_ERROR, dns.apply())
        assertEquals(automatic, gw.privateDns)
    }

    @Test fun notTheDeviceOwnerIsAnError() {
        gw.owner = false
        assertEquals(PhoneDnsStates.OFF_ERROR, dns.apply())
        assertTrue(gw.dnsCalls.isEmpty())
    }

    @Test fun isSetOnlyWithOurHostStrict() {
        assertFalse(dns.isSet())
        gw.privateDns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, "dns.example.com")
        assertFalse(dns.isSet())
        gw.privateDns = ours
        assertTrue(dns.isSet())
        assertTrue(ours.isStrictOn(host))
        assertFalse(PrivateDns(PrivateDns.MODE_OFF, host).isStrictOn(host))
    }
}
