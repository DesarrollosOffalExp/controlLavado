using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

/// <summary>Datos para iniciar un nuevo lavado.</summary>
public class NuevoLavado
{
    public string Patente { get; set; } = "";
    public string? Darsena { get; set; }
    public string? Frigorifico { get; set; }
    public int Tambores { get; set; }
    public int Pallets { get; set; }
    public List<string> Operarios { get; set; } = new();
    public string? Incidencias { get; set; }
}

/// <summary>Recursos ocupados por lavados en curso (no se pueden volver a asignar).</summary>
public class Disponibilidad
{
    public HashSet<string> PatentesOcupadas { get; } = new();
    public HashSet<string> DarsenasOcupadas { get; } = new();
    public HashSet<string> OperariosOcupados { get; } = new();
}

public class LavadoService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public LavadoService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>
    /// Devuelve qué patentes/dársenas/operarios están ocupados por lavados en curso.
    /// Los operarios se consideran ocupados en cualquier proceso (camión u hielo);
    /// patentes y dársenas, solo dentro del mismo tipo. <paramref name="excluirId"/>
    /// permite ignorar un lavado (útil al editar uno propio).
    /// </summary>
    public async Task<Disponibilidad> DisponibilidadAsync(TipoLavado tipo, int? excluirId = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var activos = await db.Lavados
            .Include(l => l.Operarios)
            .Where(l => l.Estado != EstadoLavado.Finalizado)
            .ToListAsync();

        var d = new Disponibilidad();
        foreach (var l in activos)
        {
            if (excluirId.HasValue && l.Id == excluirId.Value) continue;

            // Los operarios se liberan al TERMINAR el lavado: solo están ocupados
            // mientras el proceso está Atracado o Lavando (no durante el desatraco).
            if (l.Estado is EstadoLavado.Atracado or EstadoLavado.Lavando)
                foreach (var o in l.Operarios)
                    d.OperariosOcupados.Add(o.Nombre);

            // La patente/dársena siguen ocupadas hasta que el lavado se finaliza
            // (el camión sigue en la dársena hasta el desatraco).
            if (l.Tipo == tipo)
            {
                if (!string.IsNullOrEmpty(l.Patente)) d.PatentesOcupadas.Add(l.Patente);
                if (!string.IsNullOrEmpty(l.Darsena)) d.DarsenasOcupadas.Add(l.Darsena!);
            }
        }
        return d;
    }

    public async Task<List<Lavado>> ListarAsync(TipoLavado tipo)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Lavados
            .Include(l => l.Operarios)
            .Where(l => l.Tipo == tipo)
            .OrderByDescending(l => l.Id)
            .ToListAsync();
    }

    public async Task<List<Lavado>> EnCursoAsync(TipoLavado tipo)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Lavados
            .Include(l => l.Operarios)
            .Where(l => l.Tipo == tipo && l.Estado != EstadoLavado.Finalizado)
            .OrderBy(l => l.Id)
            .ToListAsync();
    }

    /// <summary>Resultado de intentar iniciar un lavado.</summary>
    public record InicioResultado(bool Ok, string? Error, Lavado? Lavado);

    /// <summary>
    /// Crea el registro y estampa la primera etapa (Atraco en camión, Lavado en hielo).
    /// Revalida contra los lavados en curso (patente/dársena/operarios ocupados) para
    /// evitar dobles asignaciones aunque dos usuarios envíen a la vez.
    /// </summary>
    public async Task<InicioResultado> IniciarAsync(TipoLavado tipo, NuevoLavado datos)
    {
        await using var db = await _factory.CreateDbContextAsync();

        // Revalidación server-side contra el estado actual.
        var activos = await db.Lavados
            .Include(l => l.Operarios)
            .Where(l => l.Estado != EstadoLavado.Finalizado)
            .ToListAsync();

        if (activos.Any(l => l.Tipo == tipo && l.Patente == datos.Patente))
            return new(false, $"La unidad «{datos.Patente}» ya tiene un lavado en curso.", null);

        if (tipo == TipoLavado.Camion && !string.IsNullOrEmpty(datos.Darsena)
            && activos.Any(l => l.Tipo == tipo && l.Darsena == datos.Darsena))
            return new(false, $"La {datos.Darsena} ya está ocupada.", null);

        // Un operario solo está ocupado mientras trabaja (Atracado/Lavando); se libera al terminar.
        var ocupados = activos
            .Where(l => l.Estado is EstadoLavado.Atracado or EstadoLavado.Lavando)
            .SelectMany(l => l.Operarios).Select(o => o.Nombre).ToHashSet();
        var enConflicto = datos.Operarios.Where(o => ocupados.Contains(o)).ToList();
        if (enConflicto.Count > 0)
            return new(false, $"Operario(s) ya asignado(s) a otro lavado: {string.Join(", ", enConflicto)}.", null);

        var tipoDe = await ResolverTipoOperarioAsync(db);
        var ahora = DateTime.Now;
        var lavado = new Lavado
        {
            Tipo = tipo,
            Patente = datos.Patente,
            Darsena = tipo == TipoLavado.Camion ? datos.Darsena : null,
            Frigorifico = datos.Frigorifico,
            Tambores = datos.Tambores,
            Pallets = datos.Pallets,
            Incidencias = datos.Incidencias,
            Fecha = DateOnly.FromDateTime(ahora),
            CreadoEn = ahora,
            OperariosPorSemana = datos.Operarios.Count,
            Operarios = datos.Operarios
                .Select(n => new LavadoOperario { Nombre = n, Tipo = tipoDe(n) })
                .ToList(),
        };

        if (tipo == TipoLavado.Camion)
        {
            lavado.InicioAtraco = ahora;
            lavado.Estado = EstadoLavado.Atracado;
        }
        else
        {
            lavado.InicioLavado = ahora;
            lavado.Estado = EstadoLavado.Lavando;
        }

        db.Lavados.Add(lavado);
        await db.SaveChangesAsync();
        return new(true, null, lavado);
    }

    /// <summary>Etiqueta del botón de la próxima etapa, o null si ya está finalizado.</summary>
    public static string? ProximaEtapa(Lavado l) => l.Tipo switch
    {
        TipoLavado.Camion => l.Estado switch
        {
            EstadoLavado.Atracado => "Iniciar Lavado",
            EstadoLavado.Lavando => "Fin de Lavado",
            EstadoLavado.LavadoTerminado => "Desatraco (Finalizar)",
            _ => null,
        },
        _ => l.Estado switch
        {
            EstadoLavado.Lavando => "Fin",
            _ => null,
        },
    };

    /// <summary>Avanza el lavado a la siguiente etapa estampando la hora actual.</summary>
    public async Task AvanzarAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var l = await db.Lavados.FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return;

        var ahora = DateTime.Now;
        if (l.Tipo == TipoLavado.Camion)
        {
            switch (l.Estado)
            {
                case EstadoLavado.Atracado:
                    l.InicioLavado = ahora; l.Estado = EstadoLavado.Lavando; break;
                case EstadoLavado.Lavando:
                    l.FinLavado = ahora; l.Estado = EstadoLavado.LavadoTerminado; break;
                case EstadoLavado.LavadoTerminado:
                    l.Desatraco = ahora; l.Estado = EstadoLavado.Finalizado; l.Finalizado = ahora; break;
            }
        }
        else
        {
            if (l.Estado == EstadoLavado.Lavando)
            {
                l.FinLavado = ahora; l.Estado = EstadoLavado.Finalizado; l.Finalizado = ahora;
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Actualiza campos editables (operarios, incidencias, tambores, etc.) en cualquier momento.</summary>
    public async Task GuardarEdicionAsync(int id, NuevoLavado datos)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var l = await db.Lavados.Include(x => x.Operarios).FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return;

        l.Incidencias = datos.Incidencias;
        l.Tambores = datos.Tambores;
        l.Pallets = datos.Pallets;
        l.Frigorifico = datos.Frigorifico;
        if (l.Tipo == TipoLavado.Camion) l.Darsena = datos.Darsena;

        var tipoDe = await ResolverTipoOperarioAsync(db);
        l.Operarios.Clear();
        foreach (var n in datos.Operarios)
            l.Operarios.Add(new LavadoOperario { Nombre = n, Tipo = tipoDe(n) });
        l.OperariosPorSemana = datos.Operarios.Count;

        await db.SaveChangesAsync();
    }

    /// <summary>Devuelve una función que mapea el nombre completo del operario a su tipo (Offal/Contrato).</summary>
    private static async Task<Func<string, TipoOperario>> ResolverTipoOperarioAsync(AppDbContext db)
    {
        var ops = await db.Operarios.ToListAsync();
        var map = new Dictionary<string, TipoOperario>();
        foreach (var o in ops) map[o.NombreCompleto] = o.Tipo;
        return n => map.TryGetValue(n, out var t) ? t : TipoOperario.Contrato;
    }

    public async Task EliminarAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var l = await db.Lavados.FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return;
        db.Lavados.Remove(l);
        await db.SaveChangesAsync();
    }
}
