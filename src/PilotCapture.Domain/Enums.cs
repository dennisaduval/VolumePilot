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
    Accepted = 1,
    Rejected = 2
}

public enum ImageAssetState
{
    Staged = 0,
    Available = 1,
    Missing = 2,
    Failed = 3
}


public enum CaptureJobType
{
    TeamAndIndividual = 0,
    PrintOnSiteTeamsOnly = 1,
    PrintOnSiteTeamAndIndividual = 2,
    ActionPhotography = 3
}
