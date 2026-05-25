using System.Data;
using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FileClassification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FileClassification.Infrastructure.Repositories;

#pragma warning disable CS0618 // NpgsqlLargeObjectManager is marked obsolete but still ships and works.

public class FileRepository(AppDbContext db, NpgsqlDataSource dataSource) : IFileRepository
{
    public async Task AddAsync(FileRecord file, Stream data, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var largeObjects = new NpgsqlLargeObjectManager(connection);

        var oid = await largeObjects.CreateAsync(0, ct);
        await using (var writer = await largeObjects.OpenReadWriteAsync(oid, ct))
            await data.CopyToAsync(writer, ct);

        file.DataOid = oid;
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Stream> OpenReadStreamAsync(uint oid, CancellationToken ct = default)
    {
        var connection = await dataSource.OpenConnectionAsync(ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct);
            var largeObjects = new NpgsqlLargeObjectManager(connection);
            var stream = await largeObjects.OpenReadAsync(oid, ct);
            return new LargeObjectReadStream(connection, transaction, stream);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public Task<FileRecord?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<FileRecord>> GetAllAsync(CancellationToken ct = default) =>
        await db.Files.AsNoTracking().OrderBy(f => f.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<FileRecord>> ClaimPendingAsync(string workerId, int count, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE "Files" f
            SET "State" = 'Processing', "WorkerId" = @worker, "UpdatedAt" = @now
            FROM (
                SELECT "Id" FROM "Files"
                WHERE "State" = 'Pending'
                ORDER BY "Id"
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            ) AS picked
            WHERE f."Id" = picked."Id"
            RETURNING f."Id", f."FileName", f."DataOid", f."SizeBytes", f."State",
                      f."Progress", f."WorkerId", f."Language", f."Score",
                      f."CreatedAt", f."UpdatedAt";
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worker", workerId);
        command.Parameters.AddWithValue("now", DateTime.UtcNow);
        command.Parameters.AddWithValue("limit", count);

        // Column order matches the RETURNING clause above.
        var results = new List<FileRecord>(count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FileRecord
            {
                Id = reader.GetInt32(0),
                FileName = reader.GetString(1),
                DataOid = reader.GetFieldValue<uint>(2),
                SizeBytes = reader.GetInt64(3),
                State = Enum.Parse<FileState>(reader.GetString(4)),
                Progress = reader.GetDouble(5),
                WorkerId = reader.IsDBNull(6) ? null : reader.GetString(6),
                Language = reader.IsDBNull(7) ? null : Enum.Parse<Language>(reader.GetString(7)),
                Score = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                CreatedAt = reader.GetDateTime(9),
                UpdatedAt = reader.GetDateTime(10),
            });
        }
        return results;
    }

    public async Task<bool> UpdateProgressAsync(int id, string workerId, double progress, CancellationToken ct = default) =>
        await db.Files
            .Where(f => f.Id == id && f.WorkerId == workerId && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Progress, progress)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct) > 0;

    public Task FinalizeAsync(int id, string workerId, FileState state, Language language, double? score, CancellationToken ct = default) =>
        db.Files
            .Where(f => f.Id == id && f.WorkerId == workerId && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Progress, 100.0)
                .SetProperty(f => f.State, state)
                .SetProperty(f => f.Language, language)
                .SetProperty(f => f.Score, score)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);

    public Task<int> ResetStalledAsync(DateTime cutoff, CancellationToken ct = default) =>
        db.Files
            .Where(f => f.State == FileState.Processing && f.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Pending)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct);

    public async Task<bool> CancelAsync(int id, CancellationToken ct = default) =>
        await db.Files
            .Where(f => f.Id == id && f.State == FileState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Inactive)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct) > 0;

    public async Task<bool> ResumeAsync(int id, CancellationToken ct = default) =>
        await db.Files
            .Where(f => f.Id == id && (f.State == FileState.Inactive || f.State == FileState.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.State, FileState.Pending)
                .SetProperty(f => f.WorkerId, (string?)null)
                .SetProperty(f => f.Progress, 0.0)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), ct) > 0;

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await new NpgsqlLargeObjectManager(connection).UnlinkAsync(file.DataOid, ct);
        await db.Files.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
