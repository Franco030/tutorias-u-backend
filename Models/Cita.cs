using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Cita
{
    public int Id { get; set; }

    public int EstudianteId { get; set; }

    public int TutorId { get; set; }

    public int MateriaId { get; set; }

    public DateTime FechaHoraInicio { get; set; }

    public string Estado { get; set; } = null!;

    public string? LinkReunion { get; set; }

    public string? Notas { get; set; }

    public virtual Usuario Estudiante { get; set; } = null!;

    public virtual Materia Materia { get; set; } = null!;

    public virtual Usuario Tutor { get; set; } = null!;
}
