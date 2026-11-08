package independent;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;
class OtherTest {
    @Test void number() { assertEquals(7, new Other().number()); }
}
