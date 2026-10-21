using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Resena
{
    public int Id { get; set; }

    public int CitaId { get; set; }

    public int EstudianteId { get; set; }

    public int TutorId { get; set; }

    public int Puntuacion { get; set; }

    public string? Comentario { get; set; }

    public DateTime FechaCreacion { get; set; }

    public virtual Cita Cita { get; set; } = null!;

    public virtual Usuario Estudiante { get; set; } = null!;

    public virtual Usuario Tutor { get; set; } = null!;
}
