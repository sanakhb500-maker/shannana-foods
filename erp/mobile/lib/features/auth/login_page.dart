import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/session.dart';
import '../../core/settings.dart';
import '../../core/strings.dart';
import '../../core/theme.dart';
import '../../widgets/common.dart';

/// Sign in: a brand-red header with the product range, then the form.
class LoginPage extends StatefulWidget {
  const LoginPage({super.key});

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _show = false;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_email.text.trim().isEmpty || _password.text.isEmpty) {
      setState(() => _error = context.ts('login.required'));
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    final session = context.read<Session>();
    final api = context.read<DriverApi>();
    try {
      await session.login(api, _email.text, _password.text);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = errorText(context, e);
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      body: SingleChildScrollView(
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          const _Hero(),
          Padding(
            padding: const EdgeInsets.fromLTRB(24, 28, 24, 24),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(context.t('login.welcome'), style: const TextStyle(fontSize: 30, fontWeight: FontWeight.w900, height: 1.2)),
              const SizedBox(height: 6),
              Text(context.t('login.subtitle'), style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.65))),
              const SizedBox(height: 24),
              if (_error != null)
                Container(
                  margin: const EdgeInsets.only(bottom: 16),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(color: scheme.error.withValues(alpha: 0.1), borderRadius: BorderRadius.circular(14), border: Border.all(color: scheme.error.withValues(alpha: 0.5))),
                  child: Text(_error!, style: TextStyle(color: scheme.error)),
                ),
              TextField(
                controller: _email,
                keyboardType: TextInputType.emailAddress,
                textDirection: TextDirection.ltr,
                autofillHints: const [AutofillHints.username],
                decoration: InputDecoration(labelText: context.t('login.email'), prefixIcon: const Icon(Icons.mail_outline)),
              ),
              const SizedBox(height: 14),
              TextField(
                controller: _password,
                obscureText: !_show,
                textDirection: TextDirection.ltr,
                autofillHints: const [AutofillHints.password],
                onSubmitted: (_) => _submit(),
                decoration: InputDecoration(
                  labelText: context.t('login.password'),
                  prefixIcon: const Icon(Icons.lock_outline),
                  suffixIcon: TextButton(onPressed: () => setState(() => _show = !_show), child: Text(context.t(_show ? 'login.hide' : 'login.show'))),
                ),
              ),
              const SizedBox(height: 22),
              FilledButton(
                onPressed: _busy ? null : _submit,
                child: _busy ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white)) : Text(context.t('login.submit')),
              ),
            ]),
          ),
        ]),
      ),
    );
  }
}

class _Hero extends StatelessWidget {
  const _Hero();

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.of(context).padding.top;
    final settings = context.read<Settings>();
    return Container(
      height: 340 + top,
      decoration: const BoxDecoration(
        gradient: RadialGradient(center: Alignment(-0.6, -0.9), radius: 1.5, colors: [Color(0xFFC9303C), Brand.red, Brand.redDeep], stops: [0, 0.45, 1]),
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(40)),
      ),
      child: ClipRRect(
        borderRadius: const BorderRadius.vertical(bottom: Radius.circular(40)),
        child: LayoutBuilder(builder: (context, c) {
          final w = c.maxWidth;
          // products stand on the bottom edge; positions are fractions of the width so the picture scales with the phone
          Widget product(String file, double height, {double? start, double? end, double bottom = 0, double angle = 0}) => Positioned(
                bottom: bottom,
                left: start == null ? null : w * start,
                right: end == null ? null : w * end,
                child: Transform.rotate(angle: angle, child: Image.asset('assets/images/$file', height: height, fit: BoxFit.contain)),
              );
          return Stack(children: [
            Positioned(bottom: -70, left: -50, child: Container(width: 230, height: 230, decoration: BoxDecoration(shape: BoxShape.circle, color: Brand.olive.withValues(alpha: 0.35)))),
            product('Tomato.webp', 150, end: 0.30, bottom: 118, angle: -0.2),
            product('Corn.webp', 120, start: 0.03, bottom: 8, angle: -0.05),
            product('StrawberryJam.webp', 130, start: 0.22, bottom: 4),
            product('TomatoPaste.webp', 175, start: 0.40, bottom: 0),
            product('HotSauce.webp', 150, end: 0.12, bottom: 6, angle: 0.06),
            product('HeroProduct.webp', 128, end: 0.0, bottom: 10, angle: 0.08),
            Positioned(
              top: top + 10,
              left: 16,
              right: 16,
              child: Row(children: [
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14)),
                  child: Image.asset('assets/images/shannana-logo.png', height: 44),
                ),
                const Spacer(),
                TextButton(
                  onPressed: settings.toggleLanguage,
                  style: TextButton.styleFrom(foregroundColor: Colors.white, backgroundColor: Colors.white.withValues(alpha: 0.16)),
                  child: Text(context.t('common.language')),
                ),
              ]),
            ),
            Positioned(
              top: top + 74,
              left: 20,
              right: 20,
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(context.t('app.name'), style: const TextStyle(color: Colors.white, fontSize: 26, fontWeight: FontWeight.w900)),
                Text(context.t('app.subtitle'), style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 15)),
              ]),
            ),
          ]);
        }),
      ),
    );
  }
}
