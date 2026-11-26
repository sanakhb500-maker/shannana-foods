// Read-only schema verification for Shannana Foods.
// Uses SELECT statements only; it never changes database content.
import { prisma } from '../src/config/prisma.js'

const EXPECTED = {
  tables: ['users', 'categories', 'products', 'inquiries', 'distribution_requests', 'purchase_orders', 'purchase_order_items'],
  enums: {
    user_role: ['admin'],
    product_tone: ['red', 'gold', 'rose', 'green'],
    inquiry_source: ['product', 'contact', 'whatsapp'],
    inquiry_status: ['new', 'contacted', 'closed'],
    business_type: ['shop', 'distributor', 'wholesaler', 'restaurant', 'supermarket', 'other'],
    distribution_status: ['new', 'contacted', 'qualified', 'closed'],
    content_unit: ['g', 'kg', 'ml', 'l'],
    time_unit: ['day', 'month', 'year'],
    purchase_order_status: ['new', 'under_review', 'awaiting_customer_approval', 'confirmed', 'rejected'],
  },
  foreignKeys: {
    products_category_id_fkey: ['products', 'categories', 'RESTRICT'],
    inquiries_product_id_fkey: ['inquiries', 'products', 'SET NULL'],
    purchase_order_items_order_id_fkey: ['purchase_order_items', 'purchase_orders', 'CASCADE'],
    purchase_order_items_product_id_fkey: ['purchase_order_items', 'products', 'SET NULL'],
  },
  checks: [
    'users_email_lowercase_chk',
    'categories_slug_format_chk',
    'products_slug_format_chk',
    'categories_name_not_blank_chk',
    'products_name_not_blank_chk',
    'products_image_url_not_blank_chk',
    'inquiries_contact_method_chk',
    // Product specifications — Phase 11B.1
    'products_net_content_positive_chk',
    'products_net_content_unit_pair_chk',
    'products_barcode_format_chk',
    'products_pack_texts_not_blank_chk',
    'products_carton_units_positive_chk',
    'products_carton_dims_positive_chk',
    'products_carton_dims_all_or_none_chk',
    'products_carton_weights_positive_chk',
    'products_carton_gross_ge_net_chk',
    'products_shelf_life_positive_chk',
    'products_shelf_life_pair_chk',
    'products_notes_not_blank_chk',
    'products_origin_country_format_chk',
    'products_hs_code_format_chk',
    'products_pallet_positive_chk',
    'products_pallet_layer_le_total_chk',
    // Future-ready optional prices — Phase F1
    'products_unit_price_nonnegative_chk',
    'products_price_currency_format_chk',
    'products_price_pair_chk',
    // Purchase orders — Phase F1
    'purchase_orders_customer_name_not_blank_chk',
    'purchase_orders_business_name_not_blank_chk',
    'purchase_orders_phone_format_chk',
    'purchase_orders_city_not_blank_chk',
    'purchase_orders_whatsapp_format_chk',
    'purchase_orders_email_format_chk',
    // Purchase order items — Phase F1
    'purchase_order_items_product_name_not_blank_chk',
    'purchase_order_items_product_slug_format_chk',
    'purchase_order_items_carton_quantity_nonnegative_chk',
    'purchase_order_items_unit_quantity_nonnegative_chk',
    'purchase_order_items_units_per_carton_positive_chk',
    'purchase_order_items_cartons_require_units_per_carton_chk',
    'purchase_order_items_total_units_consistent_chk',
    'purchase_order_items_unit_price_nonnegative_chk',
    'purchase_order_items_price_currency_format_chk',
    'purchase_order_items_price_pair_chk',
  ],
  indexes: [
    'users_email_key',
    'categories_name_key',
    'categories_slug_key',
    'products_slug_key',
    'categories_is_active_sort_order_idx',
    'products_is_active_sort_order_idx',
    'products_category_id_is_active_sort_order_idx',
    'inquiries_status_created_at_idx',
    'inquiries_product_id_idx',
    'distribution_requests_status_created_at_idx',
    'products_barcode_key',
    'purchase_orders_status_created_at_idx',
    'purchase_orders_phone_created_at_idx',
    'purchase_order_items_order_id_product_id_key',
    'purchase_order_items_product_id_idx',
  ],
}

// [data_type, nullable, character length, numeric precision, numeric scale, enum udt]
const C = (type, nullable = 'YES', length = null, precision = null, scale = null, enumType = null) =>
  ({ type, nullable, length, precision, scale, enum: enumType })
