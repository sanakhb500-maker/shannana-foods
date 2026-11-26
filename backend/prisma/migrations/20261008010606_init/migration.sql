-- ============================================================================
-- Shannana Foods — migration 1: init
-- يطابق prisma/schema.prisma المعتمد (PHASE 2) + قيود CHECK السبعة المعتمدة.
-- القسم 1–5: بنية Prisma القياسية.   القسم 6: CHECK constraints (يدوي — لا يعبّر عنها Prisma).
-- ============================================================================

-- 1) CreateEnum
CREATE TYPE "user_role" AS ENUM ('admin');

CREATE TYPE "product_tone" AS ENUM ('red', 'gold', 'rose', 'green');

CREATE TYPE "inquiry_source" AS ENUM ('product', 'contact', 'whatsapp');

CREATE TYPE "inquiry_status" AS ENUM ('new', 'contacted', 'closed');

CREATE TYPE "business_type" AS ENUM ('shop', 'distributor', 'wholesaler', 'restaurant', 'supermarket', 'other');

CREATE TYPE "distribution_status" AS ENUM ('new', 'contacted', 'qualified', 'closed');

-- 2) CreateTable
CREATE TABLE "users" (
    "id" SERIAL NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "email" VARCHAR(254) NOT NULL,
    "password_hash" VARCHAR(255) NOT NULL,
    "role" "user_role" NOT NULL DEFAULT 'admin',
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "users_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "categories" (
    "id" SERIAL NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "slug" VARCHAR(100) NOT NULL,
    "description" TEXT,
    "sort_order" INTEGER NOT NULL DEFAULT 0,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "categories_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "products" (
    "id" SERIAL NOT NULL,
    "name" VARCHAR(150) NOT NULL,
    "slug" VARCHAR(150) NOT NULL,
    "description" TEXT,
    "category_id" INTEGER NOT NULL,
    "image_url" VARCHAR(500) NOT NULL,
    "tone" "product_tone" NOT NULL DEFAULT 'gold',
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "is_featured" BOOLEAN NOT NULL DEFAULT false,
    "sort_order" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "products_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "inquiries" (
    "id" SERIAL NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "phone" VARCHAR(30),
    "email" VARCHAR(254),
    "message" TEXT,
    "product_id" INTEGER,
    "source" "inquiry_source" NOT NULL,
    "status" "inquiry_status" NOT NULL DEFAULT 'new',
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "inquiries_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "distribution_requests" (
    "id" SERIAL NOT NULL,
    "name" VARCHAR(100) NOT NULL,
    "business_name" VARCHAR(150) NOT NULL,
    "phone" VARCHAR(30) NOT NULL,
    "whatsapp" VARCHAR(30),
    "email" VARCHAR(254),
    "city" VARCHAR(100),
    "area" VARCHAR(100),
    "business_type" "business_type" NOT NULL,
    "message" TEXT,
    "status" "distribution_status" NOT NULL DEFAULT 'new',
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "distribution_requests_pkey" PRIMARY KEY ("id")
);

-- 3) CreateIndex (unique)
CREATE UNIQUE INDEX "users_email_key" ON "users"("email");

CREATE UNIQUE INDEX "categories_name_key" ON "categories"("name");

CREATE UNIQUE INDEX "categories_slug_key" ON "categories"("slug");

CREATE UNIQUE INDEX "products_slug_key" ON "products"("slug");

-- 4) CreateIndex (performance)
CREATE INDEX "categories_is_active_sort_order_idx" ON "categories"("is_active", "sort_order");

CREATE INDEX "products_is_active_sort_order_idx" ON "products"("is_active", "sort_order");

CREATE INDEX "products_category_id_is_active_sort_order_idx" ON "products"("category_id", "is_active", "sort_order");

CREATE INDEX "inquiries_status_created_at_idx" ON "inquiries"("status", "created_at" DESC);

CREATE INDEX "inquiries_product_id_idx" ON "inquiries"("product_id");

CREATE INDEX "distribution_requests_status_created_at_idx" ON "distribution_requests"("status", "created_at" DESC);

-- 5) AddForeignKey
-- Category -> Product : RESTRICT (لا يمكن حذف تصنيف فيه منتجات)
ALTER TABLE "products" ADD CONSTRAINT "products_category_id_fkey" FOREIGN KEY ("category_id") REFERENCES "categories"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- Product -> Inquiry : SET NULL (حذف المنتج لا يحذف الاستفسارات)
ALTER TABLE "inquiries" ADD CONSTRAINT "inquiries_product_id_fkey" FOREIGN KEY ("product_id") REFERENCES "products"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- 6) CHECK constraints (يدوية — Prisma لا يمثّلها في الـ schema ولا يحذفها لاحقًا)
ALTER TABLE "users"      ADD CONSTRAINT "users_email_lowercase_chk"        CHECK ("email" = lower("email"));

ALTER TABLE "categories" ADD CONSTRAINT "categories_slug_format_chk"       CHECK ("slug" ~ '^[a-z0-9]+(-[a-z0-9]+)*$');

ALTER TABLE "products"   ADD CONSTRAINT "products_slug_format_chk"         CHECK ("slug" ~ '^[a-z0-9]+(-[a-z0-9]+)*$');

ALTER TABLE "categories" ADD CONSTRAINT "categories_name_not_blank_chk"    CHECK (btrim("name") <> '');

ALTER TABLE "products"   ADD CONSTRAINT "products_name_not_blank_chk"      CHECK (btrim("name") <> '');

ALTER TABLE "products"   ADD CONSTRAINT "products_image_url_not_blank_chk" CHECK (btrim("image_url") <> '');

ALTER TABLE "inquiries"  ADD CONSTRAINT "inquiries_contact_method_chk"     CHECK (coalesce(btrim("phone"), '') <> '' OR coalesce(btrim("email"), '') <> '');
