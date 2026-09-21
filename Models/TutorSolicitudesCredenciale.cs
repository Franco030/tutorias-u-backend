using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class TutorSolicitudesCredenciale
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public string UrlCredencial { get; set; } = null!;

    public DateTime FechaSolicitud { get; set; }

    public virtual ICollection<SolicitudMateria> SolicitudMateria { get; set; } = new List<SolicitudMateria>();

    public virtual Usuario Usuario { get; set; } = null!;
}
