-- Phase F1: Guest purchase orders and future-ready optional pricing.
-- This migration is additive: existing catalog/forms data is preserved.

CREATE TYPE "purchase_order_status" AS ENUM (
  'new',
  'under_review',
  'awaiting_customer_approval',
  'confirmed',
  'rejected'
);

-- Optional future list price per individual product unit. Not exposed publicly.
ALTER TABLE "products"
  ADD COLUMN "unit_price" DECIMAL(12,2),
  ADD COLUMN "price_currency" VARCHAR(3);

ALTER TABLE "products"
  ADD CONSTRAINT "products_unit_price_nonnegative_chk"
    CHECK ("unit_price" IS NULL OR "unit_price" >= 0),
  ADD CONSTRAINT "products_price_currency_format_chk"
    CHECK ("price_currency" IS NULL OR "price_currency" ~ '^[A-Z]{3}$'),
  ADD CONSTRAINT "products_price_pair_chk"
    CHECK (("unit_price" IS NULL AND "price_currency" IS NULL)
        OR ("unit_price" IS NOT NULL AND "price_currency" IS NOT NULL));

CREATE TABLE "purchase_orders" (
  "id" SERIAL NOT NULL,
  "customer_name" VARCHAR(100) NOT NULL,
  "phone" VARCHAR(30) NOT NULL,
  "business_name" VARCHAR(150) NOT NULL,
  "city" VARCHAR(100) NOT NULL,
  "area" VARCHAR(100),
  "address" VARCHAR(300),
  "email" VARCHAR(254),
  "whatsapp" VARCHAR(30),
  "customer_note" TEXT,
  "admin_note" TEXT,
  "status" "purchase_order_status" NOT NULL DEFAULT 'new',
  "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

  CONSTRAINT "purchase_orders_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "purchase_orders_customer_name_not_blank_chk"
    CHECK (length(btrim("customer_name")) > 0),
  CONSTRAINT "purchase_orders_business_name_not_blank_chk"
    CHECK (length(btrim("business_name")) > 0),
  CONSTRAINT "purchase_orders_phone_format_chk"
    CHECK ("phone" ~ '^\+?[0-9]{7,15}$'),
  CONSTRAINT "purchase_orders_city_not_blank_chk"
    CHECK (length(btrim("city")) > 0),
  CONSTRAINT "purchase_orders_whatsapp_format_chk"
    CHECK ("whatsapp" IS NULL OR "whatsapp" ~ '^\+?[0-9]{7,15}$'),
  CONSTRAINT "purchase_orders_email_format_chk"
    CHECK ("email" IS NULL OR (length(btrim("email")) > 0 AND "email" ~* '^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'))
);

CREATE TABLE "purchase_order_items" (
  "id" SERIAL NOT NULL,
  "order_id" INTEGER NOT NULL,
  "product_id" INTEGER,
  "product_name" VARCHAR(150) NOT NULL,
  "product_name_en" VARCHAR(150),
  "product_slug" VARCHAR(150) NOT NULL,
  "carton_quantity" INTEGER NOT NULL DEFAULT 0,
  "unit_quantity" INTEGER NOT NULL DEFAULT 0,
  "units_per_carton" INTEGER,
  "total_units" INTEGER NOT NULL,
  "unit_price" DECIMAL(12,2),
  "price_currency" VARCHAR(3),

  CONSTRAINT "purchase_order_items_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "purchase_order_items_order_id_fkey"
    FOREIGN KEY ("order_id") REFERENCES "purchase_orders"("id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "purchase_order_items_product_id_fkey"
    FOREIGN KEY ("product_id") REFERENCES "products"("id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "purchase_order_items_product_name_not_blank_chk"
    CHECK (length(btrim("product_name")) > 0),
  CONSTRAINT "purchase_order_items_product_slug_format_chk"
    CHECK ("product_slug" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
  CONSTRAINT "purchase_order_items_carton_quantity_nonnegative_chk"
    CHECK ("carton_quantity" >= 0),
  CONSTRAINT "purchase_order_items_unit_quantity_nonnegative_chk"
    CHECK ("unit_quantity" >= 0),
  CONSTRAINT "purchase_order_items_units_per_carton_positive_chk"
    CHECK ("units_per_carton" IS NULL OR "units_per_carton" > 0),
  CONSTRAINT "purchase_order_items_cartons_require_units_per_carton_chk"
    CHECK ("carton_quantity" = 0 OR ("units_per_carton" IS NOT NULL AND "units_per_carton" > 0)),
  CONSTRAINT "purchase_order_items_total_units_consistent_chk"
    CHECK (
      "total_units" >= 1
      AND "total_units"::BIGINT =
        ("carton_quantity"::BIGINT * COALESCE("units_per_carton"::BIGINT, 0))
        + "unit_quantity"::BIGINT
    ),
  CONSTRAINT "purchase_order_items_unit_price_nonnegative_chk"
    CHECK ("unit_price" IS NULL OR "unit_price" >= 0),
  CONSTRAINT "purchase_order_items_price_currency_format_chk"
    CHECK ("price_currency" IS NULL OR "price_currency" ~ '^[A-Z]{3}$'),
  CONSTRAINT "purchase_order_items_price_pair_chk"
    CHECK (("unit_price" IS NULL AND "price_currency" IS NULL)
        OR ("unit_price" IS NOT NULL AND "price_currency" IS NOT NULL))
);

CREATE INDEX "purchase_orders_status_created_at_idx"
  ON "purchase_orders"("status", "created_at" DESC);
CREATE INDEX "purchase_orders_phone_created_at_idx"
  ON "purchase_orders"("phone", "created_at" DESC);
CREATE UNIQUE INDEX "purchase_order_items_order_id_product_id_key"
  ON "purchase_order_items"("order_id", "product_id");
CREATE INDEX "purchase_order_items_product_id_idx"
  ON "purchase_order_items"("product_id");
