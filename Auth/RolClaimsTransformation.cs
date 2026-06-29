using System.Security.Claims;
using ControlLavados.Services;
using Microsoft.AspNetCore.Authentication;

namespace ControlLavados.Auth;

/// <summary>
/// En cada login, ubica al usuario por su email (lo crea como operario si no existe)
/// y, si está marcado como administrador en la base, le agrega el rol "Admin".
/// </summary>
public class RolClaimsTransformation : IClaimsTransformation
{
    public const string RolAdmin = "Admin";
    private readonly UsuarioService _usuarios;

    public RolClaimsTransformation(UsuarioService usuarios) => _usuarios = usuarios;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null || !identity.IsAuthenticated)
            return principal;

        // Modo demo / local (login apagado): el rol ya viene del DevAuthHandler.
        // No consultamos la base (evita depender de la tabla de usuarios).
        if (identity.AuthenticationType == DevAuthHandler.SchemeName)
            return principal;

        // Evita reprocesar si ya se transformó en este request.
        if (principal.HasClaim(c => c.Type == "rol_resuelto"))
            return principal;

        var email = principal.FindFirst("preferred_username")?.Value
                    ?? principal.FindFirst(ClaimTypes.Upn)?.Value
                    ?? principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email))
            return principal;

        var nombre = principal.FindFirst("name")?.Value ?? principal.Identity?.Name;
        var usuario = await _usuarios.ResolverEnLoginAsync(email, nombre);

        identity.AddClaim(new Claim("rol_resuelto", "1"));
        if (usuario is { EsAdmin: true })
            identity.AddClaim(new Claim(ClaimTypes.Role, RolAdmin));

        return principal;
    }
}
