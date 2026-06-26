using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public record ImportacionResultado(bool Ok, int Importados, int Omitidos, int FilasLeidas, string? Error);

/// <summary>
/// Importa las respuestas del Forms ("Respuestas de formulario 1") como lavados
/// de camión finalizados, para que aparezcan en la reportería.
/// </summary>
public class ImportacionService
{
    private const string Hoja = "Respuestas de formulario 1";
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ImportacionService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<ImportacionResultado> ImportarDesdeArchivoAsync(string ruta)
    {
        if (!File.Exists(ruta))
            return new(false, 0, 0, 0, $"No se encontró el archivo: {ruta}");
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await ImportarAsync(fs);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, ex.Message);
        }
    }

    public async Task<ImportacionResultado> ImportarAsync(Stream xlsx)
    {
        // Algunos export del Forms traen pivot caches con XML inválido que ClosedXML
        // rechaza al abrir. Limpiamos esas partes del paquete (.zip) antes de leer.
        using var limpio = LimpiarPaquete(xlsx);
        using var wb = new XLWorkbook(limpio);
        if (!wb.TryGetWorksheet(Hoja, out var ws))
            return new(false, 0, 0, 0, $"El archivo no tiene la hoja «{Hoja}».");

        // Mapa de columnas por encabezado (fila 1).
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = ws.Row(1);
        foreach (var cell in headerRow.CellsUsed())
            col[Norm(cell.GetString())] = cell.Address.ColumnNumber;

        int C(string clave) => col.TryGetValue(Norm(clave), out var n) ? n : -1;
        int cFecha = C("Fecha"), cPat = C("Patente de Unidad"), cOps = C("Seleccionar Operario"),
            cAtr = C("Hora inicio Atraco"), cIniLav = C("Hora inicio de lavado"),
            cFinLav = C("Hora fin de lavado"), cDes = C("Hora de desatraco"),
            cInc = C("Incidencias genrales"), cMarca = C("Marca temporal"),
            cOffal = C("N° Operario Offal"), cAgencia = C("N° Operario Agencia");

        if (cFecha < 0 || cPat < 0 || cAtr < 0)
            return new(false, 0, 0, 0, "No se encontraron las columnas esperadas (Fecha / Patente / Hora inicio Atraco).");

        await using var db = await _factory.CreateDbContextAsync();

        // Tipos de operario conocidos del catálogo.
        var tipoCat = new Dictionary<string, TipoOperario>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in await db.Operarios.ToListAsync())
            tipoCat[o.NombreCompleto] = o.Tipo;

        // Claves ya existentes para no duplicar.
        var existentes = (await db.Lavados
                .Where(l => l.Tipo == TipoLavado.Camion && l.InicioAtraco != null)
                .Select(l => new { l.Patente, l.InicioAtraco })
                .ToListAsync())
            .Select(x => $"{x.Patente}|{x.InicioAtraco:yyyyMMddHHmm}")
            .ToHashSet();

        int leidas = 0, importados = 0, omitidos = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var fecha = LeerFecha(row.Cell(cFecha));
            var patente = row.Cell(cPat).GetString().Trim();
            var atraco = LeerHora(row.Cell(cAtr));
            if (fecha is null || string.IsNullOrWhiteSpace(patente) || atraco is null)
                continue; // fila vacía / incompleta

            leidas++;

            var f = fecha.Value;
            DateTime Comb(TimeSpan? h) => h.HasValue ? f.ToDateTime(TimeOnly.FromTimeSpan(h.Value)) : default;

            var inicioAtraco = Comb(atraco);
            var clave = $"{patente}|{inicioAtraco:yyyyMMddHHmm}";
            if (!existentes.Add(clave)) { omitidos++; continue; }

            var nombres = (cOps > 0 ? row.Cell(cOps).GetString() : "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            int nOffal = (int)(LeerNumero(row.Cell(cOffal)) ?? 0);
            int nAgencia = (int)(LeerNumero(row.Cell(cAgencia)) ?? 0);

            TipoOperario TipoDe(string nombre)
            {
                // Si la fila es de un solo tipo, lo aplico a todos.
                if (nombres.Count > 0 && nOffal == nombres.Count && nAgencia == 0) return TipoOperario.Offal;
                if (nombres.Count > 0 && nAgencia == nombres.Count && nOffal == 0) return TipoOperario.Contrato;
                return tipoCat.TryGetValue(nombre, out var t) ? t : TipoOperario.Contrato;
            }

            var inicioLav = LeerHora(cIniLav > 0 ? row.Cell(cIniLav) : null);
            var finLav = LeerHora(cFinLav > 0 ? row.Cell(cFinLav) : null);
            var desatraco = LeerHora(cDes > 0 ? row.Cell(cDes) : null);
            var marca = cMarca > 0 ? LeerFechaHora(row.Cell(cMarca)) : null;

            var lavado = new Lavado
            {
                Tipo = TipoLavado.Camion,
                Patente = patente,
                Fecha = f,
                InicioAtraco = inicioAtraco,
                InicioLavado = inicioLav.HasValue ? Comb(inicioLav) : null,
                FinLavado = finLav.HasValue ? Comb(finLav) : null,
                Desatraco = desatraco.HasValue ? Comb(desatraco) : null,
                Incidencias = cInc > 0 ? row.Cell(cInc).GetString().Trim() : null,
                Estado = EstadoLavado.Finalizado,
                CreadoEn = marca ?? inicioAtraco,
                Finalizado = desatraco.HasValue ? Comb(desatraco) : (marca ?? inicioAtraco),
                OperariosPorSemana = nombres.Count,
                Operarios = nombres.Select(n => new LavadoOperario { Nombre = n, Tipo = TipoDe(n) }).ToList(),
            };

            db.Lavados.Add(lavado);
            importados++;
        }

        await db.SaveChangesAsync();
        return new(true, importados, omitidos, leidas, null);
    }

    // ---------- Limpieza del paquete xlsx ----------

    /// <summary>
    /// Reescribe el .xlsx descartando las partes de tablas dinámicas/pivot caches
    /// (que en los export del Forms suelen venir con XML inválido) y sus referencias.
    /// </summary>
    private static MemoryStream LimpiarPaquete(Stream original)
    {
        if (original.CanSeek) original.Position = 0;
        var outMs = new MemoryStream();
        using (var src = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true))
        using (var dst = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var lower = entry.FullName.ToLowerInvariant();
                if (lower.Contains("pivotcache") || lower.Contains("pivottable"))
                    continue; // descarta la parte conflictiva (incluido su .rels malformado)

                using var es = entry.Open();
                var newEntry = dst.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var ns = newEntry.Open();

                if (lower.EndsWith(".rels") || lower.EndsWith("[content_types].xml"))
                {
                    using var sr = new StreamReader(es);
                    var texto = LimpiarReferencias(sr.ReadToEnd());
                    var bytes = Encoding.UTF8.GetBytes(texto);
                    ns.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    es.CopyTo(ns);
                }
            }
        }
        outMs.Position = 0;
        return outMs;
    }

    private static string LimpiarReferencias(string xml)
    {
        xml = Regex.Replace(xml, @"<Relationship\b[^>]*?(pivotCache|pivotTable)[^>]*?/>", "",
            RegexOptions.IgnoreCase);
        xml = Regex.Replace(xml, @"<Override\b[^>]*?(pivotCache|pivotTable)[^>]*?/>", "",
            RegexOptions.IgnoreCase);
        return xml;
    }

    // ---------- Helpers de lectura ----------

    private static string Norm(string s) => s.Trim().ToLowerInvariant();

    private static DateOnly? LeerFecha(IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var dt)) return DateOnly.FromDateTime(dt);
        if (DateTime.TryParse(cell.GetString(), out var dt2)) return DateOnly.FromDateTime(dt2);
        return null;
    }

    private static DateTime? LeerFechaHora(IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var dt)) return dt;
        if (DateTime.TryParse(cell.GetString(), out var dt2)) return dt2;
        return null;
    }

    private static TimeSpan? LeerHora(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty()) return null;
        if (cell.TryGetValue<TimeSpan>(out var ts)) return ts;
        if (cell.TryGetValue<DateTime>(out var dt)) return dt.TimeOfDay;
        var s = cell.GetString().Trim();
        if (TimeSpan.TryParse(s, out var ts2)) return ts2;
        if (DateTime.TryParse(s, out var dt2)) return dt2.TimeOfDay;
        return null;
    }

    private static double? LeerNumero(IXLCell? cell)
    {
        if (cell is null || cell.IsEmpty()) return null;
        if (cell.TryGetValue<double>(out var d)) return d;
        if (double.TryParse(cell.GetString(), out var d2)) return d2;
        return null;
    }
}
