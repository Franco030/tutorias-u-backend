using System;
using System.Collections.Generic;

namespace backend.Models;

public partial class Materia
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public int CategoriaId { get; set; }

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual ICollection<Cita> Cita { get; set; } = new List<Cita>();

    public virtual ICollection<SolicitudMateria> SolicitudMateria { get; set; } = new List<SolicitudMateria>();

    public virtual ICollection<Usuario> Estudiantes { get; set; } = new List<Usuario>();

    public virtual ICollection<Usuario> Tutors { get; set; } = new List<Usuario>();
}
