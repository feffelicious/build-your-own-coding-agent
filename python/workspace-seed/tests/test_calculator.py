from calculator import Calculator

calculator = Calculator()


def test_add_two_positive_numbers_returns_sum():
    assert calculator.add(2, 3) == 5


def test_add_negative_number_returns_sum():
    assert calculator.add(2, -3) == -1


def test_subtract_larger_from_smaller_returns_negative():
    assert calculator.subtract(2, 5) == -3
