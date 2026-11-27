using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

/// <summary>Purchase returns and returns linked to invoices (quantity cap, outstanding reduction, cancel guards).</summary>
public class ReturnsTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const int Local = 1;

    private sealed record Session(HttpClient Client, string Csrf);

    private async Task<Session> AdminAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@shannana.local", password = "Admin#12345" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return new Session(client, json.GetProperty("data").GetProperty("csrfToken").GetString()!);
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

    private static async Task<int> NewSupplierAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/suppliers", new { code = "S" + Uid(), nameAr = "مورد", nameEn = "Supplier" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewCustomerAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers", new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Retail" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "X" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses", new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task StockAsync(Session s, int wh, int item, decimal qty, decimal cost)
    {
        var adj = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId = wh, reason = "Opening", lines = new[] { new { itemId = item, countedQuantity = qty, unitCost = cost } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{adj.GetProperty("id").GetInt32()}/confirm");
    }

    private static async Task<decimal> QtyAsync(Session s, int wh, int item)
    {
        var rows = (await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={wh}&itemId={item}&nonZeroOnly=false")).GetProperty("items");
        return rows.GetArrayLength() == 0 ? 0 : rows[0].GetProperty("quantity").GetDecimal();
    }

    private static async Task<decimal> SupplierBalanceAsync(Session s, int supplier) =>
        (await OkAsync(s, HttpMethod.Get, "/api/v1/purchasing/supplier-balances")).EnumerateArray()
            .First(b => b.GetProperty("supplierId").GetInt32() == supplier).GetProperty("balance").GetDecimal();

    private static async Task<decimal> CustomerBalanceAsync(Session s, int customer) =>
        (await OkAsync(s, HttpMethod.Get, "/api/v1/sales/customer-balances")).EnumerateArray()
            .First(b => b.GetProperty("customerId").GetInt32() == customer).GetProperty("balance").GetDecimal();

    private static async Task<int> PurchaseInvoiceAsync(Session s, int supplier, int item, decimal qty, decimal price)
    {
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/purchaseinvoices", new
        {
            date = "2026-11-01", supplierId = supplier, currencyId = Local,
            lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
        }, HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{id}/confirm");
        return id;
    }

    private static async Task<int> SalesInvoiceAsync(Session s, int customer, int item, decimal qty, decimal price)
    {
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", new
        {
            date = "2026-11-01", customerId = customer, currencyId = Local,
            lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
        }, HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{id}/confirm");
        return id;
    }

    private static object PReturn(int supplier, int wh, int? invoice, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-05", supplierId = supplier, warehouseId = wh, purchaseInvoiceId = invoice, currencyId = Local,
        lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
    };

    private static object SReturn(int customer, int wh, int? invoice, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-05", customerId = customer, warehouseId = wh, salesInvoiceId = invoice, currencyId = Local,
        lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
    };

    private static async Task AssertReconciledAsync(Session s)
    {
        foreach (var r in (await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation")).EnumerateArray())
            Assert.True(r.GetProperty("reconciled").GetBoolean(), $"{r.GetProperty("key").GetString()} differs by {r.GetProperty("difference").GetDecimal()}");
    }

    [Fact]
    public async Task Purchase_return_issues_stock_credits_the_supplier_and_reverses_on_cancel()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 10, 4);
        await PurchaseInvoiceAsync(s, supplier, item, 5, 6); // we owe 30
        Assert.Equal(30m, await SupplierBalanceAsync(s, supplier));

        var ret = await OkAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, null, item, 2, 6), HttpStatusCode.Created);
        var id = ret.GetProperty("id").GetInt32();
        Assert.Equal(JsonValueKind.Null, ret.GetProperty("number").ValueKind);
        Assert.Equal(30m, await SupplierBalanceAsync(s, supplier)); // a draft changes nothing
        Assert.StartsWith("PRT-", (await OkAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{id}/confirm")).GetProperty("number").GetString());

        Assert.Equal(8m, await QtyAsync(s, wh, item));
        Assert.Equal(18m, await SupplierBalanceAsync(s, supplier)); // 30 - 2 x 6
        await AssertReconciledAsync(s);

        var stmt = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/supplier-statement?supplierId={supplier}");
        Assert.Contains(stmt.GetProperty("rows").EnumerateArray(), r => r.GetProperty("type").GetString() == "Return");

        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{id}/cancel");
        Assert.Equal(10m, await QtyAsync(s, wh, item));
        Assert.Equal(30m, await SupplierBalanceAsync(s, supplier));
        await AssertReconciledAsync(s);

        // cannot send back more than is in the warehouse
        var big = await OkAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, null, item, 50, 6), HttpStatusCode.Created);
        await FailAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{big.GetProperty("id").GetInt32()}/confirm", null);
    }

    [Fact]
    public async Task Purchase_return_linked_to_an_invoice_is_capped_and_lowers_its_outstanding_amount()
    {
        var s = await AdminAsync();
        var supplier = await NewSupplierAsync(s);
        var other = await NewSupplierAsync(s);
        var item = await NewItemAsync(s);
        var stranger = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 10, 4);
        await StockAsync(s, wh, stranger, 10, 4);
        var inv = await PurchaseInvoiceAsync(s, supplier, item, 5, 6); // 30

        Assert.Contains("not on the linked invoice", await FailAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, inv, stranger, 1, 6)));
        Assert.Contains("differs from the invoice's supplier", await FailAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(other, wh, inv, item, 1, 6)));
        Assert.Contains("exceeds what was invoiced", await FailAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, inv, item, 6, 6)));

        var r1 = await OkAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, inv, item, 3, 6), HttpStatusCode.Created);
        var r1Id = r1.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{r1Id}/confirm");
        var open = await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}");
        Assert.Equal(12m, open[0].GetProperty("outstanding").GetDecimal()); // 30 - 3 x 6

        // only 2 are still returnable
        Assert.Contains("exceeds what was invoiced", await FailAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, inv, item, 3, 6)));
        var r2 = await OkAsync(s, HttpMethod.Post, "/api/v1/purchasereturns", PReturn(supplier, wh, inv, item, 2, 6), HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{r2.GetProperty("id").GetInt32()}/confirm");
        Assert.Empty((await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}")).EnumerateArray());

        // a payment cannot be allocated to an invoice that returns have already cleared
        var (status, body) = await SendAsync(s, HttpMethod.Post, "/api/v1/supplierpayments", new
        {
            date = "2026-11-10", supplierId = supplier, currencyId = Local, amount = 10, method = "Cash",
            allocations = new[] { new { kind = "PurchaseInvoice", invoiceId = inv, amount = 10 } },
        });
        var message = status == HttpStatusCode.BadRequest ? body.GetProperty("message").GetString()
            : await FailAsync(s, HttpMethod.Post, $"/api/v1/supplierpayments/{body.GetProperty("data").GetProperty("id").GetInt32()}/confirm", null);
        Assert.Contains("exceeds its outstanding", message);

        // the invoice cannot be cancelled underneath its returns
        Assert.Contains("Returns are already linked", await FailAsync(s, HttpMethod.Post, $"/api/v1/purchaseinvoices/{inv}/cancel", null));
        await OkAsync(s, HttpMethod.Post, $"/api/v1/purchasereturns/{r1Id}/cancel");
        Assert.Equal(18m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/purchasing/open-invoices?supplierId={supplier}"))[0].GetProperty("outstanding").GetDecimal());
        await AssertReconciledAsync(s);
    }

    [Fact]
    public async Task Sales_return_linked_to_an_invoice_is_capped_and_lowers_its_outstanding_amount()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var other = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var stranger = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 20, 4);
        var inv = await SalesInvoiceAsync(s, customer, item, 10, 7); // 70

        Assert.Contains("not on the linked invoice", await FailAsync(s, HttpMethod.Post, "/api/v1/salesreturns", SReturn(customer, wh, inv, stranger, 1, 7)));
        Assert.Contains("differs from the invoice's customer", await FailAsync(s, HttpMethod.Post, "/api/v1/salesreturns", SReturn(other, wh, inv, item, 1, 7)));
        Assert.Contains("exceeds what was invoiced", await FailAsync(s, HttpMethod.Post, "/api/v1/salesreturns", SReturn(customer, wh, inv, item, 11, 7)));

        var r1 = await OkAsync(s, HttpMethod.Post, "/api/v1/salesreturns", SReturn(customer, wh, inv, item, 4, 7), HttpStatusCode.Created);
        var r1Id = r1.GetProperty("id").GetInt32();
        Assert.Equal(inv, r1.GetProperty("salesInvoiceId").GetInt32());
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesreturns/{r1Id}/confirm");
        Assert.Equal(42m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/open-invoices?customerId={customer}"))[0].GetProperty("outstanding").GetDecimal()); // 70 - 4 x 7
        Assert.Equal(42m, await CustomerBalanceAsync(s, customer));

        Assert.Contains("exceeds what was invoiced", await FailAsync(s, HttpMethod.Post, "/api/v1/salesreturns", SReturn(customer, wh, inv, item, 7, 7)));
        Assert.Contains("Returns are already linked", await FailAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{inv}/cancel", null));

        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesreturns/{r1Id}/cancel");
        Assert.Equal(70m, (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/open-invoices?customerId={customer}"))[0].GetProperty("outstanding").GetDecimal());
        await AssertReconciledAsync(s);
    }

    [Fact]
    public async Task Failed_stop_returns_the_goods_to_stock_when_the_trip_completes_but_not_before()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 10, 4);
        var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", new
        {
            date = "2026-11-06", customerId = customer, warehouseId = wh, currencyId = Local,
            lines = new[] { new { itemId = item, quantity = 3, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var dId = d.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{dId}/confirm");
        Assert.Equal(7m, await QtyAsync(s, wh, item));

        var driver = (await OkAsync(s, HttpMethod.Post, "/api/v1/drivers", new { name = "Driver " + Uid() }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var vehicle = (await OkAsync(s, HttpMethod.Post, "/api/v1/vehicles", new { plateNumber = "V-" + Uid(), capacityKg = 1000 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var trip = await OkAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { dId } }, HttpStatusCode.Created);
        var tid = trip.GetProperty("id").GetInt32();
        var started = await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/start");
        var stop = started.GetProperty("stops")[0].GetProperty("id").GetInt32();

        await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}/stops/{stop}", new { status = "Failed", notes = "closed" });
        Assert.Equal(7m, await QtyAsync(s, wh, item)); // nothing happens until the trip is completed
        await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/complete");
        Assert.Equal(10m, await QtyAsync(s, wh, item));
        Assert.Equal("Cancelled", (await OkAsync(s, HttpMethod.Get, $"/api/v1/deliverynotes/{dId}")).GetProperty("status").GetString());
        await AssertReconciledAsync(s);
    }
}
