using System.Globalization;
using ClosedXML.Excel;
using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public record MetricaSemana(
    int Semana,
    int Lavados,     // cantidad de LAVADOS (camiones). Las tareas no se contabilizan como cantidad.
    int Registros,   // cantidad total de registros del grupo (camiones + tareas); denominador de promedios.
    TimeSpan TotalHoras,
    TimeSpan HorasNetas,
    TimeSpan PromedioHoras,
    int TotalOperarios,
    double PromedioOperarios,
    double? VarHoras,
    double? VarLavados);

/// <summary>Bloque de métricas: un turno (Mañana / Tarde) o el "Resumen" (total).</summary>
public record MetricasTurno(string Turno, List<MetricaSemana> Semanas);

public record HorasOperario(string Turno, string Operario, TipoOperario Tipo, Dictionary<int, TimeSpan> PorSemana, Dictionary<int, int> DiasPorSemana, int DiasTrabajados, TimeSpan Total);

public record HorasReporte(List<int> Semanas, List<HorasOperario> Filas);

/// <summary>Resumen por operario sobre el rango filtrado.</summary>
public record OperarioResumen(string Operario, TipoOperario Tipo, int Camiones, int Lavados, int DiasTrabajados, TimeSpan Horas, TimeSpan Promedio);

/// <summary>Resumen por operario y mes.</summary>
public record OperarioMesResumen(string Mes, string Operario, int Camiones, int Lavados, int DiasTrabajados, TimeSpan Horas, TimeSpan Promedio);

/// <summary>Lavados agrupados por frigorífico (solo camiones).</summary>
public record FrigorificoCant(string Frigorifico, int Cantidad);

/// <summary>Contraste de nómina de una semana: activos totales vs cuántos trabajaron.</summary>
public record NominaSemana(int Activos, int Trabajaron);

