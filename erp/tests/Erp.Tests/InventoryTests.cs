using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Domain.Inventory;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class CostingTests
{
    [Fact]
    public void First_receipt_sets_the_cost() =>
        Assert.Equal(5m, Costing.MovingAverage(0, 0, 10, 5));

    [Fact]
    public void Second_receipt_blends_by_quantity() =>
        Assert.Equal(6.5m, Costing.MovingAverage(10, 5, 10, 8));

    [Fact]
    public void Cost_restarts_when_there_is_no_positive_stock() =>
        Assert.Equal(9m, Costing.MovingAverage(0, 4, 3, 9));

    [Fact]
    public void Result_is_rounded_to_four_decimals() =>
        Assert.Equal(3.3333m, Costing.MovingAverage(2, 3, 1, 4));

    [Fact]
    public void Incoming_quantity_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Costing.MovingAverage(1, 1, 0, 1));
}

public class InventoryTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
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

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<int> NewItemAsync(Session s, decimal reorderLevel = 0, decimal costPrice = 1) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items",
            new { code = "T" + Uid(), nameAr = "صنف", nameEn = "Test item", unitId = 1, costPrice, salePrice = 2, reorderLevel },
            HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses",
            new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Test warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    /// <summary>Posts an adjustment that sets item stock to <paramref name="counted"/> at <paramref name="cost"/>.</summary>
    private static async Task<int> AdjustAsync(Session s, int warehouseId, int itemId, decimal counted, decimal? cost, string reason = "Count")
    {
        var created = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId, reason,
            lines = new[] { new { itemId, countedQuantity = counted, unitCost = cost } },
        }, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{id}/confirm");
        return id;
    }

    private static async Task<(decimal Qty, decimal Cost)> BalanceAsync(Session s, int warehouseId, int itemId)
    {
        var data = await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={warehouseId}&itemId={itemId}&nonZeroOnly=false");
        var rows = data.GetProperty("items");
        if (rows.GetArrayLength() == 0) return (0, 0);
        return (rows[0].GetProperty("quantity").GetDecimal(), rows[0].GetProperty("averageCost").GetDecimal());
    }

    private static object TransferBody(int from, int to, int itemId, decimal qty) => new
    {
        date = "2026-11-02", fromWarehouseId = from, toWarehouseId = to,
        lines = new[] { new { itemId, quantity = qty } },
    };

    [Fact]
    public async Task Opening_balance_then_second_adjustment_blends_the_average_cost()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);

        await AdjustAsync(s, wh, item, 10, 5, "Opening");
        Assert.Equal((10m, 5m), await BalanceAsync(s, wh, item));

        await AdjustAsync(s, wh, item, 20, 8); // +10 at cost 8 -> (10*5 + 10*8) / 20
        Assert.Equal((20m, 6.5m), await BalanceAsync(s, wh, item));
    }

    [Fact]
    public async Task Count_below_system_quantity_writes_stock_off_at_average_cost()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await AdjustAsync(s, wh, item, 10, 4, "Opening");

        var id = await AdjustAsync(s, wh, item, 7, null);
        Assert.Equal((7m, 4m), await BalanceAsync(s, wh, item));

        var doc = await OkAsync(s, HttpMethod.Get, $"/api/v1/stockadjustments/{id}");
        var line = doc.GetProperty("lines")[0];
        Assert.Equal(10m, line.GetProperty("systemQuantity").GetDecimal());
        Assert.Equal(-3m, line.GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task Transfer_moves_stock_and_carries_the_cost_to_the_destination()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);
        await AdjustAsync(s, a, item, 20, 6.5m, "Opening");

        var t = await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 4), HttpStatusCode.Created);
        var id = t.GetProperty("id").GetInt32();
        Assert.StartsWith("TRF-", t.GetProperty("number").GetString());
        Assert.Equal("Draft", t.GetProperty("status").GetString());
        Assert.Equal((20m, 6.5m), await BalanceAsync(s, a, item)); // draft does not touch stock

        var confirmed = await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/confirm");
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        Assert.Equal((16m, 6.5m), await BalanceAsync(s, a, item));
        Assert.Equal((4m, 6.5m), await BalanceAsync(s, b, item));

        var moves = await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/movements?itemId={item}&documentType=StockTransfer");
        Assert.Equal(2, moves.GetProperty("total").GetInt32()); // one out, one in
    }

    [Fact]
    public async Task Transfer_with_insufficient_stock_is_rejected_and_changes_nothing()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);
        await AdjustAsync(s, a, item, 5, 2, "Opening");

        var t = await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 50), HttpStatusCode.Created);
        var id = t.GetProperty("id").GetInt32();
        var (status, json) = await SendAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/confirm");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Insufficient stock", json.GetProperty("message").GetString());
        Assert.Equal((5m, 2m), await BalanceAsync(s, a, item));
        Assert.Equal((0m, 0m), await BalanceAsync(s, b, item));
        Assert.Equal("Draft", (await OkAsync(s, HttpMethod.Get, $"/api/v1/stocktransfers/{id}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_a_confirmed_transfer_restores_both_warehouses()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);
        await AdjustAsync(s, a, item, 10, 3, "Opening");

        var id = (await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 6), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/confirm");
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/cancel");

        Assert.Equal((10m, 3m), await BalanceAsync(s, a, item));
        Assert.Equal(0m, (await BalanceAsync(s, b, item)).Qty);

        var moves = await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/movements?itemId={item}&documentType=StockTransfer");
        Assert.Equal(4, moves.GetProperty("total").GetInt32()); // original out+in plus two reversals

        var again = await SendAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/cancel");
        Assert.Equal(HttpStatusCode.BadRequest, again.Status);
    }

    [Fact]
    public async Task Cannot_cancel_a_transfer_whose_stock_has_since_been_used()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);
        var c = await NewWarehouseAsync(s);
        await AdjustAsync(s, a, item, 10, 3, "Opening");

        var first = (await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 10), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{first}/confirm");
        var second = (await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(b, c, item, 10), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{second}/confirm");

        var (status, json) = await SendAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{first}/cancel");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("already been used", json.GetProperty("message").GetString());
        Assert.Equal(10m, (await BalanceAsync(s, c, item)).Qty);
    }

    [Fact]
    public async Task Drafts_can_be_edited_but_confirmed_documents_cannot()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);
        await AdjustAsync(s, a, item, 10, 1, "Opening");

        var id = (await OkAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 2), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var edited = await OkAsync(s, HttpMethod.Put, $"/api/v1/stocktransfers/{id}", TransferBody(a, b, item, 3));
        Assert.Equal(3m, edited.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(1, edited.GetProperty("lines").GetArrayLength());

        await OkAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/confirm");
        var (status, _) = await SendAsync(s, HttpMethod.Put, $"/api/v1/stocktransfers/{id}", TransferBody(a, b, item, 1));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var (again, _) = await SendAsync(s, HttpMethod.Post, $"/api/v1/stocktransfers/{id}/confirm");
        Assert.Equal(HttpStatusCode.BadRequest, again);
    }

    [Fact]
    public async Task Transfer_validation_rejects_same_warehouse_duplicates_and_non_positive_quantities()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var a = await NewWarehouseAsync(s);
        var b = await NewWarehouseAsync(s);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, a, item, 1))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 0))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Post, "/api/v1/stocktransfers",
            new { date = "2026-11-02", fromWarehouseId = a, toWarehouseId = b, lines = new[] { new { itemId = item, quantity = 1 }, new { itemId = item, quantity = 2 } } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Post, "/api/v1/stocktransfers",
            new { date = "2026-11-02", fromWarehouseId = a, toWarehouseId = b, lines = Array.Empty<object>() })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, 999999, item, 1))).Status);
    }

    [Fact]
    public async Task Item_and_warehouse_holding_stock_cannot_be_deactivated()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await AdjustAsync(s, wh, item, 5, 2, "Opening");

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Delete, $"/api/v1/warehouses/{wh}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s, HttpMethod.Delete, $"/api/v1/items/{item}")).Status);

        await AdjustAsync(s, wh, item, 0, null); // count to zero
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(s, HttpMethod.Delete, $"/api/v1/items/{item}")).Status);
    }

    [Fact]
    public async Task Low_stock_lists_items_at_or_below_their_reorder_level()
    {
        var s = await AdminAsync();
        var low = await NewItemAsync(s, reorderLevel: 50);
        var ok = await NewItemAsync(s, reorderLevel: 5);
        var wh = await NewWarehouseAsync(s);
        await AdjustAsync(s, wh, low, 20, 1, "Opening");
        await AdjustAsync(s, wh, ok, 20, 1, "Opening");

        var rows = await OkAsync(s, HttpMethod.Get, "/api/v1/stock/low-stock");
        var ids = rows.EnumerateArray().Select(r => r.GetProperty("itemId").GetInt32()).ToList();
        Assert.Contains(low, ids);
        Assert.DoesNotContain(ok, ids);
    }

    [Fact]
    public async Task Stock_valuation_total_matches_quantity_times_cost()
    {
        var s = await AdminAsync();
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s);
        await AdjustAsync(s, wh, item, 10, 5, "Opening");
        await AdjustAsync(s, wh, item, 20, 8);

        var data = await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={wh}");
        Assert.Equal(130m, data.GetProperty("totalValue").GetDecimal()); // 20 * 6.5
    }

    [Fact]
    public async Task Only_inventory_roles_can_post_but_everyone_can_read_stock()
    {
        var admin = await AdminAsync();
        var item = await NewItemAsync(admin);
        var a = await NewWarehouseAsync(admin);
        var b = await NewWarehouseAsync(admin);

        var email = $"sales-{Uid()}@test.local";
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users",
            new { email, fullName = "Sales", password = "Passw0rd!x", roles = new[] { "Sales" } }, HttpStatusCode.Created);
        var sales = await LoginAsync(email, "Passw0rd!x");

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(sales, HttpMethod.Post, "/api/v1/stocktransfers", TransferBody(a, b, item, 1))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(sales, HttpMethod.Post, "/api/v1/stockadjustments",
            new { date = "2026-11-01", warehouseId = a, reason = "Count", lines = new[] { new { itemId = item, countedQuantity = 1 } } })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(sales, HttpMethod.Get, "/api/v1/stock/balances")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(sales, HttpMethod.Get, "/api/v1/stock/movements")).Status);
    }
}
