using FileClassification.Application.Commands.DeleteFile;
using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Commands;

public class DeleteFileCommandHandlerTests
{
    private static IFileRepository RepoWith(FileRecord? file)
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(file);
        repo.DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        return repo;
    }

    [Fact]
    public async Task Handle_missing_file_returns_not_found()
    {
        var handler = new DeleteFileCommandHandler(RepoWith(null));

        var result = await handler.Handle(new DeleteFileCommand(1), default);

        result.Should().Be(FileOperationResult.NotFound);
    }

    [Fact]
    public async Task Handle_processing_file_blocks_delete()
    {
        var handler = new DeleteFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = FileState.Processing }));

        var result = await handler.Handle(new DeleteFileCommand(1), default);

        result.Should().Be(FileOperationResult.InvalidState);
    }

    [Theory]
    [InlineData(FileState.Pending)]
    [InlineData(FileState.Completed)]
    [InlineData(FileState.Failed)]
    [InlineData(FileState.Inactive)]
    public async Task Handle_any_non_processing_returns_success(FileState state)
    {
        var handler = new DeleteFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = state }));

        var result = await handler.Handle(new DeleteFileCommand(1), default);

        result.Should().Be(FileOperationResult.Success);
    }
}
