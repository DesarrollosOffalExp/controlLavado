using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlLavados.Models;

/// <summary>Usuario de la aplicación. El acceso es por Microsoft 365 (Entra ID);
/// esta tabla define el perfil (rol) de cada uno.</summary>
public class Usuario
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Email { get; set; } = "";

    [MaxLength(120)]
    public string? Nombre { get; set; }

    /// <summary>Perfil de acceso: Operario (carga), Administrativo (+ Reportes/Config)
    /// o Admin (todo, incluida gestión de usuarios).</summary>
    public RolUsuario Rol { get; set; } = RolUsuario.Operario;

    public bool Activo { get; set; } = true;

    public DateTime? UltimoAcceso { get; set; }

    /// <summary>Hash de la contraseña para cuentas locales (email/contraseña).
    /// Null para usuarios que entran por Microsoft 365.</summary>
    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    /// <summary>Admin = ve todo, incluida la gestión de usuarios.</summary>
    [NotMapped]
    public bool EsAdmin => Rol == RolUsuario.Admin;

    /// <summary>Administrativo o Admin = puede ver Reportes y Configuración.</summary>
    [NotMapped]
    public bool PuedeGestionar => Rol >= RolUsuario.Administrativo;
}
