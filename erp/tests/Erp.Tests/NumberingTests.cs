using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class NumberingTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
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

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static int Seq(JsonElement doc) => int.Parse(doc.GetProperty("number").GetString()!.Split('-')[1]);

    private static async Task<int> NewCustomerAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers", new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Retail" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewItemAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "N" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static async Task<int> NewWarehouseAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses", new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "Warehouse" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private static Task<JsonElement> DraftOrderAsync(Session s, int customer, int item) =>
        OkAsync(s, HttpMethod.Post, "/api/v1/salesorders",
            new { date = "2026-11-01", customerId = customer, currencyId = 1, lines = new[] { new { itemId = item, quantity = 1, unitPrice = 5 } } }, HttpStatusCode.Created);

    [Fact]
    public async Task Drafts_have_no_number_and_cancelled_drafts_never_leave_a_gap()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);

        var a = await DraftOrderAsync(s, customer, item);
        var b = await DraftOrderAsync(s, customer, item);
        var c = await DraftOrderAsync(s, customer, item);
        foreach (var d in new[] { a, b, c }) Assert.Equal(JsonValueKind.Null, d.GetProperty("number").ValueKind);

        var first = await OkAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{a.GetProperty("id").GetInt32()}/confirm");
        var cancelledDraft = await OkAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{b.GetProperty("id").GetInt32()}/cancel");
        var third = await OkAsync(s, HttpMethod.Post, $"/api/v1/salesorders/{c.GetProperty("id").GetInt32()}/confirm");

        Assert.Equal(JsonValueKind.Null, cancelledDraft.GetProperty("number").ValueKind); // a cancelled draft consumed no number
        Assert.Equal(Seq(first) + 1, Seq(third));
        Assert.StartsWith("SO-", first.GetProperty("number").GetString());
    }

    [Fact]
    public async Task A_failed_confirmation_does_not_consume_a_number()
    {
        var s = await AdminAsync();
        var customer = await NewCustomerAsync(s);
        var item = await NewItemAsync(s);
        var wh = await NewWarehouseAsync(s); // empty: a delivery cannot be confirmed

        object Body(int qty) => new
        {
            date = "2026-11-02", customerId = customer, warehouseId = wh, currencyId = 1,
            lines = new[] { new { itemId = item, quantity = qty, unitPrice = 5 } },
        };

        var doomed = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", Body(5), HttpStatusCode.Created);
        var (status, _) = await SendAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{doomed.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(HttpStatusCode.BadRequest, status); // insufficient stock

        // put stock in, then confirm two deliveries: their numbers follow each other with nothing skipped
        var adj = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId = wh, reason = "Opening", lines = new[] { new { itemId = item, countedQuantity = 50, unitCost = 1 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{adj.GetProperty("id").GetInt32()}/confirm");

        var one = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", Body(1), HttpStatusCode.Created);
        var two = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", Body(2), HttpStatusCode.Created);
        var n1 = await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{one.GetProperty("id").GetInt32()}/confirm");
        var n2 = await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{two.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(Seq(n1) + 1, Seq(n2));

        // the doomed one is still an unnumbered draft; confirming it now takes the next number
        Assert.Equal(JsonValueKind.Null, (await OkAsync(s, HttpMethod.Get, $"/api/v1/deliverynotes/{doomed.GetProperty("id").GetInt32()}")).GetProperty("number").ValueKind);
        var late = await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{doomed.GetProperty("id").GetInt32()}/confirm");
        Assert.Equal(Seq(n2) + 1, Seq(late));
    }

    [Fact]
    public async Task Journal_entries_are_numbered_when_posted_without_gaps()
    {
        var s = await AdminAsync();
        var accounts = (await OkAsync(s, HttpMethod.Get, "/api/v1/accounts?pageSize=200")).GetProperty("items").EnumerateArray().ToList();
        var cash = accounts.First(a => a.GetProperty("code").GetString() == "1100").GetProperty("id").GetInt32();
        var equity = accounts.First(a => a.GetProperty("code").GetString() == "3100").GetProperty("id").GetInt32();

        async Task<JsonElement> Draft(decimal amount) => await OkAsync(s, HttpMethod.Post, "/api/v1/journalentries", new
        {
            date = "2026-11-10", description = "n",
            lines = new object[] { new { accountId = cash, debit = amount, credit = 0 }, new { accountId = equity, debit = 0, credit = amount } },
        }, HttpStatusCode.Created);

        var d1 = await Draft(1);
        var d2 = await Draft(2);
        var d3 = await Draft(3);
        await OkAsync(s, HttpMethod.Delete, $"/api/v1/journalentries/{d2.GetProperty("id").GetInt32()}"); // deleting a draft leaves no hole
        var p1 = await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{d1.GetProperty("id").GetInt32()}/post");
        var p3 = await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{d3.GetProperty("id").GetInt32()}/post");
        Assert.Equal(Seq(p1) + 1, Seq(p3));

        var reversal = await OkAsync(s, HttpMethod.Post, $"/api/v1/journalentries/{p1.GetProperty("id").GetInt32()}/reverse");
        Assert.Equal(Seq(p3) + 1, Seq(reversal));
    }
}
