using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class TutorDisponibilidad
{
    public int Id { get; set; }

    public int TutorId { get; set; }

    public byte DiaSemana { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }

    public virtual Usuario Tutor { get; set; } = null!;
}
