import 'package:flutter/foundation.dart';

import '../../core/api.dart';
import '../../models/models.dart';

/// State of one open trip, shared by the trip screen and its stop screens so they always show the same data.
class TripController extends ChangeNotifier {
  TripController(this.api, this.tripId);

  final DriverApi api;
  final int tripId;

  Trip? trip;
  bool loading = true;
  bool busy = false;
  Object? error;

  Future<void> load() async {
    loading = trip == null;
    error = null;
    notifyListeners();
    try {
      trip = await api.trip(tripId);
    } catch (e) {
      error = e;
    }
    loading = false;
    notifyListeners();
  }

  void replace(Trip t) {
    trip = t;
    notifyListeners();
  }

  /// Runs an action that returns the updated trip (start, complete, stop result). Errors go to the caller.
  Future<void> run(Future<Trip> Function() action) async {
    busy = true;
    notifyListeners();
    try {
      trip = await action();
    } finally {
      busy = false;
      notifyListeners();
    }
  }
}
