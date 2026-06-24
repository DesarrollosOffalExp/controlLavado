using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

namespace ControlLavados.Models;

/// <summary>
/// Un proceso de lavado (de camión o de fábrica de hielo).
/// Las marcas de tiempo nullables representan cada etapa; lo que está vacío
/// todavía no ocurrió. Las columnas calculadas de la grilla (duraciones,
/// turno, semana, contadores) se derivan de estos datos y no se persisten.
/// </summary>
public class Lavado
{
    public int Id { get; set; }

    public TipoLavado Tipo { get; set; }

    /// <summary>Patente de la unidad (camión) o equipo/sector (hielo).</summary>
    [MaxLength(20)]
    public string Patente { get; set; } = "";

    /// <summary>Dársena asignada (solo camiones). Ej: "Dársena 1".</summary>
    [MaxLength(20)]
    public string? Darsena { get; set; }

    [MaxLength(40)]
    public string? Frigorifico { get; set; }

    public int Tambores { get; set; }

    public int Pallets { get; set; }

    /// <summary>Fecha del lavado (sin hora).</summary>
    public DateOnly Fecha { get; set; }

    // --- Etapas ---
    public DateTime? InicioAtraco { get; set; }
    public DateTime? InicioLavado { get; set; }
    public DateTime? FinLavado { get; set; }
    public DateTime? Desatraco { get; set; }

    [MaxLength(500)]
    public string? Incidencias { get; set; }

    [MaxLength(30)]
    public string Estado { get; set; } = EstadoLavado.Pendiente;

    /// <summary>
    /// Operarios por semana (= cantidad de operarios del lavado). Se persiste en la
    /// tabla SQL pero NO se muestra en la grilla.
    /// </summary>
    public int OperariosPorSemana { get; set; }

    /// <summary>Marca temporal: cuándo se cerró/finalizó el registro.</summary>
    public DateTime? Finalizado { get; set; }

    /// <summary>Marca temporal de creación del registro.</summary>
    public DateTime CreadoEn { get; set; }

    public List<LavadoOperario> Operarios { get; set; } = new();

    // ---------- Columnas calculadas (no se guardan) ----------

    [NotMapped]
    public DateTime? MarcaTemporal => Finalizado ?? CreadoEn;

    [NotMapped]
    public string Turno
    {
        get
        {
            var referencia = Tipo == TipoLavado.Camion ? InicioAtraco ?? InicioLavado : InicioLavado;
            return referencia is null ? "" : Turnos.DesdeHora(referencia.Value);
        }
    }

    [NotMapped]
    public int Semana => ISOWeek.GetWeekOfYear(Fecha.ToDateTime(TimeOnly.MinValue));

    [NotMapped]
    public int NumOffal => Operarios.Count(o => o.Tipo == TipoOperario.Offal);

    [NotMapped]
    public int NumAgencia => Operarios.Count(o => o.Tipo == TipoOperario.Agencia);

    [NotMapped]
    public int OperariosUsados => Operarios.Count;

    [NotMapped]
    public string OperariosTexto => string.Join(", ", Operarios.Select(o => o.Nombre));

    [NotMapped]
    public TimeSpan? DurAtracoLavado => Resta(InicioLavado, InicioAtraco);

    [NotMapped]
    public TimeSpan? DurLavado => Resta(FinLavado, InicioLavado);

    [NotMapped]
    public TimeSpan? DurFinDesatraco => Resta(Desatraco, FinLavado);

    [NotMapped]
    public TimeSpan? TiempoTotal => Tipo == TipoLavado.Camion
        ? Resta(Desatraco, InicioAtraco)
        : Resta(FinLavado, InicioLavado);

    private static TimeSpan? Resta(DateTime? fin, DateTime? inicio)
        => (fin.HasValue && inicio.HasValue) ? fin.Value - inicio.Value : null;
}

/// <summary>Operario asignado a un lavado (puede haber varios por lavado).</summary>
public class LavadoOperario
{
    public int Id { get; set; }
    public int LavadoId { get; set; }
    public Lavado? Lavado { get; set; }

    [MaxLength(60)]
    public string Nombre { get; set; } = "";

    public TipoOperario Tipo { get; set; }
}
