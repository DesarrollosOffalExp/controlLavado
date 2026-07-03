using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ControlLavados.Data;
using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Services;

public record ImportacionResultado(bool Ok, int Importados, int Omitidos, int FilasLeidas,
    int OperariosNuevos, int PatentesNuevas, string? Error);

public record ImportPatentesResultado(bool Ok, int Nuevas, int Actualizadas, int FilasLeidas, string? Error);

public record ImportSimpleResultado(bool Ok, int Nuevos, int FilasLeidas, string? Error);

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
            return new(false, 0, 0, 0, 0, 0, $"No se encontró el archivo: {ruta}");
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await ImportarAsync(fs);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, 0, 0, ex.Message);
        }
    }

    // ---------- Importación del maestro de patentes ----------

    public async Task<ImportPatentesResultado> ImportarPatentesDesdeArchivoAsync(string ruta)
    {
        if (!File.Exists(ruta))
            return new(false, 0, 0, 0, $"No se encontró el archivo: {ruta}");
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await ImportarPatentesAsync(fs);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, ex.Message);
        }
    }

    /// <summary>
    /// Importa un maestro de patentes con columnas Dominio/Patente, Modelo, Marca y Tipo Unidad.
    /// Crea las nuevas y actualiza modelo/marca/tipo de las existentes.
    /// </summary>
    public async Task<ImportPatentesResultado> ImportarPatentesAsync(Stream xlsx)
    {
        using var limpio = LimpiarPaquete(xlsx);
        using var wb = new XLWorkbook(limpio);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null) return new(false, 0, 0, 0, "El archivo no tiene hojas.");

        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in ws.Row(1).CellsUsed())
            col[Norm(cell.GetString())] = cell.Address.ColumnNumber;
        int C(params string[] claves) => claves.Select(k => col.TryGetValue(Norm(k), out var n) ? n : -1)
            .FirstOrDefault(n => n > 0, -1);

        int cDom = C("Dominio", "Patente", "Patente de Unidad"),
            cMod = C("Modelo"), cMar = C("Marca"), cTipo = C("Tipo Unidad", "Tipo de unidad", "TipoUnidad");
        if (cDom < 0)
            return new(false, 0, 0, 0, "No se encontró la columna «Dominio» (o «Patente»).");

        await using var db = await _factory.CreateDbContextAsync();
        var existentes = (await db.Patentes.ToListAsync())
            .ToDictionary(p => p.Codigo, p => p, StringComparer.OrdinalIgnoreCase);

        int nuevas = 0, actualizadas = 0, leidas = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var codigo = ws.Cell(r, cDom).GetString().Trim();
            if (string.IsNullOrWhiteSpace(codigo)) continue;
            leidas++;

            string? modelo = cMod > 0 ? Vacio(ws.Cell(r, cMod).GetString()) : null;
            string? marca = cMar > 0 ? Vacio(ws.Cell(r, cMar).GetString()) : null;
            string? tipo = cTipo > 0 ? Vacio(ws.Cell(r, cTipo).GetString()) : null;

            if (existentes.TryGetValue(codigo, out var p))
            {
                p.Modelo = modelo; p.Marca = marca; p.TipoUnidad = tipo;
                db.Patentes.Update(p);
                actualizadas++;
            }
            else
            {
                var nueva = new Patente { Codigo = codigo, Modelo = modelo, Marca = marca, TipoUnidad = tipo };
                db.Patentes.Add(nueva);
                existentes[codigo] = nueva;
                nuevas++;
            }
        }

        await db.SaveChangesAsync();
        return new(true, nuevas, actualizadas, leidas, null);
    }

    private static string? Vacio(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------- Importación de frigoríficos ----------

    public async Task<ImportSimpleResultado> ImportarFrigorificosDesdeArchivoAsync(string ruta)
    {
        if (!File.Exists(ruta))
            return new(false, 0, 0, $"No se encontró el archivo: {ruta}");
        try
        {
            using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await ImportarFrigorificosAsync(fs);
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, ex.Message);
        }
    }

    /// <summary>Importa una columna con nombres de frigoríficos (Proveedor / Nombre / Frigorífico).</summary>
    public async Task<ImportSimpleResultado> ImportarFrigorificosAsync(Stream xlsx)
    {
        using var limpio = LimpiarPaquete(xlsx);
        using var wb = new XLWorkbook(limpio);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null) return new(false, 0, 0, "El archivo no tiene hojas.");

        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in ws.Row(1).CellsUsed())
            col[Norm(cell.GetString())] = cell.Address.ColumnNumber;
        int cNom = new[] { "Proveedor", "Nombre", "Frigorífico", "Frigorifico" }
            .Select(k => col.TryGetValue(Norm(k), out var n) ? n : -1).FirstOrDefault(n => n > 0, -1);
        if (cNom < 0) cNom = 1; // si no hay encabezado reconocido, uso la primera columna

        await using var db = await _factory.CreateDbContextAsync();
        var existentes = new HashSet<string>(
            (await db.Frigorificos.ToListAsync()).Select(f => f.Nombre), StringComparer.OrdinalIgnoreCase);

        int nuevos = 0, leidas = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            var nombre = ws.Cell(r, cNom).GetString().Trim();
            if (string.IsNullOrWhiteSpace(nombre)) continue;
            leidas++;
            if (existentes.Add(nombre))
            {
                db.Frigorificos.Add(new Frigorifico { Nombre = nombre });
                nuevos++;
            }
        }
        await db.SaveChangesAsync();
        return new(true, nuevos, leidas, null);
    }

    public async Task<ImportacionResultado> ImportarAsync(Stream xlsx)
    {
        // Algunos export del Forms traen pivot caches con XML inválido que ClosedXML
        // rechaza al abrir. Limpiamos esas partes del paquete (.zip) antes de leer.
        using var limpio = LimpiarPaquete(xlsx);
        using var wb = new XLWorkbook(limpio);
        if (!wb.TryGetWorksheet(Hoja, out var ws))
            return new(false, 0, 0, 0, 0, 0, $"El archivo no tiene la hoja «{Hoja}».");

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
            cOffal = C("N° Operario Offal"), cAgencia = C("N° Operario Agencia"),
            cTurno = C("Turno");
        // El Forms actual no trae frigorífico; se detecta por si se agrega a futuro.
        int cFrig = new[] { "Frigorífico", "Frigorifico", "Proveedor" }.Select(C).FirstOrDefault(n => n > 0);

        if (cFecha < 0 || cPat < 0 || cAtr < 0)
            return new(false, 0, 0, 0, 0, 0, "No se encontraron las columnas esperadas (Fecha / Patente / Hora inicio Atraco).");

        await using var db = await _factory.CreateDbContextAsync();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // ---- Pasada 1: deducir el tipo y el turno de cada operario y juntar patentes/operarios ----
        var votos = new Dictionary<string, (int Offal, int Contrato)>(StringComparer.OrdinalIgnoreCase);
        var turnoVotos = new Dictionary<string, (int Manana, int Noche)>(StringComparer.OrdinalIgnoreCase);
        var patentesArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nombresArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var pat = row.Cell(cPat).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(pat)) patentesArchivo.Add(pat);

            var nombres = SepararNombres(cOps > 0 ? row.Cell(cOps).GetString() : "");
            foreach (var n in nombres) nombresArchivo.Add(n);

            // Turno de la fila (Mañana / Noche) para asignar el turno de cada operario.
            var turnoTxt = (cTurno > 0 ? row.Cell(cTurno).GetString() : "").Trim().ToLowerInvariant();
            int idxTurno = turnoTxt.Contains("noche") ? 1 : (turnoTxt.Contains("mañana") || turnoTxt.Contains("manana")) ? 0 : -1;
            if (idxTurno >= 0)
                foreach (var n in nombres)
                {
                    turnoVotos.TryGetValue(n, out var tv);
                    turnoVotos[n] = idxTurno == 0 ? (tv.Manana + 1, tv.Noche) : (tv.Manana, tv.Noche + 1);
                }

            int nOffal = (int)(LeerNumero(row.Cell(cOffal)) ?? 0);
            int nAgencia = (int)(LeerNumero(row.Cell(cAgencia)) ?? 0);
            // Solo cuentan las filas de un único tipo.
            int idxTipo = (nombres.Count > 0 && nOffal == nombres.Count && nAgencia == 0) ? 0
                        : (nombres.Count > 0 && nAgencia == nombres.Count && nOffal == 0) ? 1 : -1;
            if (idxTipo < 0) continue;
            foreach (var n in nombres)
            {
                votos.TryGetValue(n, out var v);
                votos[n] = idxTipo == 0 ? (v.Offal + 1, v.Contrato) : (v.Offal, v.Contrato + 1);
            }
        }

        // Catálogo actual (por nombre completo y por patente).
        var opsCat = await db.Operarios.ToListAsync();
        var tipoCat = new Dictionary<string, TipoOperario>(StringComparer.OrdinalIgnoreCase);
        var nombresCat = new HashSet<string>(opsCat.Select(o => o.NombreCompleto), StringComparer.OrdinalIgnoreCase);
        foreach (var o in opsCat) tipoCat[o.NombreCompleto] = o.Tipo;
        var patentesCat = new HashSet<string>(
            (await db.Patentes.ToListAsync()).Select(p => p.Codigo), StringComparer.OrdinalIgnoreCase);

        // Tipo final por operario: mayoría de votos; si no hay, lo que diga el catálogo; default Contrato.
        TipoOperario TipoFinal(string nombre)
        {
            if (votos.TryGetValue(nombre, out var v) && (v.Offal > 0 || v.Contrato > 0))
                return v.Offal > v.Contrato ? TipoOperario.Offal : TipoOperario.Contrato;
            return tipoCat.TryGetValue(nombre, out var t) ? t : TipoOperario.Contrato;
        }

        // Turno final por operario: mayoría de la columna "Turno" del archivo (Mañana / Noche).
        string? TurnoFinal(string nombre)
        {
            if (turnoVotos.TryGetValue(nombre, out var tv) && (tv.Manana > 0 || tv.Noche > 0))
                return tv.Manana >= tv.Noche ? Turnos.Mañana : Turnos.Noche;
            return null;
        }

        // ---- Completar catálogos (sin tocar los existentes, salvo el turno si está vacío) ----
        int opsNuevos = 0, patsNuevas = 0;
        foreach (var n in nombresArchivo.Where(n => !nombresCat.Contains(n)))
        {
            var idx = n.IndexOf(' ');
            var apellido = idx < 0 ? n : n[..idx];
            var nombre = idx < 0 ? "" : n[(idx + 1)..].Trim();
            db.Operarios.Add(new Operario { Apellido = apellido, Nombre = nombre, Tipo = TipoFinal(n), Turno = TurnoFinal(n) });
            nombresCat.Add(n);
            opsNuevos++;
        }
        // Completar el turno de los operarios que ya existían y todavía no lo tienen.
        foreach (var o in opsCat.Where(o => string.IsNullOrEmpty(o.Turno)))
        {
            var t = TurnoFinal(o.NombreCompleto);
            if (t is not null) { o.Turno = t; db.Operarios.Update(o); }
        }
        foreach (var p in patentesArchivo.Where(p => !patentesCat.Contains(p)))
        {
            db.Patentes.Add(new Patente { Codigo = p });
            patentesCat.Add(p);
            patsNuevas++;
        }

        // Claves ya existentes para no duplicar lavados.
        var existentes = (await db.Lavados
                .Where(l => l.Tipo == TipoLavado.Camion && l.InicioAtraco != null)
                .Select(l => new { l.Patente, l.InicioAtraco })
                .ToListAsync())
            .Select(x => $"{x.Patente}|{x.InicioAtraco:yyyyMMddHHmm}")
            .ToHashSet();

        int leidas = 0, importados = 0, omitidos = 0;

        // ---- Pasada 2: crear los lavados ----
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
            // Si una etapa posterior tiene hora menor al atraco, el lavado cruzó la
            // medianoche: la pasamos al día siguiente para que las duraciones no den negativas.
            DateTime? CombRoll(TimeSpan? h)
            {
                if (!h.HasValue) return null;
                var dt = Comb(h);
                return dt < inicioAtraco ? dt.AddDays(1) : dt;
            }

            var clave = $"{patente}|{inicioAtraco:yyyyMMddHHmm}";
            if (!existentes.Add(clave)) { omitidos++; continue; }

            var nombres = SepararNombres(cOps > 0 ? row.Cell(cOps).GetString() : "");

            var inicioLav = LeerHora(cIniLav > 0 ? row.Cell(cIniLav) : null);
            var finLav = LeerHora(cFinLav > 0 ? row.Cell(cFinLav) : null);
            var desatraco = LeerHora(cDes > 0 ? row.Cell(cDes) : null);
            var marca = cMarca > 0 ? LeerFechaHora(row.Cell(cMarca)) : null;

            var desatracoDt = CombRoll(desatraco);
            var lavado = new Lavado
            {
                Tipo = TipoLavado.Camion,
                Patente = patente,
                Frigorifico = cFrig > 0 ? (row.Cell(cFrig).GetString().Trim() is { Length: > 0 } fr ? fr : null) : null,
                Fecha = f,
                InicioAtraco = inicioAtraco,
                InicioLavado = CombRoll(inicioLav),
                FinLavado = CombRoll(finLav),
                Desatraco = desatracoDt,
                Incidencias = cInc > 0 ? row.Cell(cInc).GetString().Trim() : null,
                Estado = EstadoLavado.Finalizado,
                CreadoEn = marca ?? inicioAtraco,
                Finalizado = desatracoDt ?? (marca ?? inicioAtraco),
                OperariosPorSemana = nombres.Count,
                Operarios = nombres.Select(n => new LavadoOperario { Nombre = n, Tipo = TipoFinal(n) }).ToList(),
            };

            db.Lavados.Add(lavado);
            importados++;
        }

        await db.SaveChangesAsync();
        return new(true, importados, omitidos, leidas, opsNuevos, patsNuevas, null);
    }

    private static List<string> SepararNombres(string celda) =>
        celda.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
