using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

/// <summary>Price lists, line and invoice discounts, and trip load against vehicle capacity.</summary>
public class PricingTests(ErpFactory factory) : IClassFixture<ErpFactory>
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

    private static async Task<decimal> NetAsync(Session s, string code)
    {
        var tb = await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/trial-balance");
        var row = tb.GetProperty("rows").EnumerateArray().FirstOrDefault(r => r.GetProperty("code").GetString() == code);
        return row.ValueKind == JsonValueKind.Undefined ? 0m : row.GetProperty("closing").GetDecimal();
    }

    private static async Task<decimal> CustomerBalanceAsync(Session s, int customer) =>
        (await OkAsync(s, HttpMethod.Get, "/api/v1/sales/customer-balances")).EnumerateArray()
            .Where(b => b.GetProperty("customerId").GetInt32() == customer).Select(b => b.GetProperty("balance").GetDecimal()).FirstOrDefault();

    private static object InvBody(int customer, int item, decimal qty, decimal price, decimal pct, decimal header) => new
    {
        date = "2026-11-07", customerId = customer, currencyId = Local, discountAmount = header,
        lines = new[] { new { itemId = item, quantity = qty, unitPrice = price, discountPercent = pct } },
    };

    [Fact]
    public async Task Line_and_invoice_discounts_reduce_the_receivable_and_are_booked_to_their_own_account()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var rev = await NetAsync(s, "4100");
        var disc = await NetAsync(s, "4250");
        var recv = await NetAsync(s, "1300");

        // 10 x 7 = 70, line discount 10% -> 63, invoice discount 3 -> 60
        var inv = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, item, 10, 7, 10, 3), HttpStatusCode.Created);
        Assert.Equal(60m, inv.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(3m, inv.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(10m, inv.GetProperty("lines")[0].GetProperty("discountPercent").GetDecimal());
        var id = inv.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{id}/confirm");

        Assert.Equal(60m, await CustomerBalanceAsync(s, customer));
        Assert.Equal(-70m, await NetAsync(s, "4100") - rev);   // revenue at the undiscounted value
        Assert.Equal(10m, await NetAsync(s, "4250") - disc);   // 7 line + 3 invoice discount
        Assert.Equal(60m, await NetAsync(s, "1300") - recv);

        // cancelling reverses all three
        await OkAsync(s, HttpMethod.Post, $"/api/v1/salesinvoices/{id}/cancel");
        Assert.Equal(0m, await NetAsync(s, "4100") - rev);
        Assert.Equal(0m, await NetAsync(s, "4250") - disc);
        Assert.Equal(0m, await CustomerBalanceAsync(s, customer));

        foreach (var r in (await OkAsync(s, HttpMethod.Get, "/api/v1/accounting/reconciliation")).EnumerateArray())
            Assert.True(r.GetProperty("reconciled").GetBoolean(), r.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Discount_rules_are_enforced()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);

        await FailAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, item, 1, 10, 150, 0)); // > 100 %
        await FailAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, item, 1, 10, 0, -1));   // negative
        Assert.Contains("cannot exceed", await FailAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, item, 1, 10, 50, 6))); // lines = 5

        // a 100 % discount is allowed and leaves nothing to pay
        var free = await OkAsync(s, HttpMethod.Post, "/api/v1/salesinvoices", InvBody(customer, item, 1, 10, 100, 0), HttpStatusCode.Created);
        Assert.Equal(0m, free.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Sales_order_lines_carry_their_discount()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var so = await OkAsync(s, HttpMethod.Post, "/api/v1/salesorders", new
        {
            date = "2026-11-05", customerId = customer, currencyId = Local,
            lines = new[] { new { itemId = item, quantity = 4, unitPrice = 5, discountPercent = 25 } },
        }, HttpStatusCode.Created);
        Assert.Equal(15m, so.GetProperty("totalAmount").GetDecimal()); // 20 - 25 %
        Assert.Equal(25m, so.GetProperty("lines")[0].GetProperty("discountPercent").GetDecimal());
    }

    [Fact]
    public async Task Items_keep_a_distributor_price_and_a_weight()
    {
        var s = await AdminAsync();
        var created = await OkAsync(s, HttpMethod.Post, "/api/v1/items", new
        {
            code = "PL" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1, salePrice = 10, distributorPrice = 8, weightKg = 1.5,
        }, HttpStatusCode.Created);
        Assert.Equal(8m, created.GetProperty("distributorPrice").GetDecimal());
        Assert.Equal(1.5m, created.GetProperty("weightKg").GetDecimal());
        await FailAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "PL" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1, weightKg = -1 });
    }

    [Fact]
    public async Task Trip_load_is_checked_against_vehicle_capacity_as_a_warning()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var wh = await NewWarehouseAsync(s);
        var heavy = (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "HV" + Uid(), nameAr = "ثقيل", nameEn = "Heavy", unitId = 1, weightKg = 2 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var unknown = await NewItemAsync(s); // no weight recorded
        await StockAsync(s, wh, heavy, 100, 1);
        await StockAsync(s, wh, unknown, 100, 1);

        async Task<int> DeliveryAsync(int item, decimal qty)
        {
            var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", new
            {
                date = "2026-11-06", customerId = customer, warehouseId = wh, currencyId = Local,
                lines = new[] { new { itemId = item, quantity = qty, unitPrice = 1 } },
            }, HttpStatusCode.Created);
            return d.GetProperty("id").GetInt32();
        }

        var driver = (await OkAsync(s, HttpMethod.Post, "/api/v1/drivers", new { name = "D" + Uid() }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var vehicle = (await OkAsync(s, HttpMethod.Post, "/api/v1/vehicles", new { plateNumber = "V-" + Uid(), capacityKg = 10 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

        var d1 = await DeliveryAsync(heavy, 4); // 8 kg
        var trip = await OkAsync(s, HttpMethod.Post, "/api/v1/trips", new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d1 } }, HttpStatusCode.Created);
        Assert.Equal(8m, trip.GetProperty("totalWeightKg").GetDecimal());
        Assert.Equal(10m, trip.GetProperty("capacityKg").GetDecimal());
        Assert.False(trip.GetProperty("overCapacity").GetBoolean());
        var tid = trip.GetProperty("id").GetInt32();

        // adding another 6 kg exceeds the 10 kg capacity: saved anyway, flagged
        var d2 = await DeliveryAsync(heavy, 3);
        var over = await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}", new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d1, d2 } });
        Assert.Equal(14m, over.GetProperty("totalWeightKg").GetDecimal());
        Assert.True(over.GetProperty("overCapacity").GetBoolean());

        // goods without a recorded weight are counted so the total can be read with care
        var d3 = await DeliveryAsync(unknown, 5);
        var partial = await OkAsync(s, HttpMethod.Put, $"/api/v1/trips/{tid}", new { date = "2026-11-10", driverId = driver, vehicleId = vehicle, deliveryNoteIds = new[] { d1, d3 } });
        Assert.Equal(1, partial.GetProperty("linesWithoutWeight").GetInt32());
        Assert.Equal(8m, partial.GetProperty("totalWeightKg").GetDecimal());
    }
}
