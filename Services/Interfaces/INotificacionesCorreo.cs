namespace backend.Services.Interfaces
{
    public sealed record DatosCitaCorreo(
        string Materia,
        DateTime FechaHoraInicio,
        string EstudianteNombre,
        string EstudianteEmail,
        string TutorNombre,
        string TutorEmail,
        string? Tema = null);

    /*
     * Avisos por correo de cambios en citas y postulaciones. Los métodos no
     * lanzan excepciones: si algo falla se registra y la operación sigue.
     */
    public interface INotificacionesCorreo
    {
        void CitaSolicitada(DatosCitaCorreo cita);

        void CitaAceptada(DatosCitaCorreo cita, string? linkReunion, string? notas);

        void CitaRechazada(DatosCitaCorreo cita, string? motivo);

        void CitaCancelada(DatosCitaCorreo cita, bool canceladaPorTutor, string? motivo);

        void TutorAprobado(string nombre, string email);

        void TutorRechazado(string nombre, string email, string? motivo);
    }
}
