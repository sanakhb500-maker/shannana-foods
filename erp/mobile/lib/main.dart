import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:provider/provider.dart';

import 'core/api.dart';
import 'core/config.dart';
import 'core/location.dart';
import 'core/session.dart';
import 'core/settings.dart';
import 'core/theme.dart';
import 'features/auth/login_page.dart';
import 'features/trips/trips_page.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final settings = await Settings.load();
  final session = Session(SecureTokenStore(), () => settings.lang);
  final api = HttpDriverApi(
    baseUrl: Config.apiBase,
    tokenProvider: () => session.token,
    langProvider: () => settings.lang,
    onUnauthorized: session.logout, // an expired or revoked token sends the driver back to sign in
  );
  session.restore();
  runApp(DriverApp(settings: settings, session: session, api: api, location: GeolocatorService()));
}

class DriverApp extends StatelessWidget {
  const DriverApp({super.key, required this.settings, required this.session, required this.api, required this.location});

  final Settings settings;
  final Session session;
  final DriverApi api;
  final LocationService location;

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: settings),
        ChangeNotifierProvider.value(value: session),
        Provider<DriverApi>.value(value: api),
        Provider<LocationService>.value(value: location),
      ],
      child: Consumer<Settings>(
        builder: (context, s, _) => MaterialApp(
          debugShowCheckedModeBanner: false,
          title: 'Shannana Driver',
          locale: s.locale,
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          theme: buildTheme(Brightness.light),
          darkTheme: buildTheme(Brightness.dark),
          themeMode: s.themeMode,
          home: Consumer<Session>(
            builder: (context, sess, _) {
              if (sess.restoring) return const Scaffold(body: Center(child: CircularProgressIndicator()));
              return sess.info == null ? const LoginPage() : const TripsPage();
            },
          ),
        ),
      ),
    );
  }
}
