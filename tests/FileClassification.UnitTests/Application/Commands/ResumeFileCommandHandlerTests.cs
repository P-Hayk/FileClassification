using FileClassification.Application.Commands.ResumeFile;
using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Commands;

public class ResumeFileCommandHandlerTests
{
    private static IFileRepository RepoWith(FileRecord? file)
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(file);
        repo.ResumeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        return repo;
    }

    [Fact]
    public async Task Handle_missing_file_returns_not_found()
    {
        var handler = new ResumeFileCommandHandler(RepoWith(null));

        var result = await handler.Handle(new ResumeFileCommand(1), default);

        result.Should().Be(FileOperationResult.NotFound);
    }

    [Theory]
    [InlineData(FileState.Pending)]
    [InlineData(FileState.Processing)]
    [InlineData(FileState.Completed)]
    public async Task Handle_invalid_states_returns_invalid_state(FileState state)
    {
        var handler = new ResumeFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = state }));

        var result = await handler.Handle(new ResumeFileCommand(1), default);

        result.Should().Be(FileOperationResult.InvalidState);
    }

    [Theory]
    [InlineData(FileState.Inactive)]
    [InlineData(FileState.Failed)]
    public async Task Handle_resumable_states_returns_success(FileState state)
    {
        var handler = new ResumeFileCommandHandler(RepoWith(new FileRecord { Id = 1, State = state }));

        var result = await handler.Handle(new ResumeFileCommand(1), default);

        result.Should().Be(FileOperationResult.Success);
    }
}
