using FileClassification.Application.DTOs;
using MediatR;

namespace FileClassification.Application.Queries.GetAllFiles;

public record GetAllFilesQuery : IRequest<IReadOnlyList<FileRecordDto>>;
