using FileClassification.Application.Commands.CancelFile;
using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Commands;

public class CancelFileCommandHandlerTests
{
    private static IFileRepository RepoWith(FileRecord? file)
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(file);
        repo.CancelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        return repo;
    }

    [Fact]
    public async Task Handle_missing_file_returns_not_found()
    {
        var handler = new CancelFileCommandHandler(RepoWith(null));

        var result = await handler.Handle(new CancelFileCommand(1), default);

        result.Should().Be(FileOperationResult.NotFound);
    }

    [Theory]
    [InlineData(FileState.Pending)]
    [InlineData(FileState.Completed)]
    [InlineData(FileState.Failed)]
    [InlineData(FileState.Inactive)]
    public async Task Handle_non_processing_returns_invalid_state(FileState state)
    {
        var handler = new CancelFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = state }));

        var result = await handler.Handle(new CancelFileCommand(1), default);

        result.Should().Be(FileOperationResult.InvalidState);
    }

    [Fact]
    public async Task Handle_processing_file_returns_success()
    {
        var handler = new CancelFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = FileState.Processing }));

        var result = await handler.Handle(new CancelFileCommand(1), default);

        result.Should().Be(FileOperationResult.Success);
    }
}
