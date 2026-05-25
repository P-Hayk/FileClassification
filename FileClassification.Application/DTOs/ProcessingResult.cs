using FileClassification.Application.Enums;

namespace FileClassification.Application.DTOs;

public record ProcessingResult(FileState FinalState, Language Language, double? Score);
