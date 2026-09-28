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

    public async Task<string?> GetSmartShooterOutputPathAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.LocalInstallations.AsNoTracking().SingleAsync(cancellationToken)).SmartShooterOutputPath;

    public async Task SetSmartShooterOutputPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Choose Smart Shooter's JPEG output folder.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Smart Shooter output folder '{fullPath}' does not exist.");
        if (fullPath.Length > 2000)
            throw new ArgumentException("The output folder path is too long.", nameof(path));
        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);
        installation.SmartShooterOutputPath = fullPath;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CaptureEventChoice>> GetEventsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Events.AsNoTracking()
            .Where(captureEvent => !captureEvent.IsArchived)
            .OrderByDescending(captureEvent => captureEvent.Id)
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
            .ThenBy(membership => membership.Id)
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
            .OrderByDescending(session => session.Id)
            .Select(session => new ActiveCaptureSession(
                session.Id,
                session.EventId,
                session.Event!.Name,
                session.Photographer!.DisplayName,
                session.CaptureProfile!.Name,
                session.CaptureProfile.WorkflowType,
                session.StationCode,
                session.StartedAtUtc,
                null))
            .FirstOrDefaultAsync(cancellationToken);
        if (activeSession is null)
            return null;

        var currentSet = await dbContext.CaptureSets.AsNoTracking()
            .Where(set => set.CaptureSessionId == activeSession.Id && set.CompletedAtUtc == null)
            .OrderByDescending(set => set.Id)
            .Select(set => new CaptureSetResult(set.Id, set.Subject!.DisplayName,
                set.Subject!.IdentityStatus == SubjectIdentityStatus.Unidentified,
                set.Membership == null ? null : set.Membership!.GroupId,
                set.MembershipId,
                set.StartedAtUtc))
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
            workflowType,
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
        var startedAt = DateTimeOffset.UtcNow;
        await CompleteCurrentCaptureSetAsync(session.Id, startedAt, cancellationToken);

        var captureSet = new CaptureSet
        {
            CaptureSessionId = session.Id,
            SubjectId = membership.SubjectId,
            MembershipId = membership.Id,
            StartedAtUtc = startedAt
        };
        dbContext.CaptureSets.Add(captureSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CaptureSetResult(captureSet.Id, subjectName, false, membership.GroupId, membership.Id, startedAt);
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
        await CompleteCurrentCaptureSetAsync(session.Id, now, cancellationToken);
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
        return new CaptureSetResult(captureSet.Id, subject.DisplayName, true, group?.Id, membership?.Id, now);
    }

    public async Task<CaptureSetResult?> NextSubjectAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.CaptureSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("The active capture session was not found.");
        var currentSet = await dbContext.CaptureSets.SingleOrDefaultAsync(
            item => item.CaptureSessionId == session.Id && item.CompletedAtUtc == null,
            cancellationToken);
        if (currentSet is null)
            return null;

        CaptureSubjectChoice? nextSubject = null;
        string? groupId = null;
        if (currentSet.MembershipId is not null)
        {
            var currentMembership = await dbContext.Memberships.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == currentSet.MembershipId,
                cancellationToken);
            if (currentMembership is not null)
            {
                groupId = currentMembership.GroupId;
                var groupSubjects = await GetSubjectsAsync(currentMembership.GroupId, cancellationToken);
                var currentIndex = -1;
                for (var index = 0; index < groupSubjects.Count; index++)
                {
                    if (groupSubjects[index].MembershipId == currentMembership.Id)
                    {
                        currentIndex = index;
                        break;
                    }
                }
                if (currentIndex >= 0 && currentIndex + 1 < groupSubjects.Count)
                    nextSubject = groupSubjects[currentIndex + 1];
            }
        }

        var now = DateTimeOffset.UtcNow;
        currentSet.CompletedAtUtc = now;
        if (nextSubject is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var nextCaptureSet = new CaptureSet
        {
            CaptureSessionId = session.Id,
            SubjectId = nextSubject.SubjectId,
            MembershipId = nextSubject.MembershipId,
            StartedAtUtc = now
        };
        dbContext.CaptureSets.Add(nextCaptureSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CaptureSetResult(
            nextCaptureSet.Id,
            nextSubject.DisplayName,
            false,
            groupId,
            nextSubject.MembershipId,
            now);
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.CaptureSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("The active capture session was not found.");
        var endedAt = DateTimeOffset.UtcNow;
        await CompleteCurrentCaptureSetAsync(session.Id, endedAt, cancellationToken);
        session.EndedAtUtc = endedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteCurrentCaptureSetAsync(
        string sessionId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var currentSets = await dbContext.CaptureSets
            .Where(set => set.CaptureSessionId == sessionId && set.CompletedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var set in currentSets)
            set.CompletedAtUtc = completedAtUtc;
    }
}
