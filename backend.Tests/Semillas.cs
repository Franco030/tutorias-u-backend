using backend.Data;
using backend.Enums;
using backend.Models;

namespace backend.Tests;

// Datos mínimos para agendar: un tutor aprobado que imparte la materia.
public static class Semillas
{
    public static void TutorAprobado(AppDbContext context, int tutorId, int materiaId, string nombreMateria = "Cálculo")
    {
        var materia = context.Materias.Find(materiaId) ?? context.Materias.Add(new Materia { Id = materiaId, Nombre = nombreMateria }).Entity;
        var tutor = new Usuario
        {
            Id = tutorId,
            Email = $"tutor{tutorId}@example.com",
            Nombre = $"Tutor {tutorId}",
            Rol = "Tutor",
            AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Aprobado
        };
        tutor.MateriaNavigation.Add(materia);
        context.Usuarios.Add(tutor);
        context.SaveChanges();
    }
}
