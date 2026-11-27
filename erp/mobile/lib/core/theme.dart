import 'package:flutter/material.dart';

/// Brand colours, shared with the website and the web ERP.
class Brand {
  static const red = Color(0xFFA8202C);
  static const redDeep = Color(0xFF771923);
  static const olive = Color(0xFF718C3F);
  static const oliveDark = Color(0xFF56702B);
  static const cream = Color(0xFFFAF7F1);
  static const sand = Color(0xFFF6F0E6);
  static const ink = Color(0xFF24201E);
  static const line = Color(0xFFE9E1D5);
  static const muted = Color(0xFF6E6762);
}

ThemeData buildTheme(Brightness b) {
  final dark = b == Brightness.dark;
  final scheme = ColorScheme.fromSeed(seedColor: Brand.red, brightness: b).copyWith(
    primary: dark ? const Color(0xFFD4545F) : Brand.red,
    onPrimary: dark ? const Color(0xFF1A0B0E) : Colors.white,
    secondary: dark ? const Color(0xFFA7BB7A) : Brand.olive,
    surface: dark ? const Color(0xFF1E1B16) : Colors.white,
    onSurface: dark ? const Color(0xFFF0ECE4) : Brand.ink,
    outlineVariant: dark ? const Color(0xFF38332B) : Brand.line,
    error: dark ? const Color(0xFFF2877F) : const Color(0xFFB3261E),
  );
  final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(999));

  return ThemeData(
    useMaterial3: true,
    brightness: b,
    colorScheme: scheme,
    fontFamily: 'Cairo',
    scaffoldBackgroundColor: dark ? const Color(0xFF15130F) : Brand.cream,
    appBarTheme: AppBarTheme(
      backgroundColor: dark ? const Color(0xFF15130F) : Brand.cream,
      foregroundColor: scheme.onSurface,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(fontFamily: 'Cairo', fontWeight: FontWeight.w800, fontSize: 20, color: scheme.onSurface),
    ),
    cardTheme: CardThemeData(
      color: scheme.surface,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20), side: BorderSide(color: scheme.outlineVariant)),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(56),
        shape: shape,
        textStyle: const TextStyle(fontFamily: 'Cairo', fontWeight: FontWeight.w700, fontSize: 17),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size.fromHeight(52),
        shape: shape,
        side: BorderSide(color: scheme.outlineVariant, width: 1.5),
        textStyle: const TextStyle(fontFamily: 'Cairo', fontWeight: FontWeight.w700, fontSize: 16),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: dark ? const Color(0xFF26221C) : Brand.sand,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(16), borderSide: BorderSide(color: scheme.outlineVariant)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(16), borderSide: BorderSide(color: scheme.outlineVariant)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(16), borderSide: BorderSide(color: scheme.primary, width: 2)),
    ),
    snackBarTheme: SnackBarThemeData(behavior: SnackBarBehavior.floating, shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
  );
}
