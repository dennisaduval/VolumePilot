using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.Capture;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class CaptureWorkflowService(PilotCaptureDbContext dbContext) : ICaptureWorkflowService
{
    public async Task<string> GetStationCodeAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.LocalInstallations.AsNoTracking().SingleAsync(cancellationToken)).StationCode;

    public async Task SetStationCodeAsync(string stationCode, CancellationToken cancellationToken = default)
    {
        if (!StationCodes.IsSupported(stationCode))
            throw new ArgumentOutOfRangeException(nameof(stationCode), stationCode, "Unsupported capture station code.");
        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);
        if (await dbContext.CaptureSessions.AnyAsync(
                session => session.InstallationId == installation.InstallationId && session.EndedAtUtc == null,
                cancellationToken))
            throw new InvalidOperationException("End the active capture session before changing the station code.");
        installation.StationCode = stationCode;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CaptureEventChoice>> GetEventsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Events.AsNoTracking()
            .Where(captureEvent => !captureEvent.IsArchived)
            .OrderByDescending(captureEvent => captureEvent.CreatedAtUtc)
            .Select(captureEvent => new CaptureEventChoice(captureEvent.Id, captureEvent.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CaptureGroupChoice>> GetGroupsAsync(string eventId, CancellationToken cancellationToken = default) =>
        await dbContext.Groups.AsNoTracking()
            .Where(group => group.EventId == eventId && group.IsActive)
            .OrderBy(group => group.Name)
            .Select(group => new CaptureGroupChoice(group.Id, group.EventId, group.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CaptureSubjectChoice>> GetSubjectsAsync(string groupId, CancellationToken cancellationToken = default) =>
        await dbContext.Memberships.AsNoTracking()
            .Where(membership => membership.GroupId == groupId && membership.IsActive && membership.Subject!.IsActive)
            .OrderBy(membership => membership.Subject!.LastName)
            .ThenBy(membership => membership.Subject!.FirstName)
            .Select(membership => new CaptureSubjectChoice(
                membership.SubjectId,
                membership.Id,
                membership.Subject!.DisplayName,
                membership.RosterNumber,
                membership.Role))
            .ToListAsync(cancellationToken);

    public async Task<ActiveCaptureSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        var installation = await dbContext.LocalInstallations.AsNoTracking().SingleAsync(cancellationToken);
        var activeSession = await dbContext.CaptureSessions.AsNoTracking()
            .Where(session => session.InstallationId == installation.InstallationId && session.EndedAtUtc == null)
            .OrderByDescending(session => session.StartedAtUtc)
            .Select(session => new ActiveCaptureSession(
                session.Id,
                session.EventId,
                session.Event!.Name,
                session.Photographer!.DisplayName,
                session.CaptureProfile!.Name,
                session.StationCode,
                session.StartedAtUtc,
                null))
            .FirstOrDefaultAsync(cancellationToken);
        if (activeSession is null)
            return null;

        var currentSet = await dbContext.CaptureSets.AsNoTracking()
            .Where(set => set.CaptureSessionId == activeSession.Id)
            .OrderByDescending(set => set.StartedAtUtc)
            .Select(set => new CaptureSetResult(set.Id, set.Subject!.DisplayName,
                set.Subject!.IdentityStatus == SubjectIdentityStatus.Unidentified,
                set.Membership == null ? null : set.Membership!.GroupId,
                set.MembershipId))
            .FirstOrDefaultAsync(cancellationToken);
        return activeSession with { CurrentCaptureSet = currentSet };
    }

    public async Task<ActiveCaptureSession> StartSessionAsync(
        string eventId,
        string photographerName,
        CaptureWorkflowType workflowType,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(workflowType))
            throw new ArgumentOutOfRangeException(nameof(workflowType), workflowType, "Unsupported capture workflow type.");
        if (string.IsNullOrWhiteSpace(photographerName))
            throw new ArgumentException("Enter a photographer name before starting a session.", nameof(photographerName));
        if (photographerName.Trim().Length > 200)
            throw new ArgumentException("Photographer names can be at most 200 characters.", nameof(photographerName));

        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);
        if (await dbContext.CaptureSessions.AnyAsync(
                session => session.InstallationId == installation.InstallationId && session.EndedAtUtc == null,
                cancellationToken))
            throw new InvalidOperationException("This station already has an active capture session. Resume it or end it first.");

        var captureEvent = await dbContext.Events.SingleOrDefaultAsync(
            captureEvent => captureEvent.Id == eventId && !captureEvent.IsArchived,
            cancellationToken)
            ?? throw new InvalidOperationException("The selected event is unavailable.");

        var photographerNameTrimmed = photographerName.Trim();
        var photographer = (await dbContext.Photographers.ToListAsync(cancellationToken))
            .FirstOrDefault(item => item.DisplayName.Equals(photographerNameTrimmed, StringComparison.OrdinalIgnoreCase));
        if (photographer is null)
        {
            photographer = new Photographer { DisplayName = photographerNameTrimmed, CreatedAtUtc = DateTimeOffset.UtcNow };
            dbContext.Photographers.Add(photographer);
        }

        var profile = await dbContext.CaptureProfiles.FirstOrDefaultAsync(
            item => item.IsActive && item.WorkflowType == workflowType,
            cancellationToken);
        if (profile is null)
        {
            profile = new CaptureProfile
            {
                Name = workflowType == CaptureWorkflowType.Portrait ? "Portrait" : "Action",
                WorkflowType = workflowType,
                Description = "Default Pilot Capture workflow profile",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.CaptureProfiles.Add(profile);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var session = new CaptureSession
        {
            EventId = captureEvent.Id,
            CaptureProfileId = profile.Id,
            PhotographerId = photographer.Id,
            InstallationId = installation.InstallationId,
            StationCode = installation.StationCode,
            StartedAtUtc = startedAt
        };
        dbContext.CaptureSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ActiveCaptureSession(
            session.Id,
            captureEvent.Id,
            captureEvent.Name,
            photographer.DisplayName,
            profile.Name,
            installation.StationCode,
            startedAt,
            null);
    }

    public async Task<CaptureSetResult> SelectSubjectAsync(
        string sessionId,
        string membershipId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.CaptureSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("The active capture session was not found.");
        var membership = await dbContext.Memberships.Include(item => item.Subject).SingleOrDefaultAsync(
            item => item.Id == membershipId && item.IsActive && item.Subject!.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("The selected roster membership was not found.");
        if (membership.Subject!.EventId != session.EventId)
            throw new InvalidOperationException("The selected subject belongs to another event.");

        var subjectName = membership.Subject!.DisplayName;
        var existingSet = await dbContext.CaptureSets.AsNoTracking()
            .Where(item => item.CaptureSessionId == session.Id
                && item.SubjectId == membership.SubjectId
                && item.MembershipId == membership.Id)
            .Select(item => new CaptureSetResult(item.Id, subjectName, false, membership.GroupId, membership.Id))
            .FirstOrDefaultAsync(cancellationToken);
        if (existingSet is not null)
            return existingSet;

        var captureSet = new CaptureSet
        {
            CaptureSessionId = session.Id,
            SubjectId = membership.SubjectId,
            MembershipId = membership.Id,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.CaptureSets.Add(captureSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CaptureSetResult(captureSet.Id, subjectName, false, membership.GroupId, membership.Id);
    }

    public async Task<CaptureSetResult> CreateUnidentifiedSubjectAsync(
        string sessionId,
        string displayName,
        string? groupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Enter a temporary subject label before selecting an unidentified subject.", nameof(displayName));
        if (displayName.Trim().Length > 250)
            throw new ArgumentException("Subject labels can be at most 250 characters.", nameof(displayName));

        var session = await dbContext.CaptureSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("The active capture session was not found.");
        Group? group = null;
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            group = await dbContext.Groups.SingleOrDefaultAsync(
                item => item.Id == groupId && item.EventId == session.EventId && item.IsActive,
                cancellationToken)
                ?? throw new InvalidOperationException("The selected group is unavailable for this event.");
        }

        var now = DateTimeOffset.UtcNow;
        var subject = new Subject
        {
            EventId = session.EventId,
            IdentityStatus = SubjectIdentityStatus.Unidentified,
            DisplayName = displayName.Trim(),
            CreatedAtUtc = now
        };
        dbContext.Subjects.Add(subject);
        Membership? membership = null;
        if (group is not null)
        {
            membership = new Membership
            {
                SubjectId = subject.Id,
                GroupId = group.Id,
                CreatedAtUtc = now
            };
            dbContext.Memberships.Add(membership);
        }

        var captureSet = new CaptureSet
        {
            CaptureSessionId = session.Id,
            SubjectId = subject.Id,
            MembershipId = membership?.Id,
            StartedAtUtc = now
        };
        dbContext.CaptureSets.Add(captureSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CaptureSetResult(captureSet.Id, subject.DisplayName, true, group?.Id, membership?.Id);
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.CaptureSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("The active capture session was not found.");
        session.EndedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
