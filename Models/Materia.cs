using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Materia
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public string Categoria { get; set; } = null!;

    public virtual ICollection<Usuario> Estudiantes { get; set; } = new List<Usuario>();
}
