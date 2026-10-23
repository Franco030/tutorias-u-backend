namespace backend.Services
{
    public static class ReglasCita
    {
        // Las sesiones duran una hora: la disponibilidad se divide en bloques de 1 h.
        public static readonly TimeSpan DuracionSesion = TimeSpan.FromHours(1);

        // Una cita aceptada sigue en curso hasta que termina su hora; las que
        // empezaron antes de este instante ya terminaron.
        public static DateTime InicioDeSesionesEnCurso(DateTime ahora) => ahora - DuracionSesion;
    }
}
