namespace ControlLavados.Models;

/// <summary>
/// Valores fijos y datos de seed inicial. Los catálogos de Operarios, Patentes
/// y Frigoríficos viven en la base y se editan desde la UI; acá quedan solo las
/// dársenas (fijas) y los sectores de los circuitos que no son camión.
/// </summary>
public static class Catalogo
{
    // -- Dársenas (solo camiones) --
    public static readonly string[] Darsenas =
    {
        "Dársena 1", "Dársena 2",
    };

    /// <summary>Sector fijo para los circuitos que no son camión.</summary>
    public static string SectorDe(TipoLavado tipo) => tipo switch
    {
        TipoLavado.Hielo => "Fábrica de Hielo",
        TipoLavado.Hiel => "Hiel",
        TipoLavado.Varias => "Varias",
        _ => "",
    };

    /// <summary>Nombre del circuito para títulos y navegación.</summary>
    public static string NombreCircuito(TipoLavado tipo) => tipo switch
    {
        TipoLavado.Camion => "Camiones",
        TipoLavado.Hielo => "Fábrica de Hielo",
        TipoLavado.Hiel => "Hiel",
        TipoLavado.Varias => "Varias",
        _ => tipo.ToString(),
    };

    // ---------- Datos de seed inicial (solo al crear la base) ----------

    public static readonly Operario[] OperariosSeed =
    {
        new() { Apellido = "TENAGLIA", Nombre = "RODRIGO", Dni = "30111222", Tipo = TipoOperario.Contrato },
        new() { Apellido = "FERNANDEZ", Nombre = "NICOLAS", Dni = "31222333", Tipo = TipoOperario.Contrato },
        new() { Apellido = "BARRIENTOS", Nombre = "AGUSTIN", Dni = "32333444", Tipo = TipoOperario.Contrato },
        new() { Apellido = "GOMEZ", Nombre = "MARTIN", Dni = "28999000", Tipo = TipoOperario.Offal },
        new() { Apellido = "PEREZ", Nombre = "JUAN", Dni = "27888111", Tipo = TipoOperario.Offal },
        new() { Apellido = "LOPEZ", Nombre = "DIEGO", Dni = "29777222", Tipo = TipoOperario.Offal },
    };

    public static readonly string[] PatentesSeed =
    {
        "AC356EI", "AB186BD", "AD742JK", "AE091LM", "AF553NP",
    };

    public static readonly string[] FrigorificosSeed =
    {
        "Offal", "Frigorífico Norte", "Frigorífico Sur", "Externo",
    };
}