const EXPECTED_COLUMNS = {
  products: {
    net_content: C('numeric', 'YES', null, 10, 3),
    net_content_unit: C('USER-DEFINED', 'YES', null, null, null, 'content_unit'),
    pack_type: C('character varying', 'YES', 80),
    pack_type_en: C('character varying', 'YES', 80),
    pack_material: C('character varying', 'YES', 80),
    pack_material_en: C('character varying', 'YES', 80),
    barcode: C('character varying', 'YES', 14),
    carton_units: C('integer'),
    carton_length_cm: C('numeric', 'YES', null, 7, 2),
    carton_width_cm: C('numeric', 'YES', null, 7, 2),
    carton_height_cm: C('numeric', 'YES', null, 7, 2),
    carton_net_weight_kg: C('numeric', 'YES', null, 8, 3),
    carton_gross_weight_kg: C('numeric', 'YES', null, 8, 3),
    shelf_life_value: C('integer'),
    shelf_life_unit: C('USER-DEFINED', 'YES', null, null, null, 'time_unit'),
    storage_note: C('text'),
    storage_note_en: C('text'),
    origin_country: C('character varying', 'YES', 2),
    hs_code: C('character varying', 'YES', 10),
    pallet_cartons: C('integer'),
    pallet_cartons_per_layer: C('integer'),
    export_note: C('text'),
    export_note_en: C('text'),
    unit_price: C('numeric', 'YES', null, 12, 2),
    price_currency: C('character varying', 'YES', 3),
  },
  purchase_orders: {
    id: C('integer', 'NO'),
    customer_name: C('character varying', 'NO', 100),
    phone: C('character varying', 'NO', 30),
    business_name: C('character varying', 'NO', 150),
    city: C('character varying', 'NO', 100),
    area: C('character varying', 'YES', 100),
    address: C('character varying', 'YES', 300),
    email: C('character varying', 'YES', 254),
    whatsapp: C('character varying', 'YES', 30),
    customer_note: C('text'),
    admin_note: C('text'),
    status: C('USER-DEFINED', 'NO', null, null, null, 'purchase_order_status'),
    created_at: C('timestamp with time zone', 'NO'),
    updated_at: C('timestamp with time zone', 'NO'),
  },
  purchase_order_items: {
    id: C('integer', 'NO'),
    order_id: C('integer', 'NO'),
    product_id: C('integer'),
    product_name: C('character varying', 'NO', 150),
    product_name_en: C('character varying', 'YES', 150),
    product_slug: C('character varying', 'NO', 150),
    carton_quantity: C('integer', 'NO'),
    unit_quantity: C('integer', 'NO'),
    units_per_carton: C('integer'),
    total_units: C('integer', 'NO'),
    unit_price: C('numeric', 'YES', null, 12, 2),
    price_currency: C('character varying', 'YES', 3),
  },
}

let failures = 0
const line = (ok, message) => {
  if (!ok) failures++
  console.log(`  ${ok ? '✅' : '❌'} ${message}`)
}
const same = (a, b) => a.length === b.length && a.every((value, i) => value === b[i])

