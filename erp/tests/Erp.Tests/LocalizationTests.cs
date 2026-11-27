using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Application.Common;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Tests;

public class LocalizationTests(ErpFactory factory) : IClassFixture<ErpFactory>
{
    private static readonly Regex[] MessageSites =
    [
        new(@"(?:BusinessRuleException|NotFoundException)\(\s*\$?""((?:[^""\\]|\\.)*)"""),
        new(@"ApiResponse\.Fail\(\s*\$?""((?:[^""\\]|\\.)*)"""),
        new(@"WithMessage\(\s*\$?""((?:[^""\\]|\\.)*)"""),
    ];

    private static readonly Regex[] ControllerSites =
    [
        new(@"return\s+\$?""((?:[^""\\]|\\.)*)"";"),
        new(@"\?\s*\$?""((?:[^""\\]|\\.)*)""\s*:\s*null"),
    ];

    /// <summary>Replaces each {interpolation} with a sample value so the text can be matched against the catalog.</summary>
    private static string Flatten(string raw)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '{') { sb.Append(raw[i]); continue; }
            var depth = 1;
            while (++i < raw.Length && depth > 0)
            {
                if (raw[i] == '{') depth++;
                else if (raw[i] == '}') depth--;
            }
            i--;
            sb.Append('X');
        }
        return sb.ToString();
    }

    /// <summary>Every user-facing message literal in the server source.</summary>
    private static IEnumerable<string> SourceMessages([CallerFilePath] string here = "")
    {
        var src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src"));
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = file[src.Length..];
            if (rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || rel.Contains("Migrations") || file.EndsWith("MessageCatalog.cs")) continue;
            var text = File.ReadAllText(file);
            foreach (var re in MessageSites)
                foreach (Match m in re.Matches(text)) yield return Flatten(m.Groups[1].Value);
            if (file.EndsWith("Controllers.cs") || file.EndsWith("Controller.cs"))
                foreach (var re in ControllerSites)
                    foreach (Match m in re.Matches(text)) yield return Flatten(m.Groups[1].Value);
        }
    }

    [Fact]
    public void Every_server_message_has_an_Arabic_translation()
    {
        var messages = SourceMessages().Distinct().ToList();
        Assert.True(messages.Count > 100, $"scanner found only {messages.Count} messages; the patterns are out of date");
        var missing = messages.Where(m => MessageCatalog.TryArabic(m) is null).ToList();
        Assert.True(missing.Count == 0, "Add these to MessageCatalog:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Placeholders_are_carried_into_the_Arabic_text()
    {
        var ar = MessageCatalog.Localize("Insufficient stock for item KET-1 in warehouse MAIN: available 5, required 9.", "ar");
        Assert.Contains("KET-1", ar);
        Assert.Contains("MAIN", ar);
        Assert.Contains("رصيد غير كافٍ", ar);

        // a message wrapped around another one (cancel refused because stock was used) translates both parts
        var nested = MessageCatalog.Localize("This document cannot be cancelled because its stock has already been used. Insufficient stock for item A in warehouse B: available 1, required 2.", "ar");
        Assert.Contains("استُخدم بالفعل", nested);
        Assert.Contains("رصيد غير كافٍ", nested);

        Assert.Equal("Unknown thing.", MessageCatalog.Localize("Unknown thing.", "ar")); // unknown text passes through
        Assert.Equal("Not found.", MessageCatalog.Localize("Not found.", "en"));
    }

    private async Task<HttpClient> LoginAsync(string? lang)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        if (lang is not null) client.DefaultRequestHeaders.Add("X-Lang", lang);
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@shannana.local", password = "Admin#12345" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var csrf = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("csrfToken").GetString()!;
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
        return client;
    }

    private static async Task<JsonElement> PostAsync(HttpClient c, string url, object body, HttpStatusCode expect)
    {
        var res = await c.PostAsJsonAsync(url, body);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(res.StatusCode == expect, $"{url} -> {(int)res.StatusCode}: {json}");
        return json;
    }

    [Fact]
    public async Task Business_rule_failures_follow_the_callers_language()
    {
        var ar = await LoginAsync("ar");
        var en = await LoginAsync("en");
        var none = await LoginAsync(null);

        // an unknown driver/vehicle: a rule raised deep in the service layer
        var body = new { date = "2026-11-10", driverId = 999999, vehicleId = 999999, deliveryNoteIds = Array.Empty<int>() };
        Assert.Equal("السائق غير موجود أو معطّل.", (await PostAsync(ar, "/api/v1/trips", body, HttpStatusCode.BadRequest)).GetProperty("message").GetString());
        Assert.Equal("The driver does not exist or is inactive.", (await PostAsync(en, "/api/v1/trips", body, HttpStatusCode.BadRequest)).GetProperty("message").GetString());
        Assert.Equal("The driver does not exist or is inactive.", (await PostAsync(none, "/api/v1/trips", body, HttpStatusCode.BadRequest)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Validation_messages_and_field_names_are_translated_too()
    {
        var ar = await LoginAsync("ar");
        var bad = new { date = "2026-11-10", supplierId = 1, currencyId = 1, lines = new[] { new { itemId = 1, quantity = 0, unitPrice = 1 } } };

        var json = await PostAsync(ar, "/api/v1/purchaseorders", bad, HttpStatusCode.BadRequest);
        Assert.Equal("فشل التحقق من البيانات.", json.GetProperty("message").GetString());
        var all = string.Join(" ", json.GetProperty("errors").EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())));
        Assert.Contains("الكمية", all);              // the field name, not "Quantity"
        Assert.DoesNotContain("Quantity", all);
        Assert.DoesNotContain("must be", all);        // FluentValidation's own wording came out in Arabic

        // custom rule messages go through the catalog
        var empty = await PostAsync(ar, "/api/v1/purchaseorders", new { date = "2026-11-10", supplierId = 1, currencyId = 1, lines = Array.Empty<object>() }, HttpStatusCode.BadRequest);
        Assert.Contains("مطلوب بند واحد على الأقل.", empty.GetProperty("errors").GetRawText());
    }

    [Fact]
    public async Task Sign_in_failures_are_translated()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lang", "ar");
        var json = await PostAsync(client, "/api/v1/auth/login", new { email = "admin@shannana.local", password = "wrong" }, HttpStatusCode.Unauthorized);
        Assert.Equal("البريد الإلكتروني أو كلمة المرور غير صحيحة.", json.GetProperty("message").GetString());
    }
}
