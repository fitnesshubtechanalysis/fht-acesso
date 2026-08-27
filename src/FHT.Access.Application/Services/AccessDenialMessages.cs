using FHT.Access.Domain.Entities;

namespace FHT.Access.Application.Services;

public static class AccessDenialMessages
{
    public static string ForKiosk(AccessDecision decision)
    {
        if (decision.Allowed)
            return string.Empty;

        return string.IsNullOrWhiteSpace(decision.PublicMessage)
            ? AccessDecisionEvaluator.PublicReception
            : decision.PublicMessage;
    }
}
