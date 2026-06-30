namespace ControlLavados.Services;

/// <summary>Normaliza textos para mantener un formato único en toda la base.</summary>
public static class Normalizador
{
    /// <summary>
    /// Patente en formato unificado y en MAYÚSCULAS, separada por bloques:
    ///   Mercosur (7 caracteres) → "AB 123 CD"
    ///   Vieja    (6 caracteres) → "ABC 123"
    ///   Otros largos           → sin espacios (tal cual, en mayúscula).
    /// Quita espacios, guiones y cualquier símbolo intermedio antes de reformatear.
    /// </summary>
    public static string Patente(string? raw)
    {
        var s = new string((raw ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return s.Length switch
        {
            7 => $"{s[..2]} {s[2..5]} {s[5..]}",
            6 => $"{s[..3]} {s[3..]}",
            _ => s,
        };
    }

    /// <summary>Texto en MAYÚSCULAS, recortado y con espacios internos colapsados a uno.</summary>
    public static string Mayus(string? raw)
    {
        var s = (raw ?? "").Trim().ToUpperInvariant();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }
}
