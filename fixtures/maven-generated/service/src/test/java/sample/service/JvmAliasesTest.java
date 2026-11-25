package sample.service;

import java.util.List;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class JvmAliasesTest {
    @Test
    void coversGenericBoundsAndConstructorOverloads() {
        var aliases = new JvmAliases<Integer>();
        assertEquals(3, aliases.measure(List.of("label"), new Integer[]{1}, 2));
        assertEquals(7, JvmAliases.Mode.SHORT.value());
        assertEquals(10, JvmAliases.Mode.LONG.value());
        assertEquals(4, aliases.new Inner(4).value());
        assertEquals(5, new JvmAliases.Nested(aliases, 5).value());
    }
}
