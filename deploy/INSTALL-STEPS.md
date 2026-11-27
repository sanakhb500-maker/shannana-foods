# خطوات تركيب السيرفر (بالترتيب)

الدومينات: الموقع `shannana.varx-dev.com`، والنظام وتطبيق السائق `erp.shannana.varx-dev.com`.
هذا ملخّص تنفيذي بالترتيب؛ الشرح الكامل والأعطال والاسترجاع في `README.md`.

## المرحلة 0: قبل ما تلمس السيرفر

1. **DNS:** أضف سجل A لـ`erp.shannana.varx-dev.com` يشير لنفس عنوان IP الذي عليه `shannana.varx-dev.com`. تحقق:
   ```powershell
   nslookup shannana.varx-dev.com
   nslookup erp.shannana.varx-dev.com
   ```
2. **جدار الشبكة / الراوتر:** افتح 80 و443 نحو السيرفر فقط. لا تفتح 1433 ولا 5432 ولا 3000.
3. جهّز حساب **Administrator** على السيرفر، والقرص D: إن وُجد (وإلا غيّر `DataRoot` في المرحلة 3).

## المرحلة 1: بناء الحزمة (على جهاز التطوير)

```powershell
cd deploy
.\build-release.ps1
```
تستغرق 3 إلى 5 دقائق. الناتج: `deploy\release\shannana-<التاريخ>-<commit>.zip`. انسخه إلى السيرفر (مثلاً `D:\Releases\`).

## المرحلة 2: تثبيت البرامج على السيرفر

ثبّتها بهذا الترتيب، وبعد كل تثبيت افتح PowerShell جديدًا:

1. **أدوار IIS:** يتولاها سكربت المرحلة 3. إن ثبّتّ Hosting Bundle قبل IIS فشغّل بعده `iisreset`.
2. **.NET 10 Hosting Bundle:** https://dotnet.microsoft.com/download/dotnet/10.0 (تحت ASP.NET Core Runtime، خيار Hosting Bundle).
3. **IIS URL Rewrite** و**Application Request Routing (ARR):** https://www.iis.net/downloads
4. **SQL Server Express** مع أدوات سطر الأوامر `sqlcmd`.
   * التثبيت الافتراضي ينشئ instance اسمه `SQLEXPRESS`: ستكتب في المرحلة 3 `SqlServer = 'localhost\SQLEXPRESS'`.
   * اختر Windows authentication (لا حاجة لحساب sa).
5. **Node.js 22.18 أو أحدث (LTS)** لكل المستخدمين.
6. **PostgreSQL:** سجّل كلمة مرور المستخدم `postgres` التي تضعها أثناء التثبيت (تحتاجها في المرحلة 5). اترك المنفذ 5432.

## المرحلة 3: فك الحزمة وتجهيز الإعدادات

```powershell
# PowerShell كمسؤول
Expand-Archive D:\Releases\shannana-....zip D:\Releases\current
cd D:\Releases\current\server
Get-ChildItem -Recurse | Unblock-File          # ملفات قادمة من جهاز آخر تكون محجوبة افتراضيًا
Set-ExecutionPolicy -Scope Process Bypass       # لهذه الجلسة فقط

Copy-Item config.example.psd1 config.psd1
notepad config.psd1
```
عدّل في الملف:
* `SqlServer = 'localhost\SQLEXPRESS'` (إن ثبّتّ Express).
* `DataRoot = 'C:\ShannanaData'` إن لم يكن عندك قرص D.
* أرقام واتساب إن كانت مختلفة.
* اترك الدومينات كما هي، و`CertThumbprint` فارغًا الآن.

ثم افحص المتطلبات:
```powershell
.\01-prerequisites.ps1
```
المتوقع: كل بند `OK`. إن ظهر `MISSING` ثبّته وأعد تشغيل السكربت. قد يطلب إعادة تشغيل بعد تثبيت IIS.

## المرحلة 4: تركيب الـERP

```powershell
.\02-install-erp.ps1 -AdminEmail you@company.com
```
* يطلب كلمة مرور المدير (8 أحرف على الأقل: حرف كبير وصغير ورقم).
* ينشئ قاعدة `ShannanaErp` والـapp pool وموقع IIS، ويشغّل التطبيق (الهجرات والبيانات الأولية عند أول إقلاع).
* النجاح: سطر `the ERP answers`، وبعده تُحذف كلمة مرور المدير من ملف الإعدادات.
* اختبار مؤقت قبل HTTPS (من السيرفر نفسه): `http://127.0.0.1:8081/api/v1/health` يجب أن يعيد `{"success":true,...}`.
* عند الفشل: اقرأ `C:\inetpub\shannana\erp\logs` وEvent Viewer (Windows Logs > Application). الأسباب الشائعة: URL Rewrite أو Hosting Bundle غير مثبّت.

## المرحلة 5: تركيب الموقع

```powershell
.\03-install-website.ps1 -AdminEmail owner@company.com -AdminName "الاسم"
```
* يطلب كلمة مرور `postgres` (من المرحلة 2) ثم كلمة مرور مدير الموقع (12 حرفًا على الأقل).
* يحتاج إنترنت على السيرفر (`npm ci`).
* النجاح: `Node answers` و`IIS forwards to Node`. إن توقف عند npm فتحقق من الاتصال وأعد التشغيل (آمن).

## المرحلة 6: HTTPS (شهادات مجانية)

1. نزّل **win-acme** من https://www.win-acme.com وفكّه في `C:\tools\win-acme`.
2. شغّل `wacs.exe` كمسؤول.
3. اختر `N` (Create certificate)، ثم الموقع `ShannanaErp`، واتبع الخيارات الافتراضية.
4. كرّر العملية للموقع `ShannanaWebsite`.
5. يربط الشهادة تلقائيًا ويجدّدها بمهمة مجدولة.
6. الشرط: DNS منتشر والمنفذ 80 مفتوح من الإنترنت. إن فشل التحقق فانتظر انتشار DNS وأعد المحاولة.

ثم جرّب من جهاز آخر:
* `https://erp.shannana.varx-dev.com/api/v1/health`
* `https://shannana.varx-dev.com`
* أي رابط http يجب أن يتحوّل إلى https.

## المرحلة 7: النسخ الاحتياطي

```powershell
.\04-backup.ps1 -Register      # يجدول نسخة يومية 2:15 صباحًا
.\04-backup.ps1                # شغّلها الآن يدويًا للتجربة
```
تأكد من وجود ملفي `.bak` و`.dump`، ومن آخر سطر `finished OK` في `<DataRoot>\backups\backup.log`.
انسخ النسخ إلى جهاز آخر: اضبط `OffsiteBackupPath` في `config.psd1` أو استعمل أداة تخزين خارجية.

## المرحلة 8: اختبارات القبول

**الـERP** (`https://erp.shannana.varx-dev.com`):
1. ادخل بحساب المدير وغيّر كلمة مروره فورًا.
2. أنشئ مستخدمين بأدوار محدودة (محاسب، موزّع ...).
3. جرّب دورة كاملة: أصناف ← مستودع ← أمر شراء ← استلام.

**الموقع** (`https://shannana.varx-dev.com`):
1. افتح `/admin/login.html` وادخل.
2. ارفع صورة منتج تجريبية وتأكد أنها تظهر وتُحفظ في `<DataRoot>\website\products`.
3. جرّب نموذج طلب من الصفحة العامة.

## المرحلة 9: تطبيق السائق

1. في الـERP: المستخدمون ← مستخدم جديد بدور **سائق** ← اربطه بسجل السائق.
2. على جهاز التطوير: أنشئ مفتاح التوقيع **مرة واحدة** واحفظ نسخة منه (إن ضاع فلن تستطيع تحديث التطبيق على الهواتف). الأوامر في `README.md` القسم 7.
3. ابنِ التطبيق:
   ```powershell
   cd erp\mobile
   flutter build apk --release --dart-define=API_BASE=https://erp.shannana.varx-dev.com
   ```
4. ثبّت `build\app\outputs\flutter-apk\app-release.apk` على هاتف السائق وجرّب رحلة كاملة.

## المرحلة 10: قائمة التحقق النهائية

- [ ] http يتحوّل إلى https على الدومينين.
- [ ] المنافذ 1433 و5432 و3000 مغلقة من الخارج (افحصها من جهاز خارج الشبكة).
- [ ] تحديثات Windows التلقائية مفعّلة.
- [ ] النسخ الاحتياطي مجرَّب ومنسوخ خارج السيرفر، وجرّبت الاسترجاع على جهاز اختبار.
- [ ] كلمات مرور المديرين قوية وفريدة.

## الترقية لاحقًا

1. على جهاز التطوير: `.\build-release.ps1`، وانسخ الـzip الجديد إلى السيرفر وفكّه.
2. انسخ `config.psd1` القديم إلى مجلد `server` الجديد.
3. أعد تشغيل `02-install-erp.ps1` و`03-install-website.ps1` بلا معاملات إضافية. الـERP يأخذ نسخة من القاعدة قبل التغيير.

التراجع والاسترجاع: `README.md` القسمان 8 و9.
