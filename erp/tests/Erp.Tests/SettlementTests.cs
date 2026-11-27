using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

/// <summary>Payment allocation, realised exchange differences, open invoices / ageing, expenses and revaluation.</summary>
public class SettlementTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const int Local = 1;
    private const int Usd = 2;

    private sealed record Session(HttpClient Client, string Csrf);

    private async Task<Session> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return new Session(client, json.GetProperty("data").GetProperty("csrfToken").GetString()!);
    }

    private Task<Session> AdminAsync() => LoginAsync("admin@shannana.local", "Admin#12345");

    private async Task<Session> UserWithRoleAsync(string role)
    {
        var admin = await AdminAsync();
        var email = $"{role.ToLowerInvariant()}-{Uid()}@test.local";
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users", new { email, fullName = role, password = "Passw0rd!x", roles = new[] { role } }, HttpStatusCode.Created);
        return await LoginAsync(email, "Passw0rd!x");
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(Session s, HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (method != HttpMethod.Get) req.Headers.Add("X-CSRF-Token", s.Csrf);
        var res = await s.Client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement);
    }

    private static async Task<JsonElement> OkAsync(Session s, HttpMethod m, string url, object? body = null, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var (status, json) = await SendAsync(s, m, url, body);
        Assert.True(status == expect, $"{m} {url} -> {(int)status}: {json}");
        return json.GetProperty("data");
    }

    private static async Task<string> FailAsync(Session s, HttpMethod m, string url, object? body, HttpStatusCode expect = HttpStatusCode.BadRequest)
    {
        var (status, json) = await SendAsync(s, m, url, body);
        Assert.True(status == expect, $"{m} {url} -> {(int)status}: {json}");
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
    }

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>Three random letters: a valid, unique ISO-style currency code.</summary>
    private static string Letters() => new(Enumerable.Range(0, 3).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());

    // ---------- fixtures ----------
    private static async Task<int> NewSupplierAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/suppliers", new { code = "S" + Uid(), nameAr = "مورد", nameEn = "Supplier" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewCustomerAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers", new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Retail" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "X" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static readonly Dictionary<string, int> AccountIds = [];

    private static async Task<int> AccountAsync(Session s, string code)
    {
        if (AccountIds.TryGetValue(code, out var id)) return id;
        foreach (var r in (await OkAsync(s, HttpMethod.Get, "/api/v1/accounts?pageSize=200")).GetProperty("items").EnumerateArray())
            AccountIds[r.GetProperty("code").GetString()!] = r.GetProperty("id").GetInt32();
        return AccountIds[code];
    }

    private static async Task<decimal> NetAsync(Session s, string code, string? to = null)
    {
        var tb = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance" + (to is null ? "" : $"?to={to}"));
        var row = tb.GetProperty("rows").EnumerateArray().FirstOrDefault(r => r.GetProperty("code").GetString() == code);
        return row.ValueKind == JsonValueKind.Undefined ? 0m : row.GetProperty("closing").GetDecimal();
    }

    private sealed class Snap(Session s)
    {
        private readonly Dictionary<string, decimal> _before = [];
        public async Task<Snap> TakeAsync(params string[] codes) { foreach (var c in codes) _before[c] = await NetAsync(s, c); return this; }
        public async Task<decimal> DeltaAsync(string code) => await NetAsync(s, code) - _before[code];
    }

    /// <summary>A confirmed purchase invoice for `amount` in the given currency (one line, no order).</summary>
    private static async Task<int> PurchaseInvoiceAsync(Session s, int supplier, int item, decimal amount, int currency, decimal? rate, string? due = null)
    {
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", new
        {
            date = due is not null && string.CompareOrdinal(due, "2026-11-01") < 0 ? "2025-12-01" : "2026-11-01", dueDate = due, supplierId = supplier, currencyId = currency, exchangeRate = rate,
            lines = new[] { new { itemId = item, quantity = 1, unitPrice = amount } },
        }, HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{id}/confirm");
        return id;
    }

    private static async Task<int> SalesInvoiceAsync(Session s, int customer, int item, decimal amount, int currency, decimal? rate, string? due = null)
    {
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", new
        {
            date = due is not null && string.CompareOrdinal(due, "2026-11-01") < 0 ? "2025-12-01" : "2026-11-01", dueDate = due, customerId = customer, currencyId = currency, exchangeRate = rate,
            lines = new[] { new { itemId = item, quantity = 1, unitPrice = amount } },
        }, HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{id}/confirm");
        return id;
    }

    private static object Alloc(string kind, int invoiceId, decimal amount) => new { kind, invoiceId, amount };

    private static async Task<JsonElement> PayAsync(Session s, int supplier, decimal amount, int currency, decimal? rate, string method, params object[] allocations)
    {
        var p = await OkAsync(s, HttpMethod.Post, "/api/v1/supplierpayments",
            new { date = "2026-11-10", supplierId = supplier, currencyId = currency, exchangeRate = rate, amount, method, allocations }, HttpStatusCode.Created);
        return await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{p.GetProperty("id").GetInt32()}/confirm");
    }

    private static async Task<JsonElement> ReceiveAsync(Session s, int customer, decimal amount, int currency, decimal? rate, string method, params object[] allocations)
    {
        var r = await OkAsync(s, HttpMethod.Post, "/api/v1/customerreceipts",
            new { date = "2026-11-10", customerId = customer, currencyId = currency, exchangeRate = rate, amount, method, allocations }, HttpStatusCode.Created);
        return await OkAsync(s, HttpMethod.Post, $"/api/v1/customerreceipts/{r.GetProperty("id").GetInt32()}/confirm");
    }

    // =====================================================================

    [Fact]
    public async Task Paying_a_foreign_bill_at_a_worse_rate_books_an_exchange_loss_and_clears_the_payable_at_the_invoice_rate()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var inv = await PurchaseInvoiceAsync(s, supplier, item, 100, Usd, 0.71m); // owes 71.00 local
        var snap = await new Snap(s).TakeAsync("2100", "1200", "5300", "4300");

        var pay = await PayAsync(s, supplier, 100, Usd, 0.75m, "BankTransfer", Alloc("PurchaseInvoice", inv, 100)); // pays 75.00 local
        Assert.Equal(71m, pay.GetProperty("localSettled").GetDecimal());

        Assert.Equal(71m, await snap.DeltaAsync("2100"));    // payable cleared at the INVOICE rate
        Assert.Equal(-75m, await snap.DeltaAsync("1200"));   // cash actually left
        Assert.Equal(4m, await snap.DeltaAsync("5300"));     // realised loss
        Assert.Equal(0m, await snap.DeltaAsync("4300"));

        var stmt = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Equal(0m, stmt.GetProperty("closingBalance").GetDecimal()); // the supplier account is clean
        Assert.Empty((await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}")).EnumerateArray());
    }

    [Fact]
    public async Task Collecting_a_foreign_invoice_at_a_better_rate_books_an_exchange_gain()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var inv = await SalesInvoiceAsync(s, customer, item, 100, Usd, 0.71m); // receivable 71.00
        var snap = await new Snap(s).TakeAsync("1300", "1200", "5300", "4300");

        await ReceiveAsync(s, customer, 100, Usd, 0.75m, "BankTransfer", Alloc("SalesInvoice", inv, 100)); // receives 75.00

        Assert.Equal(-71m, await snap.DeltaAsync("1300"));
        Assert.Equal(75m, await snap.DeltaAsync("1200"));
        Assert.Equal(-4m, await snap.DeltaAsync("4300"));
        Assert.Equal(0m, await snap.DeltaAsync("5300"));
        Assert.Equal(0m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}")).GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task The_unallocated_part_stays_on_account_at_the_payment_rate()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var inv = await PurchaseInvoiceAsync(s, supplier, item, 100, Usd, 0.71m);
        var snap = await new Snap(s).TakeAsync("2100", "1200", "5300");

        // pay 150, assign 100 to the bill: 50 stays on account
        var pay = await PayAsync(s, supplier, 150, Usd, 0.75m, "BankTransfer", Alloc("PurchaseInvoice", inv, 100));
        Assert.Equal(108.5m, pay.GetProperty("localSettled").GetDecimal()); // 71.00 + 50 x 0.75
        Assert.Equal(108.5m, await snap.DeltaAsync("2100"));
        Assert.Equal(-112.5m, await snap.DeltaAsync("1200"));               // 150 x 0.75 left the bank
        Assert.Equal(4m, await snap.DeltaAsync("5300"));                    // only the allocated part realises a difference

        var stmt = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Equal(-37.5m, stmt.GetProperty("closingBalance").GetDecimal()); // the supplier owes us 37.5 (advance)

        var rec = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation");
        foreach (var r in rec.EnumerateArray().Where(r => r.GetProperty("key").GetString() != "Inventory"))
            Assert.Equal(0m, r.GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task Allocations_are_validated_and_outstanding_tracks_confirmed_payments_only()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var other = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var inv = await PurchaseInvoiceAsync(s, supplier, item, 100, Usd, 0.71m);
        var foreign = await PurchaseInvoiceAsync(s, other, item, 10, Usd, 0.71m);

        object Body(decimal amount, int cur, params object[] a) => new { date = "2026-11-10", supplierId = supplier, currencyId = cur, exchangeRate = 0.75m, amount, method = "Cash", allocations = a };

        Assert.Contains("exceed the payment", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(50, Usd, Alloc("PurchaseInvoice", inv, 60))));
        Assert.Contains("another supplier", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(50, Usd, Alloc("PurchaseInvoice", foreign, 10))));
        Assert.Contains("different currency", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(500, Local, Alloc("PurchaseInvoice", inv, 100))));
        Assert.Contains("only once", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(50, Usd, Alloc("PurchaseInvoice", inv, 10), Alloc("PurchaseInvoice", inv, 10))));
        Assert.Contains("outstanding", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(150, Usd, Alloc("PurchaseInvoice", inv, 101))));

        // a DRAFT payment does not reduce what is outstanding; a confirmed one does
        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(100, Usd, Alloc("PurchaseInvoice", inv, 100)), HttpStatusCode.Created);
        var open = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}");
        Assert.Equal(100m, open[0].GetProperty("outstanding").GetDecimal());

        var confirmed = await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{draft.GetProperty("id").GetInt32()}/confirm");
        Assert.Empty((await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}")).EnumerateArray());

        // the second payment cannot over-settle it, and the invoice cannot be cancelled underneath a payment
        Assert.Contains("outstanding", await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", Body(1, Usd, Alloc("PurchaseInvoice", inv, 1))));
        Assert.Contains("allocated", await FailAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{inv}/cancel", null));

        // cancelling the payment brings the invoice back
        await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{confirmed.GetProperty("id").GetInt32()}/cancel");
        Assert.Equal(100m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}"))[0].GetProperty("outstanding").GetDecimal());
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{inv}/cancel");
    }

    [Fact]
    public async Task Open_invoices_and_ageing_bucket_by_days_past_due()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);

        var old = await PurchaseInvoiceAsync(s, supplier, item, 40, Local, null, due: "2026-01-01");     // long overdue
        var current = await PurchaseInvoiceAsync(s, supplier, item, 10, Local, null, due: "2099-01-01"); // not yet due
        var open = (await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}")).EnumerateArray().ToList();
        Assert.Equal(2, open.Count);
        Assert.Equal("over90", open.First(r => r.GetProperty("invoiceId").GetInt32() == old).GetProperty("bucket").GetString());
        Assert.Equal("current", open.First(r => r.GetProperty("invoiceId").GetInt32() == current).GetProperty("bucket").GetString());
        Assert.Equal(open.OrderBy(r => r.GetProperty("dueDate").GetString()).First().GetProperty("invoiceId").GetInt32(), open[0].GetProperty("invoiceId").GetInt32()); // oldest first

        var ageing = (await OkAsync(s, HttpMethod.Get, "/api/v1/purchasing/ageing")).EnumerateArray().First(r => r.GetProperty("partyId").GetInt32() == supplier);
        Assert.Equal(40m, ageing.GetProperty("over90").GetDecimal());
        Assert.Equal(10m, ageing.GetProperty("current").GetDecimal());
        Assert.Equal(50m, ageing.GetProperty("total").GetDecimal());

        var sale = await SalesInvoiceAsync(s, customer, item, 25, Local, null, due: "2026-11-20"); // a week late
        var sAgeing = (await OkAsync(s, HttpMethod.Get, "/api/v1/sales/ageing")).EnumerateArray().First(r => r.GetProperty("partyId").GetInt32() == customer);
        Assert.Equal(25m, sAgeing.GetProperty("days1To30").GetDecimal());
        Assert.Equal(sale, (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/open-invoices?customerId={customer}"))[0].GetProperty("invoiceId").GetInt32());

        // part-payment shrinks it
        await ReceiveAsync(s, customer, 10, Local, null, "Cash", Alloc("SalesInvoice", sale, 10));
        Assert.Equal(15m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/open-invoices?customerId={customer}"))[0].GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task Expense_invoice_posts_to_expense_accounts_and_the_payable_and_can_be_paid_by_allocation()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var rent = await AccountAsync(s, "5400");
        var utilities = await AccountAsync(s, "5500");
        var snap = await new Snap(s).TakeAsync("5400", "5500", "2100", "1100");

        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/expenseinvoices", new
        {
            date = "2026-11-02", supplierId = supplier, supplierInvoiceNumber = "BILL-9", currencyId = Local,
            lines = new[] { new { accountId = rent, description = "November rent", amount = 60 }, new { accountId = utilities, description = "Electricity", amount = 40 } },
        }, HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, inv.GetProperty("number").ValueKind);
        Assert.Equal(100m, inv.GetProperty("totalAmount").GetDecimal());
        var id = inv.GetProperty("id").GetInt32();

        var confirmed = await OkAsync(s, HttpMethod.Post, $"/api/v1/expenseinvoices/{id}/confirm");
        Assert.StartsWith("EXP-", confirmed.GetProperty("number").GetString());
        Assert.Equal(60m, await snap.DeltaAsync("5400"));
        Assert.Equal(40m, await snap.DeltaAsync("5500"));
        Assert.Equal(-100m, await snap.DeltaAsync("2100"));
        Assert.Equal(100m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}")).GetProperty("closingBalance").GetDecimal());

        var open = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}");
        Assert.Equal("ExpenseInvoice", open[0].GetProperty("kind").GetString());

        await PayAsync(s, supplier, 100, Local, null, "Cash", Alloc("ExpenseInvoice", id, 100));
        Assert.Equal(0m, await snap.DeltaAsync("2100"));
        Assert.Equal(-100m, await snap.DeltaAsync("1100"));
        Assert.Contains("allocated", await FailAsync(s, HttpMethod.Post, $"/api/v1/expenseinvoices/{id}/cancel", null));
    }

    [Fact]
    public async Task Foreign_expense_invoice_lines_add_up_to_the_payable_to_the_cent()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var rent = await AccountAsync(s, "5400");
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/expenseinvoices", new
        {
            date = "2026-11-02", supplierId = supplier, currencyId = Usd, exchangeRate = 0.7133m,
            lines = new[] { new { accountId = rent, amount = 33.333m }, new { accountId = rent, amount = 33.333m }, new { accountId = rent, amount = 33.334m } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/expenseinvoices/{inv.GetProperty("id").GetInt32()}/confirm");

        // 100.00 USD x 0.7133 = 71.33: per-line rounding must not leave a stray cent between the books and the supplier account
        Assert.Equal(71.33m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}")).GetProperty("closingBalance").GetDecimal());
        foreach (var r in (await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation")).EnumerateArray().Where(r => r.GetProperty("key").GetString() == "Payables"))
            Assert.Equal(0m, r.GetProperty("difference").GetDecimal());
        Assert.True((await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance")).GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task Expense_lines_must_use_postable_expense_accounts()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        object Body(int account) => new { date = "2026-11-02", supplierId = supplier, currencyId = Local, lines = new[] { new { accountId = account, amount = 5 } } };

        Assert.Contains("expense account", await FailAsync(s, HttpMethod.Post, "/api/v1/expenseinvoices", Body(await AccountAsync(s, "1100")))); // cash is an asset
        Assert.Contains("header", await FailAsync(s, HttpMethod.Post, "/api/v1/expenseinvoices", Body(await AccountAsync(s, "5000")))); // header
        await FailAsync(s, HttpMethod.Post, "/api/v1/expenseinvoices", new { date = "2026-11-02", supplierId = supplier, currencyId = Local, lines = Array.Empty<object>() });
    }

    [Fact]
    public async Task Expense_voucher_pays_straight_from_cash_or_bank_and_cancelling_reverses_it()
    {
        var s = await AdminAsync();
        var rent = await AccountAsync(s, "5400");
        var snap = await new Snap(s).TakeAsync("5400", "1100", "1200");

        object Body(string method) => new
        {
            date = "2026-11-03", currencyId = Local, method, payee = "Landlord",
            lines = new[] { new { accountId = rent, description = "Cash rent", amount = 25 } },
        };

        var cash = await OkAsync(s, HttpMethod.Post, "/api/v1/expensevouchers", Body("Cash"), HttpStatusCode.Created);
        var cashId = cash.GetProperty("id").GetInt32();
        Assert.StartsWith("EXV-", (await OkAsync(s, HttpMethod.Post, $"/api/v1/expensevouchers/{cashId}/confirm")).GetProperty("number").GetString());
        Assert.Equal(25m, await snap.DeltaAsync("5400"));
        Assert.Equal(-25m, await snap.DeltaAsync("1100"));

        var bank = await OkAsync(s, HttpMethod.Post, "/api/v1/expensevouchers", Body("BankTransfer"), HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/expensevouchers/{bank.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(-25m, await snap.DeltaAsync("1200"));

        await OkAsync(s, HttpMethod.Post, $"/api/v1/expensevouchers/{cashId}/cancel");
        Assert.Equal(25m, await snap.DeltaAsync("5400")); // only the bank one remains
        Assert.Equal(0m, await snap.DeltaAsync("1100"));
    }

    [Fact]
    public async Task Revaluation_posts_the_unrealised_difference_and_reverses_itself_the_next_day()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);

        // an isolated foreign currency so other tests' USD items do not matter
        var code = Letters();
        var eur = (await OkAsync(s, HttpMethod.Post, "/api/v1/currencies", new { code, nameAr = "ع", nameEn = "Test currency", symbol = code }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, "/api/v1/exchangerates", new { currencyId = eur, date = "2026-12-01", rate = 0.80m }, HttpStatusCode.Created);
        // other tests in this class left open USD items (booked at explicit rates): revaluation needs a closing USD rate too
        await OkAsync(s, HttpMethod.Post, "/api/v1/exchangerates", new { currencyId = Usd, date = "2026-12-01", rate = 0.80m }, HttpStatusCode.Created);

        await SalesInvoiceAsync(s, customer, item, 100, eur, 0.71m);   // receivable booked at 0.71, now worth 0.80: +9.00 gain
        await PurchaseInvoiceAsync(s, supplier, item, 50, eur, 0.71m); // payable booked at 0.71: +4.50 loss

        var preview = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/revaluation/preview?asOf=2026-12-31");
        var mine = preview.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("currencyId").GetInt32() == eur).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal(9m, mine.First(l => l.GetProperty("isReceivable").GetBoolean()).GetProperty("difference").GetDecimal());
        Assert.Equal(4.5m, mine.First(l => !l.GetProperty("isReceivable").GetBoolean()).GetProperty("difference").GetDecimal());

        var receivablesDiff = preview.GetProperty("receivablesDifference").GetDecimal();
        var payablesDiff = preview.GetProperty("payablesDifference").GetDecimal();
        var snapAtDate = new Dictionary<string, decimal> { ["1300"] = await NetAsync(s, "1300", "2026-12-31"), ["2100"] = await NetAsync(s, "2100", "2026-12-31") };

        var run = await OkAsync(s, HttpMethod.Post, "/api/v1/accounting/revaluation", new { asOf = "2026-12-31" });
        Assert.StartsWith("JV-", run.GetProperty("entryNumber").GetString());
        Assert.StartsWith("JV-", run.GetProperty("reversalNumber").GetString());

        // visible at the reporting date ...
        Assert.Equal(snapAtDate["1300"] + receivablesDiff, await NetAsync(s, "1300", "2026-12-31"));
        Assert.Equal(snapAtDate["2100"] - payablesDiff, await NetAsync(s, "2100", "2026-12-31")); // payable (credit) grows
        // ... and gone the day after
        Assert.Equal(snapAtDate["1300"], await NetAsync(s, "1300", "2027-01-01"));
        Assert.Equal(snapAtDate["2100"], await NetAsync(s, "2100", "2027-01-01"));

        // unrealised differences are timing items: they never show up as a sub-ledger difference
        foreach (var r in (await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation")).EnumerateArray().Where(r => r.GetProperty("key").GetString() != "Inventory"))
            Assert.Equal(0m, r.GetProperty("difference").GetDecimal());

        Assert.Contains("already exists", await FailAsync(s, HttpMethod.Post, "/api/v1/accounting/revaluation", new { asOf = "2026-12-31" }));
        Assert.Contains((await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/revaluation/runs")).EnumerateArray().Select(r => r.GetProperty("asOf").GetString()), a => a == "2026-12-31");
        Assert.True((await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance")).GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task Revaluation_needs_a_closing_rate()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var code = Letters();
        var cur = (await OkAsync(s, HttpMethod.Post, "/api/v1/currencies", new { code, nameAr = "ع", nameEn = "No-rate currency", symbol = code }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var inv = await SalesInvoiceAsync(s, customer, item, 10, cur, 2m);
        Assert.Contains("No exchange rate", await FailAsync(s, HttpMethod.Get, "/api/v1/accounting/revaluation/preview?asOf=2026-11-30", null));
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{inv}/cancel"); // leave no open item without a rate behind for the other tests
    }

    [Fact]
    public async Task Roles_for_expenses_allocations_and_revaluation()
    {
        var admin = await AdminAsync();
        var supplier = await NewSupplierAsync(admin);
        var rent = await AccountAsync(admin, "5400");
        var expense = new { date = "2026-11-02", supplierId = supplier, currencyId = Local, lines = new[] { new { accountId = rent, amount = 5 } } };
        var voucher = new { date = "2026-11-03", currencyId = Local, method = "Cash", lines = new[] { new { accountId = rent, amount = 5 } } };

        var buyer = await UserWithRoleAsync("Purchasing");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(buyer, HttpMethod.Post, "/api/v1/expenseinvoices", expense)).Status);
        await FailAsync(buyer, HttpMethod.Post, "/api/v1/expensevouchers", voucher, HttpStatusCode.Forbidden);          // vouchers are accounting
        await FailAsync(buyer, HttpMethod.Post, "/api/v1/accounting/revaluation", new { asOf = "2026-11-30" }, HttpStatusCode.Forbidden);

        var sales = await UserWithRoleAsync("Sales");
        await FailAsync(sales, HttpMethod.Post, "/api/v1/expenseinvoices", expense, HttpStatusCode.Forbidden);
        await FailAsync(sales, HttpMethod.Get, "/api/v1/purchasing/ageing", null, HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(sales, HttpMethod.Get, "/api/v1/sales/ageing")).Status);

        var accountant = await UserWithRoleAsync("Accountant");
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(accountant, HttpMethod.Post, "/api/v1/expensevouchers", voucher)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(accountant, HttpMethod.Get, "/api/v1/accounting/revaluation/runs")).Status);
    }
}
