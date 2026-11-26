import { fail } from '../utils/apiResponse.js'

// أي مسار /api لا يطابقه شيء
export const apiNotFound = (_req, res) => fail(res, 404, 'المسار غير موجود')
