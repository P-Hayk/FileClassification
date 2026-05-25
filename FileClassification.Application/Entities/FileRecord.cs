using FileClassification.Application.Enums;

namespace FileClassification.Entities;

public class FileRecord
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public uint DataOid { get; set; }
    public long SizeBytes { get; set; }
    public FileState State { get; set; } = FileState.Pending;
    public double Progress { get; set; }
    public string? WorkerId { get; set; }
    public Language? Language { get; set; }
    public double? Score { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
