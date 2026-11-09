Add multiply and divide to the Calculator.
Use these signatures: int Multiply(int a, int b) and double Divide(int a, int b). Divide(7, 2) returns 3.5.
Dividing by zero must throw DivideByZeroException with the message "Cannot divide by zero".
Add tests for the new methods.
All tests, including existing ones, must pass. Do not modify existing tests.

## Review
- multiply and divide exist with these signatures: int Multiply(int a, int b), double Divide(int a, int b)
- divide(7, 2) returns 3.5
- division by zero throws DivideByZeroException with exactly "Cannot divide by zero"
- new tests cover multiply, a non-integer division result, and division by zero
- no existing test was modified (check the diff)
- the test exit code is 0
- multiply and divide each have an XML doc comment (///); divide's mentions DivideByZeroException
