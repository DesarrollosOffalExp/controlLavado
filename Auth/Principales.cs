using System.Security.Claims;
using ControlLavados.Models;

namespace ControlLavados.Auth;

/// <summary>Arma el ClaimsPrincipal para las cuentas locales (cookie).</summary>
public static class Principales
{
    public static ClaimsPrincipal CrearLocal(Usuario u)
    {
        var nombre = string.IsNullOrWhiteSpace(u.Nombre) ? u.Email : u.Nombre!;
        var claims = new[]
        {
            new Claim("preferred_username", u.Email),
            new Claim(ClaimTypes.Email, u.Email),
            new Claim(ClaimTypes.Name, nombre),
        };
        var identity = new ClaimsIdentity(claims, "Local", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}
