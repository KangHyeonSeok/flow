/// Calculator business logic.
///
/// Handles number input, operator selection, calculation, and clear.
/// Designed to be UI-framework agnostic.
class CalculatorLogic {
  String _display = '0';
  String _operand1 = '';
  String _operator = '';
  bool _isNewInput = true;

  /// Current display value.
  String get display => _display;

  /// Current operator (empty if none).
  String get operator => _operator;

  /// Handle number button press (0-9).
  void onNumberPressed(String number) {
    if (_isNewInput) {
      _display = number;
      _isNewInput = false;
    } else {
      if (_display == '0' && number == '0') return;
      if (_display == '0') {
        _display = number;
      } else {
        _display += number;
      }
    }
  }

  /// Handle operator button press (+, -, ×, ÷).
  void onOperatorPressed(String op) {
    if (_operator.isNotEmpty && !_isNewInput) {
      _calculate();
    }
    _operand1 = _display;
    _operator = op;
    _isNewInput = true;
  }

  /// Handle equals button press.
  void onEqualsPressed() {
    if (_operator.isEmpty) return;
    _calculate();
  }

  /// Handle clear button press.
  void clear() {
    _display = '0';
    _operand1 = '';
    _operator = '';
    _isNewInput = true;
  }

  void _calculate() {
    if (_operand1.isEmpty || _operator.isEmpty) return;

    final num1 = double.tryParse(_operand1) ?? 0;
    final num2 = double.tryParse(_display) ?? 0;
    double result;

    switch (_operator) {
      case '+':
        result = num1 + num2;
      case '-':
        result = num1 - num2;
      case '×':
        result = num1 * num2;
      case '÷':
        if (num2 == 0) {
          _display = 'Error';
          _operator = '';
          _isNewInput = true;
          return;
        }
        result = num1 / num2;
      default:
        return;
    }

    // Display as integer if no decimal part
    if (result == result.truncateToDouble()) {
      _display = result.toInt().toString();
    } else {
      _display = result.toString();
    }

    _operand1 = _display;
    _operator = '';
    _isNewInput = true;
  }
}
