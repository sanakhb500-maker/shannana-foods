using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

/// <summary>The delivery drivers' mobile API: bearer login, scoping to the driver's own trips, results, collections and proofs.</summary>
public class DriverTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private const int Local = 1;
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

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
        return await ReadAsync(await s.Client.SendAsync(req));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith('{') ? default : JsonDocument.Parse(text).RootElement);
    }

    private static async Task<JsonElement> OkAsync(Session s, HttpMethod m, string url, object? body = null, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var (status, json) = await SendAsync(s, m, url, body);
        Assert.True(status == expect, $"{m} {url} -> {(int)status}: {json}");
        return json.GetProperty("data");
    }

    private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // ---------- the driver's phone: bearer token, no cookies, no CSRF ----------
    private sealed class Phone(HttpClient client)
    {
        public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod m, string url, object? body = null)
        {
            var req = new HttpRequestMessage(m, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await ReadAsync(await client.SendAsync(req));
        }

        public async Task<JsonElement> OkAsync(HttpMethod m, string url, object? body = null)
        {
            var (status, json) = await SendAsync(m, url, body);
            Assert.True(status == HttpStatusCode.OK, $"{m} {url} -> {(int)status}: {json}");
            return json.GetProperty("data");
        }

        public async Task<string> FailAsync(HttpMethod m, string url, object? body, HttpStatusCode expect = HttpStatusCode.BadRequest)
        {
            var (status, json) = await SendAsync(m, url, body);
            Assert.True(status == expect, $"{m} {url} -> {(int)status}: {json}");
            return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
        }

        public async Task<(HttpStatusCode Status, string Message)> UploadAsync(int stopId, string kind, byte[] data, string name = "proof.png")
        {
            var form = new MultipartFormDataContent { { new StringContent(kind), "kind" } };
            var file = new ByteArrayContent(data);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", name);
            var (status, json) = await ReadAsync(await client.PostAsync($"/api/v1/driver/stops/{stopId}/proof", form));
            return (status, json.ValueKind == JsonValueKind.Object && json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "");
        }

        public HttpClient Client => client;
    }

    /// <summary>An active driver record plus a Driver-role login linked to it; returns the signed-in phone.</summary>
    private async Task<(Phone Phone, int DriverId)> NewDriverPhoneAsync(Session admin)
    {
        var driverId = (await OkAsync(admin, HttpMethod.Post, "/api/v1/drivers", new { name = "Driver " + Uid() }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var email = $"drv-{Uid()}@test.local";
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users", new { email, fullName = "Driver", password = "Passw0rd!x", roles = new[] { "Driver" }, driverId }, HttpStatusCode.Created);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var res = await client.PostAsJsonAsync("/api/v1/auth/token", new { email, password = "Passw0rd!x" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(driverId, token.GetProperty("driverId").GetInt32());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("token").GetString());
        return (new Phone(client), driverId);
    }

    // ---------- trip fixtures ----------
    private static async Task<int> CustomerAsync(Session s) =>
        (await OkAsync(s, HttpMethod.Post, "/api/v1/customers", new { code = "C" + Uid(), nameAr = "عميل", nameEn = "Customer", type = "Retail", phone = "0999", address = "Main St" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private sealed record Fixture(int Wh, int Item, int Customer);

    private static async Task<Fixture> StockedAsync(Session s)
    {
        var item = (await OkAsync(s, HttpMethod.Post, "/api/v1/items", new { code = "X" + Uid(), nameAr = "صنف", nameEn = "Item", unitId = 1 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var wh = (await OkAsync(s, HttpMethod.Post, "/api/v1/warehouses", new { code = "W" + Uid(), nameAr = "مستودع", nameEn = "WH" }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var adj = await OkAsync(s, HttpMethod.Post, "/api/v1/stockadjustments", new
        {
            date = "2026-11-01", warehouseId = wh, reason = "Opening", lines = new[] { new { itemId = item, countedQuantity = 50, unitCost = 4 } },
        }, HttpStatusCode.Created);
        await OkAsync(s, HttpMethod.Post, $"/api/v1/stockadjustments/{adj.GetProperty("id").GetInt32()}/confirm");
        return new Fixture(wh, item, await CustomerAsync(s));
    }

    private static async Task<int> ConfirmedDeliveryAsync(Session s, Fixture f, int customer, decimal qty)
    {
        var d = await OkAsync(s, HttpMethod.Post, "/api/v1/deliverynotes", new
        {
            date = "2026-11-06", customerId = customer, warehouseId = f.Wh, currencyId = Local,
            lines = new[] { new { itemId = f.Item, quantity = qty, unitPrice = 7 } },
        }, HttpStatusCode.Created);
        var id = d.GetProperty("id").GetInt32();
        await OkAsync(s, HttpMethod.Post, $"/api/v1/deliverynotes/{id}/confirm");
        return id;
    }

    private static async Task<decimal> QtyAsync(Session s, Fixture f)
    {
        var rows = (await OkAsync(s, HttpMethod.Get, $"/api/v1/stock/balances?warehouseId={f.Wh}&itemId={f.Item}&nonZeroOnly=false")).GetProperty("items");
        return rows.GetArrayLength() == 0 ? 0 : rows[0].GetProperty("quantity").GetDecimal();
    }

    private static async Task<int> TripAsync(Session s, int driverId, string date, params int[] deliveries)
    {
        var vehicle = (await OkAsync(s, HttpMethod.Post, "/api/v1/vehicles", new { plateNumber = "V-" + Uid(), capacityKg = 1000 }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        return (await OkAsync(s, HttpMethod.Post, "/api/v1/trips", new { date, driverId, vehicleId = vehicle, deliveryNoteIds = deliveries }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
    }

    // ------------------------------------------------------------------ tests
    [Fact]
    public async Task Driver_signs_in_with_a_bearer_token_and_starts_with_no_trips()
    {
        var admin = await AdminAsync();
        var (phone, _) = await NewDriverPhoneAsync(admin);
        Assert.Empty((await phone.OkAsync(HttpMethod.Get, "/api/v1/driver/trips")).EnumerateArray());

        var anon = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/driver/trips")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/v1/auth/token", new { email = "nobody@test.local", password = "x" })).StatusCode);
    }

    [Fact]
    public async Task Driver_account_is_limited_to_the_driver_app()
    {
        var admin = await AdminAsync();
        var (phone, _) = await NewDriverPhoneAsync(admin);

        // everything outside the driver API is closed, even reads that any other signed-in user may make
        foreach (var url in new[] { "/api/v1/customers", "/api/v1/items", "/api/v1/trips", "/api/v1/customerreceipts", "/api/v1/accounting/trial-balance" })
            Assert.Equal(HttpStatusCode.Forbidden, (await phone.Client.GetAsync(url)).StatusCode);
        await phone.FailAsync(HttpMethod.Post, "/api/v1/customerreceipts", new { }, HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await phone.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Driver_only_sees_and_changes_their_own_trips()
    {
        var admin = await AdminAsync();
        var f = await StockedAsync(admin);
        var (mine, myId) = await NewDriverPhoneAsync(admin);
        var (other, otherId) = await NewDriverPhoneAsync(admin);
        var myDelivery = await ConfirmedDeliveryAsync(admin, f, f.Customer, 2);
        var otherDelivery = await ConfirmedDeliveryAsync(admin, f, await CustomerAsync(admin), 1);
        var myTrip = await TripAsync(admin, myId, "2026-11-10", myDelivery);
        var otherTrip = await TripAsync(admin, otherId, "2026-11-10", otherDelivery);

        var list = (await mine.OkAsync(HttpMethod.Get, "/api/v1/driver/trips")).EnumerateArray().Select(t => t.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(myTrip, list);
        Assert.DoesNotContain(otherTrip, list);

        await mine.FailAsync(HttpMethod.Get, $"/api/v1/driver/trips/{otherTrip}", null, HttpStatusCode.NotFound);
        await mine.FailAsync(HttpMethod.Post, $"/api/v1/driver/trips/{otherTrip}/start", null, HttpStatusCode.NotFound);

        // the detail carries what the driver needs at the door
        var detail = await mine.OkAsync(HttpMethod.Get, $"/api/v1/driver/trips/{myTrip}");
        var stop = detail.GetProperty("stops")[0];
        Assert.Equal("0999", stop.GetProperty("phone").GetString());
        Assert.Equal("Main St", stop.GetProperty("address").GetString());
        Assert.Equal(14m, stop.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2m, stop.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        await mine.FailAsync(HttpMethod.Post, $"/api/v1/driver/stops/{otherTrip}/collections", new { amount = 1, method = "Cash" }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_full_trip_with_delivery_collection_proof_and_a_failed_stop()
    {
        var admin = await AdminAsync();
        var f = await StockedAsync(admin);
        var (phone, driverId) = await NewDriverPhoneAsync(admin);
        var d1 = await ConfirmedDeliveryAsync(admin, f, f.Customer, 3);
        var d2 = await ConfirmedDeliveryAsync(admin, f, await CustomerAsync(admin), 2);
        Assert.Equal(45m, await QtyAsync(admin, f));
        var tripId = await TripAsync(admin, driverId, "2026-11-10", d1, d2);
        var trip = $"/api/v1/driver/trips/{tripId}";

        // nothing can be recorded before the trip leaves
        var stops = (await phone.OkAsync(HttpMethod.Get, trip)).GetProperty("stops");
        var s1 = stops[0].GetProperty("id").GetInt32();
        var s2 = stops[1].GetProperty("id").GetInt32();
        await phone.FailAsync(HttpMethod.Put, $"{trip}/stops/{s1}", new { status = "Delivered" });
        await phone.FailAsync(HttpMethod.Post, $"/api/v1/driver/stops/{s1}/collections", new { amount = 5, method = "Cash" });

        var started = await phone.OkAsync(HttpMethod.Post, $"{trip}/start");
        Assert.Equal("InTransit", started.GetProperty("status").GetString());
        await phone.FailAsync(HttpMethod.Post, $"{trip}/complete", null); // stops still pending

        // stop 1: delivered, 10 collected, signature recorded
        var done = await phone.OkAsync(HttpMethod.Put, $"{trip}/stops/{s1}", new { status = "Delivered", latitude = 33.5, longitude = 36.3 });
        Assert.Equal("Delivered", done.GetProperty("stops")[0].GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("stops")[0].GetProperty("actionAt").ValueKind);
        var collected = await phone.OkAsync(HttpMethod.Post, $"/api/v1/driver/stops/{s1}/collections", new { amount = 10, method = "Cash", notes = "paid on the spot" });
        Assert.Equal(10m, collected.GetProperty("totalCollected").GetDecimal());
        await phone.FailAsync(HttpMethod.Post, $"/api/v1/driver/stops/{s1}/collections", new { amount = 0, method = "Cash" });
        var (upStatus, _) = await phone.UploadAsync(s1, "Signature", Convert.FromBase64String(TinyPng));
        Assert.Equal(HttpStatusCode.OK, upStatus);

        // stop 2: failed needs a reason
        await phone.FailAsync(HttpMethod.Put, $"{trip}/stops/{s2}", new { status = "Failed" });
        await phone.FailAsync(HttpMethod.Put, $"{trip}/stops/{s2}", new { status = "Pending" });
        var failed = await phone.OkAsync(HttpMethod.Put, $"{trip}/stops/{s2}", new { status = "Failed", reason = "Shop closed", notes = "back at 5" });
        Assert.Equal("Shop closed - back at 5", failed.GetProperty("stops")[1].GetProperty("notes").GetString());

        var completed = await phone.OkAsync(HttpMethod.Post, $"{trip}/complete");
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(47m, await QtyAsync(admin, f)); // the failed delivery of 2 is back in stock

        // dispatcher side: the trip shows collection and proof, the proof file opens, the accountant can confirm the draft receipt
        var adminTrip = await OkAsync(admin, HttpMethod.Get, $"/api/v1/trips/{tripId}");
        var adminStop = adminTrip.GetProperty("stops")[0];
        Assert.Equal(10m, adminStop.GetProperty("collected").GetDecimal());
        var proofId = adminStop.GetProperty("proofIds")[0].GetInt32();
        var file = await admin.Client.GetAsync($"/api/v1/stopproofs/{proofId}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await phone.Client.GetAsync($"/api/v1/driver/proofs/{proofId}")).StatusCode);

        var receipts = await OkAsync(admin, HttpMethod.Get, $"/api/v1/customerreceipts?customerId={f.Customer}");
        var receipt = receipts.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Draft", receipt.GetProperty("status").GetString());
        Assert.Equal(10m, receipt.GetProperty("amount").GetDecimal());
        await OkAsync(admin, HttpMethod.Post, $"/api/v1/customerreceipts/{receipt.GetProperty("id").GetInt32()}/confirm");

        foreach (var r in (await OkAsync(admin, HttpMethod.Get, "/api/v1/accounting/reconciliation")).EnumerateArray())
            Assert.True(r.GetProperty("reconciled").GetBoolean(), r.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Proof_uploads_accept_only_real_small_images_and_only_for_own_stops()
    {
        var admin = await AdminAsync();
        var f = await StockedAsync(admin);
        var (phone, driverId) = await NewDriverPhoneAsync(admin);
        var (stranger, _) = await NewDriverPhoneAsync(admin);
        var delivery = await ConfirmedDeliveryAsync(admin, f, f.Customer, 1);
        var tripId = await TripAsync(admin, driverId, "2026-11-10", delivery);
        var stop = (await phone.OkAsync(HttpMethod.Get, $"/api/v1/driver/trips/{tripId}")).GetProperty("stops")[0].GetProperty("id").GetInt32();
        var png = Convert.FromBase64String(TinyPng);

        var (notImage, msg) = await phone.UploadAsync(stop, "Photo", "<html>not an image</html>"u8.ToArray(), "x.png"); // right name, wrong bytes
        Assert.Equal(HttpStatusCode.BadRequest, notImage);
        Assert.Contains("PNG or JPEG", msg);
        var big = new byte[3 * 1024 * 1024 + 10];
        png.CopyTo(big, 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await phone.UploadAsync(stop, "Photo", big)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await phone.UploadAsync(stop, "Selfie", png)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.UploadAsync(stop, "Photo", png)).Status);

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await phone.UploadAsync(stop, "Photo", png)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await phone.UploadAsync(stop, "Photo", png)).Status); // at most 5 per stop

        var proofId = (await OkAsync(admin, HttpMethod.Get, $"/api/v1/trips/{tripId}")).GetProperty("stops")[0].GetProperty("proofIds")[0].GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/v1/driver/proofs/{proofId}")).StatusCode);
    }

    [Fact]
    public async Task Driver_role_needs_a_free_driver_record()
    {
        var admin = await AdminAsync();
        var driverId = (await OkAsync(admin, HttpMethod.Post, "/api/v1/drivers", new { name = "D" + Uid() }, HttpStatusCode.Created)).GetProperty("id").GetInt32();
        object Body(int? driver) => new { email = $"d-{Uid()}@test.local", fullName = "D", password = "Passw0rd!x", roles = new[] { "Driver" }, driverId = driver };

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/v1/users", Body(null))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/v1/users", Body(99999))).Status);
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users", Body(driverId), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/v1/users", Body(driverId))).Status); // already linked

        // taking the Driver role away frees the record again
        var users = await OkAsync(admin, HttpMethod.Get, "/api/v1/users");
        var linked = users.EnumerateArray().First(u => u.TryGetProperty("driverId", out var d) && d.ValueKind == JsonValueKind.Number && d.GetInt32() == driverId);
        await OkAsync(admin, HttpMethod.Put, $"/api/v1/users/{linked.GetProperty("id").GetInt32()}", new { fullName = "D", isActive = true, roles = new[] { "Dispatcher" } });
        await OkAsync(admin, HttpMethod.Post, "/api/v1/users", Body(driverId), HttpStatusCode.Created);
    }
}
