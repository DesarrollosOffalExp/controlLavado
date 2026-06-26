using System.Globalization;
using ClosedXML.Excel;
using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public record MetricaSemana(
    int Semana,
    int Lavados,
    TimeSpan TotalHoras,
    TimeSpan PromedioHoras,
    int TotalOperarios,
    double PromedioOperarios,
    double? VarHoras,
    double? VarLavados);

public record MetricasTurno(string Turno, List<MetricaSemana> Semanas);

public record HorasOperario(string Turno, string Operario, TipoOperario Tipo, Dictionary<int, TimeSpan> PorSemana, int DiasTrabajados, TimeSpan Total);

public record HorasReporte(List<int> Semanas, List<HorasOperario> Filas);

/// <summary>Resumen por operario sobre el rango filtrado.</summary>
public record OperarioResumen(string Operario, TipoOperario Tipo, int Camiones, int Lavados, int DiasTrabajados, TimeSpan Horas, TimeSpan Promedio);

/// <summary>Resumen por operario y mes.</summary>
public record OperarioMesResumen(string Mes, string Operario, int Camiones, int Lavados, int DiasTrabajados, TimeSpan Horas, TimeSpan Promedio);

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

    /// <summary>Agrupa por turno y semana, calculando totales, promedios y variación semana a semana.</summary>
    public List<MetricasTurno> CalcularMetricas(List<Lavado> lavados)
    {
        var resultado = new List<MetricasTurno>();

        foreach (var turno in OrdenTurnos)
        {
            var delTurno = lavados.Where(l => l.Turno == turno).ToList();
            if (delTurno.Count == 0) continue;

            // Totales base por semana (sin variación todavía).
            var baseSemana = delTurno
                .GroupBy(l => l.Semana)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Cant: g.Count(),
                        Total: g.Aggregate(TimeSpan.Zero, (acc, l) => acc + (l.TiempoTotal ?? TimeSpan.Zero)),
                        TotalOp: g.Sum(l => l.OperariosUsados)));

            var semanas = new List<MetricaSemana>();
            foreach (var semana in baseSemana.Keys.OrderBy(k => k))
            {
                var cur = baseSemana[semana];

                // Variación SOLO contra la semana inmediatamente anterior (N-1), si tiene datos.
                double? varHoras = null, varLav = null;
                if (baseSemana.TryGetValue(semana - 1, out var prev))
                {
                    if (prev.Total.Ticks > 0)
                        varHoras = (cur.Total.TotalSeconds - prev.Total.TotalSeconds) / prev.Total.TotalSeconds;
                    if (prev.Cant > 0)
                        varLav = (double)(cur.Cant - prev.Cant) / prev.Cant;
                }

                semanas.Add(new MetricaSemana(
                    semana, cur.Cant, cur.Total,
                    TimeSpan.FromSeconds(cur.Total.TotalSeconds / cur.Cant),
                    cur.TotalOp, (double)cur.TotalOp / cur.Cant, varHoras, varLav));
            }

            resultado.Add(new MetricasTurno(turno, semanas));
        }

        return resultado;
    }

    /// <summary>
    /// Horas trabajadas por operario y semana, agrupadas por turno (réplica de la "Hoja 4").
    /// A cada operario presente en un lavado se le imputa la duración total de ese lavado.
    /// </summary>
    public HorasReporte CalcularHorasPorOperario(List<Lavado> lavados)
    {
        var semanas = lavados.Select(l => l.Semana).Distinct().OrderBy(x => x).ToList();
        var acum = new Dictionary<(string Turno, string Operario), (TipoOperario Tipo, Dictionary<int, TimeSpan> Sem, HashSet<DateOnly> Dias)>();

        foreach (var l in lavados)
        {
            var dur = l.TiempoTotal ?? TimeSpan.Zero;
            foreach (var o in l.Operarios)
            {
                var key = (l.Turno, o.Nombre);
                if (!acum.TryGetValue(key, out var v))
                {
                    v = (o.Tipo, new Dictionary<int, TimeSpan>(), new HashSet<DateOnly>());
                    acum[key] = v;
                }
                v.Sem.TryGetValue(l.Semana, out var cur);
                v.Sem[l.Semana] = cur + dur;
                v.Dias.Add(l.Fecha);
            }
        }

        var filas = acum
            .Select(kv => new HorasOperario(
                kv.Key.Turno, kv.Key.Operario, kv.Value.Tipo, kv.Value.Sem, kv.Value.Dias.Count,
                kv.Value.Sem.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b)))
            .OrderBy(f => f.Turno).ThenBy(f => f.Operario)
            .ToList();

        return new HorasReporte(semanas, filas);
    }

    /// <summary>Resumen por operario sobre todo el rango filtrado.</summary>
    public List<OperarioResumen> ResumenPorOperario(List<Lavado> lavados)
    {
        var porOp = AgruparPorOperario(lavados, l => "");
        return porOp
            .Select(kv => Construir(kv.Key.Op, kv.Value))
            .OrderBy(r => r.Operario)
            .Select(r => new OperarioResumen(r.Operario, r.Tipo, r.Camiones, r.Lavados, r.DiasTrabajados, r.Horas, r.Promedio))
            .ToList();
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

    public byte[] GenerarExcel(List<Lavado> lavados, List<MetricasTurno> metricas, TipoLavado? tipo)
    {
        using var wb = new XLWorkbook();
        EscribirMetricas(wb.AddWorksheet("Métricas"), metricas);
        EscribirHoras(wb.AddWorksheet("Horas por operario"), CalcularHorasPorOperario(lavados));
        EscribirResumenOperario(wb.AddWorksheet("Resumen operario"), ResumenPorOperario(lavados));
        EscribirResumenMes(wb.AddWorksheet("Resumen por mes"), ResumenPorMes(lavados));
        EscribirDetalle(wb.AddWorksheet("Detalle"), lavados, tipo);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void EscribirResumenOperario(IXLWorksheet ws, List<OperarioResumen> filas)
    {
        string[] h = { "Operario", "Tipo", "Lavados", "Días trab.", "Hs trabajadas", "Prom. lavado" };
        for (int c = 0; c < h.Length; c++)
            ws.Cell(1, c + 1).Value = h[c];
        ws.Range(1, 1, 1, h.Length).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);

        int row = 2;
        foreach (var r in filas)
        {
            ws.Cell(row, 1).Value = r.Operario;
            ws.Cell(row, 2).Value = r.Tipo.ToString();
            ws.Cell(row, 3).Value = r.Lavados;
            ws.Cell(row, 4).Value = r.DiasTrabajados;
            ws.Cell(row, 5).Value = FmtDur(r.Horas);
            ws.Cell(row, 6).Value = FmtDur(r.Promedio);
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
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

    private static void EscribirHoras(IXLWorksheet ws, HorasReporte rep)
    {
        ws.Cell(1, 1).Value = "Turno";
        ws.Cell(1, 2).Value = "Operario";
        ws.Cell(1, 3).Value = "Tipo";
        int col = 4;
        foreach (var sem in rep.Semanas)
            ws.Cell(1, col++).Value = $"Sem {sem}";
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
                ws.Cell(row, col++).Value = FmtDur(hs);
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
            ws.Cell(row, 1).Value = $"TURNO: {t.Turno}";
            ws.Range(row, 1, row, 8).Merge().Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);
            row++;

            string[] headers = { "Semana", "Lavados", "Total hs", "Prom. hs", "Operarios", "Prom. op.", "% Var. hs", "% Var. lavados" };
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
                ws.Cell(row, 4).Value = FmtDur(s.PromedioHoras);
                ws.Cell(row, 5).Value = s.TotalOperarios;
                ws.Cell(row, 6).Value = Math.Round(s.PromedioOperarios, 1);
                ws.Cell(row, 7).Value = s.VarHoras.HasValue ? s.VarHoras.Value.ToString("P1", Es) : "—";
                ws.Cell(row, 8).Value = s.VarLavados.HasValue ? s.VarLavados.Value.ToString("P1", Es) : "—";
                row++;
            }
            row++; // fila en blanco entre turnos
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
