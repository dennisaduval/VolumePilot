namespace PilotCapture.Domain;

public sealed class ActionChangeover
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public long UtcTicks { get; set; }
}

public sealed class ActionCamera
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string CameraKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public long LastSeenUtcTicks { get; set; }
    public long LastPhotoUtcTicks { get; set; }
    public string Issue { get; set; } = "Clock verification needed";
}

public sealed class ActionClockSample
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string CameraKey { get; set; } = "";
    public long FromUtcTicks { get; set; }
    public long UntilUtcTicks { get; set; }
    public long OffsetTicks { get; set; }
    public long UncertaintyTicks { get; set; }
    public string VerificationPhotoKey { get; set; } = "";
}

public sealed class ActionPhotoMetadata
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string PhotoKey { get; set; } = "";
    public string CameraKey { get; set; } = "";
    public string PathKey { get; set; } = "";
    public long? CameraTicks { get; set; }
    public string Origin { get; set; } = "";
    public string Location { get; set; } = "";
    public long FirstSeenUtcTicks { get; set; }
    public bool HasConflict { get; set; }
}

public sealed class ActionReceipt
{
    public string Id { get; set; } = Ids.New();
    public string JobId { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string PathKey { get; set; } = "";
    public long SourceTicks { get; set; }
    public long SourceLength { get; set; }
    public string StoredPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string? PhotoKey { get; set; }
    public string? CameraKey { get; set; }
    public long? CameraTicks { get; set; }
    public long? CorrectedUtcTicks { get; set; }
    public string? ClockSampleId { get; set; }
    public string? TeamId { get; set; }
    public string State { get; set; } = "Held";
    public string Issue { get; set; } = "Waiting for SS metadata";
}
