using FileClassification.Application.Enums;
using FileClassification.Application.Repositories;
using FileClassification.Entities;
using FileClassification.Infrastructure.Data;
using FileClassification.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FileClassification.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("fileclassification")
            .Build();

        await _container.StartAsync();
        _dataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dataSource)
            .Options;
        return new AppDbContext(options);
    }

    public IFileRepository NewRepository(AppDbContext db) => new FileRepository(db, _dataSource);

    public async Task ResetAsync()
    {
        await using var db = NewContext();
        // Unlink all LOs first; can't TRUNCATE because we need each row's oid
        var rows = await db.Files.AsNoTracking().Select(f => f.DataOid).ToListAsync();
        if (rows.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var oid in rows)
        {
            await using var cmd = new NpgsqlCommand("SELECT lo_unlink($1)", conn, tx);
            cmd.Parameters.AddWithValue((long)oid);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();

        await db.Files.ExecuteDeleteAsync();
    }

    public async Task<FileRecord> SeedAsync(string content = "hello world", FileState state = FileState.Pending)
    {
        await using var db = NewContext();
        var record = new FileRecord { FileName = "seed.txt", SizeBytes = content.Length };
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await NewRepository(db).AddAsync(record, stream);

        if (state != FileState.Pending)
        {
            await db.Files.Where(f => f.Id == record.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.State, state));
        }
        return record;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
