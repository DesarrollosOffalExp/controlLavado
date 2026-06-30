using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public class UsuarioService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private static readonly PasswordHasher<Usuario> Hasher = new();

    public UsuarioService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>Registra una cuenta local (email + contraseña). Registro abierto: entra como Operario.
    /// Devuelve el usuario, o un mensaje de error si el email ya existe o los datos son inválidos.</summary>
    public async Task<(Usuario? Usuario, string? Error)> RegistrarLocalAsync(string email, string password, string? nombre)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (null, "Ingresá un email válido.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (null, "La contraseña debe tener al menos 6 caracteres.");

        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Usuarios.AnyAsync(u => u.Email == email))
            return (null, "Ya existe una cuenta con ese email. Probá iniciar sesión.");

        var u = new Usuario
        {
            Email = email,
            Nombre = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim(),
            Rol = RolUsuario.Operario,
            Activo = true,
            UltimoAcceso = DateTime.Now,
        };
        u.PasswordHash = Hasher.HashPassword(u, password);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return (u, null);
    }

    /// <summary>Valida una cuenta local. Devuelve el usuario si el email/contraseña son correctos
    /// y está activo; si no, null.</summary>
    public async Task<Usuario?> ValidarLocalAsync(string email, string password)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
        if (u is null || string.IsNullOrEmpty(u.PasswordHash) || !u.Activo)
            return null;
        if (Hasher.VerifyHashedPassword(u, u.PasswordHash, password) == PasswordVerificationResult.Failed)
            return null;
        u.UltimoAcceso = DateTime.Now;
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<List<Usuario>> ListarAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Usuarios.OrderByDescending(u => u.Rol).ThenBy(u => u.Email).ToListAsync();
    }

    /// <summary>
    /// Busca el usuario por email; si no existe lo crea como operario (cualquiera del
    /// tenant entra). Actualiza nombre y último acceso. Devuelve el usuario (o null si está inactivo).
    /// </summary>
    public async Task<Usuario?> ResolverEnLoginAsync(string email, string? nombre)
    {
        email = email.Trim().ToLowerInvariant();
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
        if (u is null)
        {
            u = new Usuario { Email = email, Nombre = nombre, Rol = RolUsuario.Operario, Activo = true };
            db.Usuarios.Add(u);
        }
        else if (!string.IsNullOrWhiteSpace(nombre))
        {
            u.Nombre = nombre;
        }
        u.UltimoAcceso = DateTime.Now;
        await db.SaveChangesAsync();
        return u.Activo ? u : null;
    }

    public async Task GuardarAsync(Usuario u)
    {
        u.Email = u.Email.Trim().ToLowerInvariant();
        await using var db = await _factory.CreateDbContextAsync();
        if (u.Id == 0) db.Usuarios.Add(u);
        else db.Usuarios.Update(u);
        await db.SaveChangesAsync();
    }

    public async Task CambiarRolAsync(int id, RolUsuario rol)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Usuarios.FindAsync(id);
        if (u is null) return;
        u.Rol = rol;
        await db.SaveChangesAsync();
    }

    public async Task ToggleActivoAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Usuarios.FindAsync(id);
        if (u is null) return;
        u.Activo = !u.Activo;
        await db.SaveChangesAsync();
    }
}
