using FileClassification.Application.Enums;
using MediatR;

namespace FileClassification.Application.Commands.ResumeFile;

public record ResumeFileCommand(int Id) : IRequest<FileOperationResult>;
