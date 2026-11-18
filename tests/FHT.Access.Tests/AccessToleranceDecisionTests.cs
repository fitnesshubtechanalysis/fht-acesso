using FHT.Access.Application.Services;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Tests;

public sealed class AccessToleranceDecisionTests
{
    private static Member RegularMember() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Regular",
        Status = MemberStatus.Active,
        AccessAllowed = true,
        OperationalStatus = "active",
        FinancialStatus = "regular",
        AccessStatus = "allowed",
        UpdatedAt = DateTime.UtcNow
    };

    private static Member NoPlanMember() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Sem Plano",
        Status = MemberStatus.Inactive,
        AccessAllowed = false,
        OperationalStatus = "no_enrollment",
        FinancialStatus = "no_receivables",
        AccessStatus = "manual_review",
        ReasonCode = "no_active_enrollment",
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Regular_does_not_create_tolerance_path()
    {
        var ev = new AccessDecisionEvaluator();
        var d = ev.Evaluate(RegularMember());
        Assert.Equal(AccessDecisionKind.AllowRegular, d.Kind);
        Assert.True(d.Allowed);
        Assert.False(d.ConsumeToleranceOnPassage);
    }

    [Fact]
    public void First_eligible_gets_tolerance()
    {
        var ev = new AccessDecisionEvaluator();
        var d = ev.Evaluate(NoPlanMember());
        Assert.Equal(AccessDecisionKind.AllowTolerance, d.Kind);
        Assert.Equal(AccessDecisionEvaluator.PublicTolerance, d.PublicMessage);
        Assert.True(d.ConsumeToleranceOnPassage);
    }

    [Fact]
    public void Second_attempt_requires_reception()
    {
        var m = NoPlanMember();
        m.ToleranceUsed = true;
        m.ToleranceOccurrenceId = Guid.NewGuid();
        m.OccurrenceCauseCode = "no_active_enrollment";

        var ev = new AccessDecisionEvaluator();
        var d = ev.Evaluate(m);
        Assert.Equal(AccessDecisionKind.RequireReception, d.Kind);
        Assert.False(d.Allowed);
        Assert.Equal(AccessDecisionEvaluator.PublicReception, d.PublicMessage);
    }

    [Fact]
    public void Public_message_never_mentions_debt()
    {
        var m = new Member
        {
            Id = Guid.NewGuid(),
            Name = "Devedor",
            Status = MemberStatus.Active,
            AccessAllowed = true,
            OperationalStatus = "active",
            FinancialStatus = "overdue",
            AccessStatus = "manual_review",
            UpdatedAt = DateTime.UtcNow
        };
        var d = new AccessDecisionEvaluator().Evaluate(m);
        Assert.DoesNotContain("inadimpl", d.PublicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dívida", d.PublicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cobrança", d.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_contract_gets_no_tolerance()
    {
        var m = new Member
        {
            Id = Guid.NewGuid(),
            Name = "Eunice",
            Status = MemberStatus.Active,
            AccessAllowed = false,
            OperationalStatus = "expired_recently",
            FinancialStatus = "no_receivables",
            AccessStatus = "blocked",
            ReasonCode = "expired_recently",
            ValidUntil = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = DateTime.UtcNow
        };
        var d = new AccessDecisionEvaluator().Evaluate(m);
        Assert.Equal(AccessDecisionKind.RequireReception, d.Kind);
        Assert.False(d.Allowed);
        Assert.Equal(ContractAccess.PublicMessage, d.PublicMessage);
        Assert.Equal(ContractAccess.Reason, d.ReasonCode);
    }

    [Fact]
    public void End_date_includes_that_civil_day()
    {
        var end = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var stillValid = new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc);
        var nextDay = new DateTime(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);
        Assert.False(ContractAccess.Ended(end, stillValid));
        Assert.True(ContractAccess.Ended(end, nextDay));
    }

    [Fact]
    public void Suspended_denied_without_tolerance()
    {
        var m = new Member
        {
            Id = Guid.NewGuid(),
            Name = "Suspenso",
            Status = MemberStatus.Active,
            OperationalStatus = "suspended",
            FinancialStatus = "regular",
            AccessStatus = "blocked",
            UpdatedAt = DateTime.UtcNow
        };
        var d = new AccessDecisionEvaluator().Evaluate(m);
        Assert.Equal(AccessDecisionKind.DenyAdministrative, d.Kind);
        Assert.False(d.Allowed);
    }
}
