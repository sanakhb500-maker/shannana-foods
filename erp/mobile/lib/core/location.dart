import 'package:geolocator/geolocator.dart';

class GeoPoint {
  const GeoPoint(this.latitude, this.longitude);

  final double latitude;
  final double longitude;
}

/// The phone's position when a result is recorded. Always optional: any failure just means "no position".
abstract class LocationService {
  Future<GeoPoint?> current();
}

class GeolocatorService implements LocationService {
  /// Never holds the driver up: a pending permission prompt or a slow fix gives up after a few seconds.
  @override
  Future<GeoPoint?> current() => _read().timeout(const Duration(seconds: 6), onTimeout: () => null);

  Future<GeoPoint?> _read() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return null;
      var perm = await Geolocator.checkPermission();
      if (perm == LocationPermission.denied) perm = await Geolocator.requestPermission();
      if (perm == LocationPermission.denied || perm == LocationPermission.deniedForever) return null;
      final p = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: Duration(seconds: 6)),
      );
      return GeoPoint(p.latitude, p.longitude);
    } catch (_) {
      return null;
    }
  }
}

class NoLocation implements LocationService {
  @override
  Future<GeoPoint?> current() async => null;
}
