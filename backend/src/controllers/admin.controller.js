import { ok } from '../utils/apiResponse.js'
import { getStats } from '../services/admin.service.js'

export async function stats(_req, res) {
  return ok(res, await getStats())
}
