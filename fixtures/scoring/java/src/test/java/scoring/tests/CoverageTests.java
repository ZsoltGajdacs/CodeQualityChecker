package scoring.tests;

import org.junit.jupiter.api.Test;
import scoring.AtComplexity10;
import scoring.Healthy;
import scoring.OverComplexity11;
import static org.junit.jupiter.api.Assertions.assertEquals;

public class CoverageTests {
    @Test
    public void healthyHasBothInputs() {
        assertEquals(0, new Healthy().evaluate(-1));
        assertEquals(101, new Healthy().evaluate(100));
    }

    @Test
    public void atComplexityBoundaryHasBothInputs() {
        assertEquals(0, new AtComplexity10().evaluate(-1));
        assertEquals(9, new AtComplexity10().evaluate(100));
    }

    @Test
    public void overComplexityBoundaryHasBothInputs() {
        assertEquals(0, new OverComplexity11().evaluate(-1));
        assertEquals(10, new OverComplexity11().evaluate(100));
    }
}
