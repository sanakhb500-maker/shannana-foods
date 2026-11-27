import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/session.dart';
import '../../core/strings.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'trip_page.dart';

/// The driver's trips: on the road first, then planned, then recently completed.
class TripsPage extends StatefulWidget {
  const TripsPage({super.key});

  @override
  State<TripsPage> createState() => _TripsPageState();
}

class _TripsPageState extends State<TripsPage> {
  List<TripRow>? _rows;
  Object? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final api = context.read<DriverApi>();
    setState(() {
      _loading = _rows == null;
      _error = null;
    });
    try {
      final r = await api.trips();
      if (mounted) setState(() => _rows = r);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    }
    if (mounted) setState(() => _loading = false);
  }

  Future<void> _open(TripRow t) async {
    await Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => TripPage(tripId: t.id)));
    if (mounted) _load();
  }

  @override
  Widget build(BuildContext context) {
    final name = context.watch<Session>().info?.fullName ?? '';
    return Scaffold(
      appBar: AppBar(
        title: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(context.t('trips.title')),
          if (name.isNotEmpty) Text(name, style: TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6))),
        ]),
        toolbarHeight: 68,
        actions: const [AppMenu()],
      ),
      body: _body(),
    );
  }

  Widget _body() {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null && _rows == null) return ErrorView(message: errorText(context, _error!), onRetry: _load);
    final rows = _rows ?? [];
    Widget section(String key, TripStatus s) {
      final items = rows.where((r) => r.status == s).toList();
      if (items.isEmpty) return const SizedBox.shrink();
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Padding(padding: const EdgeInsets.fromLTRB(4, 16, 4, 8), child: Text(context.t(key), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16))),
        for (final t in items) Padding(padding: const EdgeInsets.only(bottom: 12), child: _TripCard(row: t, onTap: () => _open(t))),
      ]);
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
        children: [
          if (rows.isEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 120),
              child: Column(children: [
                Icon(Icons.local_shipping_outlined, size: 64, color: Theme.of(context).colorScheme.outline),
                const SizedBox(height: 12),
                Text(context.t('trips.empty')),
              ]),
            ),
          section('trips.inTransit', TripStatus.inTransit),
          section('trips.planned', TripStatus.planned),
          section('trips.completed', TripStatus.completed),
        ],
      ),
    );
  }
}

class _TripCard extends StatelessWidget {
  const _TripCard({required this.row, required this.onTap});

  final TripRow row;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final live = row.status == TripStatus.inTransit;
    final progress = row.stopCount == 0 ? 0.0 : row.doneCount / row.stopCount;
    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(20),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(child: Text(row.number ?? '#${row.id}', style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18))),
              StatusChip(context.t(switch (row.status) {
                TripStatus.inTransit => 'status.InTransit',
                TripStatus.completed => 'status.Completed',
                _ => 'status.Planned',
              }), live ? Brand.red : row.status == TripStatus.completed ? Brand.oliveDark : scheme.onSurface.withValues(alpha: 0.7)),
            ]),
            const SizedBox(height: 8),
            Row(children: [
              Icon(Icons.calendar_today_outlined, size: 16, color: scheme.outline),
              const SizedBox(width: 6),
              Text(row.date),
              const SizedBox(width: 16),
              Icon(Icons.local_shipping_outlined, size: 18, color: scheme.outline),
              const SizedBox(width: 6),
              Text(row.vehiclePlate, textDirection: TextDirection.ltr),
            ]),
            const SizedBox(height: 12),
            Row(children: [
              Expanded(child: ClipRRect(borderRadius: BorderRadius.circular(8), child: LinearProgressIndicator(value: progress, minHeight: 8, color: Brand.olive, backgroundColor: scheme.outlineVariant))),
              const SizedBox(width: 12),
              Text('${row.doneCount}/${row.stopCount} ${context.t('trips.stops')}', style: const TextStyle(fontWeight: FontWeight.w600)),
            ]),
          ]),
        ),
      ),
    );
  }
}
