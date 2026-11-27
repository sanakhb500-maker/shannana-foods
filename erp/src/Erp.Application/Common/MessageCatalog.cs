using System.Text.RegularExpressions;

namespace Erp.Application.Common;

/// <summary>
/// Turns the server's English messages into Arabic. The code keeps throwing plain English messages (readable in logs
/// and tests); this catalog is applied at the API boundary according to the caller's language.
/// A <c>{name}</c> in a template matches any text (and is carried into the Arabic text). A placeholder called
/// <c>{msg}</c> is itself an English message that is translated recursively. A test scans the source and fails if a
/// message has no entry here, so a new message cannot ship untranslated.
/// </summary>
public static class MessageCatalog
{
    private sealed record Rule(Regex Pattern, string[] Names, string Arabic);

    private static readonly (string En, string Ar)[] Entries =
    [
        // ----- generic -----
        ("Validation failed.", "فشل التحقق من البيانات."),
        ("Request failed.", "فشل الطلب."),
        ("Request body is required.", "محتوى الطلب مطلوب."),
        ("Not found.", "غير موجود."),
        ("Unexpected server error.", "حدث خطأ غير متوقع في الخادم."),
        ("CSRF validation failed.", "فشل التحقق الأمني (CSRF)."),
        ("The document is already cancelled.", "المستند ملغى مسبقًا."),
        ("Only draft documents can be edited or confirmed.", "يمكن تعديل أو اعتماد المسودات فقط."),
        ("A record with the same unique value (code / barcode / plate) already exists.", "يوجد سجل بنفس القيمة الفريدة (الرمز / الباركود / اللوحة)."),
        ("A referenced record does not exist or is still in use.", "السجل المشار إليه غير موجود أو ما زال مستخدمًا."),
        ("Code may contain letters, digits, '.', '_' and '-' only.", "يجوز أن يحتوي الرمز على أحرف وأرقام و '.' و '_' و '-' فقط."),
        ("At least one line is required.", "مطلوب بند واحد على الأقل."),
        ("At least two lines are required.", "مطلوب بندان على الأقل."),
        ("A party id is required with a party type.", "رقم المورد/العميل مطلوب مع نوعه."),

        // ----- sign-in and users -----
        ("Invalid email or password.", "البريد الإلكتروني أو كلمة المرور غير صحيحة."),
        ("Account temporarily locked. Try again later.", "الحساب مقفل مؤقتًا. حاول لاحقًا."),
        ("A valid email is required.", "بريد إلكتروني صالح مطلوب."),
        ("Full name is required.", "الاسم الكامل مطلوب."),
        ("At least one role is required.", "مطلوب دور واحد على الأقل."),
        ("Unknown role.", "دور غير معروف."),
        ("You cannot deactivate yourself or remove your own Admin role.", "لا يمكنك تعطيل نفسك أو إزالة دور مدير النظام عن حسابك."),
        ("Passwords must be at least {n} characters.", "يجب ألا تقل كلمة المرور عن {n} أحرف."),
        ("Passwords must have at least one digit ('0'-'9').", "يجب أن تحتوي كلمة المرور على رقم واحد على الأقل."),
        ("Passwords must have at least one uppercase ('A'-'Z').", "يجب أن تحتوي كلمة المرور على حرف كبير واحد على الأقل."),
        ("Passwords must have at least one lowercase ('a'-'z').", "يجب أن تحتوي كلمة المرور على حرف صغير واحد على الأقل."),
        ("Passwords must have at least one non alphanumeric character.", "يجب أن تحتوي كلمة المرور على رمز خاص واحد على الأقل."),
        ("Username '{u}' is already taken.", "اسم المستخدم '{u}' مستخدم مسبقًا."),
        ("Email '{e}' is already taken.", "البريد الإلكتروني '{e}' مستخدم مسبقًا."),

        // ----- not found -----
        ("Supplier not found.", "المورد غير موجود."),
        ("Customer not found.", "العميل غير موجود."),
        ("Account not found.", "الحساب غير موجود."),
        ("Adjustment not found.", "التسوية غير موجودة."),
        ("Transfer not found.", "التحويل غير موجود."),
        ("Purchase order not found.", "أمر الشراء غير موجود."),
        ("Goods receipt not found.", "مستند الاستلام غير موجود."),
        ("Purchase invoice not found.", "فاتورة المورد غير موجودة."),
        ("Payment not found.", "الدفعة غير موجودة."),
        ("Sales order not found.", "أمر البيع غير موجود."),
        ("Delivery note not found.", "سند التسليم غير موجود."),
        ("Sales invoice not found.", "فاتورة المبيعات غير موجودة."),
        ("Sales return not found.", "مرتجع المبيعات غير موجود."),
        ("Purchase return not found.", "مرتجع المشتريات غير موجود."),
        ("Receipt not found.", "سند القبض غير موجود."),
        ("Trip not found.", "الرحلة غير موجودة."),
        ("Proof not found.", "ملف الإثبات غير موجود."),
        ("Stop not found.", "المحطة غير موجودة."),
        ("Journal entry not found.", "القيد غير موجود."),

        // ----- master data -----
        ("A local currency already exists.", "توجد عملة محلية مسبقًا."),
        ("The local-currency flag cannot be changed.", "لا يمكن تغيير خاصية العملة المحلية."),
        ("The local currency cannot be deactivated.", "لا يمكن تعطيل العملة المحلية."),
        ("The local currency always has a rate of 1; add rates for foreign currencies only.", "العملة المحلية سعرها 1 دائمًا؛ أضف أسعارًا للعملات الأجنبية فقط."),
        ("Currency not found.", "العملة غير موجودة."),
        ("Currency not found or inactive.", "العملة غير موجودة أو معطّلة."),
        ("Unit not found or inactive.", "وحدة القياس غير موجودة أو معطّلة."),
        ("Category not found or inactive.", "التصنيف غير موجود أو معطّل."),
        ("This item still has stock on hand. Adjust it to zero first.", "ما زال لهذا الصنف رصيد في المخزون. صفّره بتسوية أولًا."),
        ("This warehouse still holds stock. Transfer or adjust it to zero first.", "ما زال في هذا المستودع مخزون. انقله أو صفّره بتسوية أولًا."),

        // ----- shared document checks -----
        ("An item does not exist or is inactive.", "أحد الأصناف غير موجود أو معطّل."),
        ("A warehouse does not exist or is inactive.", "أحد المستودعات غير موجود أو معطّل."),
        ("The warehouse does not exist or is inactive.", "المستودع غير موجود أو معطّل."),
        ("The currency does not exist or is inactive.", "العملة غير موجودة أو معطّلة."),
        ("The supplier does not exist or is inactive.", "المورد غير موجود أو معطّل."),
        ("The customer does not exist or is inactive.", "العميل غير موجود أو معطّل."),
        ("No exchange rate for {cur} on or before {date}. Enter one.", "لا يوجد سعر صرف للعملة {cur} في {date} أو قبله. أدخل سعرًا."),

        // ----- inventory -----
        ("Insufficient stock for item {item} in warehouse {wh}: available {avail}, required {req}.", "رصيد غير كافٍ للصنف {item} في المستودع {wh}: المتاح {avail}، المطلوب {req}."),
        ("This document cannot be cancelled because its stock has already been used.{msg}", "لا يمكن إلغاء هذا المستند لأن مخزونه استُخدم بالفعل.{msg}"),
        ("Source and destination warehouses must be different.", "يجب أن يختلف مستودع المصدر عن مستودع الوجهة."),
        ("An item may appear only once per document.", "لا يجوز تكرار الصنف في المستند الواحد."),
        ("An item may appear only once per order.", "لا يجوز تكرار الصنف في الأمر الواحد."),
        ("An item may appear only once per receipt.", "لا يجوز تكرار الصنف في مستند الاستلام الواحد."),
        ("An item may appear only once per invoice.", "لا يجوز تكرار الصنف في الفاتورة الواحدة."),
        ("An item may appear only once per delivery note.", "لا يجوز تكرار الصنف في سند التسليم الواحد."),
        ("An item may appear only once per return.", "لا يجوز تكرار الصنف في المرتجع الواحد."),
        ("The purchase invoice does not exist.", "فاتورة المورد غير موجودة."),
        ("The sales invoice does not exist.", "فاتورة المبيعات غير موجودة."),
        ("Only a confirmed invoice can be linked to a return.", "لا يمكن ربط المرتجع إلا بفاتورة مؤكدة."),
        ("The supplier differs from the invoice's supplier.", "المورد يختلف عن مورد الفاتورة."),
        ("The customer differs from the invoice's customer.", "العميل يختلف عن عميل الفاتورة."),
        ("The currency differs from the invoice's currency.", "العملة تختلف عن عملة الفاتورة."),
        ("Item {code} is not on the linked invoice.", "الصنف {code} غير موجود في الفاتورة المرتبطة."),
        ("Returned quantity for item {code} exceeds what was invoiced and not yet returned ({qty}).", "كمية المرتجع للصنف {code} تتجاوز ما فُوتر ولم يُرجَع بعد ({qty})."),
        ("The invoice discount cannot exceed the sum of the lines.", "خصم الفاتورة لا يمكن أن يتجاوز مجموع البنود."),
        ("No driver is linked to this account.", "لا يوجد سائق مرتبط بهذا الحساب."),
        ("Only Delivered or Failed can be recorded.", "يمكن تسجيل «تم التسليم» أو «تعذّر» فقط."),
        ("A reason is required when delivery fails.", "يجب ذكر السبب عند تعذّر التسليم."),
        ("The amount must be greater than zero.", "يجب أن يكون المبلغ أكبر من صفر."),
        ("Only PNG or JPEG images up to 3 MB are accepted.", "يُقبل فقط ملف صورة PNG أو JPEG بحجم أقصاه 3 ميغابايت."),
        ("Choose Signature or Photo.", "اختر توقيعًا أو صورة."),
        ("A stop can hold at most 5 proof files.", "تقبل المحطة 5 ملفات إثبات كحد أقصى."),
        ("This account can only use the driver app.", "هذا الحساب مخصص لتطبيق السائق فقط."),
        ("The Driver role needs a linked driver record.", "دور السائق يحتاج إلى سجل سائق مرتبط."),
        ("The driver does not exist.", "السائق غير موجود."),
        ("That driver is already linked to another account.", "هذا السائق مرتبط بحساب آخر."),
        ("Returns are already linked to this invoice. Cancel them first.", "توجد مرتجعات مرتبطة بهذه الفاتورة. ألغِها أولًا."),

        // ----- purchasing -----
        ("The purchase order does not exist.", "أمر الشراء غير موجود."),
        ("Only a confirmed purchase order can be received or invoiced.", "يمكن استلام أو فوترة أمر شراء معتمد فقط."),
        ("The supplier differs from the purchase order's supplier.", "المورد يختلف عن مورد أمر الشراء."),
        ("The currency differs from the purchase order's currency.", "العملة تختلف عن عملة أمر الشراء."),
        ("A line can reference an order line only when the document is linked to a purchase order.", "لا يجوز ربط بند ببند أمر شراء إلا إذا كان المستند مرتبطًا بأمر شراء."),
        ("Every line must reference a purchase order line when the document is linked to an order.", "يجب أن يرتبط كل بند ببند من أمر الشراء عندما يكون المستند مرتبطًا بأمر."),
        ("An order line does not belong to the purchase order.", "بند الأمر لا يتبع أمر الشراء."),
        ("A line's item does not match its purchase order line.", "صنف البند لا يطابق بند أمر الشراء."),
        ("A purchase order line may appear only once per document.", "لا يجوز تكرار بند أمر الشراء في المستند الواحد."),
        ("The linked purchase order is no longer confirmed.", "أمر الشراء المرتبط لم يعد معتمدًا."),
        ("This order already has confirmed receipts or invoices. Cancel those first.", "لهذا الأمر استلامات أو فواتير معتمدة. ألغِها أولًا."),
        ("Receipt quantity for item {code} exceeds the remaining ordered quantity ({qty}).", "كمية الاستلام للصنف {code} تتجاوز الكمية المتبقية من الأمر ({qty})."),
        ("Invoice quantity for item {code} exceeds the received, not-yet-invoiced quantity ({qty}).", "كمية الفاتورة للصنف {code} تتجاوز الكمية المستلمة وغير المفوترة ({qty})."),
        ("Invoices have already been confirmed against this receipt's quantities. Cancel them first.", "توجد فواتير معتمدة على كميات هذا الاستلام. ألغِها أولًا."),

        // ----- sales -----
        ("The sales order does not exist.", "أمر البيع غير موجود."),
        ("Only a confirmed sales order can be delivered or invoiced.", "يمكن تسليم أو فوترة أمر بيع معتمد فقط."),
        ("The customer differs from the sales order's customer.", "العميل يختلف عن عميل أمر البيع."),
        ("The currency differs from the sales order's currency.", "العملة تختلف عن عملة أمر البيع."),
        ("A line can reference an order line only when the document is linked to a sales order.", "لا يجوز ربط بند ببند أمر بيع إلا إذا كان المستند مرتبطًا بأمر بيع."),
        ("Every line must reference a sales order line when the document is linked to an order.", "يجب أن يرتبط كل بند ببند من أمر البيع عندما يكون المستند مرتبطًا بأمر."),
        ("An order line does not belong to the sales order.", "بند الأمر لا يتبع أمر البيع."),
        ("A line's item does not match its sales order line.", "صنف البند لا يطابق بند أمر البيع."),
        ("A sales order line may appear only once per document.", "لا يجوز تكرار بند أمر البيع في المستند الواحد."),
        ("The linked sales order is no longer confirmed.", "أمر البيع المرتبط لم يعد معتمدًا."),
        ("This order already has confirmed deliveries or invoices. Cancel those first.", "لهذا الأمر تسليمات أو فواتير معتمدة. ألغِها أولًا."),
        ("Delivery quantity for item {code} exceeds the remaining ordered quantity ({qty}).", "كمية التسليم للصنف {code} تتجاوز الكمية المتبقية من الأمر ({qty})."),
        ("Invoice quantity for item {code} exceeds the delivered, not-yet-invoiced quantity ({qty}).", "كمية الفاتورة للصنف {code} تتجاوز الكمية المسلّمة وغير المفوترة ({qty})."),
        ("Invoices have already been confirmed against this delivery's quantities. Cancel them first.", "توجد فواتير معتمدة على كميات هذا التسليم. ألغِها أولًا."),
        ("Credit limit exceeded: limit {limit}, current balance {balance}, this invoice {amount} (local currency).", "تم تجاوز حد الائتمان: الحد {limit}، الرصيد الحالي {balance}، هذه الفاتورة {amount} (بالعملة المحلية)."),
        ("This delivery is on trip {trip}, which has already left. Cancel or fix the trip first.", "هذا التسليم ضمن الرحلة {trip} التي انطلقت. ألغِ الرحلة أو عالجها أولًا."),

        // ----- trips -----
        ("The driver does not exist or is inactive.", "السائق غير موجود أو معطّل."),
        ("The vehicle does not exist or is inactive.", "المركبة غير موجودة أو معطّلة."),
        ("The driver already has trip {trip} on that date.", "السائق لديه الرحلة {trip} في هذا التاريخ."),
        ("The vehicle already has trip {trip} on that date.", "المركبة لديها الرحلة {trip} في هذا التاريخ."),
        ("A delivery note does not exist.", "أحد سندات التسليم غير موجود."),
        ("A delivery note may appear only once per trip.", "لا يجوز تكرار سند التسليم في الرحلة الواحدة."),
        ("Delivery note {number} is cancelled and cannot be on a trip.", "سند التسليم {number} ملغى ولا يمكن إضافته لرحلة."),
        ("Delivery note {number} was already delivered on trip {trip}.", "سند التسليم {number} سُلّم بالفعل ضمن الرحلة {trip}."),
        ("Delivery note {number} is already on trip {trip}.", "سند التسليم {number} موجود بالفعل ضمن الرحلة {trip}."),
        ("Only a planned trip can be edited.", "يمكن تعديل الرحلة المخططة فقط."),
        ("Only a planned trip can be started.", "يمكن إطلاق الرحلة المخططة فقط."),
        ("A trip needs at least one stop before it can start.", "تحتاج الرحلة إلى محطة واحدة على الأقل قبل الانطلاق."),
        ("These delivery notes must be confirmed before the trip starts: {list}.", "يجب اعتماد سندات التسليم التالية قبل انطلاق الرحلة: {list}."),
        ("Only a trip in transit can be completed.", "يمكن إنهاء الرحلة التي في الطريق فقط."),
        ("Mark every stop as delivered or failed before completing the trip.", "حدّد نتيجة كل محطة (تم التسليم أو تعذّر) قبل إنهاء الرحلة."),
        ("Only a planned or in-transit trip can be cancelled.", "يمكن إلغاء الرحلة المخططة أو التي في الطريق فقط."),
        ("Stop results can only be recorded while the trip is in transit.", "لا تُسجَّل نتائج المحطات إلا والرحلة في الطريق."),
        ("A delivery note may appear only once per trip.", "لا يجوز تكرار سند التسليم في الرحلة الواحدة."),

        // ----- allocations, expenses, revaluation -----
        ("Allocation amounts must be greater than zero.", "يجب أن تكون مبالغ التخصيص أكبر من صفر."),
        ("The allocations exceed the payment amount.", "مجموع التخصيصات يتجاوز مبلغ الدفعة."),
        ("An invoice may appear only once among the allocations.", "لا يجوز تكرار الفاتورة ضمن التخصيصات."),
        ("An allocated invoice does not exist.", "إحدى الفواتير المخصَّص لها غير موجودة."),
        ("Invoice {number} is not confirmed, so it cannot be paid yet.", "الفاتورة {number} غير معتمدة، فلا يمكن سدادها بعد."),
        ("Invoice {number} belongs to another supplier.", "الفاتورة {number} تخص مورّدًا آخر."),
        ("Invoice {number} belongs to another customer.", "الفاتورة {number} تخص عميلًا آخر."),
        ("Invoice {number} is in a different currency from the payment.", "الفاتورة {number} بعملة تختلف عن عملة الدفعة."),
        ("The allocation to invoice {number} exceeds its outstanding amount ({amount}).", "التخصيص للفاتورة {number} يتجاوز المبلغ المستحق عليها ({amount})."),
        ("A receipt can only be allocated to sales invoices.", "يمكن تخصيص سند القبض لفواتير المبيعات فقط."),
        ("Payments are already allocated to this invoice. Cancel them first.", "توجد دفعات مخصَّصة لهذه الفاتورة. ألغِها أولًا."),
        ("An expense line uses an inactive or header account.", "أحد بنود المصروف يستخدم حسابًا معطّلًا أو رئيسيًا."),
        ("Expense lines must use an expense account.", "يجب أن تستخدم بنود المصروف حساب مصروفات."),
        ("Expense invoice not found.", "فاتورة المصروفات غير موجودة."),
        ("Expense voucher not found.", "سند المصروف غير موجود."),
        ("A revaluation for that date already exists.", "توجد إعادة تقييم لهذا التاريخ مسبقًا."),
        ("Nothing to revalue: no open foreign-currency invoice differs from the closing rate.", "لا شيء لإعادة تقييمه: لا توجد فاتورة مفتوحة بعملة أجنبية يختلف سعرها عن سعر الإقفال."),

        // ----- accounting -----
        ("Due date cannot be before the invoice date.", "تاريخ الاستحقاق لا يسبق تاريخ الفاتورة."),
        ("Expected date cannot be before the order date.", "التاريخ المتوقع لا يسبق تاريخ الأمر."),
        ("Required date cannot be before the order date.", "التاريخ المطلوب لا يسبق تاريخ الأمر."),
        ("Each line needs exactly one of debit or credit, greater than zero.", "يجب أن يحمل كل بند مدينًا أو دائنًا فقط، بقيمة أكبر من صفر."),
        ("The entry is not balanced: debits {debit} vs credits {credit}.", "القيد غير متوازن: المدين {debit} مقابل الدائن {credit}."),
        ("An account does not exist.", "أحد الحسابات غير موجود."),
        ("Account {code} is inactive or is a header account that cannot take postings.", "الحساب {code} معطّل أو حساب رئيسي لا يقبل قيودًا."),
        ("A line's supplier/customer does not exist.", "المورد/العميل في أحد البنود غير موجود."),
        ("An account on this entry is no longer active or postable.", "أحد حسابات هذا القيد لم يعد فعّالًا أو لا يقبل قيودًا."),
        ("Only a draft entry can be edited.", "يمكن تعديل القيد المسودة فقط."),
        ("Only a draft entry can be posted.", "يمكن ترحيل القيد المسودة فقط."),
        ("Only a draft entry can be deleted.", "يمكن حذف القيد المسودة فقط."),
        ("Entries generated by a document cannot be changed here.", "القيود المولَّدة من مستندات لا تُعدَّل من هنا."),
        ("Entries generated by a document are reversed by cancelling that document.", "القيود المولَّدة من مستندات تُعكس بإلغاء المستند نفسه."),
        ("Only a posted, original entry can be reversed.", "يمكن عكس قيد مرحَّل وأصلي فقط."),
        ("The accounting period is closed up to {date}; nothing can be posted on or before that date.", "الفترة المحاسبية مغلقة حتى {date}؛ لا يمكن الترحيل في هذا التاريخ أو قبله."),
        ("No active account is mapped to the posting role '{role}'. Check the chart of accounts.", "لا يوجد حساب فعّال مرتبط بدور الترحيل '{role}'. راجع شجرة الحسابات."),
        ("An account cannot be its own parent.", "لا يمكن أن يكون الحساب أبًا لنفسه."),
        ("The parent account does not exist.", "الحساب الأب غير موجود."),
        ("The parent must be a header account (not postable).", "يجب أن يكون الأب حسابًا رئيسيًا (لا يقبل قيودًا)."),
        ("An account must have the same type as its parent.", "يجب أن يطابق نوع الحساب نوع أبيه."),
        ("The type cannot change once the account has postings.", "لا يمكن تغيير النوع بعد وجود قيود على الحساب."),
        ("An account with postings must stay postable.", "الحساب الذي عليه قيود يجب أن يبقى قابلًا للترحيل."),
        ("An account with children must be a header (not postable).", "الحساب الذي له حسابات فرعية يجب أن يكون رئيسيًا (لا يقبل قيودًا)."),
        ("Accounts used by automatic postings must keep their type and stay postable.", "الحسابات المستخدمة في الترحيل التلقائي يجب أن تبقى بنوعها وقابلة للترحيل."),
        ("This account is used by automatic postings and cannot be deactivated.", "هذا الحساب مستخدم في الترحيل التلقائي ولا يمكن تعطيله."),
        ("Deactivate or move its child accounts first.", "عطّل الحسابات الفرعية أو انقلها أولًا."),
        ("The account still has a balance.", "ما زال للحساب رصيد."),
    ];

