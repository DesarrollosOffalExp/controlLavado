using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public class UsuarioService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public UsuarioService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

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
