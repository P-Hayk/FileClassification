using FileClassification.Application.DTOs;
using MediatR;

namespace FileClassification.Application.Commands.UploadFile;

public record UploadFileCommand(string FileName, long SizeBytes, Stream Data) : IRequest<UploadFileResult>;
