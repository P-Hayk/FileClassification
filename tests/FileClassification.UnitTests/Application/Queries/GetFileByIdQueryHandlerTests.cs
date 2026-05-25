using FileClassification.Application.Enums;
using FileClassification.Application.Queries.GetFileById;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Queries;

public class GetFileByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_existing_returns_dto()
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(new FileRecord { Id = 42, FileName = "x.txt", State = FileState.Pending });

        var result = await new GetFileByIdQueryHandler(repo).Handle(new GetFileByIdQuery(42), default);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.State.Should().Be(nameof(FileState.Pending));
    }

    [Fact]
    public async Task Handle_missing_returns_null()
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((FileRecord?)null);

        var result = await new GetFileByIdQueryHandler(repo).Handle(new GetFileByIdQuery(99), default);

        result.Should().BeNull();
    }
}
