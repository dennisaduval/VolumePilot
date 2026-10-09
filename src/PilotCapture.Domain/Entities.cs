namespace PilotCapture.Domain;

public sealed class Event
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = string.Empty;
    public DateOnly? EventDate { get; set; }
    public CaptureJobType JobType { get; set; } = CaptureJobType.TeamAndIndividual;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class Group
{
    public string Id { get; set; } = Ids.New();
    public string EventId { get; set; } = string.Empty;
    public Event? Event { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Subject
{
    public string Id { get; set; } = Ids.New();
    public string EventId { get; set; } = string.Empty;
    public Event? Event { get; set; }
    public SubjectIdentityStatus IdentityStatus { get; set; } = SubjectIdentityStatus.Known;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}

public sealed class Membership
{
    public string Id { get; set; } = Ids.New();
    public string SubjectId { get; set; } = string.Empty;
    public Subject? Subject { get; set; }
    public string GroupId { get; set; } = string.Empty;
    public Group? Group { get; set; }
    public string? RosterImportId { get; set; }
    public RosterImport? SourceImport { get; set; }
    public string? RosterNumber { get; set; }
    public string? Role { get; set; }
    public string? SpaDataJson { get; set; }
    public string? SourceRowKey { get; set; }
    public string? SourceDataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Photographer
{
    public string Id { get; set; } = Ids.New();
    public string DisplayName { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LocalInstallation
{
    // Created once at first run and retained for the lifetime of this local installation.
    public string InstallationId { get; private set; } = Ids.New();
    public string StationCode { get; set; } = "s10";
    public string? SmartShooterOutputPath { get; set; }
    public string? MasterMediaPath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public static LocalInstallation Create(string stationCode, DateTimeOffset createdAtUtc)
    {
        if (!StationCodes.IsSupported(stationCode))
            throw new ArgumentOutOfRangeException(nameof(stationCode), stationCode, "Unsupported capture station code.");

        return new LocalInstallation { StationCode = stationCode, CreatedAtUtc = createdAtUtc };
    }
}

public sealed class CaptureProfile
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = string.Empty;
    public CaptureWorkflowType WorkflowType { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CaptureSession
{
    public string Id { get; set; } = Ids.New();
    public string EventId { get; set; } = string.Empty;
    public Event? Event { get; set; }
    public string CaptureProfileId { get; set; } = string.Empty;
    public CaptureProfile? CaptureProfile { get; set; }
    public string PhotographerId { get; set; } = string.Empty;
    public Photographer? Photographer { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string StationCode { get; set; } = "s10";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public ICollection<CaptureSet> CaptureSets { get; set; } = new List<CaptureSet>();
}

public sealed class CaptureSet
{
    public string Id { get; set; } = Ids.New();
    public string CaptureSessionId { get; set; } = string.Empty;
    public CaptureSession? CaptureSession { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public Subject? Subject { get; set; }
    public string? MembershipId { get; set; }
    public Membership? Membership { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ICollection<CaptureImage> Images { get; set; } = new List<CaptureImage>();
}

public sealed class CaptureImage
{
    public string Id { get; set; } = Ids.New();
    public string CaptureSetId { get; set; } = string.Empty;
    public CaptureSet? CaptureSet { get; set; }
    public string ImageAssetId { get; set; } = string.Empty;
    public ImageAsset? ImageAsset { get; set; }
    public CaptureImageReviewState ReviewState { get; set; } = CaptureImageReviewState.Pending;
    public string SelectionScopeId { get; set; } = string.Empty;
    public bool IsSecondary { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsBanner { get; set; }
    public int SequenceNumber { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}

public sealed class ImageAsset
{
    public string Id { get; set; } = Ids.New();
    public string RelativePath { get; set; } = string.Empty;
    public string? EditedPath { get; set; }
    public string? EditedSha256 { get; set; }
    public DateTimeOffset? EditedAtUtc { get; set; }
    public string? SourcePath { get; set; }
    public DateTimeOffset? SourceLastWriteUtc { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = "image/jpeg";
    public long ByteLength { get; set; }
    public string? Sha256 { get; set; }
    public int? PixelWidth { get; set; }
    public int? PixelHeight { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public ImageAssetState State { get; set; } = ImageAssetState.Staged;
}

public sealed class RosterImport
{
    public string Id { get; set; } = Ids.New();
    public string EventId { get; set; } = string.Empty;
    public Event? Event { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
    public int RowsRead { get; set; }
    public int RowsImported { get; set; }
    public int RowsSkipped { get; set; }
}

/// <summary>Preserves every source record, including rows skipped during subject or membership creation.</summary>
public sealed class RosterImportRow
{
    public string Id { get; set; } = Ids.New();
    public string RosterImportId { get; set; } = string.Empty;
    public RosterImport? RosterImport { get; set; }
    public int SourceRecordNumber { get; set; }
    public string? MembershipId { get; set; }
    public Membership? Membership { get; set; }
    public string SourceDataJson { get; set; } = string.Empty;
}

public sealed class AuditEntry
{
    public string Id { get; set; } = Ids.New();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string StationCode { get; set; } = "s10";
    public string? CorrelationId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

