using FileClassification.Application.Enums;
using FileClassification.Application.Queries.GetAllFiles;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Queries;

public class GetAllFilesQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_dto_per_record_in_order()
    {
        var files = new FileRecord[]
        {
            new() { Id = 1, FileName = "a.txt", State = FileState.Pending },
            new() { Id = 2, FileName = "b.txt", State = FileState.Processing },
            new() { Id = 3, FileName = "c.txt", State = FileState.Completed, Language = Language.English, Score = 88.5 },
        };
        var repo = Substitute.For<IFileRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(files);

        var result = await new GetAllFilesQueryHandler(repo).Handle(new GetAllFilesQuery(), default);

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("a.txt");
        result[2].Language.Should().Be(nameof(Language.English));
        result[2].Score.Should().Be(88.5);
    }

    [Fact]
    public async Task Handle_empty_returns_empty_list()
    {
        var repo = Substitute.For<IFileRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<FileRecord>());

        var result = await new GetAllFilesQueryHandler(repo).Handle(new GetAllFilesQuery(), default);

        result.Should().BeEmpty();
    }
}
