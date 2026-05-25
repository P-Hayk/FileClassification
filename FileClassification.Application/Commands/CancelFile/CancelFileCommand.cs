using FileClassification.Application.Enums;
using MediatR;

namespace FileClassification.Application.Commands.CancelFile;

public record CancelFileCommand(int Id) : IRequest<FileOperationResult>;
