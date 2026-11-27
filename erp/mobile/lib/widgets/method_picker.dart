import 'package:flutter/material.dart';

import '../core/strings.dart';

/// Cash or bank transfer, for money collected at a stop.
class MethodPicker extends StatelessWidget {
  const MethodPicker({super.key, required this.value, required this.onChanged});

  final String value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) => SegmentedButton<String>(
        segments: [
          ButtonSegment(value: 'Cash', label: Text(context.t('deliver.cash')), icon: const Icon(Icons.payments_outlined)),
          ButtonSegment(value: 'BankTransfer', label: Text(context.t('deliver.transfer')), icon: const Icon(Icons.account_balance_outlined)),
        ],
        selected: {value},
        onSelectionChanged: (s) => onChanged(s.first),
      );
}
