import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../core/api.dart';
import '../core/session.dart';
import '../core/settings.dart';
import '../core/strings.dart';
import '../core/theme.dart';

final _number = NumberFormat('#,##0.##');

String fmt(double v) => _number.format(v);

/// The message to show for any failure; connection problems get a friendly, translated text.
String errorText(BuildContext context, Object e) {
  if (e is ApiException) return e.network ? context.ts('common.noConnection') : e.message;
  return e.toString();
}

void showMessage(BuildContext context, String text, {bool error = false}) {
  final scheme = Theme.of(context).colorScheme;
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(text), backgroundColor: error ? scheme.error : Brand.oliveDark));
}

class StatusChip extends StatelessWidget {
  const StatusChip(this.label, this.color, {super.key});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        decoration: BoxDecoration(color: color.withValues(alpha: 0.14), borderRadius: BorderRadius.circular(999), border: Border.all(color: color.withValues(alpha: 0.5))),
        child: Text(label, style: TextStyle(color: color, fontWeight: FontWeight.w700, fontSize: 12.5)),
      );
}

class ErrorView extends StatelessWidget {
  const ErrorView({super.key, required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(Icons.cloud_off_rounded, size: 56, color: Theme.of(context).colorScheme.outline),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh),
              label: Text(context.t('common.retry')),
              style: FilledButton.styleFrom(minimumSize: const Size(160, 48)),
            ),
          ]),
        ),
      );
}

/// Language / theme / sign-out, shared by the app bars.
class AppMenu extends StatelessWidget {
  const AppMenu({super.key, this.showLogout = true});

  final bool showLogout;

  @override
  Widget build(BuildContext context) {
    final settings = context.read<Settings>();
    return Row(mainAxisSize: MainAxisSize.min, children: [
      TextButton(onPressed: settings.toggleLanguage, child: Text(context.t('common.language'))),
      IconButton(
        tooltip: context.t('common.theme'),
        onPressed: () => settings.toggleTheme(Theme.of(context).brightness),
        icon: Icon(Theme.of(context).brightness == Brightness.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined),
      ),
      if (showLogout)
        IconButton(tooltip: context.t('common.logout'), onPressed: () => context.read<Session>().logout(), icon: const Icon(Icons.logout)),
    ]);
  }
}

Future<bool> confirmDialog(BuildContext context, String message) async {
  final ok = await showDialog<bool>(
    context: context,
    builder: (ctx) => AlertDialog(
      content: Text(message),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text(ctx.t('common.cancel'))),
        FilledButton(
          onPressed: () => Navigator.pop(ctx, true),
          style: FilledButton.styleFrom(minimumSize: const Size(110, 44)),
          child: Text(ctx.t('common.confirm')),
        ),
      ],
    ),
  );
  return ok ?? false;
}
