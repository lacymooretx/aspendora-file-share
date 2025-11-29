using AspendoraFileShare.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AspendoraFileShare.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<ShareLink> ShareLinks { get; set; } = null!;
    public DbSet<FileModel> Files { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasMany(e => e.ShareLinks)
                  .WithOne(e => e.User)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.AuditLogs)
                  .WithOne(e => e.User)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ShareLink configuration
        modelBuilder.Entity<ShareLink>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ShortId).IsUnique();
            entity.HasMany(e => e.Files)
                  .WithOne(e => e.ShareLink)
                  .HasForeignKey(e => e.ShareLinkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // FileModel configuration
        modelBuilder.Entity<FileModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("Files");
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
