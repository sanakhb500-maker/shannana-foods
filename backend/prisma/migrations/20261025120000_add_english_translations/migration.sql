-- ============================================================================
-- Shannana Foods — migration 2: add_english_translations (PHASE 10)
-- إضافية وآمنة: أعمدة اختيارية (NULL) فقط. لا تحذف ولا تعدّل أي عمود أو صف قائم.
-- 1) AlterTable: ترجمة إنجليزية اختيارية للتصنيفات والمنتجات.
-- 2) ملء ترجمة المنتجات والتصنيفات الأصلية (من Seed) فقط عندما:
--      - الاسم والوصف العربيان ما زالا كما في Seed (لم يعدّلهما المدير)، و
--      - لا توجد ترجمة إنجليزية بعد (الحقول NULL).
--    أي صف عدّله المدير أو أضافه يبقى كما هو، ويعرض الموقع الإنجليزي نصه العربي حتى تُضاف ترجمته.
-- ============================================================================

-- AlterTable
ALTER TABLE "categories" ADD COLUMN     "description_en" TEXT,
ADD COLUMN     "name_en" VARCHAR(100);

-- AlterTable
ALTER TABLE "products" ADD COLUMN     "description_en" TEXT,
ADD COLUMN     "name_en" VARCHAR(150);

-- Backfill: categories (slug, Arabic name guard, English name)
UPDATE "categories" AS c
SET "name_en" = v.name_en
FROM (VALUES
  ('sauces', 'صلصات', 'Sauces'),
  ('seasonings', 'تتبيلات', 'Seasonings'),
  ('canned', 'معلبات', 'Canned Goods'),
  ('jams', 'مربيات', 'Jams'),
  ('grains-legumes', 'حبوب وبقوليات', 'Grains & Legumes')
) AS v(slug, name_ar, name_en)
WHERE c."slug" = v.slug AND c."name" = v.name_ar AND c."name_en" IS NULL;

-- Backfill: products (slug, Arabic name/description guards, English name/description)
UPDATE "products" AS p
SET "name_en" = v.name_en, "description_en" = v.description_en
FROM (VALUES
  ('ketchup', 'كاتشب الطماطم', 'Tomato Ketchup', 'أحمر غني بطعم الطماطم الحقيقي', 'Rich red ketchup with real tomato flavor'),
  ('hot-sauce', 'صلصة حارة', 'Hot Sauce', 'حرارة متوازنة تُشعل أي طبق', 'Balanced heat that brings any dish to life'),
  ('pizza-sauce', 'صلصة البيتزا', 'Pizza Sauce', 'نكهة غنية تكمل طعم البيتزا', 'A rich flavor that completes your pizza'),
  ('tahini', 'طحينة', 'Tahini', 'قوام ناعم ونكهة أصيلة', 'Smooth texture and authentic flavor'),
  ('pomegranate-molasses', 'دبس الرمان', 'Pomegranate Molasses', 'حلاوة وحموضة بطابع شرقي أصيل', 'Sweet and tangy with an authentic Eastern character'),
  ('white-vinegar', 'خل أبيض', 'White Vinegar', 'نقاء وجودة في كل قطرة', 'Purity and quality in every drop'),
  ('apple-vinegar', 'خل التفاح', 'Apple Vinegar', 'نكهة منعشة للاستخدام اليومي', 'A refreshing flavor for everyday use'),
  ('corn', 'ذرة حلوة', 'Sweet Corn', 'حبات مختارة بعناية', 'Carefully selected kernels'),
  ('mushroom', 'فطر', 'Mushrooms', 'نكهة غنية لمجموعة واسعة من الأطباق', 'Rich flavor for a wide range of dishes'),
  ('strawberry-jam', 'مربى الفراولة', 'Strawberry Jam', 'حلاوة الفراولة بقوام غني', 'The sweetness of strawberries in a rich texture'),
  ('apricot-jam', 'مربى المشمش', 'Apricot Jam', 'حلاوة الفاكهة كما هي', 'The natural sweetness of fruit'),
  ('tomato-paste', 'معجون الطماطم', 'Tomato Paste', 'مركّز وغني بطعم الطماطم', 'Concentrated and rich in tomato flavor'),
  ('red-lentils', 'عدس شنانة الأحمر', 'Shannana Red Lentils', 'حبات مختارة سريعة النضج', 'Carefully selected, quick-cooking lentils'),
  ('lentils', 'عدس شنانة', 'Shannana Lentils', 'حبات مختارة بعناية لطبخة صحية', 'Carefully selected lentils for a healthy meal'),
  ('bulgur', 'برغل شنانة', 'Shannana Bulgur', 'قوام مثالي لأشهى الأطباق الشرقية', 'The perfect texture for the tastiest Eastern dishes'),
  ('sugar', 'سكر شنانة', 'Shannana Sugar', 'نقاء وحلاوة تناسب كل استخدام', 'Pure sweetness for every use'),
  ('hummus', 'حمص شنانة', 'Shannana Chickpeas', 'حبات ممتازة للاستخدام اليومي', 'Excellent-quality chickpeas for everyday use'),
  ('rice', 'أرز شنانة', 'Shannana Rice', 'حبات طويلة ونقية لطبخة مثالية', 'Long, pure grains for a perfect dish'),
  ('freekeh', 'فريكة شنانة', 'Shannana Freekeh', 'نكهة أصيلة من قلب الأرض', 'Authentic flavor from the heart of the land'),
  ('lemon-substitute', 'بديل الليمون', 'Lemon Substitute', 'حموضة منعشة للاستخدام اليومي', 'Refreshing tartness for everyday use')
) AS v(slug, name_ar, name_en, description_ar, description_en)
WHERE p."slug" = v.slug AND p."name" = v.name_ar AND p."description" = v.description_ar
  AND p."name_en" IS NULL AND p."description_en" IS NULL;
