using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

/// <summary>Acceso y ABM de los catálogos (operarios, patentes, frigoríficos).</summary>
public class CatalogoService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CatalogoService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    // ---------- Operarios ----------
    public async Task<List<Operario>> OperariosAsync(bool soloActivos = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Operarios.AsQueryable();
        if (soloActivos) q = q.Where(o => o.Activo);
        return await q.OrderBy(o => o.Apellido).ThenBy(o => o.Nombre).ToListAsync();
    }

    public async Task GuardarOperarioAsync(Operario op)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (op.Id == 0) db.Operarios.Add(op);
        else db.Operarios.Update(op);
        await db.SaveChangesAsync();
    }

    public async Task ToggleOperarioAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var op = await db.Operarios.FindAsync(id);
        if (op is null) return;
        op.Activo = !op.Activo;
        await db.SaveChangesAsync();
    }

    // ---------- Patentes ----------
    public async Task<List<Patente>> PatentesAsync(bool soloActivos = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Patentes.AsQueryable();
        if (soloActivos) q = q.Where(p => p.Activo);
        return await q.OrderBy(p => p.Codigo).ToListAsync();
    }

    public async Task GuardarPatenteAsync(Patente p)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (p.Id == 0) db.Patentes.Add(p);
        else db.Patentes.Update(p);
        await db.SaveChangesAsync();
    }

    public async Task TogglePatenteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.Patentes.FindAsync(id);
        if (p is null) return;
        p.Activo = !p.Activo;
        await db.SaveChangesAsync();
    }

    // ---------- Frigoríficos ----------
    public async Task<List<Frigorifico>> FrigorificosAsync(bool soloActivos = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Frigorificos.AsQueryable();
        if (soloActivos) q = q.Where(f => f.Activo);
        return await q.OrderBy(f => f.Nombre).ToListAsync();
    }

    public async Task GuardarFrigorificoAsync(Frigorifico f)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (f.Id == 0) db.Frigorificos.Add(f);
        else db.Frigorificos.Update(f);
        await db.SaveChangesAsync();
    }

    public async Task ToggleFrigorificoAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var f = await db.Frigorificos.FindAsync(id);
        if (f is null) return;
        f.Activo = !f.Activo;
        await db.SaveChangesAsync();
    }

    /// <summary>Carga los datos iniciales si las tablas están vacías.</summary>
    public static void Seed(AppDbContext db)
    {
        if (!db.Operarios.Any())
            db.Operarios.AddRange(Catalogo.OperariosSeed);
        if (!db.Patentes.Any())
            db.Patentes.AddRange(Catalogo.PatentesSeed.Select(c => new Patente { Codigo = c }));
        if (!db.Frigorificos.Any())
            db.Frigorificos.AddRange(Catalogo.FrigorificosSeed.Select(n => new Frigorifico { Nombre = n }));
        if (!db.Usuarios.Any(u => u.Email == "roberto.sanabria@offal.com.ar"))
            db.Usuarios.Add(new Usuario { Email = "roberto.sanabria@offal.com.ar", Nombre = "Roberto Sanabria", Rol = RolUsuario.Admin });
        db.SaveChanges();
    }
}
