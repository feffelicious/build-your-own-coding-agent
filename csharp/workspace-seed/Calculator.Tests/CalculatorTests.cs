namespace Calculator.Tests;

public class CalculatorTests
{
    private readonly Calculator _calculator = new();

    [Fact]
    public void Add_TwoPositiveNumbers_ReturnsSum()
    {
        Assert.Equal(5, _calculator.Add(2, 3));
    }

    [Fact]
    public void Add_NegativeNumber_ReturnsSum()
    {
        Assert.Equal(-1, _calculator.Add(2, -3));
    }

    [Fact]
    public void Subtract_LargerFromSmaller_ReturnsNegative()
    {
        Assert.Equal(-3, _calculator.Subtract(2, 5));
    }
}
