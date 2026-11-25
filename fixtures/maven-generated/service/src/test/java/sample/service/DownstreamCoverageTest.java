package sample.service;

import org.junit.jupiter.api.Test;
import sample.api.Decision;
import sample.upstream.DownstreamOnly;
import static org.junit.jupiter.api.Assertions.assertEquals;

class DownstreamCoverageTest {
    @Test void coversUpstreamBranches() {
        assertEquals(1, Decision.value(true));
        assertEquals(2, DownstreamOnly.measure(true));
    }
}
