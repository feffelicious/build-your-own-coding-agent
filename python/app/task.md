Add multiply and divide to the Calculator.
Use these signatures: multiply(a: int, b: int) -> int and divide(a: int, b: int) -> float. divide(7, 2) returns 3.5.
Dividing by zero must throw ZeroDivisionError with the message "Cannot divide by zero".
Add tests for the new methods.
All tests, including existing ones, must pass. Do not modify existing tests.

## Review
- multiply and divide exist with these signatures: multiply(a: int, b: int) -> int, divide(a: int, b: int) -> float
- divide(7, 2) returns 3.5
- division by zero throws ZeroDivisionError with exactly "Cannot divide by zero"
- new tests cover multiply, a non-integer division result, and division by zero
- no existing test was modified (check the diff)
- the test exit code is 0
- multiply and divide each have a docstring; divide's mentions ZeroDivisionError
