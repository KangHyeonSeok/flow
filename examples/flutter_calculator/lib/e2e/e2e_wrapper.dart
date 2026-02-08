import 'package:flutter/material.dart';
import 'e2e_beacon.dart';
import 'e2e_server.dart';
import 'scenario_executor.dart';

/// E2E test bootstrap.
///
/// Wraps the app with a RepaintBoundary for screenshots,
/// starts the HTTP server and UDP beacon.
///
/// Usage in main.dart:
/// ```dart
/// const isE2E = bool.fromEnvironment('E2E_TESTS');
/// if (isE2E) {
///   runApp(E2EWrapper(child: const CalculatorApp()));
/// } else {
///   runApp(const CalculatorApp());
/// }
/// ```
class E2EWrapper extends StatefulWidget {
  final Widget child;

  const E2EWrapper({super.key, required this.child});

  @override
  State<E2EWrapper> createState() => _E2EWrapperState();
}

class _E2EWrapperState extends State<E2EWrapper> {
  final GlobalKey _screenshotKey = GlobalKey();
  late final ScenarioExecutor _executor;
  late final E2EServer _server;
  late final E2EBeacon _beacon;

  @override
  void initState() {
    super.initState();

    _executor = ScenarioExecutor(
      screenshotKey: _screenshotKey,
      onStateChanged: () {
        // Force rebuild to update UI after click actions
        if (mounted) setState(() {});
      },
    );

    _server = E2EServer(executor: _executor, port: E2EServer.defaultPort);

    _beacon = E2EBeacon(
      httpPort: E2EServer.defaultPort,
      appName: 'flutter-calculator',
    );

    _startServices();
  }

  Future<void> _startServices() async {
    try {
      await _server.start();
      await _beacon.start();
      print('[E2E] Services started — ready for test connections');
    } catch (e) {
      print('[E2E] Failed to start services: $e');
    }
  }

  @override
  void dispose() {
    _beacon.stop();
    _server.stop();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return RepaintBoundary(key: _screenshotKey, child: widget.child);
  }
}
