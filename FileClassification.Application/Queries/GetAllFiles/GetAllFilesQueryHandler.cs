using FileClassification.Application.DTOs;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using MediatR;

namespace FileClassification.Application.Queries.GetAllFiles;

public class GetAllFilesQueryHandler(IFileRepository repository)
    : IRequestHandler<GetAllFilesQuery, IReadOnlyList<FileRecordDto>>
{
    public async Task<IReadOnlyList<FileRecordDto>> Handle(GetAllFilesQuery request, CancellationToken cancellationToken)
    {
        var files = await repository.GetAllAsync(cancellationToken);
        return files.Select(ToDto).ToList();
    }

    private static FileRecordDto ToDto(FileRecord f) =>
        new(f.Id, f.FileName, f.State.ToString(), f.Progress, f.WorkerId, f.SizeBytes, f.Language?.ToString(), f.Score, f.CreatedAt, f.UpdatedAt);
}
