import 'dart:async';
import 'dart:convert';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

/// Executes E2E test scenario steps against the live Flutter Widget tree.
///
/// Supports step types:
/// - `click` -- Find widget by ValueKey and invoke onPressed/onTap
/// - `wait` -- Delay for specified milliseconds
/// - `screenshot` -- Capture current screen as PNG (base64)
/// - `input` -- Set text on an InputField
class ScenarioExecutor {
  /// Key of the RepaintBoundary wrapping the app for screenshots.
  final GlobalKey screenshotKey;

  /// Callback to trigger setState after UI manipulation.
  final VoidCallback? onStateChanged;

  final Map<String, String> _screenshots = {};
  final List<Map<String, dynamic>> _logs = [];
  String _status = 'idle';
  int _currentStep = 0;
  int _totalSteps = 0;
  String? _error;

  ScenarioExecutor({required this.screenshotKey, this.onStateChanged});

  String get status => _status;
  int get currentStep => _currentStep;
  int get totalSteps => _totalSteps;
  double get progress => _totalSteps > 0 ? _currentStep / _totalSteps : 0;
  Map<String, String> get screenshots => Map.unmodifiable(_screenshots);
  List<Map<String, dynamic>> get logs => List.unmodifiable(_logs);
  String? get error => _error;

  /// Execute a full scenario (list of steps).
  Future<void> executeScenario(Map<String, dynamic> scenario) async {
    final steps =
        (scenario['steps'] as List?)?.cast<Map<String, dynamic>>() ?? [];
    _totalSteps = steps.length;
    _currentStep = 0;
    _status = 'running';
    _screenshots.clear();
    _logs.clear();
    _error = null;

    _addLog('info', 'Starting scenario with $_totalSteps steps');

    try {
      for (var i = 0; i < steps.length; i++) {
        _currentStep = i + 1;
        final step = steps[i];
        _addLog(
          'info',
          'Step $_currentStep/$_totalSteps: ${step['type']} -- ${step['target'] ?? ''}',
        );

        await executeStep(step);
      }

      _status = 'completed';
      _addLog('info', 'All steps completed successfully');
    } catch (e) {
      _error = e.toString();
      _status = 'failed';
      _addLog('error', 'Step $_currentStep failed: $e');
    }
  }

  /// Execute a single step.
  Future<void> executeStep(Map<String, dynamic> step) async {
    final type = step['type'] as String? ?? '';

    switch (type) {
      case 'click':
        await _executeClick(step);
      case 'wait':
        await _executeWait(step);
      case 'screenshot':
        await _executeScreenshot(step);
      case 'input':
        await _executeInput(step);
      default:
        _addLog('warn', 'Unknown step type: $type');
        throw Exception('Unknown step type: $type');
    }
  }

  Future<void> _executeClick(Map<String, dynamic> step) async {
    final target = step['target'] as String? ?? '';
    if (target.isEmpty) throw Exception('Click step requires target');

    final element = _findElementByValueKey(target);
    if (element == null) {
      throw Exception('Widget not found: $target');
    }

    bool tapped = false;

    void visitor(Element el) {
      if (tapped) return;

      final widget = el.widget;

      if (widget is ElevatedButton ||
          widget is TextButton ||
          widget is OutlinedButton) {
        if (widget is ElevatedButton && widget.onPressed != null) {
          widget.onPressed!();
          tapped = true;
          return;
        }
        if (widget is TextButton && widget.onPressed != null) {
          widget.onPressed!();
          tapped = true;
          return;
        }
        if (widget is OutlinedButton && widget.onPressed != null) {
          widget.onPressed!();
          tapped = true;
          return;
        }
      }

      if (widget is GestureDetector && widget.onTap != null) {
        widget.onTap!();
        tapped = true;
        return;
      }

      if (widget is InkWell && widget.onTap != null) {
        widget.onTap!();
        tapped = true;
        return;
      }

      el.visitChildren(visitor);
    }

    visitor(element);

    if (!tapped) {
      Element? current = element;
      while (current != null && !tapped) {
        final widget = current.widget;
        if (widget is ElevatedButton && widget.onPressed != null) {
          widget.onPressed!();
          tapped = true;
        } else if (widget is TextButton && widget.onPressed != null) {
          widget.onPressed!();
          tapped = true;
        } else if (widget is GestureDetector && widget.onTap != null) {
          widget.onTap!();
          tapped = true;
        } else if (widget is InkWell && widget.onTap != null) {
          widget.onTap!();
          tapped = true;
        }

        if (!tapped) {
          Element? parent;
          current.visitAncestorElements((ancestor) {
            parent = ancestor;
            return false;
          });
          current = parent;
        }
      }
    }

    if (!tapped) {
      throw Exception('No tappable widget found for: $target');
    }

    onStateChanged?.call();
    await Future.delayed(const Duration(milliseconds: 50));
    _addLog('info', 'Clicked $target');
  }

