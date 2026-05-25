using FileClassification.Application.Commands.UploadFile;
using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileClassification.UnitTests.Application.Commands;

public class UploadFileCommandHandlerTests
{
    [Fact]
    public async Task Handle_valid_file_returns_upload_result_in_pending_state()
    {
        var repo = Substitute.For<IFileRepository>();
        var handler = new UploadFileCommandHandler(repo);

        var result = await handler.Handle(new UploadFileCommand("doc.txt", 2048, Stream.Null), CancellationToken.None);

        result.FileName.Should().Be("doc.txt");
        result.State.Should().Be(nameof(FileState.Pending));
    }

    [Fact]
    public async Task Handle_calls_repository_with_matching_record()
    {
        var repo = Substitute.For<IFileRepository>();
        var handler = new UploadFileCommandHandler(repo);

        await handler.Handle(new UploadFileCommand("doc.txt", 2048, Stream.Null), CancellationToken.None);

        await repo.Received(1).AddAsync(
            Arg.Is<FileRecord>(f => f.FileName == "doc.txt" && f.SizeBytes == 2048),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }
}
