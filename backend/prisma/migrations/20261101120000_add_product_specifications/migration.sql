-- ============================================================================
-- Shannana Foods — migration 3: add_product_specifications (PHASE 11B.1)
-- إضافية وآمنة: نوعان جديدان + أعمدة اختيارية (NULL) على "products" فقط.
-- لا تحذف ولا تعدّل أي عمود أو صف قائم، ولا تملأ أي قيمة (لا بيانات مواصفات تُخمَّن).
-- كل Product = مقاس عبوة (SKU) مستقل. الوحدات: g/kg/ml/l، الأبعاد سم، الأوزان كغ.
-- القسم 1–3: بنية Prisma القياسية.   القسم 4: قيود CHECK (يدوية — لا يعبّر عنها Prisma).
-- ============================================================================

-- 1) CreateEnum
CREATE TYPE "content_unit" AS ENUM ('g', 'kg', 'ml', 'l');

CREATE TYPE "time_unit" AS ENUM ('day', 'month', 'year');

-- 2) AlterTable
ALTER TABLE "products" ADD COLUMN     "net_content" DECIMAL(10,3),
ADD COLUMN     "net_content_unit" "content_unit",
ADD COLUMN     "pack_type" VARCHAR(80),
ADD COLUMN     "pack_type_en" VARCHAR(80),
ADD COLUMN     "pack_material" VARCHAR(80),
ADD COLUMN     "pack_material_en" VARCHAR(80),
ADD COLUMN     "barcode" VARCHAR(14),
ADD COLUMN     "carton_units" INTEGER,
ADD COLUMN     "carton_length_cm" DECIMAL(7,2),
ADD COLUMN     "carton_width_cm" DECIMAL(7,2),
ADD COLUMN     "carton_height_cm" DECIMAL(7,2),
ADD COLUMN     "carton_net_weight_kg" DECIMAL(8,3),
ADD COLUMN     "carton_gross_weight_kg" DECIMAL(8,3),
ADD COLUMN     "shelf_life_value" INTEGER,
ADD COLUMN     "shelf_life_unit" "time_unit",
ADD COLUMN     "storage_note" TEXT,
ADD COLUMN     "storage_note_en" TEXT,
ADD COLUMN     "origin_country" VARCHAR(2),
ADD COLUMN     "hs_code" VARCHAR(10),
ADD COLUMN     "pallet_cartons" INTEGER,
ADD COLUMN     "pallet_cartons_per_layer" INTEGER,
ADD COLUMN     "export_note" TEXT,
ADD COLUMN     "export_note_en" TEXT;

-- 3) CreateIndex (الباركود فريد؛ القيم NULL المتعددة مسموحة في PostgreSQL)
CREATE UNIQUE INDEX "products_barcode_key" ON "products"("barcode");

-- 4) CHECK constraints (يدوية). القيمة NULL تجتاز أي قيد، فالحقول كلها تبقى اختيارية.
-- العبوة
ALTER TABLE "products" ADD CONSTRAINT "products_net_content_positive_chk"   CHECK ("net_content" IS NULL OR "net_content" > 0);
ALTER TABLE "products" ADD CONSTRAINT "products_net_content_unit_pair_chk"  CHECK (("net_content" IS NULL) = ("net_content_unit" IS NULL));
ALTER TABLE "products" ADD CONSTRAINT "products_barcode_format_chk"         CHECK ("barcode" IS NULL OR "barcode" ~ '^([0-9]{8}|[0-9]{12,14})$');
ALTER TABLE "products" ADD CONSTRAINT "products_pack_texts_not_blank_chk"   CHECK (
  ("pack_type" IS NULL OR btrim("pack_type") <> '') AND ("pack_type_en" IS NULL OR btrim("pack_type_en") <> '') AND
  ("pack_material" IS NULL OR btrim("pack_material") <> '') AND ("pack_material_en" IS NULL OR btrim("pack_material_en") <> ''));
-- الكرتونة
ALTER TABLE "products" ADD CONSTRAINT "products_carton_units_positive_chk"  CHECK ("carton_units" IS NULL OR "carton_units" > 0);
ALTER TABLE "products" ADD CONSTRAINT "products_carton_dims_positive_chk"   CHECK (
  ("carton_length_cm" IS NULL OR "carton_length_cm" > 0) AND ("carton_width_cm" IS NULL OR "carton_width_cm" > 0) AND ("carton_height_cm" IS NULL OR "carton_height_cm" > 0));
ALTER TABLE "products" ADD CONSTRAINT "products_carton_dims_all_or_none_chk" CHECK (
  ("carton_length_cm" IS NULL) = ("carton_width_cm" IS NULL) AND ("carton_width_cm" IS NULL) = ("carton_height_cm" IS NULL));
ALTER TABLE "products" ADD CONSTRAINT "products_carton_weights_positive_chk" CHECK (
  ("carton_net_weight_kg" IS NULL OR "carton_net_weight_kg" > 0) AND ("carton_gross_weight_kg" IS NULL OR "carton_gross_weight_kg" > 0));
ALTER TABLE "products" ADD CONSTRAINT "products_carton_gross_ge_net_chk"    CHECK (
  "carton_net_weight_kg" IS NULL OR "carton_gross_weight_kg" IS NULL OR "carton_gross_weight_kg" >= "carton_net_weight_kg");
-- الصلاحية والتخزين
ALTER TABLE "products" ADD CONSTRAINT "products_shelf_life_positive_chk"    CHECK ("shelf_life_value" IS NULL OR "shelf_life_value" > 0);
ALTER TABLE "products" ADD CONSTRAINT "products_shelf_life_pair_chk"        CHECK (("shelf_life_value" IS NULL) = ("shelf_life_unit" IS NULL));
ALTER TABLE "products" ADD CONSTRAINT "products_notes_not_blank_chk"        CHECK (
  ("storage_note" IS NULL OR btrim("storage_note") <> '') AND ("storage_note_en" IS NULL OR btrim("storage_note_en") <> '') AND
  ("export_note" IS NULL OR btrim("export_note") <> '') AND ("export_note_en" IS NULL OR btrim("export_note_en") <> ''));
-- التصدير
ALTER TABLE "products" ADD CONSTRAINT "products_origin_country_format_chk"  CHECK ("origin_country" IS NULL OR "origin_country" ~ '^[A-Z]{2}$');
ALTER TABLE "products" ADD CONSTRAINT "products_hs_code_format_chk"         CHECK ("hs_code" IS NULL OR "hs_code" ~ '^[0-9]{6,10}$');
ALTER TABLE "products" ADD CONSTRAINT "products_pallet_positive_chk"        CHECK (
  ("pallet_cartons" IS NULL OR "pallet_cartons" > 0) AND ("pallet_cartons_per_layer" IS NULL OR "pallet_cartons_per_layer" > 0));
ALTER TABLE "products" ADD CONSTRAINT "products_pallet_layer_le_total_chk"  CHECK (
  "pallet_cartons" IS NULL OR "pallet_cartons_per_layer" IS NULL OR "pallet_cartons_per_layer" <= "pallet_cartons");
