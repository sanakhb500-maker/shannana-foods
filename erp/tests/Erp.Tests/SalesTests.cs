using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class SalesTests(ErpFactory factory) : IClassFixture<ErpFactory>
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
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
    }

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<int> NewCustomerAsync(Session s, decimal creditLimit = 0) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers",
            new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Distributor", creditLimit }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items",
            new { code = "S" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1, costPrice = 1, salePrice = 2 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses",
            new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewDriverAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/drivers", new { name = "Driver " + Uid() }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewVehicleAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/vehicles", new { plateNumber = "V-" + Uid(), capacityKg = 1000 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    /// <summary>Puts stock into a warehouse through a confirmed opening-balance adjustment.</summary>
    private static async Task StockAsync(Session s, int wh, int item, decimal qty, decimal cost)
    {
        var adj = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId = wh, reason = "Opening", lines = new[] { new { itemId = item, countedQuantity = qty, unitCost = cost } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{adj.GetProperty("id").GetInt32()}/confirm");
    }

    private static async Task<(decimal Qty, decimal Cost)> BalanceAsync(Session s, int wh, int item)
    {
        var rows = (await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={wh}&itemId={item}&nonZeroOnly=false")).GetProperty("items");
        return rows.GetArrayLength() == 0 ? (0, 0) : (rows[0].GetProperty("quantity").GetDecimal(), rows[0].GetProperty("averageCost").GetDecimal());
    }

    private static object SoBody(int customer, int currency, decimal? rate, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-05", customerId = customer, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { itemId = item, quantity = qty, unitPrice = price } },
    };

    private static async Task<(int Id, int LineId)> ConfirmedOrderAsync(Session s, int customer, int item, decimal qty, decimal price, int currency = Jod, decimal? rate = null)
    {
        var so = await OkAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, currency, rate, item, qty, price), HttpStatusCode.Created);
        var id = so.GetProperty("id").GetInt32();
        var confirmed = await OkAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{id}/confirm");
        return (id, confirmed.GetProperty("lines")[0].GetProperty("id").GetInt32());
    }

    private static object DlvBody(int customer, int wh, int? orderId, int currency, decimal? rate, int? orderLineId, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-06", customerId = customer, warehouseId = wh, salesOrderId = orderId, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { salesOrderLineId = orderLineId, itemId = item, quantity = qty, unitPrice = price } },
    };

    private static object InvBody(int customer, int? orderId, int currency, decimal? rate, int? orderLineId, int item, decimal qty, decimal price) => new
    {
        date = "2026-11-07", customerId = customer, salesOrderId = orderId, currencyId = currency, exchangeRate = rate,
        lines = new[] { new { salesOrderLineId = orderLineId, itemId = item, quantity = qty, unitPrice = price } },
    };

    private static async Task<int> ConfirmedDeliveryAsync(Session s, int customer, int wh, int? orderId, int? lineId, int item, decimal qty, decimal price,
        int currency = Jod, decimal? rate = null)
    {
        var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, orderId, currency, rate, lineId, item, qty, price), HttpStatusCode.Created);
        var id = d.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{id}/confirm");
        return id;
    }

    private static async Task<int> ConfirmedInvoiceAsync(Session s, int customer, int? orderId, int? lineId, int item, decimal qty, decimal price, int currency = Jod, decimal? rate = null)
    {
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, orderId, currency, rate, lineId, item, qty, price), HttpStatusCode.Created);
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{id}/confirm");
        return id;
    }

    private static async Task<decimal> CustomerBalanceAsync(Session s, int customer) =>
        (await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}")).GetProperty("closingBalance").GetDecimal();

    private static async Task<JsonElement> OrderAsync(Session s, int id) => await OkAsync(s, HttpMethod.Get, $"/api/v1/salesorders/{id}");

    [Fact]
    public async Task Delivery_against_an_order_takes_stock_out_at_cost_and_tracks_progress()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 20, 4);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 10, 7);

        var d = await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 4, 7);

        Assert.Equal((16m, 4m), await BalanceAsync(s, wh, item));
        var note = await OkAsync(s, HttpMethod.Get, $"/api/v1/deliverynotes/{d}");
        Assert.Equal(4m, note.GetProperty("lines")[0].GetProperty("unitCost").GetDecimal()); // cost of goods sold recorded
        Assert.Equal(4m, (await OrderAsync(s, so)).GetProperty("lines")[0].GetProperty("deliveredQuantity").GetDecimal());

        var pending = await OkAsync(s, HttpMethod.Get, "/api/v1/salesorders?pendingOnly=true&pageSize=200");
        Assert.Equal("Partial", pending.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == so).GetProperty("deliveryState").GetString());

        await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 6, 7);
        var done = await OkAsync(s, HttpMethod.Get, "/api/v1/salesorders?pageSize=200");
        Assert.Equal("Full", done.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == so).GetProperty("deliveryState").GetString());
    }

    [Fact]
    public async Task Delivery_is_refused_beyond_the_order_or_beyond_available_stock()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 5, 4);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 8, 7);

        var tooMuchStock = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, so, Jod, null, line, item, 6, 7), HttpStatusCode.Created);
        var msg = await FailAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{tooMuchStock.GetProperty("id").GetInt32()}/confirm", null);
        Assert.Contains("Insufficient stock", msg);
        Assert.Equal((5m, 4m), await BalanceAsync(s, wh, item));

        await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 5, 7);
        await StockAsync(s, wh, item, 100, 4); // plenty of stock now; the order itself has 3 left
        var tooMuchOrder = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, so, Jod, null, line, item, 4, 7), HttpStatusCode.Created);
        Assert.Contains("exceeds the remaining ordered quantity",
            await FailAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{tooMuchOrder.GetProperty("id").GetInt32()}/confirm", null));
    }

    [Fact]
    public async Task Invoice_cannot_run_ahead_of_deliveries_and_feeds_the_customer_statement()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 20, 4);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 10, 7);
        await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 4, 7);

        var tooMuch = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, so, Jod, null, line, item, 6, 7), HttpStatusCode.Created);
        Assert.Contains("delivered, not-yet-invoiced", await FailAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{tooMuch.GetProperty("id").GetInt32()}/confirm", null));

        await ConfirmedInvoiceAsync(s, customer, so, line, item, 4, 7);
        Assert.Equal(4m, (await OrderAsync(s, so)).GetProperty("lines")[0].GetProperty("invoicedQuantity").GetDecimal());
        Assert.Equal(28m, await CustomerBalanceAsync(s, customer)); // 4 x 7
    }

    [Fact]
    public async Task Receipts_and_returns_lower_the_balance_and_a_return_restocks_at_average_cost()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 20, 4);
        await ConfirmedDeliveryAsync(s, customer, wh, null, null, item, 10, 7);
        await ConfirmedInvoiceAsync(s, customer, null, null, item, 10, 7);
        Assert.Equal(70m, await CustomerBalanceAsync(s, customer));
        Assert.Equal((10m, 4m), await BalanceAsync(s, wh, item));

        var ret = await OkAsync(s, HttpMethod.Post, "/api/v1/salesreturns", new
        {
            date = "2026-11-08", customerId = customer, warehouseId = wh, currencyId = Jod, notes = "damaged",
            lines = new[] { new { itemId = item, quantity = 2, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var retId = ret.GetProperty("id").GetInt32();
        Assert.Equal(JsonValueKind.Null, ret.GetProperty("number").ValueKind);
        Assert.StartsWith("SRN-", (await OkAsync(s, HttpMethod.Post, $"/api/v1/salesreturns/{retId}/confirm")).GetProperty("number").GetString());
        Assert.Equal((12m, 4m), await BalanceAsync(s, wh, item)); // back at the 4.00 average, so the average is not distorted
        Assert.Equal(56m, await CustomerBalanceAsync(s, customer)); // 70 - 2 x 7

        var rct = await OkAsync(s, HttpMethod.Post, "/api/v1/customerreceipts",
            new { date = "2026-11-09", customerId = customer, currencyId = Jod, amount = 30, method = "Cash", reference = "R-1" }, HttpStatusCode.Created);
        var rctId = rct.GetProperty("id").GetInt32();
        Assert.Equal(56m, await CustomerBalanceAsync(s, customer)); // a draft receipt does not count
        await OkAsync(s, HttpMethod.Post, $"/api/v1/customerreceipts/{rctId}/confirm");
        Assert.Equal(26m, await CustomerBalanceAsync(s, customer));

        var stmt = await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}");
        Assert.Equal(new[] { "Invoice", "Return", "Receipt" }, stmt.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("type").GetString()).ToArray());
        Assert.Equal(70m, stmt.GetProperty("totalInvoiced").GetDecimal());
        Assert.Equal(44m, stmt.GetProperty("totalCredited").GetDecimal());

        var mid = await OkAsync(s, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}&from=2026-11-09");
        Assert.Equal(56m, mid.GetProperty("openingBalance").GetDecimal());

        var balances = await OkAsync(s, HttpMethod.Get, "/api/v1/sales/customer-balances");
        Assert.Equal(26m, balances.EnumerateArray().First(b => b.GetProperty("customerId").GetInt32() == customer).GetProperty("balance").GetDecimal());

        // cancelling the return reverses the stock and restores the debt
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesreturns/{retId}/cancel");
        Assert.Equal(10m, (await BalanceAsync(s, wh, item)).Qty);
        Assert.Equal(40m, await CustomerBalanceAsync(s, customer));
    }

    [Fact]
    public async Task Credit_limit_blocks_an_invoice_that_would_exceed_it()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s, creditLimit: 50);
        var item = await NewItemAsync(s);

        var big = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, null, Jod, null, null, item, 10, 7), HttpStatusCode.Created);
        var msg = await FailAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{big.GetProperty("id").GetInt32()}/confirm", null);
        Assert.Contains("Credit limit exceeded", msg);

        await ConfirmedInvoiceAsync(s, customer, null, null, item, 5, 7); // 35 <= 50
        var second = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, null, Jod, null, null, item, 3, 7), HttpStatusCode.Created); // 35 + 21 > 50
        Assert.Contains("Credit limit exceeded", await FailAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{second.GetProperty("id").GetInt32()}/confirm", null));

        // a payment frees up room
        var rct = await OkAsync(s, HttpMethod.Post, "/api/v1/customerreceipts",
            new { date = "2026-11-09", customerId = customer, currencyId = Jod, amount = 20, method = "Cash" }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/customerreceipts/{rct.GetProperty("id").GetInt32()}/confirm");
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{second.GetProperty("id").GetInt32()}/confirm"); // 15 + 21 = 36 <= 50
    }

    [Fact]
    public async Task Foreign_currency_documents_snapshot_the_rate_and_the_statement_converts_to_local()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 10, 3);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 4, 10, Usd, 0.71m);
        await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 4, 10, Usd, 0.71m);
        await ConfirmedInvoiceAsync(s, customer, so, line, item, 4, 10, Usd, 0.71m);

        Assert.Equal(28.4m, await CustomerBalanceAsync(s, customer)); // 4 x 10 USD x 0.71
        Assert.Contains("No exchange rate", await FailAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Usd, null, item, 1, 1)));
    }

    [Fact]
    public async Task Cancelling_a_delivery_restocks_but_is_blocked_while_invoiced_or_on_a_departed_trip()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 20, 4);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 10, 7);
        var d = await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 4, 7);
        var invId = await ConfirmedInvoiceAsync(s, customer, so, line, item, 4, 7);

        Assert.Contains("Invoices have already", await FailAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{d}/cancel", null));
        Assert.Equal(16m, (await BalanceAsync(s, wh, item)).Qty);

        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{invId}/cancel");
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{d}/cancel");
        Assert.Equal((20m, 4m), await BalanceAsync(s, wh, item));
        Assert.Equal(0m, (await OrderAsync(s, so)).GetProperty("lines")[0].GetProperty("deliveredQuantity").GetDecimal());

        // a delivery on a trip that has left cannot be cancelled
        var d2 = await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 2, 7);
        var trip = await OkAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-10", driverId = await NewDriverAsync(s), vehicleId = await NewVehicleAsync(s), deliveryNoteIds = new[] { d2 } }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{trip.GetProperty("id").GetInt32()}/start");
        Assert.Contains("already left", await FailAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{d2}/cancel", null));
    }

    [Fact]
    public async Task Order_with_deliveries_cannot_be_cancelled_and_drafts_can()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 5, 4);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 5, 7);
        await ConfirmedDeliveryAsync(s, customer, wh, so, line, item, 1, 7);
        Assert.Contains("deliveries or invoices", await FailAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{so}/cancel", null));

        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1), HttpStatusCode.Created);
        Assert.Equal("Cancelled", (await OkAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{draft.GetProperty("id").GetInt32()}/cancel")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Order_link_is_validated_on_deliveries()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var other = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        var (so, line) = await ConfirmedOrderAsync(s, customer, item, 10, 7);

        Assert.Contains("customer", await FailAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(other, wh, so, Jod, null, line, item, 1, 7)));
        Assert.Contains("currency", await FailAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, so, Usd, 0.71m, line, item, 1, 7)));
        Assert.Contains("reference a sales order line", await FailAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, so, Jod, null, null, item, 1, 7)));
        Assert.Contains("only when", await FailAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, null, Jod, null, line, item, 1, 7)));

        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1), HttpStatusCode.Created);
        var draftLine = (await OrderAsync(s, draft.GetProperty("id").GetInt32())).GetProperty("lines")[0].GetProperty("id").GetInt32();
        Assert.Contains("confirmed", await FailAsync(s, HttpMethod.Post, "/api/v1/deliverynotes",
            DlvBody(customer, wh, draft.GetProperty("id").GetInt32(), Jod, null, draftLine, item, 1, 1)));
    }

    [Fact]
    public async Task Trip_lifecycle_assigns_deliveries_enforces_availability_and_tracks_stops()
    {
        var s = await AdminAsync();
        var c1 = await NewCustomerAsync(s);
        var c2 = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 50, 4);
        var d1 = await ConfirmedDeliveryAsync(s, c1, wh, null, null, item, 3, 7);
        var draft = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(c2, wh, null, Jod, null, null, item, 2, 7), HttpStatusCode.Created);
        var d2 = draft.GetProperty("id").GetInt32();
        var driver = await NewDriverAsync(s);
        var vehicle = await NewVehicleAsync(s);

        var unassigned = await OkAsync(s, HttpMethod.Get, "/api/v1/deliverynotes?unassignedOnly=true&pageSize=200");
        Assert.Contains(unassigned.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt32() == d1);

        var trip = await OkAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, notes = "north route", deliveryNoteIds = new[] { d2, d1 } }, HttpStatusCode.Created);
        var tid = trip.GetProperty("id").GetInt32();
        Assert.StartsWith("TRP-", trip.GetProperty("number").GetString());
        Assert.Equal(d2, trip.GetProperty("stops")[0].GetProperty("deliveryNoteId").GetInt32()); // order preserved
        Assert.Equal(2, trip.GetProperty("stops")[1].GetProperty("sequence").GetInt32());

        // taken deliveries disappear from the unassigned list and cannot join another trip
        var after = await OkAsync(s, HttpMethod.Get, "/api/v1/deliverynotes?unassignedOnly=true&pageSize=200");
        Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt32() == d1);
        Assert.Contains("already on trip", await FailAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-11", driverId = await NewDriverAsync(s), vehicleId = await NewVehicleAsync(s), deliveryNoteIds = new[] { d1 } }));

        // same driver or vehicle, same day: refused
        Assert.Contains("driver already has trip", await FailAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-10", driverId = driver, vehicleId = await NewVehicleAsync(s), deliveryNoteIds = Array.Empty<int>() }));
        Assert.Contains("vehicle already has trip", await FailAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-10", driverId = await NewDriverAsync(s), vehicleId = vehicle, deliveryNoteIds = Array.Empty<int>() }));

        // cannot leave with an unconfirmed delivery
        Assert.Contains("must be confirmed", await FailAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/start", null));
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{d2}/confirm");
        var started = await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/start");
        Assert.Equal("InTransit", started.GetProperty("status").GetString());

        // started trips are frozen; stops must be resolved before completion
        await FailAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}",
            new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d1 } });
        Assert.Contains("every stop", await FailAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/complete", null));

        var stops = started.GetProperty("stops");
        await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}/stops/{stops[0].GetProperty("id").GetInt32()}", new { status = "Delivered" });
        await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}/stops/{stops[1].GetProperty("id").GetInt32()}", new { status = "Failed", notes = "shop closed" });
        var done = await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/complete");
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal("shop closed", done.GetProperty("stops")[1].GetProperty("notes").GetString());

        var list = await OkAsync(s, HttpMethod.Get, "/api/v1/trips?status=Completed&pageSize=200");
        var row = list.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetInt32() == tid);
        Assert.Equal(2, row.GetProperty("stopCount").GetInt32());
        Assert.Equal(1, row.GetProperty("deliveredCount").GetInt32());

        await FailAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/cancel", null); // completed trips cannot be cancelled

        // a failed stop sends the goods back: its delivery is cancelled and the stock returns; a delivered one is finished business
        Assert.Equal(48m, (await BalanceAsync(s, wh, item)).Qty); // 50 - 3 - 2, then the failed 3 came back
        Assert.Equal("Cancelled", (await OkAsync(s, HttpMethod.Get, $"/api/v1/deliverynotes/{d1}")).GetProperty("status").GetString());
        var pool = (await OkAsync(s, HttpMethod.Get, "/api/v1/deliverynotes?unassignedOnly=true&pageSize=200")).GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.DoesNotContain(d2, pool);
        Assert.DoesNotContain(d1, pool);
        Assert.Contains("already delivered", await FailAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-15", driverId = await NewDriverAsync(s), vehicleId = await NewVehicleAsync(s), deliveryNoteIds = new[] { d2 } }));
    }

    [Fact]
    public async Task Planned_trips_can_be_edited_and_cancelled_freeing_their_deliveries()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await StockAsync(s, wh, item, 10, 4);
        var d = await ConfirmedDeliveryAsync(s, customer, wh, null, null, item, 1, 7);
        var driver = await NewDriverAsync(s);
        var vehicle = await NewVehicleAsync(s);

        var trip = await OkAsync(s, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-12", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d } }, HttpStatusCode.Created);
        var tid = trip.GetProperty("id").GetInt32();

        var edited = await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}",
            new { date = "2026-11-13", driverId = driver, vehicleId = vehicle, notes = "moved", deliveryNoteIds = Array.Empty<int>() });
        Assert.Equal(0, edited.GetProperty("stops").GetArrayLength());
        Assert.Contains("at least one stop", await FailAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/start", null));

        await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}", new { date = "2026-11-13", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d } });
        await OkAsync(s, HttpMethod.Post, $"/api/v1/trips/{tid}/cancel");

        var free = await OkAsync(s, HttpMethod.Get, "/api/v1/deliverynotes?unassignedOnly=true&pageSize=200");
        Assert.Contains(free.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt32() == d);
        // and the driver/vehicle are free that day again
        await OkAsync(s, HttpMethod.Post, "/api/v1/trips", new { date = "2026-11-13", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d } }, HttpStatusCode.Created);
    }

    [Fact]
    public async Task Sales_access_follows_roles()
    {
        var admin = await AdminAsync();
        var customer = await NewCustomerAsync(admin);
        var item = await NewItemAsync(admin);
        var wh = await NewWarehouseAsync(admin);

        var buyer = await UserWithRoleAsync("Purchasing");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(buyer, HttpMethod.Get, "/api/v1/salesorders")).Status);

        var sales = await UserWithRoleAsync("Sales");
        var created = await SendAsync(sales, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        await FailAsync(sales, HttpMethod.Post, "/api/v1/trips", new { date = "2026-11-10", driverId = 1, vehicleId = 1, deliveryNoteIds = Array.Empty<int>() }, HttpStatusCode.Forbidden);
        await FailAsync(sales, HttpMethod.Post, "/api/v1/customerreceipts", new { date = "2026-11-09", customerId = customer, currencyId = Jod, amount = 5, method = "Cash" }, HttpStatusCode.Forbidden);

        var dispatcher = await UserWithRoleAsync("Dispatcher");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(dispatcher, HttpMethod.Get, "/api/v1/salesorders")).Status);
        await FailAsync(dispatcher, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1), HttpStatusCode.Forbidden);
        var trip = await SendAsync(dispatcher, HttpMethod.Post, "/api/v1/trips",
            new { date = "2026-11-20", driverId = await NewDriverAsync(admin), vehicleId = await NewVehicleAsync(admin), deliveryNoteIds = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.Created, trip.Status);

        var keeper = await UserWithRoleAsync("StoreKeeper");
        await FailAsync(keeper, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1), HttpStatusCode.Forbidden);
        var delivery = await SendAsync(keeper, HttpMethod.Post, "/api/v1/deliverynotes", DlvBody(customer, wh, null, Jod, null, null, item, 1, 1));
        Assert.Equal(HttpStatusCode.Created, delivery.Status); // store keepers issue goods

        var accountant = await UserWithRoleAsync("Accountant");
        await FailAsync(accountant, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, 1), HttpStatusCode.Forbidden);
        var receipt = await SendAsync(accountant, HttpMethod.Post, "/api/v1/customerreceipts",
            new { date = "2026-11-09", customerId = customer, currencyId = Jod, amount = 5, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, receipt.Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(accountant, HttpMethod.Get, $"/api/v1/sales/customer-statement?customerId={customer}")).Status);
    }

    [Fact]
    public async Task Validation_rejects_bad_sales_input()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);

        await FailAsync(s, HttpMethod.Post, "/api/v1/salesorders", new { date = "2026-11-01", customerId = customer, currencyId = Jod, lines = Array.Empty<object>() });
        await FailAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 0, 5));
        await FailAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(customer, Jod, null, item, 1, -1));
        await FailAsync(s, HttpMethod.Post, "/api/v1/salesorders", SoBody(999999, Jod, null, item, 1, 1));
        await FailAsync(s, HttpMethod.Post, "/api/v1/customerreceipts", new { date = "2026-11-10", customerId = customer, currencyId = Jod, amount = 0, method = "Cash" });
        await FailAsync(s, HttpMethod.Post, "/api/v1/trips", new { date = "2026-11-10", driverId = 0, vehicleId = 0, deliveryNoteIds = Array.Empty<int>() });
    }
}
