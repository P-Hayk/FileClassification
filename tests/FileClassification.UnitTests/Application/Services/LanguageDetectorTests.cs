using System.Text;
using FileClassification.Application.Enums;
using FileClassification.Application.Services;
using FileClassification.Application.Settings;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileClassification.UnitTests.Application.Services;

public class LanguageDetectorTests
{
    private static LanguageDetector NewDetector(double minRatio = 0.1) =>
        new(Options.Create(new LanguageDetectorSettings { MinLanguageRatio = minRatio }));

    private static (MemoryStream stream, long length) AsStream(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return (new MemoryStream(bytes), bytes.Length);
    }

    [Fact]
    public async Task Classify_armenian_dominant_returns_armenian()
    {
        var (stream, length) = AsStream(new string('ա', 90) + new string('1', 10));

        var result = await NewDetector().ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.Armenian);
        result.Score.Should().Be(100.0);
    }

    [Fact]
    public async Task Classify_russian_dominant_returns_russian()
    {
        var (stream, length) = AsStream(new string('а', 90) + new string('1', 10));

        var result = await NewDetector().ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.Russian);
        result.Score.Should().Be(100.0);
    }

    [Fact]
    public async Task Classify_english_dominant_returns_english()
    {
        var (stream, length) = AsStream(new string('a', 90) + new string('1', 10));

        var result = await NewDetector().ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.English);
        result.Score.Should().Be(100.0);
    }

    [Fact]
    public async Task Classify_empty_stream_returns_unknown()
    {
        var (stream, length) = AsStream(string.Empty);

        var result = await NewDetector().ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.Unknown);
        result.Score.Should().Be(0);
    }

    [Fact]
    public async Task Classify_below_min_ratio_returns_unknown()
    {
        // 1 letter out of 21 chars = ~4.7%, below the 50% threshold
        var (stream, length) = AsStream("a" + new string('1', 20));

        var result = await NewDetector(minRatio: 0.5).ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.Unknown);
    }

    [Fact]
    public async Task Classify_mixed_picks_dominant_and_reports_its_share()
    {
        // 60 Armenian + 30 Russian + 10 English → Armenian wins with 60/100 classified chars
        var (stream, length) = AsStream(new string('ա', 60) + new string('а', 30) + new string('a', 10));

        var result = await NewDetector().ClassifyAsync(stream, length);

        result.Language.Should().Be(Language.Armenian);
        result.Score.Should().Be(60.0);
    }

    [Fact]
    public async Task Classify_handles_utf8_bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(new string('a', 50)))
            .ToArray();

        using var stream = new MemoryStream(bytes);
        var result = await NewDetector().ClassifyAsync(stream, bytes.Length);

        result.Language.Should().Be(Language.English);
    }

    [Fact]
    public async Task Classify_large_input_reports_progress()
    {
        // 200k chars crosses several 16K-buffer boundaries
        var bytes = Encoding.UTF8.GetBytes(new string('a', 200_000));
        using var stream = new MemoryStream(bytes);

        var reports = new List<double>();
        await NewDetector().ClassifyAsync(stream, bytes.Length, new Progress<double>(reports.Add));

        await Task.Delay(50); // Progress<T> dispatches to the thread pool

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(value => value >= 0 && value <= 100);
    }
}
