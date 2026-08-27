using Atmos.Core.Conversions;

namespace Atmos.Core.Tests.Conversions;

public class LatinNameExtractorTests
{
    [Fact]
    public void Extract_returns_null_for_null_or_empty()
    {
        Assert.Null(LatinNameExtractor.Extract(null));
        Assert.Null(LatinNameExtractor.Extract(""));
    }

    [Fact]
    public void Extract_returns_plain_latin_name_unchanged()
    {
        Assert.Equal("Boulder", LatinNameExtractor.Extract("Boulder"));
    }

    [Fact]
    public void Extract_pulls_leading_latin_run_from_osm_dual_script_pattern()
    {
        // OSM's common "Latin Name  Local-script Name" concatenation pattern.
        var result = LatinNameExtractor.Extract("Lhasa 拉萨");

        Assert.Equal("Lhasa", result);
    }

    [Fact]
    public void Extract_rejects_names_with_no_usable_latin_portion()
    {
        Assert.Null(LatinNameExtractor.Extract("拉萨"));
    }

    [Fact]
    public void Extract_rejects_single_character_results()
    {
        Assert.Null(LatinNameExtractor.Extract("A"));
    }

    [Fact]
    public void Extract_allows_hyphenated_and_apostrophed_names()
    {
        Assert.Equal("Coeur d'Alene", LatinNameExtractor.Extract("Coeur d'Alene"));
    }
}
