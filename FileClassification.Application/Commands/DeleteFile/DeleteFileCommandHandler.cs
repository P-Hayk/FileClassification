using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using MediatR;

namespace FileClassification.Application.Commands.DeleteFile;

public class DeleteFileCommandHandler(IFileRepository repository) : IRequestHandler<DeleteFileCommand, FileOperationResult>
{
    public async Task<FileOperationResult> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var file = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (file is null) 
            return FileOperationResult.NotFound;
        
        if (file.State == FileState.Processing) 
            return FileOperationResult.InvalidState;

        await repository.DeleteAsync(request.Id, cancellationToken);
        return FileOperationResult.Success;
    }
}
