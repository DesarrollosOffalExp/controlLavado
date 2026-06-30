using System.Security.Claims;
using ControlLavados.Models;
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
    public const string RolAdministrativo = "Administrativo";
    private readonly UsuarioService _usuarios;

    public RolClaimsTransformation(UsuarioService usuarios) => _usuarios = usuarios;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null || !identity.IsAuthenticated)
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
        Usuario? usuario;
        try
        {
            usuario = await _usuarios.ResolverEnLoginAsync(email, nombre);
        }
        catch
        {
            // Ante un problema transitorio de base, no rompemos el acceso (entra sin rol elevado).
            return principal;
        }

        identity.AddClaim(new Claim("rol_resuelto", "1"));
        if (usuario is not null)
        {
            if (usuario.Rol == RolUsuario.Admin)
                identity.AddClaim(new Claim(ClaimTypes.Role, RolAdmin));
            else if (usuario.Rol == RolUsuario.Administrativo)
                identity.AddClaim(new Claim(ClaimTypes.Role, RolAdministrativo));
        }

        return principal;
    }
}
