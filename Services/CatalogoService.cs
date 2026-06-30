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
        op.Apellido = Normalizador.Mayus(op.Apellido);
        op.Nombre = Normalizador.Mayus(op.Nombre);
        op.Dni = (op.Dni ?? "").Trim();
        await using var db = await _factory.CreateDbContextAsync();
        if (op.Id == 0)
        {
            // Si ya existe el mismo operario, lo actualiza en vez de duplicarlo.
            var ex = await db.Operarios.FirstOrDefaultAsync(o => o.Apellido == op.Apellido && o.Nombre == op.Nombre);
            if (ex is not null) { ex.Dni = op.Dni; ex.Tipo = op.Tipo; ex.Turno = op.Turno; ex.Activo = true; }
            else db.Operarios.Add(op);
        }
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
        p.Codigo = Normalizador.Patente(p.Codigo);
        await using var db = await _factory.CreateDbContextAsync();
        if (p.Id == 0)
        {
            // Si la patente ya existe (mismo código normalizado), la actualiza.
            var ex = await db.Patentes.FirstOrDefaultAsync(x => x.Codigo == p.Codigo);
            if (ex is not null) { ex.Modelo = p.Modelo; ex.Marca = p.Marca; ex.TipoUnidad = p.TipoUnidad; ex.Activo = true; }
            else db.Patentes.Add(p);
        }
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
        f.Nombre = Normalizador.Mayus(f.Nombre);
        await using var db = await _factory.CreateDbContextAsync();
        if (f.Id == 0)
        {
            var ex = await db.Frigorificos.FirstOrDefaultAsync(x => x.Nombre == f.Nombre);
            if (ex is not null) ex.Activo = true;
            else db.Frigorificos.Add(f);
        }
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

    /// <summary>
    /// Limpieza de datos: pasa todo a un formato único (patentes "AA 999 AA", textos en
    /// MAYÚSCULAS) y elimina duplicados en operarios, patentes, frigoríficos, usuarios y
    /// operarios repetidos dentro de un mismo lavado. Es idempotente (se puede correr siempre).
    /// </summary>
    public async Task NormalizarYDeduplicarAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        // Patentes: formato unificado + sin duplicados por código.
        var pats = await db.Patentes.ToListAsync();
        foreach (var p in pats) p.Codigo = Normalizador.Patente(p.Codigo);
        Deduplicar(db.Patentes, pats, p => p.Codigo, p => p.Activo, p => p.Id);

        // Frigoríficos: MAYÚSCULAS + sin duplicados por nombre.
        var frigs = await db.Frigorificos.ToListAsync();
        foreach (var f in frigs) f.Nombre = Normalizador.Mayus(f.Nombre);
        Deduplicar(db.Frigorificos, frigs, f => f.Nombre, f => f.Activo, f => f.Id);

        // Operarios: MAYÚSCULAS + sin duplicados por apellido+nombre.
        var ops = await db.Operarios.ToListAsync();
        foreach (var o in ops) { o.Apellido = Normalizador.Mayus(o.Apellido); o.Nombre = Normalizador.Mayus(o.Nombre); o.Dni = (o.Dni ?? "").Trim(); }
        Deduplicar(db.Operarios, ops, o => $"{o.Apellido}|{o.Nombre}", o => o.Activo, o => o.Id);

        // Usuarios: email en minúscula + sin duplicados.
        var usrs = await db.Usuarios.ToListAsync();
        foreach (var u in usrs) u.Email = (u.Email ?? "").Trim().ToLowerInvariant();
        Deduplicar(db.Usuarios, usrs, u => u.Email, u => u.Rol == RolUsuario.Admin, u => u.Id);

        // Lavados: la patente escrita queda en el mismo formato que el catálogo.
        var lavados = await db.Lavados.ToListAsync();
        foreach (var l in lavados)
            if (l.Tipo == TipoLavado.Camion) l.Patente = Normalizador.Patente(l.Patente);

        // Operarios por lavado: MAYÚSCULAS + sin el mismo operario repetido en un lavado.
        var lops = await db.LavadoOperarios.ToListAsync();
        foreach (var lo in lops) lo.Nombre = Normalizador.Mayus(lo.Nombre);
        foreach (var g in lops.GroupBy(x => (x.LavadoId, x.Nombre)).Where(g => g.Count() > 1))
            foreach (var extra in g.OrderBy(x => x.Id).Skip(1))
                db.LavadoOperarios.Remove(extra);

        await db.SaveChangesAsync();
    }

    /// <summary>Conserva un registro por clave (prefiere el que cumple <paramref name="preferir"/>,
    /// y entre esos el de menor Id) y borra el resto.</summary>
    private static void Deduplicar<T>(Microsoft.EntityFrameworkCore.DbSet<T> set, List<T> items,
        Func<T, string> clave, Func<T, bool> preferir, Func<T, int> id) where T : class
    {
        foreach (var g in items.GroupBy(clave, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var keep = g.OrderByDescending(preferir).ThenBy(id).First();
            foreach (var extra in g.Where(x => id(x) != id(keep)))
                set.Remove(extra);
        }
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
