import 'package:flutter/widgets.dart';
import 'package:provider/provider.dart';

import 'settings.dart';

/// Arabic (default, right-to-left) and English texts. Server messages arrive already translated (X-Lang).
const Map<String, Map<String, String>> _t = {
  'app.name': {'ar': 'شنانة فودز', 'en': 'Shannana Foods'},
  'app.subtitle': {'ar': 'تطبيق سائق التوزيع', 'en': 'Delivery driver app'},
  'login.welcome': {'ar': 'مرحبًا بك', 'en': 'Welcome'},
  'login.subtitle': {'ar': 'سجّل دخولك لعرض رحلاتك وتسجيل التسليم.', 'en': 'Sign in to see your trips and record deliveries.'},
  'login.email': {'ar': 'البريد الإلكتروني', 'en': 'Email'},
  'login.password': {'ar': 'كلمة المرور', 'en': 'Password'},
  'login.submit': {'ar': 'دخول', 'en': 'Sign in'},
  'login.show': {'ar': 'إظهار', 'en': 'Show'},
  'login.hide': {'ar': 'إخفاء', 'en': 'Hide'},
  'login.notDriver': {'ar': 'هذا الحساب غير مخصص لتطبيق السائق.', 'en': 'This account is not a driver account.'},
  'login.required': {'ar': 'أدخل البريد وكلمة المرور.', 'en': 'Enter your email and password.'},
  'common.retry': {'ar': 'إعادة المحاولة', 'en': 'Retry'},
  'common.cancel': {'ar': 'إلغاء', 'en': 'Cancel'},
  'common.confirm': {'ar': 'تأكيد', 'en': 'Confirm'},
  'common.close': {'ar': 'إغلاق', 'en': 'Close'},
  'common.logout': {'ar': 'تسجيل الخروج', 'en': 'Sign out'},
  'common.language': {'ar': 'English', 'en': 'العربية'},
  'common.theme': {'ar': 'تبديل المظهر', 'en': 'Toggle theme'},
  'common.noConnection': {'ar': 'تعذّر الاتصال بالخادم. تحقق من الإنترنت.', 'en': 'Could not reach the server. Check your connection.'},
  'trips.title': {'ar': 'رحلاتي', 'en': 'My trips'},
  'trips.empty': {'ar': 'لا توجد رحلات لك حاليًا.', 'en': 'You have no trips right now.'},
  'trips.inTransit': {'ar': 'في الطريق', 'en': 'On the road'},
  'trips.planned': {'ar': 'مخطّطة', 'en': 'Planned'},
  'trips.completed': {'ar': 'منتهية', 'en': 'Completed'},
  'trips.stops': {'ar': 'محطات', 'en': 'stops'},
  'trip.start': {'ar': 'بدء الرحلة', 'en': 'Start trip'},
  'trip.complete': {'ar': 'إنهاء الرحلة', 'en': 'Complete trip'},
  'trip.completeHint': {'ar': 'سجّل نتيجة كل المحطات لتتمكن من إنهاء الرحلة.', 'en': 'Record every stop to complete the trip.'},
  'trip.startConfirm': {'ar': 'هل تبدأ الرحلة الآن؟', 'en': 'Start the trip now?'},
  'trip.completeConfirm': {'ar': 'إنهاء الرحلة؟ البضاعة غير المسلّمة تعود للمخزون.', 'en': 'Complete the trip? Undelivered goods go back to stock.'},
  'trip.vehicle': {'ar': 'المركبة', 'en': 'Vehicle'},
  'trip.load': {'ar': 'الحمولة', 'en': 'Load'},
  'trip.overCapacity': {'ar': 'الحمولة تتجاوز سعة المركبة.', 'en': 'The load exceeds the vehicle capacity.'},
  'unit.kg': {'ar': 'كغ', 'en': 'kg'},
  'trip.progress': {'ar': 'تم', 'en': 'done'},
  'trip.stopsTitle': {'ar': 'المحطات', 'en': 'Stops'},
  'status.Planned': {'ar': 'مخطّطة', 'en': 'Planned'},
  'status.InTransit': {'ar': 'في الطريق', 'en': 'In transit'},
  'status.Completed': {'ar': 'منتهية', 'en': 'Completed'},
  'stop.pending': {'ar': 'بانتظار التسليم', 'en': 'Pending'},
  'stop.delivered': {'ar': 'تم التسليم', 'en': 'Delivered'},
  'stop.failed': {'ar': 'تعذّر التسليم', 'en': 'Failed'},
  'stop.title': {'ar': 'المحطة', 'en': 'Stop'},
  'stop.call': {'ar': 'اتصال', 'en': 'Call'},
  'stop.map': {'ar': 'الخريطة', 'en': 'Map'},
  'stop.noAddress': {'ar': 'لا يوجد عنوان مسجّل', 'en': 'No address on file'},
  'stop.goods': {'ar': 'البضاعة', 'en': 'Goods'},
  'stop.total': {'ar': 'الإجمالي', 'en': 'Total'},
  'stop.collected': {'ar': 'المحصّل', 'en': 'Collected'},
  'stop.proofs': {'ar': 'ملفات إثبات', 'en': 'proof files'},
  'stop.delivered.action': {'ar': 'تم التسليم', 'en': 'Delivered'},
  'stop.failed.action': {'ar': 'تعذّر التسليم', 'en': 'Could not deliver'},
  'stop.collect': {'ar': 'تحصيل دفعة', 'en': 'Collect payment'},
  'stop.addProof': {'ar': 'إضافة إثبات', 'en': 'Add proof'},
  'stop.notes': {'ar': 'ملاحظات', 'en': 'Notes'},
  'stop.tripNotStarted': {'ar': 'ابدأ الرحلة أولًا لتسجيل نتيجة المحطة.', 'en': 'Start the trip to record this stop.'},
  'deliver.title': {'ar': 'تأكيد التسليم', 'en': 'Confirm delivery'},
  'deliver.collect': {'ar': 'تحصيل مبلغ من العميل', 'en': 'Collect money from the customer'},
  'deliver.amount': {'ar': 'المبلغ المقبوض', 'en': 'Amount received'},
  'deliver.method': {'ar': 'طريقة الدفع', 'en': 'Payment method'},
  'deliver.cash': {'ar': 'نقدًا', 'en': 'Cash'},
  'deliver.transfer': {'ar': 'حوالة', 'en': 'Transfer'},
  'deliver.proof': {'ar': 'إثبات التسليم', 'en': 'Proof of delivery'},
  'deliver.signature': {'ar': 'توقيع العميل', 'en': 'Customer signature'},
  'deliver.photo': {'ar': 'صورة', 'en': 'Photo'},
  'deliver.takePhoto': {'ar': 'التقاط صورة', 'en': 'Take a photo'},
  'deliver.clear': {'ar': 'مسح', 'en': 'Clear'},
  'deliver.signHere': {'ar': 'وقّع هنا', 'en': 'Sign here'},
  'deliver.optional': {'ar': 'اختياري', 'en': 'Optional'},
  'deliver.confirm': {'ar': 'تأكيد التسليم', 'en': 'Confirm delivery'},
  'deliver.partial': {'ar': 'سُجّل التسليم لكن تعذّرت خطوة لاحقة. يمكنك إكمالها من صفحة المحطة.', 'en': 'Delivery was recorded but a later step failed. You can finish it from the stop page.'},
  'deliver.badAmount': {'ar': 'أدخل مبلغًا صحيحًا أكبر من صفر.', 'en': 'Enter a valid amount greater than zero.'},
  'failed.title': {'ar': 'سبب تعذّر التسليم', 'en': 'Why could it not be delivered?'},
  'failed.closed': {'ar': 'المحل مغلق', 'en': 'Shop closed'},
  'failed.refused': {'ar': 'العميل رفض الاستلام', 'en': 'Customer refused'},
  'failed.noAnswer': {'ar': 'لا يوجد رد', 'en': 'No answer'},
  'failed.wrongAddress': {'ar': 'العنوان خاطئ', 'en': 'Wrong address'},
  'failed.other': {'ar': 'سبب آخر', 'en': 'Other'},
  'failed.note': {'ar': 'ملاحظة (اختياري)', 'en': 'Note (optional)'},
  'failed.submit': {'ar': 'تسجيل التعذّر', 'en': 'Record as failed'},
  'collect.title': {'ar': 'تحصيل دفعة', 'en': 'Collect payment'},
  'collect.saved': {'ar': 'سُجّل التحصيل وينتظر اعتماد المحاسب.', 'en': 'Collection recorded; the accountant will confirm it.'},
  'proof.saved': {'ar': 'تم حفظ الإثبات.', 'en': 'Proof saved.'},
  'proof.empty': {'ar': 'ارسم التوقيع أو التقط صورة أولًا.', 'en': 'Draw a signature or take a photo first.'},
};

String tr(String lang, String key) => _t[key]?[lang] ?? _t[key]?['en'] ?? key;

extension StringsX on BuildContext {
  /// Translated text for build methods: rebuilds the widget when the language changes.
  String t(String key) => tr(watch<Settings>().lang, key);

  String get lang => watch<Settings>().lang;

  /// Same text for event handlers and async code, where watching is not allowed.
  String ts(String key) => tr(read<Settings>().lang, key);

  String get langNow => read<Settings>().lang;
}
