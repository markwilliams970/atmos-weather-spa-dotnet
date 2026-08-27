namespace Atmos.Web.Configuration;

public sealed class SessionCookieOptions
{
    public const string SectionName = "SessionCookie";

    public string Name { get; set; } = "sid";
    public int MaxAgeDays { get; set; } = 365;
}
