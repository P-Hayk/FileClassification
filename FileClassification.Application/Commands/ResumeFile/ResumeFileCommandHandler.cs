using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using MediatR;

namespace FileClassification.Application.Commands.ResumeFile;

public class ResumeFileCommandHandler(IFileRepository repository) : IRequestHandler<ResumeFileCommand, FileOperationResult>
{
    public async Task<FileOperationResult> Handle(ResumeFileCommand request, CancellationToken cancellationToken)
    {
        var file = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (file is null) return FileOperationResult.NotFound;
        if (file.State is not (FileState.Inactive or FileState.Failed))
            return FileOperationResult.InvalidState;

        await repository.ResumeAsync(request.Id, cancellationToken);
        return FileOperationResult.Success;
    }
}
