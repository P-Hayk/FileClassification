using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FileClassification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FileClassification.Infrastructure.Repositories;

public class FileRepository(AppDbContext db) : IFileRepository
{
    public async Task AddAsync(FileRecord file, Stream data, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var lo = new NpgsqlLargeObjectManager(conn);

        var oid = await lo.CreateAsync(0, ct);
        var loStream = await lo.OpenReadWriteAsync(oid, ct);
        await data.CopyToAsync(loStream, ct);
        await loStream.DisposeAsync();  // close LO before SaveChanges touches the connection

        file.DataOid = oid;
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<Stream> OpenReadStreamAsync(uint oid, CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        var tx = await conn.BeginTransactionAsync(ct);
        var lo = new NpgsqlLargeObjectManager(conn);
        var loStream = await lo.OpenReadAsync(oid, ct);
        return new LargeObjectReadStream(conn, tx, loStream);
    }

    public Task<FileRecord?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task<IReadOnlyList<FileRecord>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Files.AsNoTracking()
            .OrderBy(f => f.Id)
            .Select(f => new FileRecord
            {
                Id = f.Id,
                FileName = f.FileName,
                SizeBytes = f.SizeBytes,
                State = f.State,
                Progress = f.Progress,
                WorkerId = f.WorkerId,
                Language = f.Language,
                Score = f.Score,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FileRecord>> ClaimPendingAsync(string workerId, int count, CancellationToken ct = default)
    {
        var pending = await db.Files
            .Where(f => f.State == FileState.Pending)
            .OrderBy(f => f.Id)
            .Take(count)
            .ToListAsync(ct);

        var claimed = new List<FileRecord>();
        foreach (var file in pending)
        {
            // WHERE Id = @id AND State = 'Pending' ensures only one worker wins the race.
            var affected = await db.Files
                .Where(f => f.Id == file.Id && f.State == FileState.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.State, FileState.Processing)
                    .SetProperty(f => f.WorkerId, workerId)
                    .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);

            if (affected > 0) claimed.Add(file);
        }
        return claimed;
    }

    public async Task<bool> UpdateProgressAsync(int id, string workerId, double progress, CancellationToken ct = default)
    {
        var affected = await db.Files
            .Where(f => f.Id == id && f.WorkerId == workerId && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Progress, progress)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);
        return affected > 0;
    }

    public Task FinalizeAsync(int id, string workerId, FileState state, Language language, double? score, CancellationToken ct = default)
    {
        return db.Files
            .Where(f => f.Id == id && f.WorkerId == workerId && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Progress, 100.0)
                .SetProperty(f => f.State, state)
                .SetProperty(f => f.Language, language)
                .SetProperty(f => f.Score, score)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);
    }

    public Task<int> ResetStalledAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return db.Files
            .Where(f => f.State == FileState.Processing && f.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Pending)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<bool> CancelAsync(int id, CancellationToken ct = default)
    {
        var affected = await db.Files
            .Where(f => f.Id == id && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Inactive)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);
        return affected > 0;
    }

    public async Task<bool> ResumeAsync(int id, CancellationToken ct = default)
    {
        var affected = await db.Files
            .Where(f => f.Id == id && f.State == FileState.Inactive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Pending)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var lo = new NpgsqlLargeObjectManager(conn);
        await lo.UnlinkAsync(file.DataOid, ct);

        await db.Files.Where(f => f.Id == id).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
        return true;
    }
}