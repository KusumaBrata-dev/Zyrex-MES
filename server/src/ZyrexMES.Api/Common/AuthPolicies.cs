using Microsoft.AspNetCore.Authorization;

namespace ZyrexMES.Api.Common;

public static class Roles
{
    public const string Operator = "Operator";
    public const string Leader = "Leader";
    public const string Qa = "Qa";
    public const string Supervisor = "Supervisor";
    public const string Admin = "Admin";
    /// <summary>Machine account role for station print agents (claim/ack print jobs).</summary>
    public const string Agent = "Agent";
    // Note: Agent is deliberately NOT in All — it is a machine role, not a human
    // production role; only the printing endpoints accept it.
    public static readonly string[] All = [Operator, Leader, Qa, Supervisor, Admin];
}

public static class EndpointRouteExtensions
{
    /// <summary>Allow only the given roles; anyone else gets 403.</summary>
    public static RouteHandlerBuilder RequireRoles(this RouteHandlerBuilder b, params string[] roles)
        => b.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', roles) });
}
