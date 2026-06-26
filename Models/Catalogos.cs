using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlLavados.Models;

/// <summary>Operario del catálogo (editable desde la UI).</summary>
public class Operario
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string Nombre { get; set; } = "";

    [MaxLength(40)]
    public string Apellido { get; set; } = "";

    [MaxLength(15)]
    public string Dni { get; set; } = "";

    public TipoOperario Tipo { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>Como se muestra y se guarda en los lavados: "APELLIDO Nombre".</summary>
    [NotMapped]
    public string NombreCompleto => $"{Apellido} {Nombre}".Trim();
}

/// <summary>Patente de camión del catálogo.</summary>
public class Patente
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Codigo { get; set; } = "";

    public bool Activo { get; set; } = true;
}

/// <summary>Frigorífico del catálogo.</summary>
public class Frigorifico
{
    public int Id { get; set; }

    [MaxLength(60)]
    public string Nombre { get; set; } = "";

    public bool Activo { get; set; } = true;
}
