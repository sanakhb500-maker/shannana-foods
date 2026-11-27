import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/location.dart';
import '../../core/strings.dart';
import '../../core/theme.dart';
import '../../widgets/common.dart';
import '../../widgets/method_picker.dart';
import 'proof_picker.dart';
import 'trip_controller.dart';

/// Confirms a delivery: records the result, optionally collects money, and attaches a signature or photo.
class DeliverPage extends StatefulWidget {
  const DeliverPage({super.key, required this.stopId});

  final int stopId;

  @override
  State<DeliverPage> createState() => _DeliverPageState();
}

class _DeliverPageState extends State<DeliverPage> {
  final _proof = ProofController();
  final _amount = TextEditingController();
  bool _collect = false;
  String _method = 'Cash';
  bool _busy = false;
  bool _seeded = false;

  @override
  void dispose() {
    _proof.dispose();
    _amount.dispose();
    super.dispose();
  }

  Future<void> _confirm(TripController c) async {
    final stop = c.trip!.stop(widget.stopId)!;
    double? amount;
    if (_collect) {
      amount = double.tryParse(_amount.text.replaceAll(',', '.').trim());
      if (amount == null || amount <= 0) {
        showMessage(context, context.ts('deliver.badAmount'), error: true);
        return;
      }
    }
    final location = context.read<LocationService>();
    final proof = await _proof.resolve();
    setState(() => _busy = true);

    // 1) the result itself: if this fails nothing else is sent
    try {
      final pos = await location.current();
      await c.run(() => c.api.setResult(c.tripId, stop.id, delivered: true, latitude: pos?.latitude, longitude: pos?.longitude));
    } catch (e) {
      if (mounted) {
        setState(() => _busy = false);
        showMessage(context, errorText(context, e), error: true);
      }
      return;
    }

    // 2) money and proof: the delivery is already recorded, so a failure here is reported without undoing it
    try {
      if (amount != null) await c.api.collect(stop.id, amount: amount, method: _method);
      if (proof != null) await c.api.uploadProof(stop.id, kind: proof.kind, bytes: proof.bytes, filename: proof.filename);
      await c.load();
      if (mounted) {
        showMessage(context, context.ts('stop.delivered'));
        Navigator.of(context).pop();
      }
    } catch (e) {
      await c.load();
      if (mounted) {
        showMessage(context, '${context.ts('deliver.partial')}\n${errorText(context, e)}', error: true);
        Navigator.of(context).pop();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.read<TripController>();
    final stop = c.trip?.stop(widget.stopId);
    if (stop == null) return const Scaffold(body: SizedBox.shrink());
    if (!_seeded) {
      _seeded = true;
      _collect = stop.totalAmount > 0 && stop.collected == 0;
      _amount.text = fmt(stop.totalAmount).replaceAll(',', '');
    }
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: Text(context.t('deliver.title'))),
      body: ListView(padding: const EdgeInsets.fromLTRB(20, 4, 20, 32), children: [
        Text(stop.customerName(context.lang), style: const TextStyle(fontWeight: FontWeight.w900, fontSize: 22)),
        Row(children: [
          Text('${context.t('stop.total')}:  ', style: TextStyle(color: scheme.outline)),
          Text('${fmt(stop.totalAmount)} ${stop.currencyCode}', textDirection: TextDirection.ltr, style: TextStyle(color: scheme.outline, fontWeight: FontWeight.w700)),
        ]),
        const SizedBox(height: 18),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                value: _collect,
                activeThumbColor: Brand.oliveDark,
                onChanged: (v) => setState(() => _collect = v),
                title: Text(context.t('deliver.collect'), style: const TextStyle(fontWeight: FontWeight.w700)),
              ),
              if (_collect) ...[
                const SizedBox(height: 6),
                TextField(
                  controller: _amount,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  textDirection: TextDirection.ltr,
                  style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w800),
                  decoration: InputDecoration(labelText: '${context.t('deliver.amount')} (${stop.currencyCode})'),
                ),
                const SizedBox(height: 12),
                MethodPicker(value: _method, onChanged: (m) => setState(() => _method = m)),
              ],
            ]),
          ),
        ),
        const SizedBox(height: 14),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('${context.t('deliver.proof')} (${context.t('deliver.optional')})', style: const TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 12),
              ProofPicker(controller: _proof),
            ]),
          ),
        ),
        const SizedBox(height: 22),
        FilledButton.icon(
          onPressed: _busy ? null : () => _confirm(c),
          style: FilledButton.styleFrom(backgroundColor: Brand.oliveDark, minimumSize: const Size.fromHeight(60)),
          icon: _busy ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white)) : const Icon(Icons.check_circle_outline),
          label: Text(context.t('deliver.confirm')),
        ),
      ]),
    );
  }
}
