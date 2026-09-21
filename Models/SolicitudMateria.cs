using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class SolicitudMateria
{
    public int Id { get; set; }

    public int SolicitudId { get; set; }

    public int MateriaId { get; set; }

    public virtual Materia Materia { get; set; } = null!;

    public virtual TutorSolicitudesCredenciale Solicitud { get; set; } = null!;
}
