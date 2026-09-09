using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Usuario
{
    public int Id { get; set; }

    public string Email { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string? FotoUrl { get; set; }

    public string Rol { get; set; } = null!;

    public string AuthProvider { get; set; } = null!;

    public string? ProviderId { get; set; }

    public DateTime FechaRegistro { get; set; }
}
