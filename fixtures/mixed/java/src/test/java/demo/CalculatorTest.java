package demo;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import static org.junit.jupiter.api.Assertions.assertEquals;

class CalculatorTest {
    @ParameterizedTest
    @ValueSource(ints = {1, 2})
    void choosesPositive(int value) { check(value); }

    private void check(int value) { assertEquals(value + 1, new Calculator().choose(value)); }

    @Test
    void weakExample() { new Calculator().choose(0); }
}
