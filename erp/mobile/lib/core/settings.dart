import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Language and theme, remembered between launches.
class Settings extends ChangeNotifier {
  Settings._(this._prefs, this.lang, this.themeMode);

  final SharedPreferences _prefs;
  String lang;
  ThemeMode themeMode;

  static Future<Settings> load() async {
    final p = await SharedPreferences.getInstance();
    return Settings._(p, p.getString('lang') ?? 'ar', switch (p.getString('theme')) {
      'dark' => ThemeMode.dark,
      'light' => ThemeMode.light,
      _ => ThemeMode.system,
    });
  }

  bool get isArabic => lang == 'ar';

  TextDirection get direction => isArabic ? TextDirection.rtl : TextDirection.ltr;

  Locale get locale => Locale(lang);

  void toggleLanguage() {
    lang = isArabic ? 'en' : 'ar';
    _prefs.setString('lang', lang);
    notifyListeners();
  }

  void toggleTheme(Brightness current) {
    themeMode = current == Brightness.dark ? ThemeMode.light : ThemeMode.dark;
    _prefs.setString('theme', themeMode == ThemeMode.dark ? 'dark' : 'light');
    notifyListeners();
  }
}
