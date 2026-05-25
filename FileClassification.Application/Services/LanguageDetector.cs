using System.Text;
using FileClassification.Application.DTOs;
using FileClassification.Application.Enums;
using FileClassification.Application.Interfaces;
using FileClassification.Application.Settings;
using Microsoft.Extensions.Options;

namespace FileClassification.Application.Services;

public class LanguageDetector(IOptions<LanguageDetectorSettings> options) : IFileClassifier
{
    private const int BufferChars = 16 * 1024;

    private readonly double _minLanguageRatio = options.Value.MinLanguageRatio;

    public async Task<ClassificationResult> ClassifyAsync(
        Stream data, long totalBytes, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var reader = new StreamReader(data, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);

        var buffer = new char[BufferChars];
        long armenian = 0, russian = 0, english = 0, totalChars = 0;
        double lastReportedPercent = -1;

        int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
        {
            foreach (var c in buffer.AsSpan(0, read))
            {
                if (IsEnglish(c)) english++;
                else if (IsRussian(c)) russian++;
                else if (IsArmenian(c)) armenian++;
            }

            totalChars += read;

            if (progress is null || totalBytes <= 0) continue;
            var percent = Math.Round(reader.BaseStream.Position * 100.0 / totalBytes, 1);
            
            if (percent > lastReportedPercent)
            {
                progress.Report(percent);
                lastReportedPercent = percent;
            }
        }

        var classified = armenian + russian + english;
        if (classified == 0 || (double)classified / totalChars < _minLanguageRatio)
            return new ClassificationResult(Language.Unknown, 0);

        var counts = new (Language Language, long Count)[]
        {
            (Language.English, english),
            (Language.Russian, russian),
            (Language.Armenian, armenian),
        };
        var top = counts.MaxBy(x => x.Count);

        return new ClassificationResult(top.Language, Math.Round(top.Count * 100.0 / classified, 2));
    }

    private static bool IsEnglish(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsRussian(char c) =>
        (c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё';

    private static bool IsArmenian(char c) =>
        (c >= 'Ա' && c <= 'Ֆ') || (c >= 'ա' && c <= 'և');
}
