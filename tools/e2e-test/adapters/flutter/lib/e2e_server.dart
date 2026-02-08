import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:shelf/shelf.dart';
import 'scenario_executor.dart';

/// HTTP server for E2E test communication.
///
/// Endpoints:
/// - POST /e2e/run    -- Submit test scenario for execution
/// - GET  /e2e/status -- Poll execution status
/// - GET  /e2e/result -- Retrieve results (screenshots, logs)
/// - GET  /e2e/health -- Health check
Map<String, String> _extractHeaders(HttpHeaders httpHeaders) {
  final map = <String, String>{};
  httpHeaders.forEach((name, values) {
    map[name] = values.join(', ');
  });
  return map;
}

class E2EServer {
  static const int defaultPort = 51321;

  final int port;
  final String appName;
  final String platform;
  final ScenarioExecutor executor;

  HttpServer? _server;
  String _sessionId = '';

  E2EServer({
    required this.executor,
    required this.appName,
    this.platform = 'flutter',
    this.port = defaultPort,
  });

  /// Start the HTTP server.
  Future<void> start() async {
    final handler = const Pipeline().addHandler(_handleRequest);

    _server = await HttpServer.bind(InternetAddress.anyIPv4, port);
    print('[E2EServer] Listening on http://0.0.0.0:$port');

    _server!.listen((HttpRequest request) async {
      try {
        final shelfRequest = Request(
          request.method,
          request.uri.replace(scheme: 'http', host: 'localhost', port: port),
          body: request,
          headers: _extractHeaders(request.headers),
        );

        final response = await handler(shelfRequest);

        request.response.statusCode = response.statusCode;
        request.response.headers.contentType = ContentType.json;
        response.headers.forEach((key, value) {
          if (key.toLowerCase() != 'transfer-encoding') {
            request.response.headers.set(key, value);
          }
        });

        final body = await response.readAsString();
        request.response.write(body);
        await request.response.close();
      } catch (e) {
        request.response.statusCode = 500;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({'error': e.toString()}));
        await request.response.close();
      }
    });
  }

  Future<Response> _handleRequest(Request request) async {
    final path = request.url.path;
    final method = request.method.toUpperCase();

    if (method == 'POST' && path == 'e2e/run') {
      return await _handleRun(request);
    }
    if (method == 'GET' && path.startsWith('e2e/status')) {
      return _handleStatus();
    }
    if (method == 'GET' && path.startsWith('e2e/result')) {
      return _handleResult();
    }
    if (method == 'GET' && path == 'e2e/health') {
      return _handleHealth();
    }
    return _jsonResponse(404, {'error': 'Not found: /$path'});
  }

  Future<Response> _handleRun(Request request) async {
    if (executor.status == 'running') {
      return _jsonResponse(409, {'error': 'Test already running'});
    }

    try {
      final body = await request.readAsString();
      if (body.isEmpty) {
        return _jsonResponse(400, {'error': 'Request body is empty'});
      }

      final decoded = jsonDecode(body) as Map<String, dynamic>;
      _sessionId = 'e2e-${DateTime.now().millisecondsSinceEpoch}';

      final scenario = decoded.containsKey('scenario')
          ? decoded['scenario'] as Map<String, dynamic>
          : decoded;

      Future.microtask(() => executor.executeScenario(scenario));

      return _jsonResponse(200, {
        'session_id': _sessionId,
        'status': 'running',
      });
    } catch (e) {
      return _jsonResponse(400, {'error': 'Invalid JSON: $e'});
    }
  }

  Response _handleStatus() {
    return _jsonResponse(200, {
      'status': executor.status,
      'progress': executor.progress,
      'current_step': executor.currentStep,
      'total_steps': executor.totalSteps,
    });
  }

  Response _handleResult() {
    if (executor.status == 'running') {
      return _jsonResponse(409, {'error': 'Test still running'});
    }
    if (executor.status == 'idle') {
      return _jsonResponse(404, {'error': 'No test results available'});
    }

    return _jsonResponse(200, executor.toResultMap());
  }

  Response _handleHealth() {
    return _jsonResponse(200, {
      'status': 'ok',
      'app': appName,
      'platform': platform,
    });
  }

  Response _jsonResponse(int statusCode, Map<String, dynamic> body) {
    return Response(
      statusCode,
      body: jsonEncode(body),
      headers: {'content-type': 'application/json'},
    );
  }

  /// Stop the server.
  Future<void> stop() async {
    await _server?.close();
    _server = null;
    print('[E2EServer] Stopped');
  }
}
