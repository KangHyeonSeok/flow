import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_calculator/calculator_logic.dart';

void main() {
  late CalculatorLogic calc;

  setUp(() {
    calc = CalculatorLogic();
  });

  group('CalculatorLogic', () {
    test('initial display is 0', () {
      expect(calc.display, '0');
    });

    test('number input replaces initial 0', () {
      calc.onNumberPressed('7');
      expect(calc.display, '7');
    });

    test('multiple number input concatenates', () {
      calc.onNumberPressed('9');
      calc.onNumberPressed('9');
      calc.onNumberPressed('9');
      expect(calc.display, '999');
    });

    test('7 + 3 = 10', () {
      calc.onNumberPressed('7');
      calc.onOperatorPressed('+');
      calc.onNumberPressed('3');
      calc.onEqualsPressed();
      expect(calc.display, '10');
    });

    test('5 × 4 = 20', () {
      calc.onNumberPressed('5');
      calc.onOperatorPressed('×');
      calc.onNumberPressed('4');
      calc.onEqualsPressed();
      expect(calc.display, '20');
    });

    test('sequential: 5 × 4 - 2 = 18', () {
      calc.onNumberPressed('5');
      calc.onOperatorPressed('×');
      calc.onNumberPressed('4');
      calc.onOperatorPressed('-'); // triggers 5*4=20, then sets operator to -
      calc.onNumberPressed('2');
      calc.onEqualsPressed(); // 20-2=18
      expect(calc.display, '18');
    });

    test('9 - 3 = 6', () {
      calc.onNumberPressed('9');
      calc.onOperatorPressed('-');
      calc.onNumberPressed('3');
      calc.onEqualsPressed();
      expect(calc.display, '6');
    });

    test('8 ÷ 4 = 2', () {
      calc.onNumberPressed('8');
      calc.onOperatorPressed('÷');
      calc.onNumberPressed('4');
      calc.onEqualsPressed();
      expect(calc.display, '2');
    });

    test('division by zero shows Error', () {
      calc.onNumberPressed('5');
      calc.onOperatorPressed('÷');
      calc.onNumberPressed('0');
      calc.onEqualsPressed();
      expect(calc.display, 'Error');
    });

    test('clear resets to 0', () {
      calc.onNumberPressed('9');
      calc.onNumberPressed('9');
      calc.onNumberPressed('9');
      expect(calc.display, '999');
      calc.clear();
      expect(calc.display, '0');
    });

    test('equals without operator does nothing', () {
      calc.onNumberPressed('5');
      calc.onEqualsPressed();
      expect(calc.display, '5');
    });

    test('leading zeros are prevented', () {
      calc.onNumberPressed('0');
      calc.onNumberPressed('0');
      calc.onNumberPressed('5');
      expect(calc.display, '5');
    });
  });
}
