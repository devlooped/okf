using Devlooped;

namespace Tests;

public class TrustSignalsTests
{
    [Fact]
    public void NormalizeVerified_treats_bare_mapping_as_list()
    {
        var doc = Parse("""
            ---
            type: Metric
            verified: { by: human:ahormati, at: 2026-06-25T09:00:00Z }
            ---
            Body
            """);

        var verified = TrustSignals.NormalizeVerified(doc.Frontmatter);
        var ev = Assert.Single(verified);
        Assert.Equal("human:ahormati", ev.By);
        Assert.Equal(DateTimeOffset.Parse("2026-06-25T09:00:00Z"), ev.At);
        Assert.Equal(TrustSignals.HumanReviewed, TrustSignals.TrustTier(verified));
    }

    [Fact]
    public void TrustTier_follows_spec()
    {
        Assert.Equal(TrustSignals.Unverified, TrustSignals.TrustTier([]));
        Assert.Equal(
            TrustSignals.MachineConfirmed,
            TrustSignals.TrustTier([new ActorEvent { By = "process:finance-nightly" }]));
        Assert.Equal(
            TrustSignals.HumanReviewed,
            TrustSignals.TrustTier(
            [
                new ActorEvent { By = "process:finance-nightly" },
                new ActorEvent { By = "human:ahormati" },
            ]));
    }

    [Fact]
    public void IsStale_is_inclusive_of_stale_after()
    {
        var today = new DateOnly(2026, 9, 23);
        var doc = Parse("""
            ---
            type: Metric
            stale_after: 2026-09-23
            ---
            Body
            """);

        Assert.True(TrustSignals.IsStale(doc.Frontmatter, today));

        var later = Parse("""
            ---
            type: Metric
            stale_after: 2026-09-24
            ---
            Body
            """);
        Assert.False(TrustSignals.IsStale(later.Frontmatter, today));
        Assert.False(TrustSignals.IsStale(Parse("""
            ---
            type: Metric
            ---
            Body
            """).Frontmatter, today));
    }

    [Fact]
    public void ParseCitations_reads_legacy_heading()
    {
        var sources = TrustSignals.ParseCitations("""
            # Definition
            A claim.

            # Citations
            [1] [Revenue policy](https://wiki.acme/finance/revenue)
            - https://wiki.acme/finance/cost
            """);

        Assert.NotNull(sources);
        Assert.Equal(2, sources!.Count);
        Assert.Equal("https://wiki.acme/finance/revenue", sources[0].Resource);
        Assert.Equal("Revenue policy", sources[0].Title);
        Assert.Equal("https://wiki.acme/finance/cost", sources[1].Resource);
    }

    [Fact]
    public void GraphProducer_uses_okf_format_version()
    {
        var at = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var ev = TrustSignals.GraphProducer(at);
        Assert.Equal("okf/0.2", ev.By);
        Assert.Equal(at, ev.At);
    }

    static OKFDocument Parse(string text)
    {
        Assert.True(OKFDocument.TryParse(text, out var document, out var error), error);
        return document!;
    }
}
