using HelpMotivateMe.SaaS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpMotivateMe.SaaS.Api.Persistence;

public sealed class ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantBackup> Backups => Set<TenantBackup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.HasKey(item => item.Id);
            tenant.HasIndex(item => item.Subdomain).IsUnique();
            tenant.Property(item => item.OwnerEmail).HasMaxLength(254);
            tenant.Property(item => item.Subdomain).HasMaxLength(63);
            tenant.Property(item => item.Username).HasMaxLength(100);
            tenant.Property(item => item.ContainerId).HasMaxLength(128);
            tenant.Property(item => item.VolumeName).HasMaxLength(128);
            tenant.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            tenant.Property(item => item.Plan).HasConversion<string>().HasMaxLength(16);
            tenant.Property(item => item.AiMode).HasConversion<string>().HasMaxLength(32);
            tenant.Property(item => item.AiProvider).HasMaxLength(50);
            tenant.Property(item => item.AiModel).HasMaxLength(100);
        });

        modelBuilder.Entity<TenantBackup>(backup =>
        {
            backup.HasKey(item => item.Id);
            backup.HasIndex(item => new { item.TenantId, item.CreatedUtc });
            backup.Property(item => item.FileName).HasMaxLength(128);
            backup.HasOne<Tenant>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}