public class ReporteService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] OrdenTurnos = { Turnos.Mañana, Turnos.Tarde, Turnos.Noche };

    public ReporteService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>Lavados finalizados (los que tienen tiempos completos) para el rango/tipo dado.</summary>
    public async Task<List<Lavado>> ObtenerAsync(TipoLavado? tipo, DateOnly? desde, DateOnly? hasta)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Lavados.Include(l => l.Operarios)
            .Where(l => l.Estado == EstadoLavado.Finalizado);

        if (tipo.HasValue) q = q.Where(l => l.Tipo == tipo.Value);
        if (desde.HasValue) q = q.Where(l => l.Fecha >= desde.Value);
        if (hasta.HasValue) q = q.Where(l => l.Fecha <= hasta.Value);

        return await q.OrderBy(l => l.Id).ToListAsync();
    }

    /// <summary>
    /// Métricas por turno (Mañana / Tarde) más un bloque "Resumen" que totaliza ambos,
    /// con totales, promedios y variación vs la semana anterior (N-1).
    /// </summary>
    public List<MetricasTurno> CalcularMetricas(List<Lavado> lavados)
    {
        var resultado = new List<MetricasTurno>
        {
            MetricasDe(Turnos.Mañana, lavados.Where(l => l.Turno == Turnos.Mañana).ToList()),
            MetricasDe(Turnos.Tarde,  lavados.Where(l => l.Turno != Turnos.Mañana).ToList()),
            MetricasDe("Resumen",     lavados), // total Mañana + Tarde
        };
        return resultado.Where(m => m.Semanas.Count > 0).ToList();
    }

    private static MetricasTurno MetricasDe(string etiqueta, List<Lavado> lavados)
    {
        var baseSemana = lavados
            .GroupBy(l => l.Semana)
            .ToDictionary(
                g => g.Key,
                g => (
                    Cant: g.Count(),
                    // Solo los LAVADOS (camiones) se contabilizan como cantidad; las tareas no.
                    Lavados: g.Count(l => l.Tipo == TipoLavado.Camion),
                    Total: g.Aggregate(TimeSpan.Zero, (acc, l) => acc + (l.TiempoTotal ?? TimeSpan.Zero)),
                    // Horas NETAS de lavado (Fin de Lavado − Inicio de Lavado), sin atraco/desatraco.
                    Neta: g.Aggregate(TimeSpan.Zero, (acc, l) => acc + (l.DurLavado ?? TimeSpan.Zero)),
                    // Operarios DISTINTOS de la semana: una persona cuenta 1 aunque haga varios lavados.
                    Operarios: g.SelectMany(l => l.Operarios)
                                .Select(o => o.Nombre)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count(),
                    // Total de asignaciones (para el promedio de operarios por lavado).
                    Asignaciones: g.Sum(l => l.OperariosUsados)));

        var semanas = new List<MetricaSemana>();
        foreach (var semana in baseSemana.Keys.OrderBy(k => k))
        {
            var cur = baseSemana[semana];
            double? varHoras = null, varLav = null;
            if (baseSemana.TryGetValue(semana - 1, out var prev))
            {
                if (prev.Total.Ticks > 0)
                    varHoras = (cur.Total.TotalSeconds - prev.Total.TotalSeconds) / prev.Total.TotalSeconds;
                // La variación de cantidad se calcula sobre los lavados (camiones), no sobre las tareas.
                if (prev.Lavados > 0)
                    varLav = (double)(cur.Lavados - prev.Lavados) / prev.Lavados;
            }
            semanas.Add(new MetricaSemana(
                semana, cur.Lavados, cur.Cant, cur.Total, cur.Neta,
                TimeSpan.FromSeconds(cur.Total.TotalSeconds / cur.Cant),
                cur.Operarios, (double)cur.Asignaciones / cur.Cant, varHoras, varLav));
        }
        return new MetricasTurno(etiqueta, semanas);
    }

    /// <summary>Cantidad de lavados por frigorífico (solo camiones), de mayor a menor.</summary>
    public List<FrigorificoCant> LavadosPorFrigorifico(List<Lavado> lavados) =>
        lavados
            .Where(l => l.Tipo == TipoLavado.Camion)
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Frigorifico) ? "(sin frigorífico)" : l.Frigorifico!.Trim())
            .Select(g => new FrigorificoCant(g.Key, g.Count()))
            .OrderByDescending(f => f.Cantidad)
            .ThenBy(f => f.Frigorifico)
            .ToList();

    /// <summary>
    /// Contraste de nómina de la semana: cuántos operarios activos hay (nómina completa)
    /// y cuántos de ellos efectivamente trabajaron o hicieron tareas esa semana.
    /// </summary>
    public NominaSemana NominaContraste(IEnumerable<string> operariosActivos, List<Lavado> lavadosSemana)
    {
        var activos = new HashSet<string>(operariosActivos, StringComparer.OrdinalIgnoreCase);
        var trabajaron = lavadosSemana
            .SelectMany(l => l.Operarios)
            .Select(o => o.Nombre)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(n => activos.Contains(n));
        return new NominaSemana(activos.Count, trabajaron);
    }

    /// <summary>
    /// Horas trabajadas por operario y semana, agrupadas por turno (réplica de la "Hoja 4").
    /// A cada operario presente en un lavado se le imputa la duración total de ese lavado.
    /// </summary>
    public HorasReporte CalcularHorasPorOperario(List<Lavado> lavados, IReadOnlyDictionary<string, string?> turnoPorOperario)
    {
        var semanas = lavados.Select(l => l.Semana).Distinct().OrderBy(x => x).ToList();
        var acum = new Dictionary<(string Turno, string Operario),
            (TipoOperario Tipo, Dictionary<int, TimeSpan> Sem, HashSet<DateOnly> Dias, Dictionary<int, HashSet<DateOnly>> DiasSem)>();

        foreach (var l in lavados)
        {
            var dur = l.TiempoTotal ?? TimeSpan.Zero;
            foreach (var o in l.Operarios)
            {
                // El turno es el ASIGNADO al operario (no la hora del lavado).
                var turnoOp = (turnoPorOperario.TryGetValue(o.Nombre, out var t) && !string.IsNullOrEmpty(t)) ? t! : "(sin turno)";
                var key = (turnoOp, o.Nombre);
                if (!acum.TryGetValue(key, out var v))
                {
                    v = (o.Tipo, new Dictionary<int, TimeSpan>(), new HashSet<DateOnly>(), new Dictionary<int, HashSet<DateOnly>>());
                    acum[key] = v;
                }
                v.Sem.TryGetValue(l.Semana, out var cur);
                v.Sem[l.Semana] = cur + dur;
                v.Dias.Add(l.Fecha);
                if (!v.DiasSem.TryGetValue(l.Semana, out var ds)) { ds = new HashSet<DateOnly>(); v.DiasSem[l.Semana] = ds; }
                ds.Add(l.Fecha);
            }
        }

        int Rank(string turno) => Array.IndexOf(OrdenTurnos, turno) is var i && i >= 0 ? i : 99;
        var filas = acum
            .Select(kv => new HorasOperario(
                kv.Key.Turno, kv.Key.Operario, kv.Value.Tipo, kv.Value.Sem,
                kv.Value.DiasSem.ToDictionary(x => x.Key, x => x.Value.Count),
                kv.Value.Dias.Count,
                kv.Value.Sem.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b)))
            .OrderBy(f => Rank(f.Turno)).ThenBy(f => f.Operario)
            .ToList();

        return new HorasReporte(semanas, filas);
    }

    /// <summary>Resumen por operario y mes.</summary>
    public List<OperarioMesResumen> ResumenPorMes(List<Lavado> lavados)
    {
        var porOpMes = AgruparPorOperario(lavados, l => $"{l.Fecha.Year:0000}-{l.Fecha.Month:00}");
        return porOpMes
            .Select(kv => (kv.Key, R: Construir(kv.Key.Op, kv.Value)))
            .OrderBy(x => x.Key.Clave).ThenBy(x => x.Key.Op)
            .Select(x => new OperarioMesResumen(x.Key.Clave, x.R.Operario, x.R.Camiones, x.R.Lavados, x.R.DiasTrabajados, x.R.Horas, x.R.Promedio))
            .ToList();
    }

    private static Dictionary<(string Clave, string Op), (TipoOperario Tipo, List<Lavado> Lavs)> AgruparPorOperario(
        List<Lavado> lavados, Func<Lavado, string> clave)
    {
        var acc = new Dictionary<(string Clave, string Op), (TipoOperario Tipo, List<Lavado> Lavs)>();
        foreach (var l in lavados)
            foreach (var o in l.Operarios)
            {
                var key = (clave(l), o.Nombre);
                if (!acc.TryGetValue(key, out var v)) { v = (o.Tipo, new List<Lavado>()); acc[key] = v; }
                v.Lavs.Add(l);
            }
        return acc;
    }

    private static OperarioResumen Construir(string operario, (TipoOperario Tipo, List<Lavado> Lavs) v)
    {
        var horas = v.Lavs.Aggregate(TimeSpan.Zero, (a, l) => a + (l.TiempoTotal ?? TimeSpan.Zero));
        var dias = v.Lavs.Select(l => l.Fecha).Distinct().Count();
        var camiones = v.Lavs.Count(l => l.Tipo == TipoLavado.Camion);
        var prom = v.Lavs.Count > 0 ? TimeSpan.FromSeconds(horas.TotalSeconds / v.Lavs.Count) : TimeSpan.Zero;
        return new OperarioResumen(operario, v.Tipo, camiones, v.Lavs.Count, dias, horas, prom);
    }

    // ---------- Excel ----------

    public byte[] GenerarExcel(List<Lavado> lavados, List<MetricasTurno> metricas, TipoLavado? tipo,
        IReadOnlyDictionary<string, string?> turnoPorOperario)
    {
        using var wb = new XLWorkbook();
        EscribirMetricas(wb.AddWorksheet("Métricas"), metricas);
        var frig = LavadosPorFrigorifico(lavados);
        if (frig.Count > 0) EscribirFrigorificos(wb.AddWorksheet("Frigoríficos"), frig);
        EscribirHoras(wb.AddWorksheet("Horas por operario"), CalcularHorasPorOperario(lavados, turnoPorOperario));
        EscribirResumenMes(wb.AddWorksheet("Resumen por mes"), ResumenPorMes(lavados));
        EscribirDetalle(wb.AddWorksheet("Detalle"), lavados, tipo);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void EscribirResumenMes(IXLWorksheet ws, List<OperarioMesResumen> filas)
    {
        string[] h = { "Mes", "Operario", "Lavados", "Días trab.", "Hs trabajadas", "Prom. lavado" };
        for (int c = 0; c < h.Length; c++)
            ws.Cell(1, c + 1).Value = h[c];
        ws.Range(1, 1, 1, h.Length).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);

        int row = 2;
        foreach (var r in filas)
        {
            ws.Cell(row, 1).Value = r.Mes;
            ws.Cell(row, 2).Value = r.Operario;
            ws.Cell(row, 3).Value = r.Lavados;
            ws.Cell(row, 4).Value = r.DiasTrabajados;
            ws.Cell(row, 5).Value = FmtDur(r.Horas);
            ws.Cell(row, 6).Value = FmtDur(r.Promedio);
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void EscribirFrigorificos(IXLWorksheet ws, List<FrigorificoCant> filas)
    {
        ws.Cell(1, 1).Value = "Frigorífico";
        ws.Cell(1, 2).Value = "Lavados";
        ws.Range(1, 1, 1, 2).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);

        int row = 2;
        foreach (var f in filas)
        {
            ws.Cell(row, 1).Value = f.Frigorifico;
            ws.Cell(row, 2).Value = f.Cantidad;
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void EscribirHoras(IXLWorksheet ws, HorasReporte rep)
    {
        ws.Cell(1, 1).Value = "Turno";
        ws.Cell(1, 2).Value = "Operario";
        ws.Cell(1, 3).Value = "Tipo";
        int col = 4;
        foreach (var sem in rep.Semanas)
        {
            ws.Cell(1, col++).Value = $"Sem {sem} hs";
            ws.Cell(1, col++).Value = $"Sem {sem} d";
        }
        ws.Cell(1, col++).Value = "Días trab.";
        ws.Cell(1, col).Value = "Total hs";
        ws.Range(1, 1, 1, col).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);

        int row = 2;
        foreach (var f in rep.Filas)
        {
            ws.Cell(row, 1).Value = f.Turno;
            ws.Cell(row, 2).Value = f.Operario;
            ws.Cell(row, 3).Value = f.Tipo.ToString();
            col = 4;
            foreach (var sem in rep.Semanas)
            {
                f.PorSemana.TryGetValue(sem, out var hs);
                f.DiasPorSemana.TryGetValue(sem, out var d);
                ws.Cell(row, col++).Value = FmtDur(hs);
                ws.Cell(row, col++).Value = d;
            }
            ws.Cell(row, col++).Value = f.DiasTrabajados;
            ws.Cell(row, col).Value = FmtDur(f.Total);
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void EscribirMetricas(IXLWorksheet ws, List<MetricasTurno> metricas)
    {
        int row = 1;
        foreach (var t in metricas)
        {
            var etiqueta = t.Turno == "Resumen" ? "RESUMEN (TOTAL)" : $"TURNO: {t.Turno}";
            ws.Cell(row, 1).Value = etiqueta;
            ws.Range(row, 1, row, 9).Merge().Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);
            row++;

            string[] headers = { "Semana", "Cantidad", "Total hs", "Neto hs", "Prom. hs", "Operarios", "Prom. op.", "% Var. hs", "% Var. cant." };
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(row, c + 1).Value = headers[c];
                ws.Cell(row, c + 1).Style.Font.SetBold();
            }
            row++;

            foreach (var s in t.Semanas)
            {
                ws.Cell(row, 1).Value = s.Semana;
                ws.Cell(row, 2).Value = s.Lavados;
                ws.Cell(row, 3).Value = FmtDur(s.TotalHoras);
                ws.Cell(row, 4).Value = FmtDur(s.HorasNetas);
                ws.Cell(row, 5).Value = FmtDur(s.PromedioHoras);
                ws.Cell(row, 6).Value = s.TotalOperarios;
                ws.Cell(row, 7).Value = Math.Round(s.PromedioOperarios, 1);
                ws.Cell(row, 8).Value = s.VarHoras.HasValue ? s.VarHoras.Value.ToString("P1", Es) : "—";
                ws.Cell(row, 9).Value = s.VarLavados.HasValue ? s.VarLavados.Value.ToString("P1", Es) : "—";
                row++;
            }
            row++; // fila en blanco entre bloques
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirDetalle(IXLWorksheet ws, List<Lavado> lavados, TipoLavado? tipo)
    {
        var soloCamion = tipo == TipoLavado.Camion;
        string[] headers = soloCamion
            ? new[] { "Marca temporal", "Fecha", "Turno", "N° Offal", "N° Contrato", "Patente", "Dársena", "Frigorífico",
                      "Tambores", "Pallets", "Operarios", "Inicio Atraco", "Inicio Lavado", "Fin Lavado", "Desatraco",
                      "Atraco→Lavado", "Lavado", "Fin→Desatraco", "Total", "Semana", "Op. usados", "Incidencias", "Estado" }
            : new[] { "Marca temporal", "Fecha", "Turno", "N° Offal", "N° Contrato", "Tipo", "Equipo/Patente",
                      "Operarios", "Inicio Lavado", "Fin Lavado", "Total", "Semana", "Op. usados", "Incidencias", "Estado" };

        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);
        }

        int row = 2;
        foreach (var l in lavados)
        {
            int c = 1;
            ws.Cell(row, c++).Value = FmtFechaHora(l.MarcaTemporal);
            ws.Cell(row, c++).Value = l.Fecha.ToString("dd/MM/yyyy");
            ws.Cell(row, c++).Value = l.Turno;
            ws.Cell(row, c++).Value = l.NumOffal;
            ws.Cell(row, c++).Value = l.NumContrato;
            if (soloCamion)
            {
                ws.Cell(row, c++).Value = l.Patente;
                ws.Cell(row, c++).Value = l.Darsena ?? "";
                ws.Cell(row, c++).Value = l.Frigorifico ?? "";
                ws.Cell(row, c++).Value = l.Tambores;
                ws.Cell(row, c++).Value = l.Pallets;
                ws.Cell(row, c++).Value = l.OperariosTexto;
                ws.Cell(row, c++).Value = FmtHora(l.InicioAtraco);
                ws.Cell(row, c++).Value = FmtHora(l.InicioLavado);
                ws.Cell(row, c++).Value = FmtHora(l.FinLavado);
                ws.Cell(row, c++).Value = FmtHora(l.Desatraco);
                ws.Cell(row, c++).Value = FmtDur(l.DurAtracoLavado);
                ws.Cell(row, c++).Value = FmtDur(l.DurLavado);
                ws.Cell(row, c++).Value = FmtDur(l.DurFinDesatraco);
                ws.Cell(row, c++).Value = FmtDur(l.TiempoTotal);
            }
            else
            {
                ws.Cell(row, c++).Value = l.Tipo.ToString();
                ws.Cell(row, c++).Value = l.Patente;
                ws.Cell(row, c++).Value = l.OperariosTexto;
                ws.Cell(row, c++).Value = FmtHora(l.InicioLavado);
                ws.Cell(row, c++).Value = FmtHora(l.FinLavado);
                ws.Cell(row, c++).Value = FmtDur(l.TiempoTotal);
            }
            ws.Cell(row, c++).Value = l.Semana;
            ws.Cell(row, c++).Value = l.OperariosUsados;
            ws.Cell(row, c++).Value = l.Incidencias ?? "";
            ws.Cell(row, c++).Value = l.Estado;
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static string FmtHora(DateTime? d) => d?.ToString("HH:mm:ss", Es) ?? "";
    private static string FmtFechaHora(DateTime? d) => d?.ToString("dd/MM/yyyy HH:mm:ss", Es) ?? "";
    private static string FmtDur(TimeSpan? t) =>
        t.HasValue ? $"{(int)t.Value.TotalHours}:{t.Value.Minutes:00}:{t.Value.Seconds:00}" : "";
}
