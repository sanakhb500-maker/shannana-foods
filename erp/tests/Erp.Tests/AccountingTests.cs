using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class AccountingTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const int Jod = 1;
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
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users",
            new { email, fullName = role, password = "Passw0rd!x", roles = new[] { role } }, HttpStatusCode.Created);
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

    // ---------- chart of accounts ----------
    private static readonly Dictionary<string, int> AccountIds = [];

    private static async Task<int> AccountAsync(Session s, string code)
    {
        if (AccountIds.TryGetValue(code, out var id)) return id;
        var rows = (await OkAsync(s, HttpMethod.Get, "/api/v1/accounts?pageSize=200")).GetProperty("items");
        foreach (var r in rows.EnumerateArray()) AccountIds[r.GetProperty("code").GetString()!] = r.GetProperty("id").GetInt32();
        return AccountIds[code];
    }

    /// <summary>Net debit balance (debit - credit) of an account right now. Tests compare before/after so they stay independent.</summary>
    private static async Task<decimal> NetAsync(Session s, string code)
    {
        var tb = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance");
        var row = tb.GetProperty("rows").EnumerateArray().FirstOrDefault(r => r.GetProperty("code").GetString() == code);
        return row.ValueKind == JsonValueKind.Undefined ? 0m : row.GetProperty("closing").GetDecimal();
    }

    private sealed class Snapshot(Session s)
    {
        private readonly Dictionary<string, decimal> _before = [];
        public async Task<Snapshot> TakeAsync(params string[] codes)
        {
            foreach (var c in codes) _before[c] = await NetAsync(s, c);
            return this;
        }
        public async Task<decimal> DeltaAsync(string code) => await NetAsync(s, code) - _before[code];
    }

    // ---------- fixtures ----------
    private static async Task<int> NewSupplierAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/suppliers", new { code = "S" + Uid(), nameAr = "مورد", nameEn = "Supplier" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewCustomerAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers", new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Retail" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "A" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses", new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> StockInAsync(Session s, int wh, int item, decimal qty, decimal cost, string reason = "Opening")
    {
        var adj = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId = wh, reason, lines = new[] { new { itemId = item, countedQuantity = qty, unitCost = cost } },
        }, HttpStatusCode.Created);
        var id = adj.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{id}/confirm");
        return id;
    }

    private static async Task<JsonElement> EntriesOfAsync(Session s, string sourceType, int sourceId)
    {
        var rows = (await OkAsync(s, HttpMethod.Get, $"/api/v1/journalentries?sourceType={sourceType}&pageSize=200")).GetProperty("items");
        return JsonSerializer.SerializeToElement(rows.EnumerateArray().Where(r => r.GetProperty("sourceId").GetInt32() == sourceId).ToList());
    }

    // =====================================================================

    [Fact]
    public async Task Starter_chart_is_seeded_with_one_account_per_posting_role()
    {
        var s = await AdminAsync();
        var rows = (await OkAsync(s, HttpMethod.Get, "/api/v1/accounts?pageSize=200")).GetProperty("items").EnumerateArray().ToList();
        var roles = rows.Where(r => r.GetProperty("systemRole").ValueKind == JsonValueKind.String).Select(r => r.GetProperty("systemRole").GetString()).ToList();
        foreach (var role in new[] { "Cash", "Bank", "Receivables", "Inventory", "Payables", "GRNI", "OpeningEquity", "SalesRevenue", "SalesReturns", "COGS", "InventoryAdjustments" })
            Assert.Contains(role, roles);
        Assert.Equal(roles.Count, roles.Distinct().Count());
        Assert.Contains(rows, r => r.GetProperty("code").GetString() == "1000" && !r.GetProperty("isPostable").GetBoolean()); // headers
    }

    [Fact]
    public async Task System_accounts_are_protected_and_roles_cannot_be_forged()
    {
        var s = await AdminAsync();
        var cogs = await AccountAsync(s, "5100");
        Assert.Contains("automatic postings", await FailAsync(s, HttpMethod.Delete, $"/api/v1/accounts/{cogs}", null));

        // a client cannot move or invent a role
        var created = await OkAsync(s, HttpMethod.Post, "/api/v1/accounts",
            new { code = "5900", nameAr = "مصروف", nameEn = "Misc", type = "Expense", parentId = await AccountAsync(s, "5000"), isPostable = true, systemRole = "COGS" }, HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("systemRole").ValueKind);

        // type change refused on a role account
        var current = await OkAsync(s, HttpMethod.Get, $"/api/v1/accounts/{cogs}");
        var body = new { code = "5100", nameAr = "x", nameEn = "x", type = "Asset", parentId = (int?)null, isPostable = true, isActive = true };
        Assert.Contains("keep their type", await FailAsync(s, HttpMethod.Put, $"/api/v1/accounts/{cogs}", body));
        Assert.Equal("COGS", current.GetProperty("systemRole").GetString());
    }

    [Fact]
    public async Task Account_rules_parent_must_be_a_header_of_the_same_type()
    {
        var s = await AdminAsync();
        var cash = await AccountAsync(s, "1100");
        Assert.Contains("header", await FailAsync(s, HttpMethod.Post, "/api/v1/accounts",
            new { code = "1190", nameAr = "ا", nameEn = "a", type = "Asset", parentId = cash, isPostable = true }));
        Assert.Contains("same type", await FailAsync(s, HttpMethod.Post, "/api/v1/accounts",
            new { code = "1191", nameAr = "ا", nameEn = "a", type = "Liability", parentId = await AccountAsync(s, "1000"), isPostable = true }));
        // (duplicate codes are a unique index: enforced by SQL Server, which the in-memory test database does not emulate)
    }

    [Fact]
    public async Task Opening_stock_posts_inventory_against_opening_equity_and_a_shortage_against_adjustments()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var snap = await new Snapshot(s).TakeAsync("1400", "3100", "5200");

        var adj = await StockInAsync(s, wh, item, 10, 4);
        Assert.Equal(40m, await snap.DeltaAsync("1400"));   // Dr inventory
        Assert.Equal(-40m, await snap.DeltaAsync("3100"));  // Cr opening equity

        // count shows 7 instead of 10 -> 3 x 4 written off
        await StockInAsync(s, wh, item, 7, 0, "Count");
        Assert.Equal(28m, await snap.DeltaAsync("1400"));
        Assert.Equal(12m, await snap.DeltaAsync("5200"));    // Dr adjustments (expense)

        // cancelling the opening adjustment is refused while its stock is used... here 7 >= 10 is false, so it fails; the count stays
        Assert.Contains("already been used", await FailAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{adj}/cancel", null));
        Assert.Equal(28m, await snap.DeltaAsync("1400"));    // nothing half-applied
    }

    [Fact]
    public async Task Purchase_cycle_posts_inventory_grni_payables_and_bank_and_cancelling_reverses_it()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var snap = await new Snapshot(s).TakeAsync("1400", "2200", "2100", "1200", "1100");

        var po = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", new
        {
            date = "2026-11-01", supplierId = supplier, currencyId = Usd, exchangeRate = 0.71m, lines = new[] { new { itemId = item, quantity = 10, unitPrice = 5 } },
        }, HttpStatusCode.Created);
        var poId = po.GetProperty("id").GetInt32();
        var line = (await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{poId}/confirm")).GetProperty("lines")[0].GetProperty("id").GetInt32();

        var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", new
        {
            date = "2026-11-02", supplierId = supplier, warehouseId = wh, purchaseOrderId = poId, currencyId = Usd, exchangeRate = 0.71m,
            lines = new[] { new { purchaseOrderLineId = line, itemId = item, quantity = 4, unitPrice = 5 } },
        }, HttpStatusCode.Created);
        var grnId = grn.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grnId}/confirm");
        Assert.Equal(14.2m, await snap.DeltaAsync("1400"));   // 4 x 5 x 0.71 into stock
        Assert.Equal(-14.2m, await snap.DeltaAsync("2200"));  // owed to GRNI until invoiced

        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", new
        {
            date = "2026-11-03", supplierId = supplier, purchaseOrderId = poId, currencyId = Usd, exchangeRate = 0.71m,
            lines = new[] { new { purchaseOrderLineId = line, itemId = item, quantity = 4, unitPrice = 5 } },
        }, HttpStatusCode.Created);
        var invId = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{invId}/confirm");
        Assert.Equal(0m, await snap.DeltaAsync("2200"));      // GRNI cleared
        Assert.Equal(-14.2m, await snap.DeltaAsync("2100"));  // payable raised

        var pay = await OkAsync(s, HttpMethod.Post, "/api/v1/supplierpayments",
            new { date = "2026-11-10", supplierId = supplier, currencyId = Usd, exchangeRate = 0.71m, amount = 10, method = "BankTransfer" }, HttpStatusCode.Created);
        var payId = pay.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{payId}/confirm");
        Assert.Equal(-7.1m, await snap.DeltaAsync("2100"));   // 14.2 - 7.1
        Assert.Equal(-7.1m, await snap.DeltaAsync("1200"));   // bank
        Assert.Equal(0m, await snap.DeltaAsync("1100"));      // not cash

        // the supplier's own sub-ledger agrees
        var payables = await AccountAsync(s, "2100");
        var ledger = await OkAsync(s, HttpMethod.Get, $"/api/v1/accounting/ledger?accountId={payables}&partyType=Supplier&partyId={supplier}");
        Assert.Equal(-7.1m, ledger.GetProperty("closing").GetDecimal());
        Assert.Equal(2, ledger.GetProperty("rows").GetArrayLength());

        // cancelling the payment, invoice and receipt (in that order) undoes everything
        await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{payId}/cancel");
        Assert.Equal(-14.2m, await snap.DeltaAsync("2100"));
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{invId}/cancel");
        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grnId}/cancel");
        foreach (var code in new[] { "1400", "2200", "2100", "1200" }) Assert.Equal(0m, await snap.DeltaAsync(code));

        // originals are marked Reversed and a mirror entry exists for each
        var entries = await EntriesOfAsync(s, "GoodsReceipt", grnId);
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Contains(entries.EnumerateArray(), e => e.GetProperty("status").GetString() == "Reversed");
        Assert.Contains(entries.EnumerateArray(), e => e.GetProperty("status").GetString() == "Posted");
    }

    [Fact]
    public async Task Sales_cycle_posts_cogs_revenue_returns_and_cash()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockInAsync(s, wh, item, 20, 4);
        var snap = await new Snapshot(s).TakeAsync("1400", "5100", "1300", "4100", "4200", "1100");

        var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", new
        {
            date = "2026-11-05", customerId = customer, warehouseId = wh, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 4, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var dId = d.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{dId}/confirm");
        Assert.Equal(16m, await snap.DeltaAsync("5100"));    // Dr COGS 4 x 4
        Assert.Equal(-16m, await snap.DeltaAsync("1400"));   // Cr inventory

        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", new
        {
            date = "2026-11-06", customerId = customer, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 4, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var invId = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{invId}/confirm");
        Assert.Equal(28m, await snap.DeltaAsync("1300"));    // Dr receivables
        Assert.Equal(-28m, await snap.DeltaAsync("4100"));   // Cr revenue

        var ret = await OkAsync(s, HttpMethod.Post, "/api/v1/salesreturns", new
        {
            date = "2026-11-07", customerId = customer, warehouseId = wh, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 1, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var retId = ret.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesreturns/{retId}/confirm");
        Assert.Equal(7m, await snap.DeltaAsync("4200"));     // Dr returns (contra revenue)
        Assert.Equal(21m, await snap.DeltaAsync("1300"));    // 28 - 7
        Assert.Equal(-12m, await snap.DeltaAsync("1400"));   // goods back in at cost 4: -16 + 4
        Assert.Equal(12m, await snap.DeltaAsync("5100"));    // COGS reduced by 4

        var rct = await OkAsync(s, HttpMethod.Post, "/api/v1/customerreceipts",
            new { date = "2026-11-08", customerId = customer, currencyId = Jod, amount = 10, method = "Cash" }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/customerreceipts/{rct.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(10m, await snap.DeltaAsync("1100"));    // Dr cash
        Assert.Equal(11m, await snap.DeltaAsync("1300"));    // 21 - 10

        // the customer sub-ledger agrees with the customer statement
        var receivables = await AccountAsync(s, "1300");
        var ledger = await OkAsync(s, HttpMethod.Get, $"/api/v1/accounting/ledger?accountId={receivables}&partyType=Customer&partyId={customer}");
        var stmt = await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}");
        Assert.Equal(stmt.GetProperty("closingBalance").GetDecimal(), ledger.GetProperty("closing").GetDecimal());
        Assert.Equal(11m, ledger.GetProperty("closing").GetDecimal());
    }

    [Fact]
    public async Task Books_stay_balanced_and_reconcile_with_the_sub_ledgers()
    {
        var s = await AdminAsync();
        // generate a bit of everything first
        var supplier = await NewSupplierAsync(s);
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockInAsync(s, wh, item, 10, 5);
        var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", new
        {
            date = "2026-11-05", customerId = customer, warehouseId = wh, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 3, unitPrice = 9 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{d.GetProperty("id").GetInt32()}/confirm");
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", new
        {
            date = "2026-11-06", customerId = customer, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 3, unitPrice = 9 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{inv.GetProperty("id").GetInt32()}/confirm");
        var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", new
        {
            date = "2026-11-02", supplierId = supplier, warehouseId = wh, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 5, unitPrice = 6 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn.GetProperty("id").GetInt32()}/confirm");

        var tb = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance");
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(tb.GetProperty("totalDebit").GetDecimal(), tb.GetProperty("totalCredit").GetDecimal());

        var bs = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/balance-sheet");
        Assert.True(bs.GetProperty("isBalanced").GetBoolean());

        var income = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/income-statement");
        Assert.Equal(income.GetProperty("totalRevenue").GetDecimal() - income.GetProperty("totalExpenses").GetDecimal(), income.GetProperty("netProfit").GetDecimal());
        Assert.Equal(bs.GetProperty("currentEarnings").GetDecimal(), income.GetProperty("netProfit").GetDecimal());

        var rec = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation");
        foreach (var r in rec.EnumerateArray())
        {
            Assert.True(r.GetProperty("reconciled").GetBoolean(), $"{r.GetProperty("key").GetString()} differs by {r.GetProperty("difference").GetDecimal()}");
            // money sub-ledgers must match to the cent; only stock valuation may carry average-cost rounding
            if (r.GetProperty("key").GetString() != "Inventory") Assert.Equal(0m, r.GetProperty("difference").GetDecimal());
        }
    }

    [Fact]
    public async Task Trial_balance_and_ledger_respect_the_date_range_with_opening_balances()
    {
        var s = await AdminAsync();

        // two manual entries a month apart on a fresh account so the numbers are ours alone
        var acct = await OkAsync(s, HttpMethod.Post, "/api/v1/accounts",
            new { code = "5" + Uid()[..4], nameAr = "ت", nameEn = "Test expense", type = "Expense", parentId = await AccountAsync(s, "5000"), isPostable = true }, HttpStatusCode.Created);
        var id = acct.GetProperty("id").GetInt32();
        var cash = await AccountAsync(s, "1100");
        foreach (var (date, amount) in new[] { ("2026-03-10", 100m), ("2026-04-20", 40m) })
        {
            var e = await OkAsync(s, HttpMethod.Post, "/api/v1/journalentries", new
            {
                date, description = "test",
                lines = new object[] { new { accountId = id, debit = amount, credit = 0 }, new { accountId = cash, debit = 0, credit = amount } },
            }, HttpStatusCode.Created);
            await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{e.GetProperty("id").GetInt32()}/post");
        }

        var april = await OkAsync(s, HttpMethod.Get, $"/api/v1/accounting/ledger?accountId={id}&from=2026-04-01&to=2026-04-30");
        Assert.Equal(100m, april.GetProperty("opening").GetDecimal());
        Assert.Equal(1, april.GetProperty("rows").GetArrayLength());
        Assert.Equal(140m, april.GetProperty("closing").GetDecimal());

        var tb = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance?from=2026-04-01&to=2026-04-30");
        var row = tb.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("accountId").GetInt32() == id);
        Assert.Equal(100m, row.GetProperty("opening").GetDecimal());
        Assert.Equal(40m, row.GetProperty("debit").GetDecimal());
        Assert.Equal(140m, row.GetProperty("closing").GetDecimal());
        Assert.True(tb.GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task Manual_entries_must_balance_use_postable_accounts_and_can_be_reversed()
    {
        var s = await AdminAsync();
        var cash = await AccountAsync(s, "1100");
        var equity = await AccountAsync(s, "3100");
        var header = await AccountAsync(s, "1000");

        object Body(params object[] lines) => new { date = "2026-11-10", description = "capital injection", lines };

        Assert.Contains("not balanced", await FailAsync(s, HttpMethod.Post, "/api/v1/journalentries",
            Body(new { accountId = cash, debit = 100, credit = 0 }, new { accountId = equity, debit = 0, credit = 90 })));
        Assert.Contains("header", await FailAsync(s, HttpMethod.Post, "/api/v1/journalentries",
            Body(new { accountId = header, debit = 100, credit = 0 }, new { accountId = equity, debit = 0, credit = 100 })));
        await FailAsync(s, HttpMethod.Post, "/api/v1/journalentries", Body(new { accountId = cash, debit = 100, credit = 100 }, new { accountId = equity, debit = 0, credit = 0 })); // both sides / none
        await FailAsync(s, HttpMethod.Post, "/api/v1/journalentries", Body(new { accountId = cash, debit = 100, credit = 0 })); // one line only

        var snap = await new Snapshot(s).TakeAsync("1100", "3100");
        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/journalentries",
            Body(new { accountId = cash, debit = 100, credit = 0 }, new { accountId = equity, debit = 0, credit = 100 }), HttpStatusCode.Created);
        var id = draft.GetProperty("id").GetInt32();
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("number").ValueKind); // numbered when posted
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(0m, await snap.DeltaAsync("1100")); // drafts do not count

        // drafts are editable and deletable
        var edited = await OkAsync(s, HttpMethod.Put, $"/api/v1/journalentries/{id}",
            Body(new { accountId = cash, debit = 120, credit = 0 }, new { accountId = equity, debit = 0, credit = 120 }));
        Assert.Equal(120m, edited.GetProperty("lines")[0].GetProperty("debit").GetDecimal());
        var other = await OkAsync(s, HttpMethod.Post, "/api/v1/journalentries",
            Body(new { accountId = cash, debit = 1, credit = 0 }, new { accountId = equity, debit = 0, credit = 1 }), HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Delete, $"/api/v1/journalentries/{other.GetProperty("id").GetInt32()}");
        await FailAsync(s, HttpMethod.Get, $"/api/v1/journalentries/{other.GetProperty("id").GetInt32()}", null, HttpStatusCode.NotFound);

        var posted = await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{id}/post");
        Assert.Equal("Posted", posted.GetProperty("status").GetString());
        Assert.StartsWith("JV-", posted.GetProperty("number").GetString());
        Assert.Equal(120m, await snap.DeltaAsync("1100"));
        Assert.Equal(-120m, await snap.DeltaAsync("3100"));

        // posted entries are frozen
        await FailAsync(s, HttpMethod.Put, $"/api/v1/journalentries/{id}", Body(new { accountId = cash, debit = 5, credit = 0 }, new { accountId = equity, debit = 0, credit = 5 }));
        await FailAsync(s, HttpMethod.Delete, $"/api/v1/journalentries/{id}", null);
        await FailAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{id}/post", null);

        var rev = await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{id}/reverse");
        Assert.Equal(id, rev.GetProperty("reversalOfId").GetInt32());
        Assert.Equal(0m, await snap.DeltaAsync("1100"));
        Assert.Equal("Reversed", (await OkAsync(s, HttpMethod.Get, $"/api/v1/journalentries/{id}")).GetProperty("status").GetString());
        await FailAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{id}/reverse", null); // already reversed
        await FailAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{rev.GetProperty("id").GetInt32()}/reverse", null); // a reversal is not reversed again
    }

    [Fact]
    public async Task Generated_entries_cannot_be_edited_or_reversed_by_hand()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var adj = await StockInAsync(s, wh, item, 5, 2);
        var entry = (await EntriesOfAsync(s, "StockAdjustment", adj))[0];
        var id = entry.GetProperty("id").GetInt32();

        Assert.Contains("cancelling that document", await FailAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{id}/reverse", null));
        var cash = await AccountAsync(s, "1100");
        Assert.Contains("cannot be changed", await FailAsync(s, HttpMethod.Put, $"/api/v1/journalentries/{id}",
            new { date = "2026-11-10", description = "x", lines = new object[] { new { accountId = cash, debit = 1, credit = 0 }, new { accountId = cash, debit = 0, credit = 1 } } }));
    }

    [Fact]
    public async Task Closed_period_blocks_postings_and_reversals_until_reopened()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var cash = await AccountAsync(s, "1100");
        var equity = await AccountAsync(s, "3100");

        var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", new
        {
            date = "2026-11-02", supplierId = supplier, warehouseId = wh, currencyId = Jod, lines = new[] { new { itemId = item, quantity = 1, unitPrice = 3 } },
        }, HttpStatusCode.Created);
        var grnId = grn.GetProperty("id").GetInt32();
        var manual = await OkAsync(s, HttpMethod.Post, "/api/v1/journalentries", new
        {
            date = "2026-11-02", description = "late entry",
            lines = new object[] { new { accountId = cash, debit = 5, credit = 0 }, new { accountId = equity, debit = 0, credit = 5 } },
        }, HttpStatusCode.Created);

        await OkAsync(s, HttpMethod.Put, "/api/v1/accounting/settings", new { lockDate = "2026-11-30" });
        try
        {
            Assert.Contains("closed", await FailAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grnId}/confirm", null));
            Assert.Contains("closed", await FailAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{manual.GetProperty("id").GetInt32()}/post", null));
            Assert.Equal("Draft", (await OkAsync(s, HttpMethod.Get, $"/api/v1/goodsreceipts/{grnId}")).GetProperty("status").GetString()); // nothing half-applied
            Assert.Equal("2026-11-30", (await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/settings")).GetProperty("lockDate").GetString());
        }
        finally
        {
            await OkAsync(s, HttpMethod.Put, "/api/v1/accounting/settings", new { lockDate = (string?)null });
        }

        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grnId}/confirm");
        await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{manual.GetProperty("id").GetInt32()}/post");
    }

    [Fact]
    public async Task Accounting_data_is_for_accountants_and_admins_only()
    {
        var admin = await AdminAsync();
        var cash = await AccountAsync(admin, "1100");
        var equity = await AccountAsync(admin, "3100");

        foreach (var role in new[] { "Sales", "Purchasing", "StoreKeeper", "Dispatcher" })
        {
            var u = await UserWithRoleAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(u, HttpMethod.Get, "/api/v1/journalentries")).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(u, HttpMethod.Get, "/api/v1/accounting/trial-balance")).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(u, HttpMethod.Get, "/api/v1/accounting/summary")).Status);
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(u, HttpMethod.Get, "/api/v1/accounts")).Status); // the chart itself is readable
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(u, HttpMethod.Post, "/api/v1/accounts",
                new { code = "9" + Uid()[..3], nameAr = "ا", nameEn = "a", type = "Asset", isPostable = true })).Status);
        }

        var acct = await UserWithRoleAsync("Accountant");
        var e = await SendAsync(acct, HttpMethod.Post, "/api/v1/journalentries", new
        {
            date = "2026-11-10", description = "by accountant",
            lines = new object[] { new { accountId = cash, debit = 2, credit = 0 }, new { accountId = equity, debit = 0, credit = 2 } },
        });
        Assert.Equal(HttpStatusCode.Created, e.Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(acct, HttpMethod.Get, "/api/v1/accounting/trial-balance")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(acct, HttpMethod.Put, "/api/v1/accounting/settings", new { lockDate = (string?)null })).Status);
    }

    [Fact]
    public async Task Summary_reports_cash_receivables_payables_inventory_and_six_months()
    {
        var s = await AdminAsync();
        var sum = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/summary");
        Assert.Equal(6, sum.GetProperty("months").GetArrayLength());

        var cash = await NetAsync(s, "1100");
        var bank = await NetAsync(s, "1200");
        Assert.Equal(cash + bank, sum.GetProperty("cashAndBank").GetDecimal());
        Assert.Equal(await NetAsync(s, "1400"), sum.GetProperty("inventoryValue").GetDecimal());
        Assert.Equal(-await NetAsync(s, "2100"), sum.GetProperty("payables").GetDecimal());
    }
}
