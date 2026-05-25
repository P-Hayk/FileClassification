using FileClassification.Application.Enums;
using MediatR;

namespace FileClassification.Application.Commands.DeleteFile;

public record DeleteFileCommand(int Id) : IRequest<FileOperationResult>;
