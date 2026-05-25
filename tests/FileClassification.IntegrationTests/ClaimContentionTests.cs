using FileClassification.Application.Enums;
using FluentAssertions;
using Xunit;

namespace FileClassification.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ClaimContentionTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Claim_returns_empty_when_nothing_pending()
    {
        await pg.SeedAsync(state: FileState.Completed);
        await pg.SeedAsync(state: FileState.Failed);

        await using var db = pg.NewContext();
        var claimed = await pg.NewRepository(db).ClaimPendingAsync("worker-1", count: 5);

        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_marks_rows_processing_and_assigns_worker()
    {
        await pg.SeedAsync(); await pg.SeedAsync(); await pg.SeedAsync();

        await using var db = pg.NewContext();
        var repo = pg.NewRepository(db);
        var claimed = await repo.ClaimPendingAsync("worker-A", count: 2);

        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(f => f.State == FileState.Processing && f.WorkerId == "worker-A");

        var leftover = (await repo.GetAllAsync()).Where(f => !claimed.Any(c => c.Id == f.Id)).ToList();
        leftover.Should().HaveCount(1);
        leftover[0].State.Should().Be(FileState.Pending);
    }

    [Fact]
    public async Task Concurrent_claims_partition_rows_without_overlap()
    {
        // 10 pending rows, two workers grab 5 each simultaneously
        for (var i = 0; i < 10; i++) await pg.SeedAsync($"file-{i}");

        // Separate contexts (one per "worker") to simulate independent processes
        await using var ctxA = pg.NewContext();
        await using var ctxB = pg.NewContext();
        var repoA = pg.NewRepository(ctxA);
        var repoB = pg.NewRepository(ctxB);

        var taskA = repoA.ClaimPendingAsync("worker-A", count: 5);
        var taskB = repoB.ClaimPendingAsync("worker-B", count: 5);
        var (claimedA, claimedB) = (await taskA, await taskB);

        var idsA = claimedA.Select(f => f.Id).ToHashSet();
        var idsB = claimedB.Select(f => f.Id).ToHashSet();

        idsA.Intersect(idsB).Should().BeEmpty("SKIP LOCKED must prevent double-claim");
        (idsA.Count + idsB.Count).Should().Be(10);

        await using var verifyCtx = pg.NewContext();
        var all = await pg.NewRepository(verifyCtx).GetAllAsync();
        all.Should().OnlyContain(f => f.State == FileState.Processing);
    }

    [Fact]
    public async Task Concurrent_claims_with_more_workers_than_rows_does_not_block()
    {
        for (var i = 0; i < 3; i++) await pg.SeedAsync();

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            await using var ctx = pg.NewContext();
            return await pg.NewRepository(ctx).ClaimPendingAsync($"worker-{i}", count: 3);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var totalClaimed = results.Sum(r => r.Count);

        totalClaimed.Should().Be(3, "five workers should collectively claim exactly the three available rows");
        results.SelectMany(r => r).Select(f => f.Id).Distinct().Count().Should().Be(3);
    }
}
