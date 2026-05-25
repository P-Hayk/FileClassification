using FileClassification.Application.DTOs;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using MediatR;

namespace FileClassification.Application.Queries.GetFileById;

public class GetFileByIdQueryHandler(IFileRepository repository)
    : IRequestHandler<GetFileByIdQuery, FileRecordDto?>
{
    public async Task<FileRecordDto?> Handle(GetFileByIdQuery request, CancellationToken cancellationToken)
    {
        var file = await repository.GetByIdAsync(request.Id, cancellationToken);
        return file is null ? null : ToDto(file);
    }

    private static FileRecordDto ToDto(FileRecord f) =>
        new(f.Id, f.FileName, f.State.ToString(), f.Progress, f.WorkerId, f.SizeBytes, f.Language?.ToString(), f.Score, f.CreatedAt, f.UpdatedAt);
}
