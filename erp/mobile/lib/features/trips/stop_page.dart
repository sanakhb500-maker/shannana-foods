import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api.dart';
import '../../core/location.dart';
import '../../core/strings.dart';
import '../../core/theme.dart';
import '../../models/models.dart';
import '../../widgets/common.dart';
import '../../widgets/method_picker.dart';
import 'deliver_page.dart';
import 'proof_picker.dart';
import 'trip_controller.dart';
import 'trip_page.dart' show stopColor, stopLabel;

/// One customer on the route: how to reach them, what is on the truck for them, and the actions for this stop.
class StopPage extends StatelessWidget {
  const StopPage({super.key, required this.stopId});

  final int stopId;

  Future<void> _open(BuildContext context, Uri uri) async {
    final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
    if (!ok && context.mounted) showMessage(context, uri.toString(), error: true);
  }

  Future<void> _failed(BuildContext context, TripController c) async {
    final result = await showModalBottomSheet<_FailedResult>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _FailedSheet(),
    );
    if (result == null || !context.mounted) return;
    final api = context.read<DriverApi>();
    final location = context.read<LocationService>();
    try {
      final pos = await location.current();
      await c.run(() => api.setResult(c.tripId, stopId, delivered: false, reason: result.reason, notes: result.note, latitude: pos?.latitude, longitude: pos?.longitude));
      if (context.mounted) Navigator.of(context).pop();
    } catch (e) {
      if (context.mounted) showMessage(context, errorText(context, e), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.watch<TripController>();
    final trip = c.trip;
    final stop = trip?.stop(stopId);
    if (trip == null || stop == null) return Scaffold(appBar: AppBar(), body: const Center(child: CircularProgressIndicator()));

    final scheme = Theme.of(context).colorScheme;
    final inTransit = trip.status == TripStatus.inTransit;
    final pending = stop.status == StopStatus.pending;
    final phone = (stop.phone ?? '').trim();
    final address = (stop.address ?? '').trim();

    return Scaffold(
      appBar: AppBar(title: Text('${context.t('stop.title')} ${stop.sequence}'), actions: const [AppMenu(showLogout: false)]),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  Expanded(child: Text(stop.customerName(context.lang), style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 22, height: 1.3))),
                  StatusChip(stopLabel(context, stop.status), stopColor(context, stop.status)),
                ]),
                const SizedBox(height: 12),
                Row(children: [
                  Icon(Icons.place_outlined, color: scheme.outline),
                  const SizedBox(width: 8),
                  Expanded(child: Text(address.isEmpty ? context.t('stop.noAddress') : address)),
                ]),
                if (phone.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Row(children: [
                    Icon(Icons.phone_outlined, color: scheme.outline),
                    const SizedBox(width: 8),
                    Text(phone, textDirection: TextDirection.ltr),
                  ]),
                ],
                const SizedBox(height: 14),
                Row(children: [
                  if (phone.isNotEmpty)
                    Expanded(child: OutlinedButton.icon(onPressed: () => _open(context, Uri(scheme: 'tel', path: phone)), icon: const Icon(Icons.call), label: Text(context.t('stop.call')))),
                  if (phone.isNotEmpty && address.isNotEmpty) const SizedBox(width: 10),
                  if (address.isNotEmpty)
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () => _open(context, Uri.https('www.google.com', '/maps/search/', {'api': '1', 'query': address})),
                        icon: const Icon(Icons.map_outlined),
                        label: Text(context.t('stop.map')),
                      ),
                    ),
                ]),
              ]),
            ),
          ),
          const SizedBox(height: 14),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('${context.t('stop.goods')}${stop.deliveryNumber == null ? '' : '  ·  ${stop.deliveryNumber}'}', style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
                const SizedBox(height: 8),
                for (final l in stop.lines)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Row(children: [
                      Expanded(child: Text(context.lang == 'ar' && l.nameAr.isNotEmpty ? l.nameAr : (l.nameEn.isNotEmpty ? l.nameEn : l.nameAr))),
                      Text('× ${fmt(l.quantity)}', style: const TextStyle(fontWeight: FontWeight.w700)),
                      const SizedBox(width: 14),
                      SizedBox(width: 84, child: Text(fmt(l.total), textAlign: TextAlign.end, textDirection: TextDirection.ltr)),
                    ]),
                  ),
                const Divider(height: 22),
                Row(children: [
                  Text(context.t('stop.total'), style: const TextStyle(fontWeight: FontWeight.w800)),
                  const Spacer(),
                  Text('${fmt(stop.totalAmount)} ${stop.currencyCode}', textDirection: TextDirection.ltr, style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 18)),
                ]),
                if (stop.collected > 0)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Row(children: [
                      Text(context.t('stop.collected'), style: TextStyle(color: Brand.oliveDark, fontWeight: FontWeight.w700)),
                      const Spacer(),
                      Text('${fmt(stop.collected)} ${stop.currencyCode}', textDirection: TextDirection.ltr, style: TextStyle(color: Brand.oliveDark, fontWeight: FontWeight.w800)),
                    ]),
                  ),
                if (stop.proofIds.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Row(children: [
                      const Icon(Icons.verified_outlined, size: 18, color: Brand.oliveDark),
                      const SizedBox(width: 6),
                      Text('${stop.proofIds.length} ${context.t('stop.proofs')}'),
                    ]),
                  ),
                if ((stop.notes ?? '').isNotEmpty)
                  Padding(padding: const EdgeInsets.only(top: 10), child: Text('${context.t('stop.notes')}: ${stop.notes}', style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.7)))),
              ]),
            ),
          ),
          const SizedBox(height: 18),
          if (!inTransit && pending)
            Text(context.t('stop.tripNotStarted'), textAlign: TextAlign.center, style: TextStyle(color: scheme.outline)),
          if (inTransit && pending) ...[
            FilledButton.icon(
              onPressed: c.busy
                  ? null
                  : () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ChangeNotifierProvider.value(value: c, child: DeliverPage(stopId: stopId)))).then((_) {
                        if (context.mounted && c.trip?.stop(stopId)?.status != StopStatus.pending) Navigator.of(context).pop();
                      }),
              style: FilledButton.styleFrom(backgroundColor: Brand.oliveDark, minimumSize: const Size.fromHeight(64)),
              icon: const Icon(Icons.check_circle_outline),
              label: Text(context.t('stop.delivered.action')),
            ),
            const SizedBox(height: 10),
            OutlinedButton.icon(
              onPressed: c.busy ? null : () => _failed(context, c),
              style: OutlinedButton.styleFrom(foregroundColor: scheme.error, side: BorderSide(color: scheme.error.withValues(alpha: 0.6), width: 1.5)),
              icon: const Icon(Icons.highlight_off),
              label: Text(context.t('stop.failed.action')),
            ),
          ],
          if (inTransit && !pending) ...[
            OutlinedButton.icon(
              onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ChangeNotifierProvider.value(value: c, child: _CollectPage(stopId: stopId)))),
              icon: const Icon(Icons.payments_outlined),
              label: Text(context.t('stop.collect')),
            ),
            const SizedBox(height: 10),
            OutlinedButton.icon(
              onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ChangeNotifierProvider.value(value: c, child: _AddProofPage(stopId: stopId)))),
              icon: const Icon(Icons.draw_outlined),
              label: Text(context.t('stop.addProof')),
            ),
          ],
        ],
      ),
    );
  }
}

