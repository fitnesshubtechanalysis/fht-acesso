using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Moq;

namespace FHT.Access.Tests;

public sealed class PresenceServiceTests
{
    [Fact]
    public async Task Timeout_does_not_change_inside_to_outside_without_passage()
    {
        var personId = Guid.NewGuid();
        var unitId = "unit-1";
        var presence = new PersonPresenceState
        {
            PersonId = personId,
            UnitId = unitId,
            State = PresenceStateKind.Inside,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        var presenceRepo = new Mock<IPresenceRepository>();
        presenceRepo.Setup(r => r.GetAsync(personId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => presence);

        AccessAttemptRecord? attempt = null;
        var attemptRepo = new Mock<IAccessAttemptRepository>();
        attemptRepo.Setup(r => r.AddAsync(It.IsAny<AccessAttemptRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AccessAttemptRecord, CancellationToken>((a, _) => attempt = a)
            .Returns(Task.CompletedTask);
        attemptRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => attempt);
        attemptRepo.Setup(r => r.UpdateAsync(It.IsAny<AccessAttemptRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var visitRepo = new Mock<IVisitRepository>();
        var correctionRepo = new Mock<IPresenceCorrectionRepository>();
        var eventsRepo = new Mock<IAccessEventRepository>();
        eventsRepo.Setup(r => r.AddAsync(It.IsAny<AccessEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new PresenceService(
            presenceRepo.Object,
            attemptRepo.Object,
            visitRepo.Object,
            correctionRepo.Object,
            eventsRepo.Object)
        {
            EntryOnlyMode = false
        };

        var (allowed, _) = await svc.TryBeginRecognitionAsync(personId, unitId);
        Assert.True(allowed);

        var plan = await svc.PlanPassageAsync(personId, unitId, "face", null, null);
        Assert.NotNull(plan);
        Assert.Equal(AccessDirection.Exit, plan!.Direction);

        var (_, ev) = await svc.ConfirmPassageAsync(plan.AttemptId, passageConfirmed: false, "face", null);
        Assert.NotNull(ev);
        Assert.False(ev!.PassageConfirmed);
        Assert.Equal(PresenceStateKind.Inside, presence.State);
    }

    [Fact]
    public async Task Confirmed_entry_moves_outside_to_inside()
    {
        var personId = Guid.NewGuid();
        var unitId = "unit-1";
        var presence = new PersonPresenceState
        {
            PersonId = personId,
            UnitId = unitId,
            State = PresenceStateKind.Outside,
            Version = 0,
            UpdatedAt = DateTime.UtcNow
        };

        var presenceRepo = new Mock<IPresenceRepository>();
        presenceRepo.Setup(r => r.GetAsync(personId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => presence);
        presenceRepo.Setup(r => r.UpsertAsync(It.IsAny<PersonPresenceState>(), It.IsAny<CancellationToken>()))
            .Callback<PersonPresenceState, CancellationToken>((p, _) =>
            {
                presence.State = p.State;
                presence.ActiveVisitId = p.ActiveVisitId;
            })
            .Returns(Task.CompletedTask);

        AccessAttemptRecord? attempt = null;
        var attemptRepo = new Mock<IAccessAttemptRepository>();
        attemptRepo.Setup(r => r.AddAsync(It.IsAny<AccessAttemptRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AccessAttemptRecord, CancellationToken>((a, _) => attempt = a)
            .Returns(Task.CompletedTask);
        attemptRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => attempt);
        attemptRepo.Setup(r => r.UpdateAsync(It.IsAny<AccessAttemptRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var visitRepo = new Mock<IVisitRepository>();
        visitRepo.Setup(r => r.AddAsync(It.IsAny<VisitRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var correctionRepo = new Mock<IPresenceCorrectionRepository>();
        var eventsRepo = new Mock<IAccessEventRepository>();
        eventsRepo.Setup(r => r.AddAsync(It.IsAny<AccessEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new PresenceService(
            presenceRepo.Object,
            attemptRepo.Object,
            visitRepo.Object,
            correctionRepo.Object,
            eventsRepo.Object);

        await svc.TryBeginRecognitionAsync(personId, unitId);
        var plan = await svc.PlanPassageAsync(personId, unitId, "face", null, null);
        Assert.NotNull(plan);

        var (visitId, ev) = await svc.ConfirmPassageAsync(plan!.AttemptId, true, "face", null);
        Assert.NotNull(ev);
        Assert.True(ev!.PassageConfirmed);
        Assert.NotNull(visitId);
        Assert.Equal(PresenceStateKind.Inside, presence.State);
    }
}
