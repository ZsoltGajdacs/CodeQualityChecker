package example.service;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;
class ServiceTest {
    @Test void number() { assertEquals(42, new Service().number()); }
}
