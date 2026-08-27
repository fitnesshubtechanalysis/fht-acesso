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
    public void Decide_Regular_ActiveAllowed()
    {
        var member = RegularMember();
        var d = _sut.Decide(member, score: 0.97);
        Assert.True(d.Allowed);
        Assert.Equal(AccessDecisionKind.AllowRegular, d.Kind);
        Assert.Equal(member.Id, d.MemberId);
        Assert.Equal(0.97, d.Score);
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
