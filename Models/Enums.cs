namespace ControlLavados.Models;

/// <summary>Tipo de proceso de lavado / circuito.</summary>
public enum TipoLavado
{
    Camion = 0,
    Hielo = 1,
    Hiel = 2,
    Varias = 3
}

/// <summary>Origen del operario, usado para los contadores de la grilla.</summary>
public enum TipoOperario
{
    Offal = 0,
    Contrato = 1
}

/// <summary>
/// Perfil de acceso del usuario.
/// Operario: solo pantallas de carga. Administrativo: + Reportes y Configuración.
/// Admin: todo, incluida la gestión de usuarios.
/// </summary>
public enum RolUsuario
{
    Operario = 0,
    Administrativo = 1,
    Admin = 2
}

/// <summary>
/// Estados del proceso.
/// Camión:  Pendiente -> Atracado -> Lavando -> LavadoTerminado -> Finalizado
/// Hielo:   Pendiente -> Lavando -> Finalizado
/// </summary>
public static class EstadoLavado
{
    public const string Pendiente = "Pendiente";
    public const string Atracado = "Atracado";
    public const string Lavando = "Lavando";
    public const string LavadoTerminado = "Lavado terminado";
    public const string Finalizado = "Finalizado";
}

/// <summary>Turno calculado automáticamente según la hora de inicio.</summary>
public static class Turnos
{
    public const string Mañana = "Mañana";
    public const string Tarde = "Tarde";
    public const string Noche = "Noche";

    /// <summary>
    /// Mañana: 06:00–17:59. Tarde/Noche: 18:00–05:59 (incluye la madrugada).
    /// Operación de dos turnos: todo lo que no es Mañana es Tarde.
    /// </summary>
    public static string DesdeHora(DateTime hora)
    {
        var h = hora.Hour;
        return (h >= 6 && h < 18) ? Mañana : Tarde;
    }

    /// <summary>True si la hora dada cae en el turno Mañana (06:00–17:59).</summary>
    public static bool EsManana(DateTime hora) => hora.Hour >= 6 && hora.Hour < 18;
}
