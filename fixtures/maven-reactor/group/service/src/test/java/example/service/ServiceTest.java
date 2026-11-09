package example.service;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;
class ServiceTest {
    @Test void number() { assertEquals(example.api.FixtureSupport.expected(), new Service().number()); }
}
