using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Bloqueo
{
    public int Id { get; set; }

    public int TutorId { get; set; }

    public DateOnly Fecha { get; set; }

    public string? Motivo { get; set; }

    public virtual Usuario Tutor { get; set; } = null!;
}
