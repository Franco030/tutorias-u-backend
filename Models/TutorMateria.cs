namespace backend.Models;

public partial class TutorMateria
{
    public int TutorId { get; set; }

    public int MateriaId { get; set; }

    public virtual Materia Materia { get; set; } = null!;

    public virtual Usuario Tutor { get; set; } = null!;
}
