import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../models/models.dart';

/// A failed call: the server's (already translated) message, or a connection problem.
class ApiException implements Exception {
  ApiException(this.message, {this.status, this.network = false});

  final String message;
  final int? status;
  final bool network;

  bool get unauthorized => status == 401;

  @override
  String toString() => message;
}

/// What the app needs from the server. The real implementation talks HTTP; tests plug in a fake.
abstract class DriverApi {
  Future<TokenInfo> login(String email, String password);
  Future<List<TripRow>> trips();
  Future<Trip> trip(int id);
  Future<Trip> startTrip(int id);
  Future<Trip> completeTrip(int id);
  Future<Trip> setResult(int tripId, int stopId, {required bool delivered, String? reason, String? notes, double? latitude, double? longitude});
  Future<void> collect(int stopId, {required double amount, required String method, String? notes});
  Future<void> uploadProof(int stopId, {required String kind, required Uint8List bytes, required String filename});
}

class HttpDriverApi implements DriverApi {
  HttpDriverApi({required String baseUrl, required this.tokenProvider, required this.langProvider, this.onUnauthorized})
      : _dio = Dio(BaseOptions(baseUrl: '$baseUrl/api/v1', connectTimeout: const Duration(seconds: 15), receiveTimeout: const Duration(seconds: 30)));

  final Dio _dio;
  final String? Function() tokenProvider;
  final String Function() langProvider;
  final void Function()? onUnauthorized;

  Future<dynamic> _send(String method, String path, {Object? data}) async {
    try {
      final token = tokenProvider();
      final res = await _dio.request<dynamic>(
        path,
        data: data,
        options: Options(method: method, headers: {if (token != null) 'Authorization': 'Bearer $token', 'X-Lang': langProvider()}),
      );
      final body = res.data;
      return body is Map<String, dynamic> ? body['data'] : body;
    } on DioException catch (e) {
      final body = e.response?.data;
      final status = e.response?.statusCode;
      if (status == 401 && tokenProvider() != null) onUnauthorized?.call();
      if (body is Map && body['message'] is String) {
        final errors = body['errors'];
        final detail = errors is Map ? errors.values.expand((v) => v is List ? v : [v]).join(' ') : '';
        throw ApiException(detail.isEmpty ? body['message'] as String : '${body['message']} $detail', status: status);
      }
      throw ApiException(e.message ?? 'Connection error', status: status, network: status == null);
    }
  }

  @override
  Future<TokenInfo> login(String email, String password) async =>
      TokenInfo.fromJson(await _send('POST', '/auth/token', data: {'email': email.trim(), 'password': password}) as Map<String, dynamic>);

  @override
  Future<List<TripRow>> trips() async =>
      ((await _send('GET', '/driver/trips')) as List).map((j) => TripRow.fromJson(j as Map<String, dynamic>)).toList();

  @override
  Future<Trip> trip(int id) async => Trip.fromJson(await _send('GET', '/driver/trips/$id') as Map<String, dynamic>);

  @override
  Future<Trip> startTrip(int id) async => Trip.fromJson(await _send('POST', '/driver/trips/$id/start') as Map<String, dynamic>);

  @override
  Future<Trip> completeTrip(int id) async => Trip.fromJson(await _send('POST', '/driver/trips/$id/complete') as Map<String, dynamic>);

  @override
  Future<Trip> setResult(int tripId, int stopId, {required bool delivered, String? reason, String? notes, double? latitude, double? longitude}) async =>
      Trip.fromJson(await _send('PUT', '/driver/trips/$tripId/stops/$stopId', data: {
        'status': delivered ? 'Delivered' : 'Failed',
        'reason': reason,
        'notes': notes,
        'latitude': latitude,
        'longitude': longitude,
      }) as Map<String, dynamic>);

  @override
  Future<void> collect(int stopId, {required double amount, required String method, String? notes}) async {
    await _send('POST', '/driver/stops/$stopId/collections', data: {'amount': amount, 'method': method, 'notes': notes});
  }

  @override
  Future<void> uploadProof(int stopId, {required String kind, required Uint8List bytes, required String filename}) async {
    final form = FormData.fromMap({'kind': kind, 'file': MultipartFile.fromBytes(bytes, filename: filename)});
    await _send('POST', '/driver/stops/$stopId/proof', data: form);
  }
}
