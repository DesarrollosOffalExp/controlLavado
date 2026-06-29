using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ControlLavados.Auth;

/// <summary>
/// Autenticación de DESARROLLO: simula al usuario indicado para poder probar la app
/// localmente sin Entra ID. Nunca se usa en producción.
/// El email se puede cambiar con la variable DEV_USER (default: el admin).
/// </summary>
public class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Dev";

    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Environment.GetEnvironmentVariable("DEV_USER") ?? "roberto.sanabria@offal.com.ar";
        var claims = new[]
        {
            new Claim("preferred_username", email),
            new Claim("name", "Roberto Sanabria"),
            new Claim(ClaimTypes.Role, "Admin"),  // sin login => admin, sin consultar la base
        };
        var identity = new ClaimsIdentity(claims, SchemeName, "name", ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
