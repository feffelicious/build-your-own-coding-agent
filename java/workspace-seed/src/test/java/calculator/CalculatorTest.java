package calculator;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class CalculatorTest {
    private final Calculator calculator = new Calculator();

    @Test
    void addTwoPositiveNumbersReturnsSum() {
        assertEquals(5, calculator.add(2, 3));
    }

    @Test
    void addNegativeNumberReturnsSum() {
        assertEquals(-1, calculator.add(2, -3));
    }

    @Test
    void subtractLargerFromSmallerReturnsNegative() {
        assertEquals(-3, calculator.subtract(2, 5));
    }
}
