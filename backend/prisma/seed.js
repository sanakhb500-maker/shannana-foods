// Shannana Foods — PHASE 5 seed (safe to re-run).
//   * Creates the 5 categories and 20 products if they are missing. Existing rows are NEVER updated,
//     so later admin edits survive a re-run. Nothing is deleted.
//   * Creates the first admin from ADMIN_* environment variables if that email does not exist yet.
//     An existing account is left completely untouched (password, name, active flag).
//   * Never prints the password, its hash or DATABASE_URL.
import 'dotenv/config';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import argon2 from 'argon2';
import { z } from 'zod';
import { PrismaPg } from '@prisma/adapter-pg';
import { PrismaClient } from '../src/generated/prisma/client.ts';
import { categories, products } from './seed-data.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const imgDir = path.resolve(here, '../../frontend/img');

const adminSchema = z.object({
  name: z.string().trim().min(1, 'ADMIN_NAME is empty').max(100, 'ADMIN_NAME is longer than 100 characters'),
  email: z.string().trim().toLowerCase().pipe(z.email('ADMIN_EMAIL is not a valid email address').max(254)),
  password: z
    .string()
    .min(12, 'ADMIN_PASSWORD must be at least 12 characters')
    .max(128, 'ADMIN_PASSWORD must be at most 128 characters')
    .refine(p => !/change[-_ ]?me|password|example|your[-_ ]?password/i.test(p), 'ADMIN_PASSWORD looks like a placeholder; choose a real one'),
});

// Fail before touching the database if the admin variables are present but wrong.
function readAdminConfig() {
  const { ADMIN_NAME, ADMIN_EMAIL, ADMIN_PASSWORD } = process.env;
  if (!ADMIN_EMAIL && !ADMIN_PASSWORD) return null;
  if (!ADMIN_EMAIL || !ADMIN_PASSWORD) throw new Error('Set BOTH ADMIN_EMAIL and ADMIN_PASSWORD (or neither).');
  const parsed = adminSchema.safeParse({ name: ADMIN_NAME?.trim() || 'Admin', email: ADMIN_EMAIL, password: ADMIN_PASSWORD });
  if (!parsed.success) throw new Error(parsed.error.issues.map(i => i.message).join('; '));
  return parsed.data;
}

// Every product image must exist in frontend/img (checked only when that folder is reachable).
function checkImages() {
  if (!fs.existsSync(imgDir)) { console.log('  images: frontend/img not found next to backend/, file check skipped'); return; }
  const files = new Set(fs.readdirSync(imgDir));
  const missing = products.filter(p => !files.has(p.imageUrl.replace(/^\/img\//, '')));
  if (missing.length) throw new Error('Missing image files in frontend/img: ' + missing.map(p => p.imageUrl).join(', '));
  console.log(`  images: all ${products.length} product images found in frontend/img`);
}

const redact = text => [process.env.DATABASE_URL, process.env.ADMIN_PASSWORD]
  .filter(Boolean)
  .reduce((t, secret) => t.split(secret).join('[redacted]'), String(text));

async function main() {
  if (!process.env.DATABASE_URL) throw new Error('DATABASE_URL is not set (backend/.env).');
  console.log('Seeding Shannana Foods…');
  const admin = readAdminConfig();
  checkImages();

  const prisma = new PrismaClient({
  adapter: new PrismaPg({
    connectionString: process.env.DATABASE_URL,
  }),
  transactionOptions: {
    maxWait: 15000,
    timeout: 60000,
  },
});
  try {
    const stats = { categories: { created: 0, existing: 0 }, products: { created: 0, existing: 0 } };

    await prisma.$transaction(async tx => {
      const categoryId = {};
      for (const c of categories) {
        let row = await tx.category.findFirst({ where: { OR: [{ slug: c.slug }, { name: c.name }] } });
        if (row) stats.categories.existing++;
        else { row = await tx.category.create({ data: { name: c.name, nameEn: c.nameEn ?? null, slug: c.slug, sortOrder: c.sortOrder } }); stats.categories.created++; }
        categoryId[c.slug] = row.id;
      }
      for (const p of products) {
        const found = await tx.product.findUnique({ where: { slug: p.slug } });
        if (found) { stats.products.existing++; continue; }
        await tx.product.create({
          data: { name: p.name, nameEn: p.nameEn ?? null, descriptionEn: p.descriptionEn ?? null, slug: p.slug, description: p.description, categoryId: categoryId[p.category], imageUrl: p.imageUrl, tone: p.tone, sortOrder: p.sortOrder },
        });
        stats.products.created++;
      }
    });
    console.log(`  categories: ${stats.categories.created} created, ${stats.categories.existing} already existed`);
    console.log(`  products:   ${stats.products.created} created, ${stats.products.existing} already existed`);

    if (!admin) {
      console.log('  admin: ADMIN_EMAIL / ADMIN_PASSWORD not set, admin step skipped');
    } else if (await prisma.user.findUnique({ where: { email: admin.email } })) {
      console.log('  admin: account already exists, left untouched');
    } else {
      const passwordHash = await argon2.hash(admin.password, { type: argon2.argon2id, memoryCost: 65536, timeCost: 3, parallelism: 1 });
      try {
        await prisma.user.create({ data: { name: admin.name, email: admin.email, passwordHash, role: 'admin' } });
        console.log('  admin: account created (password stored as Argon2id hash)');
      } catch (e) {
        if (e?.code === 'P2002') console.log('  admin: account already exists, left untouched');
        else throw e;
      }
    }

    const [c, p, u] = await Promise.all([prisma.category.count(), prisma.product.count(), prisma.user.count()]);
    console.log(`Done. Totals in DB: ${c} categories, ${p} products, ${u} users.`);
  } finally {
    await prisma.$disconnect();
  }
}

main().catch(err => {
  console.error('Seed failed:', redact(err?.message ?? err));
  process.exitCode = 1;
});
