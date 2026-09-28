namespace PilotCapture.Domain;

public enum SubjectIdentityStatus
{
    Known = 0,
    Unidentified = 1
}

public enum CaptureWorkflowType
{
    Portrait = 0,
    Action = 1
}

public enum CaptureImageReviewState
{
    Pending = 0,
    Primary = 1,
    Banner = 2,
    Rejected = 3
}

public enum ImageAssetState
{
    Staged = 0,
    Available = 1,
    Missing = 2,
    Failed = 3
}
