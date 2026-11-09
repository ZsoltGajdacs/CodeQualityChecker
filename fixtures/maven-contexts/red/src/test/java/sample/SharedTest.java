package sample; import org.junit.jupiter.api.Test; import static org.junit.jupiter.api.Assertions.assertEquals; class SharedTest { @Test void checks() { assertEquals(1, new Shared().read()); } }
