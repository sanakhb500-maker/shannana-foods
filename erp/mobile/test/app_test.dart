import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shannana_driver/core/api.dart';
import 'package:shannana_driver/core/location.dart';
import 'package:shannana_driver/core/session.dart';
import 'package:shannana_driver/core/settings.dart';
import 'package:shannana_driver/main.dart';
import 'package:shannana_driver/models/models.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// An in-memory server: one trip with two stops, following the real API's rules.
class FakeApi implements DriverApi {
  String status = 'Planned';
  final stops = <Map<String, dynamic>>[
    _stop(11, 1, 'عميل أول', 'Customer One', '0911', 'Street 1', 100),
    _stop(12, 2, 'عميل ثان', 'Customer Two', '', '', 50),
  ];
  final calls = <String>[];
  String? lastReason;
  double? lastCollected;

  static Map<String, dynamic> _stop(int id, int seq, String ar, String en, String phone, String address, double total) => {
        'id': id, 'sequence': seq, 'status': 'Pending', 'notes': null, 'actionAt': null, 'deliveryNumber': 'DLV-$id',
        'customerNameAr': ar, 'customerNameEn': en, 'phone': phone, 'address': address, 'currencyCode': 'SYP',
        'totalAmount': total, 'collected': 0, 'proofIds': <int>[],
        'lines': [{'itemCode': 'X1', 'itemNameAr': 'صنف', 'itemNameEn': 'Item', 'quantity': 2, 'unitPrice': total / 2, 'total': total}],
      };

  Trip get _trip => Trip.fromJson({
        'id': 1, 'number': 'TRP-000001', 'date': '2026-11-10', 'status': status, 'vehiclePlate': 'V-100',
        'capacityKg': 1000, 'totalWeightKg': 20, 'overCapacity': false, 'stops': stops,
      });

  @override
  Future<TokenInfo> login(String email, String password) async {
    calls.add('login');
    if (password != 'good') throw ApiException('بيانات الدخول غير صحيحة', status: 401);
    return TokenInfo(token: 't', expiresAt: DateTime.now().add(const Duration(hours: 5)), fullName: 'سائق تجريبي', email: email, roles: ['Driver'], driverId: 1);
  }

  @override
  Future<List<TripRow>> trips() async =>
      [TripRow(id: 1, number: 'TRP-000001', date: '2026-11-10', status: _trip.status, vehiclePlate: 'V-100', stopCount: 2, doneCount: _trip.doneCount)];

  @override
  Future<Trip> trip(int id) async => _trip;

  @override
  Future<Trip> startTrip(int id) async {
    calls.add('start');
    status = 'InTransit';
    return _trip;
  }

  @override
  Future<Trip> completeTrip(int id) async {
    calls.add('complete');
    if (_trip.hasPending) throw ApiException('Mark every stop');
    status = 'Completed';
    return _trip;
  }

  @override
  Future<Trip> setResult(int tripId, int stopId, {required bool delivered, String? reason, String? notes, double? latitude, double? longitude}) async {
    calls.add(delivered ? 'delivered' : 'failed');
    lastReason = reason;
    stops.firstWhere((s) => s['id'] == stopId)['status'] = delivered ? 'Delivered' : 'Failed';
    return _trip;
  }

  @override
  Future<void> collect(int stopId, {required double amount, required String method, String? notes}) async {
    calls.add('collect:$method');
    lastCollected = amount;
    stops.firstWhere((s) => s['id'] == stopId)['collected'] = amount;
  }

  @override
  Future<void> uploadProof(int stopId, {required String kind, required Uint8List bytes, required String filename}) async {
    calls.add('proof:$kind');
  }
}

Future<(FakeApi, Session)> boot(WidgetTester tester, {bool signedIn = true}) async {
  SharedPreferences.setMockInitialValues({});
  final settings = await Settings.load();
  final session = Session(MemoryTokenStore(), () => settings.lang);
  final api = FakeApi();
  await tester.binding.setSurfaceSize(const Size(412, 915));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  if (signedIn) await session.login(api, 'd@test.local', 'good');
  await session.restore();
  await tester.pumpWidget(DriverApp(settings: settings, session: session, api: api, location: NoLocation()));
  await tester.pumpAndSettle();
  return (api, session);
}

void main() {
  testWidgets('sign in shows the driver their trips; a wrong password shows the server message', (tester) async {
    final (api, _) = await boot(tester, signedIn: false);
    expect(find.text('تسجيل الدخول'), findsNothing);
    final fields = find.byType(TextField);
    await tester.enterText(fields.at(0), 'd@test.local');
    await tester.enterText(fields.at(1), 'bad');
    await tester.tap(find.text('دخول'));
    await tester.pumpAndSettle();
    expect(find.text('بيانات الدخول غير صحيحة'), findsOneWidget);

    await tester.enterText(fields.at(1), 'good');
    await tester.tap(find.text('دخول'));
    await tester.pumpAndSettle();
    expect(api.calls.where((c) => c == 'login').length, 2);
    expect(find.text('رحلاتي'), findsOneWidget);
    expect(find.text('TRP-000001'), findsOneWidget);
  });

  testWidgets('a full trip: start, deliver with money and a failed stop, then complete', (tester) async {
    final (api, _) = await boot(tester);
    await tester.tap(find.text('TRP-000001'));
    await tester.pumpAndSettle();

    // the stops cannot be worked until the trip has started; completing is not offered yet
    expect(find.text('بدء الرحلة'), findsOneWidget);
    await tester.tap(find.text('بدء الرحلة'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('تأكيد'));
    await tester.pumpAndSettle();
    expect(api.status, 'InTransit');
    final complete = find.widgetWithText(FilledButton, 'إنهاء الرحلة');
    expect(tester.widget<FilledButton>(complete).onPressed, isNull); // stops are still pending

    // stop 1: delivered, money collected (amount pre-filled with the total)
    await tester.tap(find.text('عميل أول'));
    await tester.pumpAndSettle();
    expect(find.text('0911'), findsOneWidget);
    await tester.tap(find.text('تم التسليم').last);
    await tester.pumpAndSettle();
    expect(find.text('تأكيد التسليم'), findsWidgets);
    await tester.tap(find.widgetWithText(FilledButton, 'تأكيد التسليم'));
    await tester.pumpAndSettle();
    expect(api.calls, containsAllInOrder(['delivered', 'collect:Cash']));
    expect(api.lastCollected, 100);

    // stop 2: failed needs a reason
    await tester.tap(find.text('عميل ثان'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('تعذّر التسليم').last);
    await tester.pumpAndSettle();
    final submit = find.widgetWithText(FilledButton, 'تسجيل التعذّر');
    expect(tester.widget<FilledButton>(submit).onPressed, isNull);
    await tester.tap(find.text('المحل مغلق'));
    await tester.pumpAndSettle();
    await tester.tap(submit);
    await tester.pumpAndSettle();
    expect(api.lastReason, 'المحل مغلق');

    // back on the trip: everything recorded, so it can be completed
    expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'إنهاء الرحلة')).onPressed, isNotNull);
    await tester.tap(find.text('إنهاء الرحلة'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('تأكيد'));
    await tester.pumpAndSettle();
    expect(api.status, 'Completed');
  });

  testWidgets('the language switch changes the texts', (tester) async {
    await boot(tester);
    expect(find.text('رحلاتي'), findsOneWidget);
    await tester.tap(find.text('English'));
    await tester.pumpAndSettle();
    expect(find.text('My trips'), findsOneWidget);
  });
}
