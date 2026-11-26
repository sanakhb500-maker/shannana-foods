# HANDOFF — Shannana Foods

> وثيقة تسليم لمتابعة المشروع في أي جلسة جديدة. **لا تحتوي أي سر** (لا كلمات مرور، ولا `DATABASE_URL`، ولا `JWT_SECRET`).
> آخر تحديث: 29 أكتوبر 2026 — أُعدّت حزمة PHASE 10 لدعم العربية والإنجليزية، ولم تُطبَّق بعد على جهاز المالك. PHASE 8 وPHASE 9 تعملان محليًا بحسب تأكيد المالك السابق.

## 1. المشروع
الموقع الرسمي لمصنع/علامة **شنانة فودز | Shannana Foods**: واجهة عربية RTL (HTML/CSS/JS عادي) + Backend (Express + Prisma + PostgreSQL).

المسار المحلي: `C:\Users\DELL\Documents\shannana-foods`

```
shannana-foods/
├── frontend/            # الواجهة العامة + لوحة الإدارة (تُخدَم من Express)
│   ├── index.html, css/style.css, css/forms.css, css/i18n.css, js/main.js, js/forms.js, js/hero3d.js, js/boot.js, js/vendor/
│   ├── img/             # صور المنتجات الأصلية (لا تُعدَّل أسماؤها)
│   │   └── products/    # صور يرفعها المدير (أسماء مولَّدة p-<uuid>.png|jpg|webp، لا تُحذف تلقائيًا)
│   └── admin/           # login, dashboard, products, categories (html + js)، admin.css، admin-common.js
├── backend/
│   ├── prisma/          # schema.prisma, migrations/, seed.js, seed-data.js
│   ├── prisma.config.ts # يقرأ DATABASE_URL عبر dotenv
│   ├── scripts/         # check-db.js, verify-db.js, verify-seed.js
│   └── src/             # app.js, server.js, config/, i18n/, routes/, controllers/, services/, validators/, middleware/, utils/
└── HANDOFF.md
```

