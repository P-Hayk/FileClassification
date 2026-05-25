using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using MediatR;

namespace FileClassification.Application.Commands.CancelFile;

public class CancelFileCommandHandler(IFileRepository repository) : IRequestHandler<CancelFileCommand, FileOperationResult>
{
    public async Task<FileOperationResult> Handle(CancelFileCommand request, CancellationToken cancellationToken)
    {
        var file = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (file is null) return FileOperationResult.NotFound;
        if (file.State != FileState.Processing) return FileOperationResult.InvalidState;

        await repository.CancelAsync(request.Id, cancellationToken);
        return FileOperationResult.Success;
    }
}
