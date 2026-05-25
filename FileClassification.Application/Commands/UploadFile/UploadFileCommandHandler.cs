using FileClassification.Application.DTOs;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using MediatR;

namespace FileClassification.Application.Commands.UploadFile;

public class UploadFileCommandHandler(IFileRepository repository)
    : IRequestHandler<UploadFileCommand, UploadFileResult>
{
    public async Task<UploadFileResult> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var file = new FileRecord { FileName = request.FileName, SizeBytes = request.SizeBytes };
        await repository.AddAsync(file, request.Data, cancellationToken);
        return new UploadFileResult(file.Id, file.FileName, file.State.ToString());
    }
}
