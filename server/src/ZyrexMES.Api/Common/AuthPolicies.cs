using Microsoft.AspNetCore.Authorization;

namespace ZyrexMES.Api.Common;

public static class Roles
{
    public const string Operator = "Operator";
    public const string Leader = "Leader";
    public const string Qa = "Qa";
    public const string Supervisor = "Supervisor";
    public const string Admin = "Admin";
    public static readonly string[] All = [Operator, Leader, Qa, Supervisor, Admin];
}

public static class EndpointRouteExtensions
{
    /// <summary>Allow only the given roles; anyone else gets 403.</summary>
    public static RouteHandlerBuilder RequireRoles(this RouteHandlerBuilder b, params string[] roles)
        => b.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', roles) });
}
