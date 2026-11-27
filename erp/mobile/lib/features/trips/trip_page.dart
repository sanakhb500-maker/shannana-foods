import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/strings.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import 'stop_page.dart';
import 'trip_controller.dart';

class TripPage extends StatelessWidget {
  const TripPage({super.key, required this.tripId});

  final int tripId;

  @override
  Widget build(BuildContext context) => ChangeNotifierProvider(
        create: (ctx) => TripController(ctx.read<DriverApi>(), tripId)..load(),
        child: const _TripView(),
      );
}

class _TripView extends StatelessWidget {
  const _TripView();

  Future<void> _act(BuildContext context, TripController c, String confirmKey, Future<Trip> Function() action) async {
    if (!await confirmDialog(context, context.ts(confirmKey))) return;
    try {
      await c.run(action);
    } catch (e) {
      if (context.mounted) showMessage(context, errorText(context, e), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.watch<TripController>();
    final t = c.trip;
    return Scaffold(
      appBar: AppBar(title: Text(t?.number ?? context.t('trips.title')), actions: const [AppMenu(showLogout: false)]),
      body: c.loading
          ? const Center(child: CircularProgressIndicator())
          : t == null
              ? ErrorView(message: errorText(context, c.error ?? ''), onRetry: c.load)
              : RefreshIndicator(
                  onRefresh: c.load,
                  child: ListView(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
                    children: [
                      _Summary(trip: t),
                      const SizedBox(height: 16),
                      if (t.status == TripStatus.planned)
                        FilledButton.icon(
                          onPressed: c.busy ? null : () => _act(context, c, 'trip.startConfirm', () => c.api.startTrip(t.id)),
                          icon: const Icon(Icons.play_arrow_rounded),
                          label: Text(context.t('trip.start')),
                        ),
                      if (t.status == TripStatus.inTransit) ...[
                        FilledButton.icon(
                          onPressed: c.busy || t.hasPending ? null : () => _act(context, c, 'trip.completeConfirm', () => c.api.completeTrip(t.id)),
                          style: FilledButton.styleFrom(backgroundColor: Brand.oliveDark),
                          icon: const Icon(Icons.flag_rounded),
                          label: Text(context.t('trip.complete')),
                        ),
                        if (t.hasPending)
                          Padding(
                            padding: const EdgeInsets.only(top: 8),
                            child: Text(context.t('trip.completeHint'), textAlign: TextAlign.center, style: TextStyle(color: Theme.of(context).colorScheme.outline, fontSize: 13)),
                          ),
                      ],
                      const SizedBox(height: 20),
                      Text(context.t('trip.stopsTitle'), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18)),
                      const SizedBox(height: 10),
                      for (final s in t.stops)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: _StopCard(
                            stop: s,
                            onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(
                              builder: (_) => ChangeNotifierProvider.value(value: c, child: StopPage(stopId: s.id)),
                            )),
                          ),
                        ),
                    ],
                  ),
                ),
    );
  }
}

class _Summary extends StatelessWidget {
  const _Summary({required this.trip});

  final Trip trip;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final progress = trip.stops.isEmpty ? 0.0 : trip.doneCount / trip.stops.length;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(Icons.local_shipping_outlined, color: scheme.primary),
            const SizedBox(width: 8),
            Text(trip.vehiclePlate, textDirection: TextDirection.ltr, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18)),
            const Spacer(),
            Text(trip.date, style: TextStyle(color: scheme.outline)),
          ]),
          const SizedBox(height: 14),
          Row(children: [
            Expanded(child: ClipRRect(borderRadius: BorderRadius.circular(8), child: LinearProgressIndicator(value: progress, minHeight: 10, color: Brand.olive, backgroundColor: scheme.outlineVariant))),
            const SizedBox(width: 12),
            Text('${trip.doneCount}/${trip.stops.length} ${context.t('trip.progress')}', style: const TextStyle(fontWeight: FontWeight.w700)),
          ]),
          if (trip.capacityKg > 0) ...[
            const SizedBox(height: 12),
            Text('${context.t('trip.load')}: ${fmt(trip.totalWeightKg)} / ${fmt(trip.capacityKg)} ${context.t('unit.kg')}', style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.7))),
          ],
          if (trip.overCapacity)
            Container(
              margin: const EdgeInsets.only(top: 10),
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(color: const Color(0xFFD4A72C).withValues(alpha: 0.15), borderRadius: BorderRadius.circular(12)),
              child: Row(children: [
                const Icon(Icons.warning_amber_rounded, color: Color(0xFF9A6700)),
                const SizedBox(width: 8),
                Expanded(child: Text(context.t('trip.overCapacity'))),
              ]),
            ),
        ]),
      ),
    );
  }
}

Color stopColor(BuildContext context, StopStatus s) => switch (s) {
      StopStatus.delivered => Brand.oliveDark,
      StopStatus.failed => Theme.of(context).colorScheme.error,
      StopStatus.pending => Theme.of(context).colorScheme.outline,
    };

String stopLabel(BuildContext context, StopStatus s) => context.t(switch (s) {
      StopStatus.delivered => 'stop.delivered',
      StopStatus.failed => 'stop.failed',
      StopStatus.pending => 'stop.pending',
    });

class _StopCard extends StatelessWidget {
  const _StopCard({required this.stop, required this.onTap});

  final Stop stop;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = stopColor(context, stop.status);
    final scheme = Theme.of(context).colorScheme;
    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(20),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            CircleAvatar(
              radius: 20,
              backgroundColor: color.withValues(alpha: 0.15),
              child: stop.status == StopStatus.pending
                  ? Text('${stop.sequence}', style: TextStyle(color: color, fontWeight: FontWeight.w800))
                  : Icon(stop.status == StopStatus.delivered ? Icons.check_rounded : Icons.close_rounded, color: color),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(stop.customerName(context.lang), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
                if ((stop.address ?? '').isNotEmpty) Text(stop.address!, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.65), fontSize: 13)),
                const SizedBox(height: 8),
                Row(children: [
                  StatusChip(stopLabel(context, stop.status), color),
                  const Spacer(),
                  Flexible(child: Text('${fmt(stop.totalAmount)} ${stop.currencyCode}', overflow: TextOverflow.ellipsis, textDirection: TextDirection.ltr, style: const TextStyle(fontWeight: FontWeight.w800))),
                ]),
              ]),
            ),
          ]),
        ),
      ),
    );
  }
}
