# نشر شنانة فودز على Windows Server

يشمل هذا الدليل نظام الـERP (الواجهة + الـAPI + تطبيق السائق) والموقع العام. كل شيء يُركَّب بسكربتات PowerShell قابلة للإعادة (idempotent): نفس الأمر يصلح للتركيب الأول وللترقية.

## 1. الصورة العامة

```
الإنترنت ──► IIS (المنفذان 80 و 443، شهادات HTTPS)
               ├─ erp.شركتك.com ──► ASP.NET Core داخل IIS ──► SQL Server  (قاعدة ShannanaErp)
               │                     └─ صور الإثبات في D:\ShannanaData\erp\proofs
               └─ www.شركتك.com ──► (Reverse proxy / ARR) ──► Node + Express على 127.0.0.1:3000 ──► PostgreSQL (قاعدة shannana)
                                     └─ صور المنتجات في D:\ShannanaData\website\products
```

* **ERP**: تطبيق ASP.NET Core 10 يعمل داخل IIS (in-process). الدخول إلى SQL Server بحساب الـapp pool نفسه (Windows authentication) فلا توجد كلمة مرور قاعدة بيانات مخزّنة.
* **الموقع**: Node يعمل مهمة بدء تشغيل (Scheduled Task) بحساب `LOCAL SERVICE` (بلا صلاحيات إدارية) على 127.0.0.1 فقط، وIIS يمرّر له الطلبات.
* **ما يبقى عند كل ترقية** (في `DataRoot`): صور الإثبات، صور المنتجات، السجلات، النسخ الاحتياطية، الأسرار المولَّدة. الملفات التنفيذية في `SitesRoot` تُستبدل.
* لا تُفتح للخارج منافذ SQL Server (1433) ولا PostgreSQL (5432) ولا Node (3000). السكربت يفتح 80 و 443 فقط.

## 2. ما تحتاجه قبل البدء

| البند | ملاحظة |
|---|---|
| Windows Server 2019 / 2022 | بصلاحيات Administrator، ويفضَّل قرص ثانٍ (D:) للبيانات والنسخ |
| دومينان فرعيان | مثلاً `erp.شركتك.com` للنظام و`www.شركتك.com` للموقع، بسجل DNS من نوع A يشير إلى IP السيرفر العام |
| فتح المنفذين 80 و 443 | في جدار حماية الشبكة/الراوتر نحو السيرفر |
| **.NET 10 Hosting Bundle** | https://dotnet.microsoft.com/download/dotnet/10.0 (ثم `iisreset`) |
| **IIS URL Rewrite** و**Application Request Routing (ARR)** | https://www.iis.net/downloads |
| **SQL Server** (Express يكفي للبداية) + `sqlcmd` | للـERP |
| **Node.js 22.18+ (LTS)** و**PostgreSQL** | للموقع |
| إنترنت على السيرفر | لأمر `npm ci` أثناء تركيب الموقع |

سكربت `01-prerequisites.ps1` يثبّت أدوار IIS ويفتح المنفذين ويطبع ما ينقص مع رابط تحميله.

## 3. تجهيز الحزمة (على جهاز التطوير)

