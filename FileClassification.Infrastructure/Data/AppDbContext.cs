using FileClassification.Application.Enums;
using FileClassification.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileClassification.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<FileRecord> Files { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.State)
                  .HasConversion<string>()
                  .HasDefaultValue(FileState.Pending);
            entity.Property(e => e.FileName).IsRequired();
            entity.Property(e => e.DataOid).HasColumnType("oid");
            entity.Property(e => e.Language).HasConversion<string>();
        });
    }
}
