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

    public string? PasswordHash { get; set; }

    public bool IsEmailVerified { get; set; }

    public int EstadoAprobacionId { get; set; }

    public bool OnboardingCompleto { get; set; }

    public decimal? CalificacionPromedio { get; set; }

    public virtual EstadosAprobacion EstadoAprobacion { get; set; } = null!;

    public virtual ICollection<TutorSolicitudesCredenciale> TutorSolicitudesCredenciales { get; set; } = new List<TutorSolicitudesCredenciale>();

    public virtual ICollection<Materia> Materia { get; set; } = new List<Materia>();

    public virtual ICollection<Materia> MateriaNavigation { get; set; } = new List<Materia>();
}
