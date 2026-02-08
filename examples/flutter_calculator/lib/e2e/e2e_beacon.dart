import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// UDP broadcast beacon for E2E test app discovery.
///
/// Broadcasts connection info (app name, platform, port) on UDP port 51320
/// at 1-second intervals so the Python E2E test tool can discover this app.
///
/// Only active when `E2E_TESTS=true` dart define is set.
class E2EBeacon {
  /// Fixed UDP discovery port (must match Python listener).
  static const int discoveryPort = 51320;

  /// Broadcast interval.
  static const Duration interval = Duration(seconds: 1);

  final int httpPort;
  final String appName;

  RawDatagramSocket? _socket;
  Timer? _timer;

  E2EBeacon({required this.httpPort, this.appName = 'flutter-calculator'});

  /// Start broadcasting.
  Future<void> start() async {
    _socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
    _socket!.broadcastEnabled = true;

    _timer = Timer.periodic(interval, (_) => _broadcast());
    _broadcast(); // Immediate first broadcast

    print(
      '[E2EBeacon] Broadcasting on UDP port $discoveryPort '
      '(app=$appName, httpPort=$httpPort)',
    );
  }

  void _broadcast() {
    if (_socket == null) return;

    final message = jsonEncode({
      'app': appName,
      'platform': 'flutter',
      'port': httpPort,
      'version': '1.0.0',
    });

    try {
      final data = utf8.encode(message);
      _socket!.send(data, InternetAddress('255.255.255.255'), discoveryPort);
    } catch (e) {
      // Network temporarily unavailable — skip this cycle
    }
  }

  /// Stop broadcasting and release resources.
  void stop() {
    _timer?.cancel();
    _timer = null;
    _socket?.close();
    _socket = null;
    print('[E2EBeacon] Stopped');
  }
}
