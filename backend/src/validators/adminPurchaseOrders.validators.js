import { z } from 'zod'
import { cleanText } from '../utils/formGuards.js'

export const PURCHASE_ORDER_STATUSES = [
  'new',
  'under_review',
  'awaiting_customer_approval',
  'confirmed',
  'rejected',
]

const blank = (value) => (typeof value === 'string' && value.trim() === '' ? undefined : value)

export const purchaseOrderListQuery = z.object({
  status: z.enum(['all', ...PURCHASE_ORDER_STATUSES]).default('all'),
  search: z.preprocess(blank, z.string().trim().max(100, 'عبارة البحث طويلة').optional()),
  page: z.preprocess(blank, z.coerce.number().int().min(1).max(100000).default(1)),
  pageSize: z.preprocess(blank, z.coerce.number().int().min(1).max(100).default(20)),
})

export const purchaseOrderIdParam = z.object({
  id: z.coerce.number().int().min(1).max(2_147_483_647),
})

export const updatePurchaseOrderBody = z.object({
  status: z.enum(PURCHASE_ORDER_STATUSES).optional(),
  adminNote: z.preprocess(
    (value) => (typeof value === 'string' ? cleanText(value) : value),
    z.string().max(2000, 'الملاحظة طويلة جدًا').optional(),
  ),
}).refine((value) => value.status !== undefined || value.adminNote !== undefined, {
  message: 'لا توجد تغييرات لحفظها',
})
