package scoring.tests;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;

public class BranchingTests {
    @Test
    public void branchesOnTrueVariable() {
        boolean condition = true;
        int value = 0;
        if (condition) {
            value++;
        }
        assertEquals(1, value);
    }
}
