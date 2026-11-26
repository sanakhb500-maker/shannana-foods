using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class PurchasingTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const int Jod = 1; // seeded local currency
    private const int Usd = 2; // seeded foreign currency

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
        // 401/403 responses have no body
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
    }

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<int> NewSupplierAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/suppliers",
            new { code = "S" + Uid(), nameAr = "مورد", nameEn = "Supplier" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items",
            new { code = "P" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1, costPrice = 1, salePrice = 2 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses",
            new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static object PoBody(int supplier, int currency, decimal? rate, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-01", supplierId = supplier, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
    };

    /// <summary>Creates and confirms a one-line order; returns (orderId, orderLineId).</summary>
    private static async Task<(int Id, int LineId)> ConfirmedOrderAsync(Session s, int supplier, int item, decimal qty, decimal price,
        int currency = Usd, decimal? rate = 0.71m)
    {
        var po = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, currency, rate, item, qty, price), HttpStatusCode.Created);
        var id = po.GetProperty("id").GetInt32();
        var confirmed = await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{id}/confirm");
        return (id, confirmed.GetProperty("lines")[0].GetProperty("id").GetInt32());
    }

    private static object GrnBody(int supplier, int warehouse, int? orderId, int currency, decimal? rate, int? orderLineId, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-02", supplierId = supplier, warehouseId = warehouse, purchaseOrderId = orderId, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { purchaseOrderLineId = orderLineId, itemId = item, quantity = qty, unitPrice = price } },
    };

    private static object InvBody(int supplier, int? orderId, int currency, decimal? rate, int? orderLineId, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-03", supplierId = supplier, supplierInvoiceNumber = "INV-" + Uid(), purchaseOrderId = orderId, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { purchaseOrderLineId = orderLineId, itemId = item, quantity = qty, unitPrice = price } },
    };

    private static async Task<int> ConfirmedReceiptAsync(Session s, int supplier, int wh, int orderId, int lineId, int item, decimal qty, decimal price,
        int currency = Usd, decimal? rate = 0.71m)
    {
        var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, orderId, currency, rate, lineId, item, qty, price), HttpStatusCode.Created);
        var id = grn.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{id}/confirm");
        return id;
    }

    private static async Task<(decimal Qty, decimal Cost)> StockAsync(Session s, int wh, int item)
    {
        var rows = (await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={wh}&itemId={item}&nonZeroOnly=false")).GetProperty("items");
        return rows.GetArrayLength() == 0 ? (0, 0) : (rows[0].GetProperty("quantity").GetDecimal(), rows[0].GetProperty("averageCost").GetDecimal());
    }

    private static async Task<JsonElement> OrderAsync(Session s, int id) => await OkAsync(s, HttpMethod.Get, $"/api/v1/purchaseorders/{id}");

    [Fact]
    public async Task Order_total_is_computed_and_rate_is_snapshotted()
    {
        var s = await AdminAsync();
        var po = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders",
            PoBody(await NewSupplierAsync(s), Usd, 0.71m, await NewItemAsync(s), 10, 5), HttpStatusCode.Created);
        Assert.Equal(50m, po.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0.71m, po.GetProperty("exchangeRate").GetDecimal());
        Assert.StartsWith("PO-", po.GetProperty("number").GetString());
        Assert.Equal("Draft", po.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Local_currency_always_uses_rate_one_and_foreign_without_a_rate_is_rejected()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);

        var local = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Jod, 5m, item, 1, 1), HttpStatusCode.Created);
        Assert.Equal(1m, local.GetProperty("exchangeRate").GetDecimal()); // a supplied rate is ignored for the local currency

        var msg = await FailAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, null, item, 1, 1));
        Assert.Contains("No exchange rate", msg);
    }

    [Fact]
    public async Task Receipt_against_an_order_increases_stock_at_local_cost_and_tracks_progress()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5); // USD 5 x 0.71 = 3.55 local

        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 4, 5);

        Assert.Equal((4m, 3.55m), await StockAsync(s, wh, item));
        Assert.Equal(4m, (await OrderAsync(s, po)).GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());

        var pending = await OkAsync(s, HttpMethod.Get, "/api/v1/purchaseorders?pendingOnly=true&pageSize=200");
        var row = pending.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == po);
        Assert.Equal("Partial", row.GetProperty("receiptState").GetString());

        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 6, 5);
        var done = await OkAsync(s, HttpMethod.Get, "/api/v1/purchaseorders?pageSize=200");
        Assert.Equal("Full", done.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == po).GetProperty("receiptState").GetString());
        var stillPending = await OkAsync(s, HttpMethod.Get, "/api/v1/purchaseorders?pendingOnly=true&pageSize=200");
        Assert.DoesNotContain(stillPending.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt32() == po);
    }

    [Fact]
    public async Task Receipt_cannot_exceed_the_remaining_ordered_quantity()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 4, 5);

        var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, po, Usd, 0.71m, line, item, 7, 5), HttpStatusCode.Created);
        var msg = await FailAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn.GetProperty("id").GetInt32()}/confirm", null);

        Assert.Contains("exceeds the remaining ordered quantity", msg);
        Assert.Equal((4m, 3.55m), await StockAsync(s, wh, item));
    }

    [Fact]
    public async Task Direct_receipts_blend_the_moving_average_cost_in_local_currency()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);

        foreach (var (qty, price) in new[] { (10m, 5m), (10m, 8m) })
        {
            var grn = await OkAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, null, Jod, null, null, item, qty, price), HttpStatusCode.Created);
            await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn.GetProperty("id").GetInt32()}/confirm");
        }
        Assert.Equal((20m, 6.5m), await StockAsync(s, wh, item));
    }

    [Fact]
    public async Task Order_link_is_validated_on_receipts_and_invoices()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var other = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);

        Assert.Contains("supplier", await FailAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(other, wh, po, Usd, 0.71m, line, item, 1, 5)));
        Assert.Contains("currency", await FailAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, po, Jod, null, line, item, 1, 5)));
        Assert.Contains("reference a purchase order line", await FailAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, po, Usd, 0.71m, null, item, 1, 5)));
        Assert.Contains("only when", await FailAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, null, Usd, 0.71m, line, item, 1, 5)));

        // an order that is still a draft cannot be received
        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, 0.71m, item, 1, 1), HttpStatusCode.Created);
        var draftLine = (await OrderAsync(s, draft.GetProperty("id").GetInt32())).GetProperty("lines")[0].GetProperty("id").GetInt32();
        Assert.Contains("confirmed", await FailAsync(s, HttpMethod.Post, "/api/v1/goodsreceipts",
            GrnBody(supplier, wh, draft.GetProperty("id").GetInt32(), Usd, 0.71m, draftLine, item, 1, 1)));
    }

    [Fact]
    public async Task Invoice_quantity_cannot_run_ahead_of_received_quantity()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 4, 5);

        var tooMuch = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", InvBody(supplier, po, Usd, 0.71m, line, item, 6, 5), HttpStatusCode.Created);
        var msg = await FailAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{tooMuch.GetProperty("id").GetInt32()}/confirm", null);
        Assert.Contains("received, not-yet-invoiced", msg);

        var ok = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", InvBody(supplier, po, Usd, 0.71m, line, item, 4, 5), HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{ok.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(4m, (await OrderAsync(s, po)).GetProperty("lines")[0].GetProperty("invoicedQuantity").GetDecimal());
    }

    [Fact]
    public async Task Supplier_statement_tracks_invoices_and_payments_in_local_currency()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 4, 5);

        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", InvBody(supplier, po, Usd, 0.71m, line, item, 4, 5), HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{inv.GetProperty("id").GetInt32()}/confirm");

        var pay = await OkAsync(s, HttpMethod.Post, "/api/v1/supplierpayments",
            new { date = "2026-11-10", supplierId = supplier, currencyId = Usd, exchangeRate = 0.71m, amount = 10, method = "BankTransfer", reference = "TRX-1" },
            HttpStatusCode.Created);
        var payId = pay.GetProperty("id").GetInt32();

        // a draft payment is not on the statement yet
        var before = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Equal(14.2m, before.GetProperty("closingBalance").GetDecimal()); // 4 x 5 USD x 0.71

        await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{payId}/confirm");
        var after = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Equal(7.1m, after.GetProperty("closingBalance").GetDecimal()); // minus 10 USD x 0.71
        Assert.Equal(14.2m, after.GetProperty("totalInvoiced").GetDecimal());
        Assert.Equal(7.1m, after.GetProperty("totalPaid").GetDecimal());
        var rows = after.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("Invoice", rows[0].GetProperty("type").GetString());
        Assert.Equal("Payment", rows[1].GetProperty("type").GetString());
        Assert.Equal(7.1m, rows[1].GetProperty("balance").GetDecimal());

        // opening balance: from a date after the invoice but before the payment
        var mid = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}&from=2026-11-05");
        Assert.Equal(14.2m, mid.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(1, mid.GetProperty("rows").GetArrayLength());

        var balances = await OkAsync(s, HttpMethod.Get, "/api/v1/purchasing/supplier-balances");
        var mine = balances.EnumerateArray().First(b => b.GetProperty("supplierId").GetInt32() == supplier);
        Assert.Equal(7.1m, mine.GetProperty("balance").GetDecimal());

        // cancelling the payment puts the balance back
        await OkAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{payId}/cancel");
        var cancelled = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Equal(14.2m, cancelled.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Cancelling_a_receipt_reverses_stock_but_is_blocked_while_invoiced()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        var grn = await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 4, 5);

        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", InvBody(supplier, po, Usd, 0.71m, line, item, 4, 5), HttpStatusCode.Created);
        var invId = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{invId}/confirm");

        Assert.Contains("Invoices have already", await FailAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn}/cancel", null));
        Assert.Equal(4m, (await StockAsync(s, wh, item)).Qty);

        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{invId}/cancel");
        Assert.Equal(0m, (await OrderAsync(s, po)).GetProperty("lines")[0].GetProperty("invoicedQuantity").GetDecimal());

        await OkAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn}/cancel");
        Assert.Equal(0m, (await StockAsync(s, wh, item)).Qty);
        Assert.Equal(0m, (await OrderAsync(s, po)).GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());

        // the order is receivable again
        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 10, 5);
        Assert.Equal(10m, (await StockAsync(s, wh, item)).Qty);
    }

    [Fact]
    public async Task Cancelling_a_receipt_is_refused_when_its_stock_was_already_used()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var other = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        var grn = await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 10, 5);

        var transfer = await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", new
        {
            date = "2026-11-04", fromWarehouseId = wh, toWarehouseId = other, lines = new[] { new { itemId = item, quantity = 10 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{transfer.GetProperty("id").GetInt32()}/confirm");

        Assert.Contains("already been used", await FailAsync(s, HttpMethod.Post, $"/api/v1/goodsreceipts/{grn}/cancel", null));
        Assert.Equal(10m, (await OrderAsync(s, po)).GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal()); // nothing half-applied
    }

    [Fact]
    public async Task Order_with_receipts_cannot_be_cancelled_and_drafts_can()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (po, line) = await ConfirmedOrderAsync(s, supplier, item, 10, 5);
        await ConfirmedReceiptAsync(s, supplier, wh, po, line, item, 1, 5);
        Assert.Contains("receipts or invoices", await FailAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{po}/cancel", null));

        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, 0.71m, item, 1, 1), HttpStatusCode.Created);
        var cancelled = await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{draft.GetProperty("id").GetInt32()}/cancel");
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Drafts_are_editable_and_confirmed_orders_are_not()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var po = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, 0.71m, item, 10, 5), HttpStatusCode.Created);
        var id = po.GetProperty("id").GetInt32();

        var edited = await OkAsync(s, HttpMethod.Put, $"/api/v1/purchaseorders/{id}", PoBody(supplier, Usd, 0.7m, item, 20, 6));
        Assert.Equal(120m, edited.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, edited.GetProperty("lines").GetArrayLength());

        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{id}/confirm");
        await FailAsync(s, HttpMethod.Put, $"/api/v1/purchaseorders/{id}", PoBody(supplier, Usd, 0.7m, item, 1, 1));
        await FailAsync(s, HttpMethod.Post, $"/api/v1/purchaseorders/{id}/confirm", null);
    }

    [Fact]
    public async Task Purchasing_data_is_hidden_from_sales_and_writes_follow_roles()
    {
        var admin = await AdminAsync();
        var supplier = await NewSupplierAsync(admin);
        var item = await NewItemAsync(admin);
        var wh = await NewWarehouseAsync(admin);

        var sales = await UserWithRoleAsync("Sales");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(sales, HttpMethod.Get, "/api/v1/purchaseorders")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(sales, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}")).Status);

        var keeper = await UserWithRoleAsync("StoreKeeper");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(keeper, HttpMethod.Get, "/api/v1/purchaseorders")).Status);
        await FailAsync(keeper, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, 0.71m, item, 1, 1), HttpStatusCode.Forbidden);
        var grn = await SendAsync(keeper, HttpMethod.Post, "/api/v1/goodsreceipts", GrnBody(supplier, wh, null, Jod, null, null, item, 1, 1));
        Assert.Equal(HttpStatusCode.Created, grn.Status); // storekeepers receive goods

        var buyer = await UserWithRoleAsync("Purchasing");
        await FailAsync(buyer, HttpMethod.Post, "/api/v1/supplierpayments",
            new { date = "2026-11-10", supplierId = supplier, currencyId = Jod, amount = 5, method = "Cash" }, HttpStatusCode.Forbidden);

        var accountant = await UserWithRoleAsync("Accountant");
        await FailAsync(accountant, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Usd, 0.71m, item, 1, 1), HttpStatusCode.Forbidden);
        var payment = await SendAsync(accountant, HttpMethod.Post, "/api/v1/supplierpayments",
            new { date = "2026-11-10", supplierId = supplier, currencyId = Jod, amount = 5, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, payment.Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(accountant, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}")).Status);
    }

    [Fact]
    public async Task Validation_rejects_bad_purchasing_input()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);

        await FailAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", new { date = "2026-11-01", supplierId = supplier, currencyId = Jod, lines = Array.Empty<object>() });
        await FailAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Jod, null, item, 0, 5));
        await FailAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(supplier, Jod, null, item, 1, -1));
        await FailAsync(s, HttpMethod.Post, "/api/v1/purchaseorders", PoBody(999999, Jod, null, item, 1, 1));
        await FailAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", new { date = "2026-11-10", supplierId = supplier, currencyId = Jod, amount = 0, method = "Cash" });
    }
}
