using FileClassification.Application.Enums;

namespace FileClassification.Application.DTOs;

public record ClassificationResult(Language Language, double Score);
