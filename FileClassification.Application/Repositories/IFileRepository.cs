using FileClassification.Application.Enums;
using FileClassification.Entities;

namespace FileClassification.Application.Repositories;

public interface IFileRepository
{
    Task AddAsync(FileRecord file, Stream data, CancellationToken ct = default);
    Task<Stream> OpenReadStreamAsync(uint oid, CancellationToken ct = default);
    Task<FileRecord?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<FileRecord>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FileRecord>> ClaimPendingAsync(string workerId, int count, CancellationToken ct = default);
    Task<bool> UpdateProgressAsync(int id, string workerId, double progress, CancellationToken ct = default);
    Task FinalizeAsync(int id, string workerId, FileState state, Language language, double? score, CancellationToken ct = default);
    Task<int> ResetStalledAsync(DateTime cutoff, CancellationToken ct = default);
    Task<bool> CancelAsync(int id, CancellationToken ct = default);
    Task<bool> ResumeAsync(int id, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
