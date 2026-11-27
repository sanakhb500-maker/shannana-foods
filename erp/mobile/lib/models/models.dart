// Plain data classes mirroring the driver API (camelCase JSON, enums as strings).

enum TripStatus { planned, inTransit, completed, cancelled }

enum StopStatus { pending, delivered, failed }

TripStatus _tripStatus(String? s) => switch (s) {
      'InTransit' => TripStatus.inTransit,
      'Completed' => TripStatus.completed,
      'Cancelled' => TripStatus.cancelled,
      _ => TripStatus.planned,
    };

StopStatus _stopStatus(String? s) => switch (s) {
      'Delivered' => StopStatus.delivered,
      'Failed' => StopStatus.failed,
      _ => StopStatus.pending,
    };

double _num(Object? v) => (v as num?)?.toDouble() ?? 0;

class TokenInfo {
  TokenInfo({required this.token, required this.expiresAt, required this.fullName, required this.email, required this.roles, this.driverId});

  final String token;
  final DateTime expiresAt;
  final String fullName;
  final String email;
  final List<String> roles;
  final int? driverId;

  factory TokenInfo.fromJson(Map<String, dynamic> j) => TokenInfo(
        token: j['token'] as String,
        expiresAt: DateTime.parse(j['expiresAt'] as String), // the server sends UTC with a Z
        fullName: j['fullName'] as String? ?? '',
        email: j['email'] as String? ?? '',
        roles: (j['roles'] as List? ?? []).cast<String>(),
        driverId: j['driverId'] as int?,
      );
}

class TripRow {
  TripRow({required this.id, this.number, required this.date, required this.status, required this.vehiclePlate, required this.stopCount, required this.doneCount});

  final int id;
  final String? number;
  final String date;
  final TripStatus status;
  final String vehiclePlate;
  final int stopCount;
  final int doneCount;

  factory TripRow.fromJson(Map<String, dynamic> j) => TripRow(
        id: j['id'] as int,
        number: j['number'] as String?,
        date: j['date'] as String,
        status: _tripStatus(j['status'] as String?),
        vehiclePlate: j['vehiclePlate'] as String? ?? '',
        stopCount: j['stopCount'] as int? ?? 0,
        doneCount: j['doneCount'] as int? ?? 0,
      );
}

class StopLine {
  StopLine({required this.code, required this.nameAr, required this.nameEn, required this.quantity, required this.unitPrice, required this.total});

  final String code;
  final String nameAr;
  final String nameEn;
  final double quantity;
  final double unitPrice;
  final double total;

  factory StopLine.fromJson(Map<String, dynamic> j) => StopLine(
        code: j['itemCode'] as String? ?? '',
        nameAr: j['itemNameAr'] as String? ?? '',
        nameEn: j['itemNameEn'] as String? ?? '',
        quantity: _num(j['quantity']),
        unitPrice: _num(j['unitPrice']),
        total: _num(j['total']),
      );
}

class Stop {
  Stop({
    required this.id,
    required this.sequence,
    required this.status,
    this.notes,
    this.actionAt,
    this.deliveryNumber,
    required this.customerNameAr,
    required this.customerNameEn,
    this.phone,
    this.address,
    required this.currencyCode,
    required this.totalAmount,
    required this.collected,
    required this.proofIds,
    required this.lines,
  });

  final int id;
  final int sequence;
  final StopStatus status;
  final String? notes;
  final DateTime? actionAt;
  final String? deliveryNumber;
  final String customerNameAr;
  final String customerNameEn;
  final String? phone;
  final String? address;
  final String currencyCode;
  final double totalAmount;
  final double collected;
  final List<int> proofIds;
  final List<StopLine> lines;

  String customerName(String lang) {
    final primary = lang == 'ar' ? customerNameAr : customerNameEn;
    final other = lang == 'ar' ? customerNameEn : customerNameAr;
    return primary.isNotEmpty ? primary : other;
  }

  factory Stop.fromJson(Map<String, dynamic> j) => Stop(
        id: j['id'] as int,
        sequence: j['sequence'] as int? ?? 0,
        status: _stopStatus(j['status'] as String?),
        notes: j['notes'] as String?,
        actionAt: j['actionAt'] == null ? null : DateTime.tryParse(j['actionAt'] as String),
        deliveryNumber: j['deliveryNumber'] as String?,
        customerNameAr: j['customerNameAr'] as String? ?? '',
        customerNameEn: j['customerNameEn'] as String? ?? '',
        phone: j['phone'] as String?,
        address: j['address'] as String?,
        currencyCode: j['currencyCode'] as String? ?? '',
        totalAmount: _num(j['totalAmount']),
        collected: _num(j['collected']),
        proofIds: (j['proofIds'] as List? ?? []).cast<int>(),
        lines: (j['lines'] as List? ?? []).map((l) => StopLine.fromJson(l as Map<String, dynamic>)).toList(),
      );
}

class Trip {
  Trip({
    required this.id,
    this.number,
    required this.date,
    required this.status,
    required this.vehiclePlate,
    required this.capacityKg,
    required this.totalWeightKg,
    required this.overCapacity,
    required this.stops,
  });

  final int id;
  final String? number;
  final String date;
  final TripStatus status;
  final String vehiclePlate;
  final double capacityKg;
  final double totalWeightKg;
  final bool overCapacity;
  final List<Stop> stops;

  int get doneCount => stops.where((s) => s.status != StopStatus.pending).length;
  bool get hasPending => stops.any((s) => s.status == StopStatus.pending);

  Stop? stop(int id) {
    for (final s in stops) {
      if (s.id == id) return s;
    }
    return null;
  }

  factory Trip.fromJson(Map<String, dynamic> j) => Trip(
        id: j['id'] as int,
        number: j['number'] as String?,
        date: j['date'] as String,
        status: _tripStatus(j['status'] as String?),
        vehiclePlate: j['vehiclePlate'] as String? ?? '',
        capacityKg: _num(j['capacityKg']),
        totalWeightKg: _num(j['totalWeightKg']),
        overCapacity: j['overCapacity'] as bool? ?? false,
        stops: (j['stops'] as List? ?? []).map((s) => Stop.fromJson(s as Map<String, dynamic>)).toList(),
      );
}
