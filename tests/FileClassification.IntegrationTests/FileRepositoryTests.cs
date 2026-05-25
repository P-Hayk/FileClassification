using System.Text;
using FileClassification.Application.Enums;
using FileClassification.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FileClassification.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class FileRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Add_then_open_round_trips_content()
    {
        const string payload = "The quick brown fox jumps over the lazy dog";
        await using var db = pg.NewContext();
        var repo = pg.NewRepository(db);
        var record = new FileRecord { FileName = "rt.txt", SizeBytes = payload.Length };

        await using (var input = new MemoryStream(Encoding.UTF8.GetBytes(payload)))
            await repo.AddAsync(record, input);

        record.Id.Should().BeGreaterThan(0);
        record.DataOid.Should().BeGreaterThan(0u);

        await using var stream = await repo.OpenReadStreamAsync(record.DataOid);
        using var reader = new StreamReader(stream);
        var read = await reader.ReadToEndAsync();

        read.Should().Be(payload);
    }

    [Fact]
    public async Task GetAll_orders_by_id()
    {
        await pg.SeedAsync("first");
        await pg.SeedAsync("second");
        await pg.SeedAsync("third");

        await using var db = pg.NewContext();
        var all = await pg.NewRepository(db).GetAllAsync();

        all.Select(f => f.Id).Should().BeInAscendingOrder();
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task Cancel_only_works_on_processing()
    {
        var pending = await pg.SeedAsync(state: FileState.Pending);
        var processing = await pg.SeedAsync(state: FileState.Processing);

        await using var db = pg.NewContext();
        var repo = pg.NewRepository(db);

        (await repo.CancelAsync(pending.Id)).Should().BeFalse();
        (await repo.CancelAsync(processing.Id)).Should().BeTrue();

        var after = await repo.GetByIdAsync(processing.Id);
        after!.State.Should().Be(FileState.Inactive);
        after.Progress.Should().Be(0);
    }

    [Theory]
    [InlineData(FileState.Inactive)]
    [InlineData(FileState.Failed)]
    public async Task Resume_accepts_inactive_and_failed(FileState from)
    {
        var file = await pg.SeedAsync(state: from);

        await using var db = pg.NewContext();
        var repo = pg.NewRepository(db);

        (await repo.ResumeAsync(file.Id)).Should().BeTrue();

        var after = await repo.GetByIdAsync(file.Id);
        after!.State.Should().Be(FileState.Pending);
        after.WorkerId.Should().BeNull();
        after.Progress.Should().Be(0);
    }

    [Theory]
    [InlineData(FileState.Pending)]
    [InlineData(FileState.Processing)]
    [InlineData(FileState.Completed)]
    public async Task Resume_rejects_other_states(FileState from)
    {
        var file = await pg.SeedAsync(state: from);
        await using var db = pg.NewContext();

        (await pg.NewRepository(db).ResumeAsync(file.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_row_and_unlinks_large_object()
    {
        var file = await pg.SeedAsync("payload");

        await using var db = pg.NewContext();
        var repo = pg.NewRepository(db);

        (await repo.DeleteAsync(file.Id)).Should().BeTrue();
        (await repo.GetByIdAsync(file.Id)).Should().BeNull();

        // The LO must be gone — opening it should throw
        var act = async () => await (await repo.OpenReadStreamAsync(file.DataOid)).DisposeAsync();
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ResetStalled_only_touches_old_processing_rows()
    {
        var fresh = await pg.SeedAsync(state: FileState.Processing);
        var stale = await pg.SeedAsync(state: FileState.Processing);
        var notProcessing = await pg.SeedAsync(state: FileState.Completed);

        await using var db = pg.NewContext();
        // Backdate `stale` so it's "older" than the cutoff we'll pass
        await db.Files.Where(f => f.Id == stale.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.UpdatedAt, DateTime.UtcNow.AddMinutes(-5)));

        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        var reset = await pg.NewRepository(db).ResetStalledAsync(cutoff);

        reset.Should().Be(1);
        (await pg.NewRepository(db).GetByIdAsync(stale.Id))!.State.Should().Be(FileState.Pending);
        (await pg.NewRepository(db).GetByIdAsync(fresh.Id))!.State.Should().Be(FileState.Processing);
        (await pg.NewRepository(db).GetByIdAsync(notProcessing.Id))!.State.Should().Be(FileState.Completed);
    }
}
