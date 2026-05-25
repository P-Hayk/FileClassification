namespace FileClassification.Application.DTOs;

public record FileRecordDto(
    int Id,
    string FileName,
    string State,
    double Progress,
    string? WorkerId,
    long SizeBytes,
    string? Language,
    double? Score,
    DateTime CreatedAt,
    DateTime UpdatedAt);
