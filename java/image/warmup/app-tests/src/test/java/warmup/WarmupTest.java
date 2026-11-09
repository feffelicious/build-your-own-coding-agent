package warmup;

import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/**
 * Run once in the image with app-tests/pom.xml, so Maven fetches Surefire's JUnit provider
 * and everything else the tests need, without the participant's code in the image.
 */
class WarmupTest {
    @Test
    void runs() {
        assertTrue(true);
    }

    @ParameterizedTest
    @CsvSource({"a, b"})
    void runsWithParameters(String first, String second) {
        assertTrue(!first.equals(second));
    }
}