```powershell
cd deploy
.\build-release.ps1          # يشغّل الاختبارات، يبني واجهة Angular، ينشر الـAPI، ويجمّع الموقع والسكربتات
# الناتج: deploy\release\shannana-<التاريخ>-<commit>.zip
```
انسخ الملف إلى السيرفر (مثلاً `D:\Releases\`) وفكّ ضغطه. ستجد: `erp\` و`website\` و`server\` و`VERSION.txt`.

## 4. التركيب على السيرفر (PowerShell كمسؤول)

```powershell
cd D:\Releases\shannana-...\server

# 1) إعدادات هذا السيرفر
Copy-Item config.example.psd1 config.psd1
notepad config.psd1          # الدومينات، المسارات، أرقام واتساب ... لا كلمات مرور فيه

# 2) المتطلبات (كرّره حتى لا يبقى شيء ناقص)
.\01-prerequisites.ps1

# 3) نظام الـERP: ينشئ القاعدة والـapp pool والموقع، ويشغّل الهجرات والبيانات الأولية
.\02-install-erp.ps1 -AdminEmail you@company.com        # يطلب كلمة مرور المدير

# 4) الموقع العام: ينشئ قاعدة PostgreSQL ومهمة التشغيل والـproxy
.\03-install-website.ps1 -AdminEmail owner@company.com -AdminName "الاسم"   # يطلب كلمة مرور postgres وكلمة مرور مدير الموقع

# 5) النسخ الاحتياطي اليومي (2:15 صباحًا)
.\04-backup.ps1 -Register
```

ما يفعله كل سكربت وما ينشئه على السيرفر:

| السكربت | ينشئ |
|---|---|
| `02-install-erp.ps1` | قاعدة `ShannanaErp` + مستخدم `IIS APPPOOL\ShannanaErp` (db_owner)، موقع IIS `ShannanaErp`، ملف `C:\inetpub\shannana\erp\appsettings.Production.json` (فيه مفتاح JWT مولَّد، وصلاحية قراءته للمسؤولين والـapp pool فقط)، مجلد `D:\ShannanaData\erp\proofs` |
| `03-install-website.ps1` | قاعدة `shannana` ومستخدمها في PostgreSQL، `backend\.env` بأسرار مولَّدة، مهمة `ShannanaWebsite`، موقع IIS `ShannanaWebsite` مع قواعد التمرير، وصلة (Junction) تربط `frontend\img\products` بمجلد الرفع الدائم |
| `04-backup.ps1` | مهمة `ShannanaBackup` يومية |

كلمات المرور التي يولّدها السكربت (مفتاح JWT، كلمة مرور PostgreSQL للتطبيق) تُحفظ في `D:\ShannanaData\secrets` بصلاحية المسؤولين فقط، وتُعاد استعمالها عند الترقية ولا تتغير.

## 5. HTTPS (شهادات مجانية بـwin-acme)

بعد نجاح التركيب يعمل الموقعان على http. لتفعيل HTTPS:

1. نزّل **win-acme** من https://www.win-acme.com وفكّه في `C:\tools\win-acme`.
2. شغّل `wacs.exe` كمسؤول ← `N` (طلب شهادة جديدة) ← اختر موقع IIS: `ShannanaErp` ثم كرّر لـ`ShannanaWebsite` (حدّد كل الدومينات). يُصدر الشهادة، ينشئ ربط https (443)، ويجدّدها تلقائيًا بمهمة مجدولة.
3. تحقق: `https://erp.شركتك.com` و`https://www.شركتك.com`. أي طلب http يتحوّل تلقائيًا إلى https (ما عدا ملفات التحقق `.well-known`).

إن كان لديك شهادة جاهزة: ضع بصمتها في `CertThumbprint` داخل `config.psd1` وأعد تشغيل `02` و`03`.
إن فشل التحقق عبر http لأن DNS لم ينتشر بعد، أعد المحاولة لاحقًا أو استعمل تحقق DNS في win-acme.

## 6. التحقق بعد التركيب

* `https://erp.شركتك.com/api/v1/health` ← `{"success":true,...}`، ثم الدخول بحساب المدير.
* أنشئ في الـERP: العملات وأسعار الصرف، المستودعات، الأصناف... (البيانات الأولية: عملة محلية + دولار، وحدات، مستودع MAIN، شجرة حسابات).
* `https://www.شركتك.com` ثم `/admin/login.html` للوحة الموقع، وجرّب رفع صورة منتج (يجب أن تُحفظ في `D:\ShannanaData\website\products`).
* السجلات: ERP في `C:\inetpub\shannana\erp\logs` + Event Viewer، الموقع في `D:\ShannanaData\website\logs\website.log`.

## 7. تطبيق السائق للإنتاج

* في `ERP` ← المستخدمون: أنشئ مستخدمًا بدور **سائق** واربطه بسجل السائق.
* ابنِ التطبيق على عنوان الإنتاج (HTTPS إلزامي في نسخة release):
```powershell
cd erp\mobile
flutter build apk --release --dart-define=API_BASE=https://erp.شركتك.com
```
* **توقيع التطبيق**: أنشئ مفتاحًا مرة واحدة واحفظه نسخة احتياطية آمنة (إن ضاع لا يمكن تحديث التطبيق على الأجهزة):
```powershell
keytool -genkey -v -keystore $env:USERPROFILE\shannana-driver.jks -keyalg RSA -keysize 2048 -validity 10000 -alias driver
```
  ثم أنشئ `erp\mobile\android\key.properties` (ملف لا يدخل git):
```
storeFile=C:\\Users\\أنت\\shannana-driver.jks
storePassword=...
keyAlias=driver
keyPassword=...
```
  الناتج: `build\app\outputs\flutter-apk\app-release.apk` يُثبَّت على هواتف السائقين.

## 8. الترقية إلى إصدار جديد

1. على جهاز التطوير: `.\build-release.ps1` ثم انسخ الـzip الجديد إلى السيرفر وفكّه في مجلد جديد.
2. من مجلد `server` الجديد: انسخ `config.psd1` من الإصدار السابق، ثم شغّل `.\02-install-erp.ps1` و`.\03-install-website.ps1` (بلا معاملات إضافية).
   * الـERP: يأخذ نسخة من قاعدة البيانات قبل أي تغيير (`D:\ShannanaData\backups\...-before-upgrade-*.bak`)، يعرض صفحة "جاري التحديث"، يستبدل الملفات، ويطبّق الهجرات تلقائيًا عند الإقلاع.
   * الموقع: يوقف Node، يحدّث الملفات والحزم والهجرات، ويعيد التشغيل. صور المنتجات لا تُمسّ.
3. **التراجع**: أعد تشغيل السكربتين من مجلد الإصدار السابق. إن كانت الترقية غيّرت بنية قاعدة الـERP فاسترجع النسخة `before-upgrade` أولاً (القسم التالي).

## 9. النسخ الاحتياطي والاسترجاع

النسخة اليومية (2:15) تضع في `D:\ShannanaData\backups`: `erp-sql\*.bak` و`website-pg\*.dump` ومرآة للملفات `files\`، وتحتفظ بآخر `BackupKeepDays` يومًا. السجل في `backups\backup.log`. شغّلها يدويًا بـ`.\04-backup.ps1`.

> **انسخ النسخ إلى جهاز آخر.** نسخة على نفس القرص لا تحميك من تلف القرص. اضبط `OffsiteBackupPath` على مشاركة شبكية، أو انسخ المجلد بأداة تخزين سحابي.

استرجاع قاعدة الـERP (بعد إيقاف الموقع من IIS):
```powershell
sqlcmd -S localhost -E -C -Q "RESTORE DATABASE [ShannanaErp] FROM DISK=N'D:\ShannanaData\backups\erp-sql\ShannanaErp-....bak' WITH REPLACE"
```
استرجاع قاعدة الموقع:
```powershell
Stop-ScheduledTask ShannanaWebsite
$env:PGPASSWORD = '<postgres password>'
pg_restore -h 127.0.0.1 -U postgres -d shannana --clean --if-exists "D:\ShannanaData\backups\website-pg\shannana-....dump"
Start-ScheduledTask ShannanaWebsite
```
الصور: انسخ `backups\files\erp-proofs` و`website-products` إلى `D:\ShannanaData\erp\proofs` و`website\products`.
**جرّب الاسترجاع مرة على جهاز اختبار** قبل أن تحتاجه فعلاً.

## 10. قائمة الأمان

- [ ] HTTPS يعمل على الدومينين، وhttp يحوّل إليه.
- [ ] غيّر كلمة مرور المدير الأولى بعد أول دخول، وأنشئ مستخدمين بأدوار محدودة بدل استعمال المدير للعمل اليومي.
- [ ] منافذ 1433 و5432 و3000 مغلقة من الخارج (تحقق من جدار الشبكة).
- [ ] تحديثات Windows مفعّلة، وتحديث Node و.NET و PostgreSQL عند صدور تصحيحات أمنية.
- [ ] نسخ احتياطية تُنسخ خارج السيرفر وتم اختبار استرجاعها.
- [ ] لا تضع ملفات `config.psd1` أو `D:\ShannanaData\secrets` في git أو في البريد.
- [ ] على كل حساب مدير كلمة مرور قوية فريدة.

## 11. أعطال شائعة

| العَرَض | السبب والحل |
|---|---|
| ERP: **HTTP 500.19** | وحدة URL Rewrite غير مثبتة. ثبّتها ثم `iisreset`. |
| ERP: **HTTP 500.30 / 502.5** | الـHosting Bundle غير مثبّت أو التطبيق فشل في الإقلاع: اقرأ `erp\logs\stdout*.log` وEvent Viewer (غالبًا الاتصال بـSQL أو مفتاح JWT). |
| ERP: خطأ دخول SQL | أعد تشغيل `02-install-erp.ps1`؛ يعيد إنشاء login مستخدم الـapp pool. |
| الموقع: **502 / 404 من IIS** | Node لا يعمل: `Get-ScheduledTask ShannanaWebsite`، واقرأ `website.log`. أو ARR غير مثبّت/غير مفعّل. |
| الموقع: كل عمليات الحفظ والنماذج ترجع **403** | الـproxy لا يحافظ على ترويسة Host. السكربت يضبط `preserveHostHeader`؛ لا تغيّره من إعدادات ARR. |
| الموقع لا يقلع ويقول إعدادات `.env` غير صحيحة | اقرأ `website.log`: يذكر المتغير الناقص. أعد تشغيل `03-install-website.ps1`. |
| رفع صورة منتج يفشل | تحقق من صلاحية `LOCAL SERVICE` على `D:\ShannanaData\website\products` ومن حد الحجم (10 ميغا). |
| تطبيق السائق: "تعذّر الاتصال" | تأكد أن العنوان في `API_BASE` هو `https://erp...` وأن الشهادة صحيحة (الأجهزة ترفض الشهادات غير الموثوقة). |