  Future<void> _executeWait(Map<String, dynamic> step) async {
    final ms = (step['ms'] as num?)?.toInt() ?? 1000;
    await Future.delayed(Duration(milliseconds: ms));
    _addLog('info', 'Waited ${ms}ms');
  }

  Future<void> _executeScreenshot(Map<String, dynamic> step) async {
    final name = step['target'] as String? ?? 'screenshot_$_currentStep';

    await Future.delayed(const Duration(milliseconds: 100));

    try {
      final boundary = screenshotKey.currentContext?.findRenderObject()
          as RenderRepaintBoundary?;
      if (boundary == null) {
        throw Exception('RepaintBoundary not found for screenshot');
      }

      final image = await boundary.toImage(pixelRatio: 2.0);
      final byteData = await image.toByteData(format: ui.ImageByteFormat.png);
      if (byteData == null) {
        throw Exception('Failed to encode screenshot as PNG');
      }

      final base64Data = base64Encode(byteData.buffer.asUint8List());
      _screenshots[name] = base64Data;
      _addLog(
        'info',
        'Screenshot captured: $name (${base64Data.length} bytes)',
      );
    } catch (e) {
      _addLog('error', 'Screenshot failed: $e');
      rethrow;
    }
  }

  Future<void> _executeInput(Map<String, dynamic> step) async {
    final target = step['target'] as String? ?? '';
    final text = step['text'] as String? ?? '';
    if (target.isEmpty) throw Exception('Input step requires target');

    final element = _findElementByValueKey(target);
    if (element == null) {
      throw Exception('Widget not found: $target');
    }

    bool inputSet = false;
    void visitor(Element el) {
      if (inputSet) return;
      if (el.widget is EditableText) {
        final editableText = el.widget as EditableText;
        editableText.controller.text = text;
        inputSet = true;
        return;
      }
      el.visitChildren(visitor);
    }

    visitor(element);

    if (!inputSet) {
      throw Exception('No input field found for: $target');
    }

    onStateChanged?.call();
    await Future.delayed(const Duration(milliseconds: 50));
    _addLog('info', 'Input set for $target');
  }

  Map<String, dynamic> toResultMap() {
    return {
      'status': _status,
      'screenshots': _screenshots.entries
          .map((e) => {'name': e.key, 'data': e.value})
          .toList(),
      'logs': _logs,
      if (_error != null) 'error': _error,
    };
  }

  void _addLog(String level, String message) {
    _logs.add({
      'timestamp': DateTime.now().toUtc().toIso8601String(),
      'level': level,
      'message': message,
    });
  }

  Element? _findElementByValueKey(String key) {
    Element? result;
    void visitor(Element element) {
      if (result != null) return;
      if (element.widget.key == ValueKey<String>(key)) {
        result = element;
        return;
      }
      element.visitChildren(visitor);
    }

    WidgetsBinding.instance.rootElement?.visitChildren(visitor);
    return result;
  }
}
