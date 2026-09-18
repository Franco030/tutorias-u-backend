using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Categoria
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public virtual ICollection<Materia> Materia { get; set; } = new List<Materia>();
}
