namespace PilotCapture.Domain;

public sealed class FastCaptureJob
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public CaptureJobType JobType { get; set; }
    public string SourceFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string? IndividualsFolder { get; set; }
    public string? CurrentTeamId { get; set; }
    public long StartedAtTicks { get; set; }
    public string? IgnoredSourceVersionsJson { get; set; }
}
public sealed class FastCaptureTeam
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public int CurrentPlayer { get; set; } = 1;
    public int SortOrder { get; set; }
    public string PerformanceKey { get; set; } = "";
    public string PerformanceLabel { get; set; } = "";
}
public sealed class FastCaptureImage
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public int PlayerNumber { get; set; }
    public int Sequence { get; set; }
    public string SourcePath { get; set; } = "";
    public long SourceTicks { get; set; }
    public long SourceLength { get; set; }
    public string StoredPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool IsRejected { get; set; }
    public bool Released { get; set; }
    public bool NeedsDelivery { get; set; } = true;
    public string? DeliveredPath { get; set; }
}

public sealed class FastCaptureRosterImport
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string SourceDataJson { get; set; } = "";
    public string MappingJson { get; set; } = "";
}
