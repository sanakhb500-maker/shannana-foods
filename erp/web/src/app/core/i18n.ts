import { Injectable, effect, signal } from '@angular/core';

export type Lang = 'ar' | 'en';

const DICT: Record<string, { ar: string; en: string }> = {
  // app / shell
  'app.name': { ar: 'نظام شنانة ERP', en: 'Shannana ERP' },
  'nav.dashboard': { ar: 'الرئيسية', en: 'Dashboard' },
  'nav.masterData': { ar: 'البيانات الأساسية', en: 'Master data' },
  'nav.admin': { ar: 'الإدارة', en: 'Administration' },
  'nav.items': { ar: 'الأصناف', en: 'Items' },
  'nav.categories': { ar: 'تصنيفات الأصناف', en: 'Item categories' },
  'nav.units': { ar: 'وحدات القياس', en: 'Units' },
  'nav.warehouses': { ar: 'المستودعات', en: 'Warehouses' },
  'nav.suppliers': { ar: 'الموردون', en: 'Suppliers' },
  'nav.customers': { ar: 'العملاء', en: 'Customers' },
  'nav.drivers': { ar: 'السائقون', en: 'Drivers' },
  'nav.vehicles': { ar: 'المركبات', en: 'Vehicles' },
  'nav.currencies': { ar: 'العملات', en: 'Currencies' },
  'nav.rates': { ar: 'أسعار الصرف', en: 'Exchange rates' },
  'nav.users': { ar: 'المستخدمون', en: 'Users' },
  'nav.logout': { ar: 'تسجيل الخروج', en: 'Sign out' },
  // dashboard
  'dash.welcome': { ar: 'مرحباً', en: 'Welcome' },
  'dash.intro': { ar: 'جاهز الآن: البيانات الأساسية والمخازن والمخزون. الوحدات القادمة:', en: 'Ready now: master data and inventory. Coming next:' },
  'dash.phase2': { ar: 'المخازن والمخزون', en: 'Inventory & warehouses' },
  'dash.phase3': { ar: 'المشتريات', en: 'Purchasing' },
  'dash.phase4': { ar: 'المبيعات والتوزيع', en: 'Sales & distribution' },
  'dash.phase5': { ar: 'المحاسبة', en: 'Accounting' },
  'dash.soon': { ar: 'قريباً', en: 'Soon' },
  // login
  'login.title': { ar: 'تسجيل الدخول', en: 'Sign in' },
  'login.email': { ar: 'البريد الإلكتروني', en: 'Email' },
  'login.password': { ar: 'كلمة المرور', en: 'Password' },
  'login.submit': { ar: 'دخول', en: 'Sign in' },
  'login.failed': { ar: 'بيانات الدخول غير صحيحة', en: 'Invalid email or password' },
  // common
  'common.search': { ar: 'بحث…', en: 'Search…' },
  'common.add': { ar: 'إضافة', en: 'Add' },
  'common.edit': { ar: 'تعديل', en: 'Edit' },
  'common.save': { ar: 'حفظ', en: 'Save' },
  'common.cancel': { ar: 'إلغاء', en: 'Cancel' },
  'common.deactivate': { ar: 'تعطيل', en: 'Deactivate' },
  'common.activate': { ar: 'تفعيل', en: 'Activate' },
  'common.actions': { ar: 'إجراءات', en: 'Actions' },
  'common.active': { ar: 'فعّال', en: 'Active' },
  'common.inactive': { ar: 'معطّل', en: 'Inactive' },
  'common.status': { ar: 'الحالة', en: 'Status' },
  'common.showInactive': { ar: 'إظهار المعطّلين', en: 'Show inactive' },
  'common.empty': { ar: 'لا توجد بيانات', en: 'No records' },
  'common.loading': { ar: 'جاري التحميل…', en: 'Loading…' },
  'common.select': { ar: '— اختر —', en: '— Select —' },
  'common.none': { ar: '— بدون —', en: '— None —' },
  'common.confirmDeactivate': { ar: 'هل تريد تعطيل هذا السجل؟', en: 'Deactivate this record?' },
  'common.prev': { ar: 'السابق', en: 'Previous' },
  'common.next': { ar: 'التالي', en: 'Next' },
  'common.total': { ar: 'الإجمالي', en: 'Total' },
  'common.readOnly': { ar: 'عرض فقط', en: 'Read-only' },
  'common.saved': { ar: 'تم الحفظ', en: 'Saved' },
  'common.error': { ar: 'حدث خطأ', en: 'Something went wrong' },
  'common.theme': { ar: 'تبديل المظهر', en: 'Toggle theme' },
  // inventory
  'nav.inventory': { ar: 'المخازن والمخزون', en: 'Inventory' },
  'nav.stockBalances': { ar: 'أرصدة المخزون', en: 'Stock balances' },
  'nav.stockMovements': { ar: 'حركة المخزون', en: 'Stock movements' },
  'nav.lowStock': { ar: 'أصناف تحت حد الطلب', en: 'Low stock' },
  'nav.transfers': { ar: 'التحويلات بين المستودعات', en: 'Transfers' },
  'nav.adjustments': { ar: 'الجرد والتسويات', en: 'Adjustments' },
  'f.item': { ar: 'الصنف', en: 'Item' },
  'f.warehouse': { ar: 'المستودع', en: 'Warehouse' },
  'inv.allWarehouses': { ar: 'كل المستودعات', en: 'All warehouses' },
  'inv.allItems': { ar: 'كل الأصناف', en: 'All items' },
  'inv.allStatuses': { ar: 'كل الحالات', en: 'All statuses' },
  'inv.includeZero': { ar: 'إظهار الأرصدة الصفرية', en: 'Include zero balances' },
  'inv.totalValue': { ar: 'قيمة المخزون', en: 'Stock value' },
  'inv.quantity': { ar: 'الكمية', en: 'Quantity' },
  'inv.avgCost': { ar: 'متوسط التكلفة', en: 'Avg cost' },
  'inv.unitCost': { ar: 'تكلفة الوحدة', en: 'Unit cost' },
  'inv.value': { ar: 'القيمة', en: 'Value' },
  'inv.from': { ar: 'من', en: 'From' },
  'inv.to': { ar: 'إلى', en: 'To' },
  'inv.document': { ar: 'المستند', en: 'Document' },
  'inv.onHand': { ar: 'الرصيد الحالي', en: 'On hand' },
  'inv.shortfall': { ar: 'النقص', en: 'Shortfall' },
  'inv.noLowStock': { ar: 'لا توجد أصناف تحت حد الطلب', en: 'No items below their reorder level' },
  'inv.number': { ar: 'الرقم', en: 'Number' },
  'inv.lines': { ar: 'البنود', en: 'Lines' },
  'inv.notes': { ar: 'ملاحظات', en: 'Notes' },
  'inv.reason': { ar: 'السبب', en: 'Reason' },
  'inv.newTransfer': { ar: 'تحويل جديد', en: 'New transfer' },
  'inv.newAdjustment': { ar: 'تسوية جديدة', en: 'New adjustment' },
  'inv.addLine': { ar: 'إضافة بند', en: 'Add line' },
  'inv.noLines': { ar: 'لا توجد بنود', en: 'No lines' },
  'inv.confirm': { ar: 'اعتماد', en: 'Confirm' },
  'inv.cancelDoc': { ar: 'إلغاء المستند', en: 'Cancel document' },
  'inv.reverse': { ar: 'إلغاء وعكس الحركة', en: 'Cancel and reverse' },
  'inv.confirmCancel': { ar: 'هل تريد إلغاء هذا المستند؟', en: 'Cancel this document?' },
  'inv.confirmReverse': { ar: 'سيتم عكس حركات المخزون لهذا المستند. هل تريد المتابعة؟', en: 'This reverses the stock movements of this document. Continue?' },
  'inv.counted': { ar: 'الكمية المجرودة', en: 'Counted qty' },
  'inv.surplusCost': { ar: 'تكلفة الزيادة (اختياري)', en: 'Surplus cost (optional)' },
  'inv.systemQty': { ar: 'كمية النظام', en: 'System qty' },
  'inv.difference': { ar: 'الفرق', en: 'Difference' },
  'inv.adjustHint': { ar: 'أدخل الكمية الفعلية المجرودة؛ يُسجَّل الفرق عن كمية النظام عند الاعتماد.', en: 'Enter the actual counted quantity; the difference from the system quantity is posted on confirm.' },
  'status.Draft': { ar: 'مسودة', en: 'Draft' },
  'status.Confirmed': { ar: 'معتمد', en: 'Confirmed' },
  'status.Cancelled': { ar: 'ملغى', en: 'Cancelled' },
  'reason.Count': { ar: 'جرد', en: 'Stock count' },
  'reason.Opening': { ar: 'رصيد افتتاحي', en: 'Opening balance' },
  'reason.Damage': { ar: 'تالف / هالك', en: 'Damage / write-off' },
  'reason.Other': { ar: 'أخرى', en: 'Other' },
  'mt.TransferOut': { ar: 'تحويل صادر', en: 'Transfer out' },
  'mt.TransferIn': { ar: 'تحويل وارد', en: 'Transfer in' },
  'mt.AdjustmentOut': { ar: 'تسوية (نقص)', en: 'Adjustment out' },
  'mt.AdjustmentIn': { ar: 'تسوية (زيادة)', en: 'Adjustment in' },
  'mt.Reversal': { ar: 'عكس حركة', en: 'Reversal' },
  'common.close': { ar: 'إغلاق', en: 'Close' },
  'common.remove': { ar: 'حذف', en: 'Remove' },
  // fields
  'f.code': { ar: 'الرمز', en: 'Code' },
  'f.nameAr': { ar: 'الاسم (عربي)', en: 'Name (Arabic)' },
  'f.nameEn': { ar: 'الاسم (إنجليزي)', en: 'Name (English)' },
  'f.name': { ar: 'الاسم', en: 'Name' },
  'f.symbol': { ar: 'الرمز المختصر', en: 'Symbol' },
  'f.isLocal': { ar: 'عملة محلية', en: 'Local currency' },
  'f.currency': { ar: 'العملة', en: 'Currency' },
  'f.date': { ar: 'التاريخ', en: 'Date' },
  'f.rate': { ar: 'السعر (مقابل المحلية)', en: 'Rate (to local)' },
  'f.unit': { ar: 'الوحدة', en: 'Unit' },
  'f.category': { ar: 'التصنيف', en: 'Category' },
  'f.barcode': { ar: 'الباركود', en: 'Barcode' },
  'f.costPrice': { ar: 'سعر التكلفة', en: 'Cost price' },
  'f.salePrice': { ar: 'سعر البيع', en: 'Sale price' },
  'f.reorderLevel': { ar: 'حد إعادة الطلب', en: 'Reorder level' },
  'f.address': { ar: 'العنوان', en: 'Address' },
  'f.phone': { ar: 'الهاتف', en: 'Phone' },
  'f.email': { ar: 'البريد الإلكتروني', en: 'Email' },
  'f.taxNumber': { ar: 'الرقم الضريبي', en: 'Tax number' },
  'f.type': { ar: 'النوع', en: 'Type' },
  'f.creditLimit': { ar: 'حد الائتمان', en: 'Credit limit' },
  'f.licenseNumber': { ar: 'رقم الرخصة', en: 'License number' },
  'f.plateNumber': { ar: 'رقم اللوحة', en: 'Plate number' },
  'f.description': { ar: 'الوصف', en: 'Description' },
  'f.capacityKg': { ar: 'السعة (كغ)', en: 'Capacity (kg)' },
  'f.fullName': { ar: 'الاسم الكامل', en: 'Full name' },
  'f.password': { ar: 'كلمة المرور', en: 'Password' },
  'f.newPassword': { ar: 'كلمة مرور جديدة', en: 'New password' },
  'f.roles': { ar: 'الأدوار', en: 'Roles' },
  // enums
  'enum.Retail': { ar: 'مفرق', en: 'Retail' },
  'enum.Distributor': { ar: 'موزّع', en: 'Distributor' },
  // roles
  'role.Admin': { ar: 'مدير النظام', en: 'Admin' },
  'role.Accountant': { ar: 'محاسب', en: 'Accountant' },
  'role.StoreKeeper': { ar: 'أمين مستودع', en: 'Store keeper' },
  'role.Purchasing': { ar: 'مشتريات', en: 'Purchasing' },
  'role.Sales': { ar: 'مبيعات', en: 'Sales' },
  'role.Dispatcher': { ar: 'مسؤول توزيع', en: 'Dispatcher' },
  // users screen
  'users.reset': { ar: 'إعادة تعيين كلمة المرور', en: 'Reset password' },
  'users.rolesHint': { ar: 'اختر دوراً واحداً على الأقل', en: 'Pick at least one role' },
};

@Injectable({ providedIn: 'root' })
export class I18n {
  readonly lang = signal<Lang>(this.initial());

  constructor() {
    effect(() => {
      const l = this.lang();
      document.documentElement.lang = l;
      document.documentElement.dir = l === 'ar' ? 'rtl' : 'ltr';
      localStorage.setItem('erp.lang', l);
    });
  }

  /** Translate a key; reading `lang()` makes templates re-render on language switch. */
  t(key: string): string {
    const e = DICT[key];
    return e ? e[this.lang()] : key;
  }

  /** Pick the Arabic or English name of a record for display. */
  name(row: { nameAr?: string; nameEn?: string; name?: string } | null | undefined): string {
    if (!row) return '';
    if (row.name) return row.name;
    return (this.lang() === 'ar' ? row.nameAr || row.nameEn : row.nameEn || row.nameAr) ?? '';
  }

  toggle(): void {
    this.lang.update((l) => (l === 'ar' ? 'en' : 'ar'));
  }

  private initial(): Lang {
    const saved = localStorage.getItem('erp.lang');
    return saved === 'en' || saved === 'ar' ? saved : 'ar'; // Arabic by default
  }
}
