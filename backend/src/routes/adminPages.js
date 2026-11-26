import fs from 'node:fs'
import path from 'node:path'
import { Router } from 'express'
import express from 'express'
import { readSessionUser } from '../services/auth.service.js'

// صفحات لوحة الإدارة (frontend/admin). صفحة اللوحة لا تُقدَّم إلا لمدير مسجّل الدخول،
// وصفحة الدخول تحوّل المسجّل إلى اللوحة. ملفات CSS/JS ثابتة وعامة (لا أسرار فيها).
export function createAdminPages(frontendDir) {
  const dir = path.join(frontendDir, 'admin')
  const router = Router()
  if (!fs.existsSync(dir)) return router

  const sendPage = (res, file) => res.sendFile(path.join(dir, file), { headers: { 'Cache-Control': 'no-store' } })

  // صفحات محمية بالجلسة: المسار → ملف HTML
  const pages = { '/': 'dashboard.html', '/products': 'products.html', '/categories': 'categories.html', '/inquiries': 'inquiries.html', '/distribution': 'distribution.html' }
  for (const [route, file] of Object.entries(pages)) {
    router.get(route, async (req, res) => {
      if (!(await readSessionUser(req))) return res.redirect(302, '/admin/login')
      sendPage(res, file)
    })
  }
  router.get('/login', async (req, res) => {
    if (await readSessionUser(req)) return res.redirect(302, '/admin')
    sendPage(res, 'login.html')
  })
  // منع فتح ملفات HTML مباشرة (تجاوز فحص الجلسة)
  router.get(/\.html$/, (_req, res) => res.redirect(302, '/admin'))
  router.use(express.static(dir, { index: false, redirect: false }))
  return router
}
