namespace Erp.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "erp";
    public string Audience { get; set; } = "erp-web";
    public int ExpiryMinutes { get; set; } = 480;
}
