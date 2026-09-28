using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class PilotCaptureDbContext(DbContextOptions<PilotCaptureDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Photographer> Photographers => Set<Photographer>();
    public DbSet<LocalInstallation> LocalInstallations => Set<LocalInstallation>();
    public DbSet<CaptureProfile> CaptureProfiles => Set<CaptureProfile>();
    public DbSet<CaptureSession> CaptureSessions => Set<CaptureSession>();
    public DbSet<CaptureSet> CaptureSets => Set<CaptureSet>();
    public DbSet<CaptureImage> CaptureImages => Set<CaptureImage>();
    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();
    public DbSet<RosterImport> RosterImports => Set<RosterImport>();
    public DbSet<RosterImportRow> RosterImportRows => Set<RosterImportRow>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ExternalKey).HasMaxLength(200);
            entity.HasIndex(x => new { x.EventId, x.Name });
            entity.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Subject>(entity =>
        {
            entity.ToTable("subjects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.IdentityStatus).HasConversion<int>();
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.DisplayName).HasMaxLength(250).IsRequired();
            entity.Property(x => x.ExternalKey).HasMaxLength(200);
            entity.HasIndex(x => new { x.EventId, x.ExternalKey }).IsUnique();
            entity.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.RosterNumber).HasMaxLength(50);
            entity.Property(x => x.Role).HasMaxLength(100);
            entity.Property(x => x.SourceRowKey).HasMaxLength(200);
            entity.Property(x => x.SourceDataJson);
            entity.HasIndex(x => new { x.SubjectId, x.GroupId }).IsUnique();
            entity.HasAlternateKey(x => new { x.Id, x.SubjectId });
            entity.HasOne(x => x.Subject).WithMany(x => x.Memberships).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SourceImport).WithMany().HasForeignKey(x => x.RosterImportId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Photographer>(entity =>
        {
            entity.ToTable("photographers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ExternalKey).HasMaxLength(200);
        });

        modelBuilder.Entity<LocalInstallation>(entity =>
        {
            entity.ToTable("local_installation");
            entity.HasKey(x => x.InstallationId);
            entity.Property(x => x.InstallationId).HasMaxLength(26);
            entity.Property(x => x.StationCode).HasMaxLength(3).IsRequired();
        });

        modelBuilder.Entity<CaptureProfile>(entity =>
        {
            entity.ToTable("capture_profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.WorkflowType).HasConversion<int>();
        });

        modelBuilder.Entity<CaptureSession>(entity =>
        {
            entity.ToTable("capture_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.InstallationId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.StationCode).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.EventId, x.StartedAtUtc });
            entity.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CaptureProfile).WithMany().HasForeignKey(x => x.CaptureProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Photographer).WithMany().HasForeignKey(x => x.PhotographerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LocalInstallation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CaptureSet>(entity =>
        {
            entity.ToTable("capture_sets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.HasIndex(x => new { x.CaptureSessionId, x.SubjectId });
            entity.HasOne(x => x.CaptureSession).WithMany(x => x.CaptureSets).HasForeignKey(x => x.CaptureSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Membership).WithMany()
                .HasForeignKey(x => new { x.MembershipId, x.SubjectId })
                .HasPrincipalKey(x => new { x.Id, x.SubjectId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CaptureImage>(entity =>
        {
            entity.ToTable("capture_images");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.ReviewState).HasConversion<int>();
            entity.HasIndex(x => new { x.CaptureSetId, x.SequenceNumber }).IsUnique();
            entity.HasOne(x => x.CaptureSet).WithMany(x => x.Images).HasForeignKey(x => x.CaptureSetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ImageAsset).WithOne().HasForeignKey<CaptureImage>(x => x.ImageAssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImageAsset>(entity =>
        {
            entity.ToTable("image_assets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.MediaType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64);
            entity.Property(x => x.State).HasConversion<int>();
            entity.HasIndex(x => x.RelativePath).IsUnique();
            entity.HasIndex(x => x.Sha256);
        });

        modelBuilder.Entity<RosterImport>(entity =>
        {
            entity.ToTable("roster_imports");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.SourceFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.SourceSha256).HasMaxLength(64).IsRequired();
            entity.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RosterImportRow>(entity =>
        {
            entity.ToTable("roster_import_rows");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.SourceDataJson).IsRequired();
            entity.HasIndex(x => new { x.RosterImportId, x.SourceRecordNumber }).IsUnique();
            entity.HasIndex(x => x.MembershipId);
            entity.HasOne(x => x.RosterImport).WithMany().HasForeignKey(x => x.RosterImportId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Membership).WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(26);
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.InstallationId).HasMaxLength(26).IsRequired();
            entity.Property(x => x.StationCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(26);
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAtUtc });
            entity.HasIndex(x => x.CorrelationId);
            entity.HasOne<LocalInstallation>().WithMany().HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var result = new System.Text.StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (char.IsUpper(current) && index > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }
}
