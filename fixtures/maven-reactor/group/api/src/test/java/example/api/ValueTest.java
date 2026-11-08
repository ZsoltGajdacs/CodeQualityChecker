package example.api;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;
class ValueTest {
    @Test void number() { assertEquals(42, new Value().number()); }
}
