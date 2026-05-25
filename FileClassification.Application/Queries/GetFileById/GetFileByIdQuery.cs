using FileClassification.Application.DTOs;
using MediatR;

namespace FileClassification.Application.Queries.GetFileById;

public record GetFileByIdQuery(int Id) : IRequest<FileRecordDto?>;
