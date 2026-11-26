// خطأ "متوقَّع" يُرمى من الخدمات ويتحول إلى استجابة JSON بالرمز المناسب.
export class AppError extends Error {
  constructor(statusCode, message, errors) {
    super(message)
    this.name = 'AppError'
    this.statusCode = statusCode
    this.errors = errors // اختياري: [{ field, message }]
  }
}
