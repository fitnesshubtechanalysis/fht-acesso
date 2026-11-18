using FHT.Access.Application.Services;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Tests;

public class AccessDecisionServiceTests
{
    private readonly AccessDecisionService _sut = new(new AccessDecisionEvaluator());

    [Fact]
    public void Decide_NullMember_NotFound()
    {
        var d = _sut.Decide(null);
        Assert.False(d.Allowed);
        Assert.Equal(AccessDecisionService.ReasonMemberNotFound, d.ReasonCode);
    }

    [Fact]
    public void Decide_Blocked_DeniedSecurity()
    {
        var member = RegularMember();
        member.Status = MemberStatus.Blocked;
        var d = _sut.Decide(member);
        Assert.False(d.Allowed);
        Assert.Equal(AccessDecisionKind.DenySecurity, d.Kind);
    }

    [Fact]
    public void Decide_NoEnrollment_FirstTolerance()
    {
        var member = RegularMember();
        member.AccessAllowed = false;
        member.OperationalStatus = "no_enrollment";
        member.AccessStatus = "manual_review";
        var d = _sut.Decide(member);
        Assert.True(d.Allowed);
        Assert.Equal(AccessDecisionKind.AllowTolerance, d.Kind);
    }

    [Fact]
    public void Decide_ExpiredContract_DoesNotTurn_EvenIfCacheSaysAllow()
    {
        var member = RegularMember();
        member.OperationalStatus = "expired_recently";
        member.FinancialStatus = "no_receivables";
        member.AccessStatus = "blocked";
        member.AccessDecisionKind = AccessDecisionKind.AllowRegular.ToString();
        member.ValidUntil = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        var d = _sut.Decide(member);
        Assert.False(d.Allowed);
        Assert.False(d.AllowAutomaticRelease);
        Assert.Equal(AccessDecisionKind.RequireReception, d.Kind);
        Assert.Equal(ContractAccess.Reason, d.ReasonCode);
        Assert.Equal(ContractAccess.PublicMessage, d.PublicMessage);
    }

    [Fact]
    public void Decide_StaleAllowWithPastEndDate_DoesNotTurn()
    {
        var member = RegularMember();
        member.AccessDecisionKind = AccessDecisionKind.AllowTolerance.ToString();
        member.OccurrenceCauseCode = "expired_recently";
        member.ValidUntil = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var d = _sut.Decide(member);
        Assert.False(d.AllowAutomaticRelease);
        Assert.Equal(ContractAccess.Reason, d.ReasonCode);
    }

    [Fact]
    public void Decide_ManualRelease_StillOpensExpiredContract()
    {
        var member = RegularMember();
        member.OperationalStatus = "expired_recently";
        member.ValidUntil = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        var d = _sut.DecideManual(member, "Liberação da recepção");
        Assert.True(d.Allowed);
        Assert.Equal(AccessDecisionKind.AllowManual, d.Kind);
        Assert.True(d.AllowAutomaticRelease);
    }

    [Fact]
    public void Decide_Regular_ActiveAllowed()
    {
        var member = RegularMember();
        var d = _sut.Decide(member, score: 0.97);
        Assert.True(d.Allowed);
        Assert.Equal(AccessDecisionKind.AllowRegular, d.Kind);
        Assert.Equal(member.Id, d.MemberId);
        Assert.Equal(0.97, d.Score);
    }

    [Fact]
    public void Decide_Employee_SkipsPlanAndShowsStaffMessage()
    {
        var allowed = RegularMember();
        allowed.Name = "Ana Costa";
        allowed.PersonType = "employee";
        allowed.AccessAllowed = true;
        allowed.FinancialStatus = "overdue";
        allowed.OperationalStatus = "no_enrollment";

        var released = _sut.Decide(allowed);
        Assert.True(released.Allowed);
        Assert.Equal(AccessDecisionKind.AllowRegular, released.Kind);
        Assert.Contains("Funcionário / Profissional", released.PublicMessage);
        Assert.Contains("Ana Costa", released.PublicMessage);
        Assert.Contains("Acesso liberado", released.PublicMessage);
        Assert.DoesNotContain("plano", released.PublicMessage, StringComparison.OrdinalIgnoreCase);

        var blocked = RegularMember();
        blocked.PersonType = "employee";
        blocked.AccessAllowed = false;
        blocked.ReasonCode = "employee_inactive";
        var denied = _sut.Decide(blocked);
        Assert.False(denied.Allowed);
        Assert.Equal("Acesso de funcionário desabilitado. Procure a recepção.", denied.PublicMessage);
        Assert.Equal("employee_inactive", denied.ReasonCode);
    }

    private static Member RegularMember() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Member",
        Status = MemberStatus.Active,
        AccessAllowed = true,
        OperationalStatus = "active",
        FinancialStatus = "regular",
        AccessStatus = "allowed",
        ValidUntil = DateTime.UtcNow.AddDays(30),
        UpdatedAt = DateTime.UtcNow
    };
}
