namespace Atmos.Web.Configuration;

public sealed class RecentSearchOptions
{
    public const string SectionName = "RecentSearch";

    public int MaxPerSession { get; set; } = 10;
}
