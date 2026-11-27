using System.Globalization;

namespace Erp.Application.Common;

/// <summary>
/// Human names for request properties, used by FluentValidation in its messages ("'Quantity' must be greater than 0").
/// Without this the Arabic messages would still quote English property names.
/// </summary>
public static class FieldNames
{
    private static readonly Dictionary<string, (string Ar, string En)> Names = new()
    {
        ["Code"] = ("الرمز", "Code"), ["NameAr"] = ("الاسم (عربي)", "Name (Arabic)"), ["NameEn"] = ("الاسم (إنجليزي)", "Name (English)"),
        ["Name"] = ("الاسم", "Name"), ["Symbol"] = ("الرمز المختصر", "Symbol"), ["CurrencyId"] = ("العملة", "Currency"),
        ["Rate"] = ("السعر", "Rate"), ["ExchangeRate"] = ("سعر الصرف", "Exchange rate"), ["Date"] = ("التاريخ", "Date"),
        ["UnitId"] = ("الوحدة", "Unit"), ["CostPrice"] = ("سعر التكلفة", "Cost price"), ["SalePrice"] = ("سعر البيع", "Sale price"),
        ["ReorderLevel"] = ("حد إعادة الطلب", "Reorder level"), ["Barcode"] = ("الباركود", "Barcode"), ["Address"] = ("العنوان", "Address"),
        ["Email"] = ("البريد الإلكتروني", "Email"), ["Phone"] = ("الهاتف", "Phone"), ["CreditLimit"] = ("حد الائتمان", "Credit limit"),
        ["TaxNumber"] = ("الرقم الضريبي", "Tax number"), ["PlateNumber"] = ("رقم اللوحة", "Plate number"), ["CapacityKg"] = ("السعة (كغ)", "Capacity (kg)"),
        ["Description"] = ("البيان", "Description"), ["Type"] = ("النوع", "Type"), ["FromWarehouseId"] = ("مستودع المصدر", "Source warehouse"),
        ["ToWarehouseId"] = ("مستودع الوجهة", "Destination warehouse"), ["WarehouseId"] = ("المستودع", "Warehouse"), ["Notes"] = ("الملاحظات", "Notes"),
        ["Lines"] = ("البنود", "Lines"), ["ItemId"] = ("الصنف", "Item"), ["Quantity"] = ("الكمية", "Quantity"),
        ["CountedQuantity"] = ("الكمية المجرودة", "Counted quantity"), ["UnitCost"] = ("تكلفة الوحدة", "Unit cost"), ["UnitPrice"] = ("سعر الوحدة", "Unit price"),
        ["Reason"] = ("السبب", "Reason"), ["SupplierId"] = ("المورد", "Supplier"), ["CustomerId"] = ("العميل", "Customer"),
        ["ExpectedDate"] = ("التاريخ المتوقع", "Expected date"), ["RequiredDate"] = ("التاريخ المطلوب", "Required date"), ["DueDate"] = ("تاريخ الاستحقاق", "Due date"),
        ["SupplierInvoiceNumber"] = ("رقم فاتورة المورد", "Supplier invoice number"), ["Amount"] = ("المبلغ", "Amount"), ["Method"] = ("طريقة الدفع", "Method"),
        ["Reference"] = ("المرجع", "Reference"), ["DriverId"] = ("السائق", "Driver"), ["VehicleId"] = ("المركبة", "Vehicle"),
        ["DeliveryNoteIds"] = ("سندات التسليم", "Delivery notes"), ["Status"] = ("الحالة", "Status"), ["AccountId"] = ("الحساب", "Account"),
        ["Debit"] = ("المدين", "Debit"), ["Credit"] = ("الدائن", "Credit"), ["PartyType"] = ("نوع الطرف", "Party type"), ["PartyId"] = ("الطرف", "Party"),
        ["PurchaseOrderId"] = ("أمر الشراء", "Purchase order"), ["SalesOrderId"] = ("أمر البيع", "Sales order"),
    };

    /// <summary>Display name for a property, in the current UI culture (ar / en); falls back to the raw name.</summary>
    public static string? For(string? property)
    {
        if (property is null || !Names.TryGetValue(property, out var n)) return property;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? n.Ar : n.En;
    }
}
