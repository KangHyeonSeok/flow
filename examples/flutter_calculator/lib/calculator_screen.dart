import 'package:flutter/material.dart';
import 'calculator_logic.dart';

/// Calculator screen with display and button grid.
///
/// All interactive widgets have [ValueKey] for E2E test targeting:
/// - Display: `ValueKey('display')`
/// - Numbers: `ValueKey('btn_0')` .. `ValueKey('btn_9')`
/// - Operators: `ValueKey('btn_plus')`, `btn_minus`, `btn_mul`, `btn_div`
/// - Equals: `ValueKey('btn_eq')`
/// - Clear: `ValueKey('btn_clear')`
class CalculatorScreen extends StatefulWidget {
  const CalculatorScreen({super.key});

  @override
  State<CalculatorScreen> createState() => _CalculatorScreenState();
}

class _CalculatorScreenState extends State<CalculatorScreen> {
  final _logic = CalculatorLogic();

  void _onPressed(String label) {
    setState(() {
      switch (label) {
        case 'C':
          _logic.clear();
        case '=':
          _logic.onEqualsPressed();
        case '+' || '-' || '×' || '÷':
          _logic.onOperatorPressed(label);
        default:
          _logic.onNumberPressed(label);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF1C1C1E),
      body: SafeArea(
        child: Column(
          children: [
            // Display area
            Expanded(
              flex: 2,
              child: Container(
                alignment: Alignment.bottomRight,
                padding: const EdgeInsets.symmetric(
                  horizontal: 24,
                  vertical: 16,
                ),
                child: Text(
                  _logic.display,
                  key: const ValueKey('display'),
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 64,
                    fontWeight: FontWeight.w300,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ),
            const Divider(color: Colors.white24, height: 1),
            // Button grid
            Expanded(flex: 5, child: _buildButtonGrid()),
          ],
        ),
      ),
    );
  }

  Widget _buildButtonGrid() {
    final buttons = [
      ['7', '8', '9', '÷'],
      ['4', '5', '6', '×'],
      ['1', '2', '3', '-'],
      ['C', '0', '=', '+'],
    ];

    return Column(
      children: buttons.map((row) {
        return Expanded(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: row.map((label) {
              return Expanded(child: _buildButton(label));
            }).toList(),
          ),
        );
      }).toList(),
    );
  }

  Widget _buildButton(String label) {
    final isOperator = ['+', '-', '×', '÷', '='].contains(label);
    final isClear = label == 'C';

    Color bgColor;
    Color textColor;

    if (isOperator) {
      bgColor = const Color(0xFFFF9500);
      textColor = Colors.white;
    } else if (isClear) {
      bgColor = const Color(0xFFA5A5A5);
      textColor = Colors.black;
    } else {
      bgColor = const Color(0xFF333333);
      textColor = Colors.white;
    }

    return Padding(
      padding: const EdgeInsets.all(1),
      child: ElevatedButton(
        key: ValueKey(_keyName(label)),
        onPressed: () => _onPressed(label),
        style: ElevatedButton.styleFrom(
          backgroundColor: bgColor,
          foregroundColor: textColor,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(0)),
          padding: EdgeInsets.zero,
          elevation: 0,
        ),
        child: Text(
          label,
          style: const TextStyle(fontSize: 28, fontWeight: FontWeight.w400),
        ),
      ),
    );
  }

  /// Map button labels to E2E-friendly key names.
  String _keyName(String label) {
    return switch (label) {
      '+' => 'btn_plus',
      '-' => 'btn_minus',
      '×' => 'btn_mul',
      '÷' => 'btn_div',
      '=' => 'btn_eq',
      'C' => 'btn_clear',
      _ => 'btn_$label',
    };
  }
}
