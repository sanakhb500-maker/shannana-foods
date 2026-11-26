// Read-only check of the PHASE 5 seed data. Changes nothing and prints no secrets.
// Compares the DB with prisma/seed-data.js, so it describes the INITIAL state: if an admin edits
// products later, those fields will legitimately be reported as different.
import 'dotenv/config';
import { PrismaPg } from '@prisma/adapter-pg';
import { PrismaClient } from '../src/generated/prisma/client.ts';
import { categories, products } from '../prisma/seed-data.js';

const problems = [];
const check = (ok, label, detail = '') => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${!ok && detail ? ' — ' + detail : ''}`);
  if (!ok) problems.push(label);
};

if (!process.env.DATABASE_URL) { console.error('DATABASE_URL is not set (backend/.env).'); process.exit(1); }
const prisma = new PrismaClient({ adapter: new PrismaPg({ connectionString: process.env.DATABASE_URL }) });

try {
  const dbCats = await prisma.category.findMany({ orderBy: { sortOrder: 'asc' } });
  const dbProds = await prisma.product.findMany({ include: { category: true }, orderBy: { sortOrder: 'asc' } });

  check(dbCats.length === categories.length, `categories count = ${categories.length}`, `found ${dbCats.length}`);
  check(dbProds.length === products.length, `products count = ${products.length}`, `found ${dbProds.length}`);
  check(new Set(dbCats.map(c => c.slug)).size === dbCats.length && new Set(dbProds.map(p => p.slug)).size === dbProds.length, 'no duplicate slugs');

  const badCats = categories.filter(c => { const r = dbCats.find(x => x.slug === c.slug); return !r || r.name !== c.name || r.sortOrder !== c.sortOrder || !r.isActive; });
  check(badCats.length === 0, 'every category matches (name, slug, order, active)', badCats.map(c => c.slug).join(', '));

  const badProds = products.filter(p => {
    const r = dbProds.find(x => x.slug === p.slug);
    return !r || r.name !== p.name || r.description !== p.description || r.category.slug !== p.category ||
      r.imageUrl !== p.imageUrl || r.tone !== p.tone || r.sortOrder !== p.sortOrder || !r.isActive;
  });
  check(badProds.length === 0, 'every product matches (name, description, category, image, tone, order, active)', badProds.map(p => p.slug).join(', '));

  const sugar = dbProds.find(p => p.slug === 'sugar');
  check(sugar?.category.slug === 'grains-legumes', 'sugar is in "حبوب وبقوليات"');
  check(dbProds.find(p => p.slug === 'pizza-sauce')?.imageUrl === '/img/HeroProduct.png', 'pizza sauce uses HeroProduct.png');
  check(!dbProds.some(p => /splash/i.test(p.imageUrl)), 'no decorative images used as product images');

  const admins = await prisma.user.findMany({ select: { role: true, isActive: true, passwordHash: true } });
  check(admins.length >= 1, 'at least one admin user exists', 'none (run seed with ADMIN_EMAIL / ADMIN_PASSWORD)');
  check(admins.every(a => a.passwordHash.startsWith('$argon2id$')), 'all password hashes are Argon2id');
} finally {
  await prisma.$disconnect();
}

console.log(problems.length ? `\n${problems.length} check(s) failed.` : '\nAll seed checks passed.');
process.exit(problems.length ? 1 : 0);