class _FailedResult {
  _FailedResult(this.reason, this.note);

  final String reason;
  final String? note;
}

class _FailedSheet extends StatefulWidget {
  const _FailedSheet();

  @override
  State<_FailedSheet> createState() => _FailedSheetState();
}

class _FailedSheetState extends State<_FailedSheet> {
  static const _keys = ['failed.closed', 'failed.refused', 'failed.noAnswer', 'failed.wrongAddress', 'failed.other'];
  String? _key;
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 0, 20, 20 + MediaQuery.of(context).viewInsets.bottom),
      child: SingleChildScrollView(
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(context.t('failed.title'), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18)),
          const SizedBox(height: 12),
          Wrap(spacing: 8, runSpacing: 8, children: [
            for (final k in _keys)
              ChoiceChip(
                label: Text(context.t(k)),
                selected: _key == k,
                onSelected: (_) => setState(() => _key = k),
                selectedColor: scheme.error.withValues(alpha: 0.18),
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 8),
              ),
          ]),
          const SizedBox(height: 14),
          TextField(controller: _note, maxLength: 200, decoration: InputDecoration(labelText: context.t('failed.note'))),
          const SizedBox(height: 6),
          FilledButton(
            onPressed: _key == null ? null : () => Navigator.pop(context, _FailedResult(tr(context.langNow, _key!), _note.text.trim().isEmpty ? null : _note.text.trim())),
            style: FilledButton.styleFrom(backgroundColor: scheme.error),
            child: Text(context.t('failed.submit')),
          ),
        ]),
      ),
    );
  }
}

