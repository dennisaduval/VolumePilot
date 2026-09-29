using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VolumePilot.HQ.Api.Persistence;

public sealed class HqDbContext(
    DbContextOptions<HqDbContext> options,
    ITenantContext tenantContext) : IdentityUserContext<HqUser>(options)
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> DateTimeOffsetConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableDateTimeOffsetConverter = new(
        value => value.HasValue ? value.Value.UtcDateTime : null,
        value => value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null);

    public string? CurrentTenantId => tenantContext.TenantId;

    public DbSet<CompanyAccount> CompanyAccounts => Set<CompanyAccount>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<ClientOrganization> ClientOrganizations => Set<ClientOrganization>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CompanyAccount>(entity =>
        {
            entity.ToTable("company_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => CurrentTenantId != null && x.Id == CurrentTenantId);
        });

        modelBuilder.Entity<CompanyMembership>(entity =>
        {
            entity.ToTable("company_memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasOne<CompanyAccount>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HqUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<StaffInvitation>(entity =>
        {
            entity.ToTable("staff_invitations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(40).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.InvitedByUserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.NormalizedEmail });
            entity.HasOne<CompanyAccount>().WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HqUser>().WithMany().HasForeignKey(x => x.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ClientOrganization>(entity =>
        {
            entity.ToTable("client_organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.OrganizationType).HasMaxLength(80);
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.HasOne<CompanyAccount>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.ClientOrganizationId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.InternalReference).HasMaxLength(100);
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });
            entity.HasOne<CompanyAccount>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClientOrganization>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.ClientOrganizationId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TenantId, x.ClientOrganizationId, x.CreatedAtUtc });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.TenantId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.JobId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100);
            entity.Property(x => x.LocationName).HasMaxLength(200);
            entity.HasOne<CompanyAccount>()
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Job>()
                .WithMany()
                .HasForeignKey(x => new { x.TenantId, x.JobId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TenantId, x.JobId, x.StartsAtUtc });
            entity.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(DateTimeOffsetConverter);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(NullableDateTimeOffsetConverter);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantOwnership()
    {
        var tenantId = CurrentTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            if (ChangeTracker.Entries<ITenantOwned>().Any(entry => entry.State is not EntityState.Detached))
            {
                throw new InvalidOperationException("A tenant context is required to save tenant-owned data.");
            }

            if (ChangeTracker.Entries<CompanyAccount>().Any(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                throw new InvalidOperationException("A tenant context is required to save company data.");
            }

            return;
        }

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrWhiteSpace(entry.Entity.TenantId))
            {
                entry.Entity.TenantId = tenantId;
            }

            if ((entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                entry.Entity.TenantId != tenantId)
            {
                throw new InvalidOperationException("Tenant-owned data cannot be written outside the active tenant.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<CompanyAccount>())
        {
            if ((entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                entry.Entity.Id != tenantId)
            {
                throw new InvalidOperationException("Company data cannot be written outside the active tenant.");
            }
        }
    }
}
