import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../models/models.dart';
import 'api.dart';
import 'strings.dart';

abstract class TokenStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<void> clear();
}

class SecureTokenStore implements TokenStore {
  static const _key = 'driver_session';
  final _s = const FlutterSecureStorage();

  @override
  Future<String?> read() => _s.read(key: _key);

  @override
  Future<void> write(String value) => _s.write(key: _key, value: value);

  @override
  Future<void> clear() => _s.delete(key: _key);
}

class MemoryTokenStore implements TokenStore {
  String? value;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String v) async => value = v;

  @override
  Future<void> clear() async => value = null;
}

/// Who is signed in. The token lives in secure storage so the driver stays signed in for the shift.
class Session extends ChangeNotifier {
  Session(this._store, this._lang);

  final TokenStore _store;
  final String Function() _lang;
  TokenInfo? info;
  bool restoring = true;

  String? get token => info?.token;

  Future<void> restore() async {
    try {
      final raw = await _store.read();
      if (raw != null) {
        final j = jsonDecode(raw) as Map<String, dynamic>;
        final restored = TokenInfo.fromJson(j);
        if (restored.expiresAt.isAfter(DateTime.now())) info = restored;
      }
    } catch (_) {
      await _store.clear();
    }
    restoring = false;
    notifyListeners();
  }

  Future<void> login(DriverApi api, String email, String password) async {
    final t = await api.login(email, password);
    if (!t.roles.contains('Driver')) throw ApiException(tr(_lang(), 'login.notDriver'));
    await _store.write(jsonEncode({
      'token': t.token,
      'expiresAt': t.expiresAt.toUtc().toIso8601String(),
      'fullName': t.fullName,
      'email': t.email,
      'roles': t.roles,
      'driverId': t.driverId,
    }));
    info = t;
    notifyListeners();
  }

  Future<void> logout() async {
    info = null;
    await _store.clear();
    notifyListeners();
  }
}
