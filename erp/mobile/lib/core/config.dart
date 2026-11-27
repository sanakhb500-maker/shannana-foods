import 'package:flutter/foundation.dart';

/// API address, set at build time: `--dart-define=API_BASE=http://192.168.1.20:5004`.
/// Defaults: Android emulator (10.0.2.2 is the PC) and localhost for the web preview.
class Config {
  static const _fromEnv = String.fromEnvironment('API_BASE');

  static String get apiBase {
    if (_fromEnv.isNotEmpty) return _fromEnv;
    return kIsWeb ? 'http://localhost:5004' : 'http://10.0.2.2:5004';
  }
}
