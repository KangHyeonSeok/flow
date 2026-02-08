import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_calculator/main.dart';

void main() {
  testWidgets('CalculatorApp renders', (WidgetTester tester) async {
    await tester.pumpWidget(const CalculatorApp());
    expect(find.byKey(const ValueKey('display')), findsOneWidget);
  });
}