## 2. التقنيات المعتمدة
Node.js 24.21 · Express 5.2 · PostgreSQL 18.6 · Prisma 7.10.0 (adapter-pg، العميل المولَّد TypeScript في `src/generated/prisma`) · Zod 4 · Argon2id (`argon2`) · JWT (`jsonwebtoken` 9.0.3) في HttpOnly cookie (`cookie-parser` 1.4.7) · رفع الملفات: `busboy` 1.6.0 (multipart في الذاكرة، بدون multer) · Helmet + express-rate-limit.
ممنوع: React/Next.js، وإعادة بناء الواجهة، وتغيير الهوية (الألوان: أحمر #A8202C/#771923/#C95A62، زيتوني #718C3F/#56702B، كريمي #FAF7F1، رملي #EFE6D8، حبر #24201E، رمادي #6E6762، خط Cairo).

## 3. المراحل
| المرحلة | الحالة |
|---|---|
| Frontend (تصميم + Motion + Hero ثلاثي الأبعاد) | مكتمل ومعتمد |
| PHASE 1 Architecture / 2 Schema / 3 Setup | مكتملة |
| PHASE 4 Migration (`20261008010606_init`) | مطبّقة محليًا؛ `db:status` و`db:verify` و`db:drift` نجحت |
| PHASE 5 Seed (5 تصنيفات، 20 منتجًا، حساب مدير) | مكتملة؛ `db:seed:verify` نجح |
| PHASE 6 Public API (`/api/categories`, `/api/products`) | تعمل محليًا بحسب تأكيد المالك |
| PHASE 7 Admin Login + Dashboard | تعمل محليًا بحسب تأكيد المالك |
| PHASE 8 إدارة المنتجات والتصنيفات + رفع الصور | تعمل محليًا بحسب تأكيد المالك |
| PHASE 9 الاستفسارات وطلبات التوزيع (النماذج العامة + إدارتها) | مطبَّقة وتعمل محليًا بحسب تأكيد المالك |
| PHASE 10 العربية والإنجليزية + حقول الترجمة بلوحة الإدارة | حزمة تطبيق جاهزة؛ Migration لم تُطبّق بعد محليًا |
| لاحقًا: PHASE 11 تفاصيل المنتج، PHASE 12 الطلبات عبر واتساب | لم تبدأ |

## 4. متغيرات البيئة (`backend/.env`، لا يُرفع إلى Git)
`NODE_ENV`, `PORT`, `DATABASE_URL`, `JWT_SECRET` (32 حرفًا+)، `WHATSAPP_NUMBER`, `CORS_ORIGINS` (اختياري)، `TRUST_PROXY` (اختياري).
حساب المدير الأول يُنشأ بـ `ADMIN_NAME` و`ADMIN_EMAIL` و`ADMIN_PASSWORD` عند تشغيل `npm run db:seed` (يفضَّل تمريرها في جلسة PowerShell لا في ملف).

## 5. التشغيل (PowerShell، من `backend/`)
```powershell
npm install
npm run dev                # http://localhost:3000
npm run db:status          # حالة الـ migrations
npm run db:verify          # فحص بنية القاعدة
npm run db:seed            # آمن لإعادة التشغيل (لا يكرر ولا يستبدل تعديلات إدارية)
npm run db:seed:verify
npm run db:drift           # يجب أن يعيد "No difference detected"
```
ممنوع: `migrate dev`, `db push`, `reset` أو حذف جداول/بيانات. أُضيفت Migration منفصلة ومراجَعة لحقول ترجمة PHASE 10؛ لا تعدّل migrations القديمة.

## 6. الـ API
عام (بدون مصادقة):
- `GET /api/health`
- `GET /api/categories[?lang=ar|en]` — التصنيفات النشطة بترتيب `sortOrder`، مع `productsCount` وترجمة اختيارية
- `GET /api/products[?category=<slug>&lang=ar|en]` — المنتجات النشطة ضمن تصنيف نشط؛ يعرض الإنجليزية عند وجودها ويرجع للعربية عند غيابها؛ slug خاطئ 400، غير موجود 404
- `GET /api/forms/token` — رمز مؤقّت موقّع من الخادم للنماذج العامة (لا يُخزّن في قاعدة البيانات).
- `POST /api/inquiries` — حفظ استفسار عام أو متعلق بمنتج في `Inquiry`.
- `POST /api/distribution-requests` — حفظ طلب توزيع في `DistributionRequest`.
- حماية النماذج العامة: Zod validation، فخّ honeypot، تحقق من رمز النموذج، منع التكرار الحديث، وحدود معدل قصيرة ويومية بالإضافة إلى الحد العام للـ API.

المصادقة (`/api/auth`، كل طلب غير آمن يخضع لفحص Origin):
- `POST /api/auth/login` — `{email, password}` → كوكي الجلسة (الرمز لا يُعاد في الجسم)
- `POST /api/auth/logout`
- `GET /api/auth/me` — يتطلب جلسة

الإدارة (`/api/admin`، كلها تتطلب جلسة admin + فحص Origin + حد 120 عملية كتابة/10 دقائق):
- `GET /api/admin/stats`
- `GET /api/admin/products?search=&category=<id>&status=all|active|inactive&limit=` · `GET /products/:id`
- `POST /api/admin/products` و`PATCH /api/admin/products/:id` — `multipart/form-data` (حقول + ملف `image` اختياري) أو JSON. لا يوجد DELETE للمنتجات: التعطيل (`isActive=false`) بدل الحذف.
- `GET/POST /api/admin/categories` · `PATCH /api/admin/categories/:id` · `DELETE /api/admin/categories/:id` (يُرفض 409 إن كان فيه منتجات)
- `GET /api/admin/inquiries?status=&search=&page=&pageSize=` · `PATCH /api/admin/inquiries/:id` لتغيير الحالة إلى `new|contacted|closed`.
- `GET /api/admin/distribution-requests?status=&search=&page=&pageSize=` · `PATCH /api/admin/distribution-requests/:id` لتغيير الحالة إلى `new|contacted|qualified|closed`.
- صفحات الإدارة المحمية: `/admin/inquiries` و`/admin/distribution`. كل المسارات تخضع لجلسة المدير وفحص Origin وحدّ الكتابة الحالي.
- الـ slug يُحدَّد عند الإنشاء ولا يتغير. المنتج بلا slug يأخذ `product-<hex>` تلقائيًا؛ التصنيف يتطلب slug.

شكل الاستجابة الموحّد: نجاح `{ success: true, data, meta? }`، فشل `{ success: false, message, errors? }` (`src/utils/apiResponse.js`).

## 7. تصميم الجلسة والأمان (PHASE 7)
- كوكي `sf_admin` (في الإنتاج `__Host-sf_admin` مع Secure): `HttpOnly`, `SameSite=Strict`, `Path=/`, مدة 8 ساعات.
- JWT HS256 بسرّ `JWT_SECRET`؛ يُتحقق من `iss` و`aud` والخوارزمية. في **كل طلب** يُقرأ المستخدم من القاعدة: تعطيل الحساب أو تغيير كلمة المرور يُبطل الجلسات فورًا (بصمة hash كلمة المرور داخل الرمز).
- CSRF: `SameSite=Strict` + فحص `Origin` لكل POST/PUT/PATCH/DELETE (يجب أن يطابق مضيف الخادم أو `CORS_ORIGINS`) + قبول JSON فقط.
- تسجيل الدخول: Argon2id (m=64MiB, t=3, p=1)، رسالة خطأ واحدة لكل الحالات، hash وهمي لتساوي الزمن، حد 10 محاولات فاشلة/15 دقيقة لكل IP و30 لكل بريد (الناجحة لا تُحتسب).
- صفحة `/admin` تُقدَّم لمدير مسجّل فقط (وإلا تحويل إلى `/admin/login`)؛ فتح `*.html` مباشرة يُحوَّل؛ الصفحات `no-store`.
- CSP (في `app.js`): السكربتات `'self'` فقط، لذا صفحات الإدارة بملفات JS خارجية بلا inline.

## 7.1 رفع الصور (PHASE 8)
- يُقبل JPEG وPNG وWebP فقط، بحد 8 ميغابايت و300×300 إلى 8000×8000 بكسل. النوع والأبعاد من **محتوى الملف** (magic bytes، `utils/imageInfo.js`) لا من الامتداد ولا Content-Type. SVG/GIF/HTML/الملفات المبتورة مرفوضة.
- اسم الملف الأصلي لا يُستعمل أبدًا: يُحفظ في `frontend/img/products/p-<uuid>.<ext>` بوضع `wx` (لا يستبدل ملفًا موجودًا) ويُخدَم علنًا عبر `express.static` من `/img/products/...`.
- الترتيب: تحقق من الحقول والصورة ← كتابة الملف الجديد ← حفظ المنتج في القاعدة. فشل القاعدة (مثل slug مكرر) يحذف **الملف الجديد فقط**. الصور القديمة لا تُحذف ولا تُستبدل (للاستعادة).
- إنشاء منتج يتطلب صورة مرفوعة أو مسار صورة موجودة فعلًا تحت `/img/` (يُتحقق من وجود الملف).
- الكتالوج العام يتحقق من التحديث في كل طلب (`max-age=0, must-revalidate` + ETag) لتظهر تعديلات المدير فورًا.
- المعاينة قبل الحفظ تستعمل `FileReader` (data: URL) لأن CSP لا يسمح بـ `blob:`.

## 8. حدود معروفة
- تسجيل الخروج يمسح الكوكي لكن الرمز عديم الحالة: رمز مسروق يبقى صالحًا حتى انتهاء مدته (8 ساعات) أو تعطيل الحساب/تغيير كلمة المرور.
- عدّادات تحديد المحاولات في ذاكرة العملية (تُصفَّر عند إعادة التشغيل، ولا تُشارَك بين عدة نسخ).
- أثناء قفل المحاولات (429) تُرفض حتى كلمة المرور الصحيحة لمدة النافذة.
- الصور المرفوعة تُخزَّن كما هي (بدون تصغير/تحويل). صور 5–8 ميغابايت تبطئ الموقع، والحل المخطَّط: توليد نسخ مصغّرة WebP (يحتاج `sharp` وموافقة).
- لا يوجد تنظيف للصور القديمة غير المستعملة (مقصود)؛ يمكن إضافة أداة تنظيف يدوية لاحقًا.
- مشهد Hero ثلاثي الأبعاد: عمق العبوة تقدير من شكل الصورة (ليس نموذج 3D حقيقيًا).
- تصنيفات الفوتر في `index.html` مكتوبة نصيًا وتختلف عن تصنيفات قاعدة البيانات (الواجهة معتمدة، لم تُغيَّر).

## 9. PHASE 9 — حالة الحزمة وما يجب فعله محليًا
- تتضمن الحزمة ملفات Backend وواجهة النماذج العامة (استفسار + طلب توزيع) وصفحتَي الإدارة، وروابط لوحة التحكم، وتحديث `HANDOFF.md`.
- لم يُعدَّل `schema.prisma` أو migrations أو Seed، ولم تُضف أي اعتماديات جديدة. مخطط البيانات الحالي لا يحتوي حقل ملاحظات داخلية، لذلك لا توجد ملاحظات محفوظة في هذه المرحلة بدل تغيير قاعدة البيانات.
- تم تصحيح التحقق من الهاتف والبريد الاختياريين بحيث تُقبل القيم الفارغة بعد التطبيع، مع بقاء التحقق من رقم التواصل المطلوب في كل نموذج.
- فحوص الحزمة الآلية هنا تشمل تحليل صياغة JavaScript وفحص بنية HTML وملفات الحزمة فقط. **لم يُشغّل هذا التسليم على قاعدة بيانات المالك أو متصفح Windows؛ يجب تنفيذ اختبار التشغيل أدناه قبل اعتبار PHASE 9 مكتملة نهائيًا.**

### تطبيق واختبار PHASE 9 على Windows
1. أوقف `npm run dev` إن كان يعمل (`Ctrl+C`) وخذ نسخة احتياطية كاملة من مجلد `shannana-foods`.
2. فك ضغط `phase9.zip` فوق جذر المشروع `C:\Users\DELL\Documents\shannana-foods` مع الحفاظ على المسارات، ووافق على استبدال الملفات الواردة في الحزمة فقط.
3. من `backend` شغّل `npm run dev`؛ لا حاجة إلى migration أو أوامر قاعدة بيانات.
4. اختبر `/` وافتح نافذة الاستفسار وأرسل اختبارًا ببيانات وهمية، ثم افتح نافذة طلب توزيع وأرسل اختبارًا ببيانات وهمية.
5. سجّل الدخول إلى `/admin`، ثم افتح `/admin/inquiries` و`/admin/distribution` وتأكد من ظهور السجلين. غيّر حالة كل واحد، ثم حدّث الصفحة وتأكد من بقاء الحالة.
6. تأكد أن `/admin/products` و`/admin/categories` ورفع صور المنتجات ما زالت تعمل.

لا تُستخدم بيانات عملاء حقيقية للاختبار. احذف سجلات الاختبار فقط من خلال قاعدة البيانات بعد نسخة احتياطية وبإجراء منفصل ومقصود؛ لا توجد وظيفة حذف لهذه السجلات ضمن الواجهة الإدارية الحالية.

## 10. PHASE 10 — الموقع بالعربية والإنجليزية

**الحالة:** ملفات المرحلة موجودة في `phase10-final.zip`، لكنها لم تُطبَّق على قاعدة بيانات المالك بعد. لا تعتبر المرحلة مكتملة نهائيًا حتى تنجح خطوات التطبيق والاختبار على Windows.

- اللغة العربية افتراضية على `/` والإنجليزية على `/en`.
- قالب الصفحة الرئيسية يُعرَض من Express عبر `backend/src/routes/publicPages.js`، ويستبدل مفاتيح `{{key}}` باستخدام `backend/src/i18n/`.
- تتضمن الصفحة زر تبديل لغة في الهيدر والجوال، و`lang` و`dir` وcanonical وhreflang وOpen Graph وrobots.txt وsitemap.xml.
- يعرض `GET /api/categories?lang=en` و`GET /api/products?lang=en` النص الإنجليزي عند وجوده، ويرجعان للنص العربي عند غياب الترجمة.
- أضيفت الحقول الاختيارية `nameEn` و`descriptionEn` إلى `Product` و`Category`، مع Migration إضافية `20261025120000_add_english_translations`.
- تعبّئ Migration الترجمات لبيانات Seed المطابقة فقط، وتترك السجلات التي تغير اسمها أو وصفها كما هي. يمكن إكمال الترجمة الناقصة من لوحة الإدارة.
- أُضيفت حقول اسم ووصف إنجليزي اختيارية إلى نماذج إدارة المنتجات والتصنيفات. يمكن تركها فارغة، فيعرض الموقع الإنجليزي النص العربي كحل احتياطي.
- يعرض فلتر الموقع كل التصنيفات النشطة حتى لو لم يكن لديها منتجات بعد (مثل «بهارات»)، ثم تظهر رسالة عدم توفر المنتجات عند اختيارها.
- أُصلحت روابط الاستفسارات وطلبات التوزيع في الشريط الجانبي لصفحتَي المنتجات والتصنيفات.
- سلوك زر «اطلب المنتج» لم يتحول إلى سلة؛ لا يزال يستخدم WhatsApp كما كان. نظام الطلبات الفعلي خارج PHASE 10.

### ما اختُبر في بيئة تجهيز الحزمة

- سلامة أرشيف المصدر واستخراجه.
- فحص صياغة ملفات JavaScript ضمن Backend والواجهة ولوحة الإدارة باستخدام `node --check`.
- فحص HTML: لا توجد معرفات مكررة في الصفحات التي فُحصت، ومعرفات حقول الترجمة الجديدة متطابقة مع مراجع JavaScript.
- فحص 106 مفتاح ترجمة في قالب الصفحة: لا مفاتيح مفقودة بالعربية أو الإنجليزية، ولا رموز قالب غير مستبدلة، ونتيجة العرض تضبط `ar/rtl` و`en/ltr`.
- تطابق مجموعة مفاتيح الترجمة العامة ومفاتيح رسائل العميل بين العربية والإنجليزية.
- هذه الفحوص لا تعني أن التطبيق اختُبر مع قاعدة بيانات المالك أو متصفح Windows. تقارير اختبار Claude على PostgreSQL تجريبية تظل منفصلة عن اختباراتنا في الحزمة.

## 11. تطبيق PHASE 10 على Windows

1. أوقف السيرفر بـ `Ctrl+C` وخذ نسخة احتياطية من مجلد المشروع كاملًا.
2. خذ نسخة احتياطية من قاعدة `shannana_foods` عبر pgAdmin (Backup) أو باستخدام `pg_dump` قبل تشغيل أي Migration.
3. فك ضغط `phase10-final.zip` داخل `C:\Users\DELL\Documents\shannana-foods` مع الحفاظ على المسارات. الحزمة تحتوي الملفات المعدلة والجديدة فقط ولا تحتوي صور المنتجات أو `.env`.
4. افتح PowerShell:

```powershell
cd C:\Users\DELL\Documents\shannana-foods\backend
npm run db:validate
npm run db:status
```

5. إذا ظهر أن Migration `20261025120000_add_english_translations` معلَّقة، وبعد التأكد من نجاح النسخ الاحتياطي، نفّذ:

```powershell
npm run db:migrate:deploy
npm run db:generate
npm run db:verify
npm run db:drift
```

إذا كان وضع الهجرات غير متوقع، أو فشل `db:verify` أو `db:drift`، توقّف وأرسل المخرجات بدل تجربة أوامر إصلاح عشوائية. لا تشغّل `db:seed` لتطبيق الترجمات.

6. شغّل `npm run dev` ثم اختبر `/` و`/en` و`/api/products?lang=en` و`/api/categories?lang=en`.
7. افتح `/admin/products` وحرر منتجًا تجريبيًا، وأدخل الاسم والوصف بالإنجليزية واحفظه ثم تأكد من ظهوره في `/en`. جرّب الأمر نفسه على تصنيف، وأدخل `Spices` للاسم الإنجليزي لتصنيف «بهارات».
8. أرسل استفسارًا وطلب توزيع تجريبيين من `/en` وتأكد من وصولهما إلى صفحات الإدارة. اختبر كذلك رفع صورة منتج موجودة، وتسجيل الدخول، والتنقل في الشريط الجانبي من صفحتَي المنتجات والتصنيفات.
9. لا تحذف سجلات الاختبار أو صورًا يدويًا ضمن هذا الاختبار. لا تشغّل `migrate dev` أو `db push` أو `reset`.

## 12. الخطوات القادمة

1. بعد نجاح PHASE 10 محليًا، تُخطط PHASE 11 لصفحة تفاصيل المنتج مع بيانات العبوة والكرتونة والتخزين والتصدير التي يؤكدها صاحب المعمل.
2. لاحقًا، PHASE 12: سلة طلبات بدون أسعار، حفظ الطلب في قاعدة البيانات، وصفحة إدارة الطلبات وزر WhatsApp برسالة جاهزة. لا يُرسل WhatsApp تلقائيًا في الخلفية.
