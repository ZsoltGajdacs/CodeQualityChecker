package sample.api;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;

class DecisionTest {
    @Test void coversDisabledBranch() {
        assertEquals(0, Decision.value(false));
    }
}