try {
  console.log('\n[الجداول]')
  const tables = (await prisma.$queryRaw`
    SELECT table_name FROM information_schema.tables
    WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
  `).map((row) => row.table_name)
  for (const table of EXPECTED.tables) line(tables.includes(table), table)
  const extra = tables.filter((table) => !EXPECTED.tables.includes(table) && table !== '_prisma_migrations')
  if (extra.length) line(false, `جداول غير متوقعة: ${extra.join(', ')}`)

  console.log('\n[Enums]')
  const enumRows = await prisma.$queryRaw`
    SELECT t.typname AS name, e.enumlabel AS label
    FROM pg_type t
    JOIN pg_enum e ON e.enumtypid = t.oid
    JOIN pg_namespace n ON n.oid = t.typnamespace AND n.nspname = 'public'
    ORDER BY t.typname, e.enumsortorder
  `
  for (const [name, values] of Object.entries(EXPECTED.enums)) {
    const actual = enumRows.filter((row) => row.name === name).map((row) => row.label)
    line(same(actual, values), `${name}: ${actual.join(' | ') || '(غير موجود)'}`)
  }

  console.log('\n[Foreign Keys]')
  const foreignKeys = await prisma.$queryRaw`
    SELECT c.conname AS name, c.conrelid::regclass::text AS tbl, c.confrelid::regclass::text AS ref,
      CASE c.confdeltype WHEN 'r' THEN 'RESTRICT' WHEN 'n' THEN 'SET NULL'
        WHEN 'c' THEN 'CASCADE' WHEN 'a' THEN 'NO ACTION' ELSE c.confdeltype::text END AS on_delete
    FROM pg_constraint c WHERE c.contype = 'f' AND c.connamespace = 'public'::regnamespace
  `
  for (const [name, [table, ref, onDelete]] of Object.entries(EXPECTED.foreignKeys)) {
    const fk = foreignKeys.find((row) => row.name === name)
    line(Boolean(fk) && fk.tbl === table && fk.ref === ref && fk.on_delete === onDelete,
      `${table} → ${ref} ON DELETE ${fk?.on_delete ?? '(غير موجود)'} [${name}]`)
  }

  console.log('\n[CHECK constraints]')
  const checks = await prisma.$queryRaw`
    SELECT conname AS name, conrelid::regclass::text AS tbl, convalidated AS validated
    FROM pg_constraint WHERE contype = 'c' AND connamespace = 'public'::regnamespace
  `
  for (const name of EXPECTED.checks) {
    const check = checks.find((row) => row.name === name)
    const expectedTable = name.startsWith('purchase_orders_') ? 'purchase_orders'
      : name.startsWith('purchase_order_items_') ? 'purchase_order_items'
      : name.startsWith('products_') ? 'products'
      : name.startsWith('users_') ? 'users'
      : name.startsWith('categories_') ? 'categories'
      : name.startsWith('inquiries_') ? 'inquiries' : null
    line(Boolean(check) && check.tbl === expectedTable && check.validated === true,
      check ? `${name} على ${check.tbl}${check.validated ? '' : ' (NOT VALID)'}` : `${name} (غير موجود)`)
  }

  console.log('\n[Indexes]')
  const indexes = (await prisma.$queryRaw`SELECT indexname FROM pg_indexes WHERE schemaname = 'public'`)
    .map((row) => row.indexname)
  for (const name of EXPECTED.indexes) line(indexes.includes(name), name)

  console.log('\n[أعمدة المواصفات والطلبات]')
  const columns = await prisma.$queryRaw`
    SELECT table_name AS tbl, column_name AS name, is_nullable AS nullable, data_type AS type,
      udt_name AS udt, character_maximum_length AS len,
      numeric_precision AS prec, numeric_scale AS scale
    FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name IN ('products', 'purchase_orders', 'purchase_order_items')
  `
  for (const [table, fields] of Object.entries(EXPECTED_COLUMNS)) {
    for (const [name, want] of Object.entries(fields)) {
      const actual = columns.find((row) => row.tbl === table && row.name === name)
      if (!actual) { line(false, `${table}.${name}: غير موجود`); continue }
      const okay = actual.type === want.type && actual.nullable === want.nullable
        && (want.length == null || Number(actual.len) === want.length)
        && (want.precision == null || (Number(actual.prec) === want.precision && Number(actual.scale) === want.scale))
        && (want.enum == null || actual.udt === want.enum)
      const detail = actual.type === 'USER-DEFINED' ? `enum ${actual.udt}`
        : actual.type === 'numeric' ? `numeric(${actual.prec},${actual.scale})`
        : actual.type === 'character varying' ? `varchar(${actual.len})` : actual.type
      line(okay, `${table}.${name}: ${detail}, ${actual.nullable === 'YES' ? 'NULL' : 'NOT NULL'}`)
    }
  }

  console.log('\n[Migration Phase F1]')
  const migration = await prisma.$queryRaw`
    SELECT migration_name AS name, finished_at IS NOT NULL AS done
    FROM _prisma_migrations WHERE migration_name = '20261118193000_add_purchase_orders'
  `
  line(migration.length === 1 && migration[0].done === true, '20261118193000_add_purchase_orders مطبّقة')

  console.log(failures === 0 ? '\n🎉 قاعدة البيانات مطابقة للمخطط المعتمد' : `\n⚠️ ${failures} فحص(ات) فاشل(ة)`)
} catch (error) {
  console.error('❌ تعذّر التحقق:', error.message?.trim().split('\n').pop() ?? error)
  failures++
} finally {
  await prisma.$disconnect()
}

process.exitCode = failures ? 1 : 0
