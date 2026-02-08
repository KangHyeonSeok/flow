import 'package:flutter/material.dart';
import 'calculator_screen.dart';
import 'package:flow_e2e_flutter/flow_e2e_flutter.dart';

/// Whether E2E test mode is enabled.
/// Pass `--dart-define=E2E_TESTS=true` at build/run time to activate.
const bool kE2ETests = bool.fromEnvironment('E2E_TESTS');

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final app = const CalculatorApp();

  if (kE2ETests) {
    runApp(
      E2EWrapper(child: app, appName: 'flutter-calculator', version: '1.0.0'),
    );
  } else {
    runApp(app);
  }
}

class CalculatorApp extends StatelessWidget {
  const CalculatorApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Flutter Calculator',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: Colors.blue,
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      home: const CalculatorScreen(),
    );
  }
}
