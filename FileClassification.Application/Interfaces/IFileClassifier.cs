using FileClassification.Application.DTOs;

namespace FileClassification.Application.Interfaces;

public interface IFileClassifier
{
    Task<ClassificationResult> ClassifyAsync(Stream data, long totalBytes, IProgress<double>? progress = null, CancellationToken ct = default);
}