    private static readonly Rule[] Rules = [.. Entries.Select(Compile)];

    private static Rule Compile((string En, string Ar) e)
    {
        var names = new List<string>();
        var pattern = new System.Text.StringBuilder("^");
        var last = 0;
        foreach (Match m in Regex.Matches(e.En, @"\{(\w+)\}"))
        {
            pattern.Append(Regex.Escape(e.En[last..m.Index]));
            names.Add(m.Groups[1].Value);
            pattern.Append(m.Groups[1].Value == "msg" ? "(.*)" : "(.+?)");
            last = m.Index + m.Length;
        }
        pattern.Append(Regex.Escape(e.En[last..])).Append('$');
        return new Rule(new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), [.. names], e.Ar);
    }

    /// <summary>Returns the Arabic text for an English message, or null when the catalog has no entry for it.</summary>
    public static string? TryArabic(string english)
    {
        if (string.IsNullOrEmpty(english)) return null;
        foreach (var r in Rules)
        {
            var m = r.Pattern.Match(english);
            if (!m.Success) continue;
            var text = r.Arabic;
            for (var i = 0; i < r.Names.Length; i++)
            {
                var value = m.Groups[i + 1].Value;
                if (r.Names[i] == "msg") value = value.Length > 0 ? " " + (TryArabic(value.Trim()) ?? value.Trim()) : "";
                text = text.Replace("{" + r.Names[i] + "}", value);
            }
            return text;
        }
        return null;
    }

    /// <summary>Message in the caller's language ("ar" or "en"); unknown messages pass through unchanged.</summary>
    public static string Localize(string? english, string? lang) =>
        english is null ? "" : string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase) ? TryArabic(english) ?? english : english;
}
