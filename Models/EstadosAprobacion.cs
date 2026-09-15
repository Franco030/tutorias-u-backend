using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class EstadosAprobacion
{
    public int Id { get; set; }

    public string Estado { get; set; } = null!;

    public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