/// Money collected at a stop that was already marked delivered (the accountant confirms the receipt later).
class _CollectPage extends StatefulWidget {
  const _CollectPage({required this.stopId});

  final int stopId;

  @override
  State<_CollectPage> createState() => _CollectPageState();
}

class _CollectPageState extends State<_CollectPage> {
  final _amount = TextEditingController();
  String _method = 'Cash';
  bool _busy = false;

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  Future<void> _save(TripController c) async {
    final value = double.tryParse(_amount.text.replaceAll(',', '.').trim());
    if (value == null || value <= 0) {
      showMessage(context, context.ts('deliver.badAmount'), error: true);
      return;
    }
    setState(() => _busy = true);
    try {
      await c.api.collect(widget.stopId, amount: value, method: _method);
      await c.load();
      if (!mounted) return;
      showMessage(context, context.ts('collect.saved'));
      Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        showMessage(context, errorText(context, e), error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.read<TripController>();
    final stop = c.trip?.stop(widget.stopId);
    if (_amount.text.isEmpty && stop != null) _amount.text = fmt(stop.totalAmount - stop.collected > 0 ? stop.totalAmount - stop.collected : 0).replaceAll(',', '');
    return Scaffold(
      appBar: AppBar(title: Text(context.t('collect.title'))),
      body: ListView(padding: const EdgeInsets.all(20), children: [
        TextField(
          controller: _amount,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          textDirection: TextDirection.ltr,
          style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800),
          decoration: InputDecoration(labelText: '${context.t('deliver.amount')} (${stop?.currencyCode ?? ''})'),
        ),
        const SizedBox(height: 16),
        MethodPicker(value: _method, onChanged: (m) => setState(() => _method = m)),
        const SizedBox(height: 24),
        FilledButton(onPressed: _busy ? null : () => _save(c), child: Text(context.t('common.confirm'))),
      ]),
    );
  }
}

class _AddProofPage extends StatefulWidget {
  const _AddProofPage({required this.stopId});

  final int stopId;

  @override
  State<_AddProofPage> createState() => _AddProofPageState();
}

class _AddProofPageState extends State<_AddProofPage> {
  final _proof = ProofController();
  bool _busy = false;

  @override
  void dispose() {
    _proof.dispose();
    super.dispose();
  }

  Future<void> _save(TripController c) async {
    final data = await _proof.resolve();
    if (data == null) {
      if (mounted) showMessage(context, context.ts('proof.empty'), error: true);
      return;
    }
    setState(() => _busy = true);
    try {
      await c.api.uploadProof(widget.stopId, kind: data.kind, bytes: data.bytes, filename: data.filename);
      await c.load();
      if (!mounted) return;
      showMessage(context, context.ts('proof.saved'));
      Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        showMessage(context, errorText(context, e), error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.read<TripController>();
    return Scaffold(
      appBar: AppBar(title: Text(context.t('stop.addProof'))),
      body: ListView(padding: const EdgeInsets.all(20), children: [
        ProofPicker(controller: _proof),
        const SizedBox(height: 24),
        FilledButton(onPressed: _busy ? null : () => _save(c), child: Text(context.t('common.confirm'))),
      ]),
    );
  }
}
