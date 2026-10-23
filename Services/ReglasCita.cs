namespace backend.Services
{
    public static class ReglasCita
    {
        // Las sesiones duran una hora: la disponibilidad se divide en bloques de 1 h.
        public static readonly TimeSpan DuracionSesion = TimeSpan.FromHours(1);

        // Al terminar, el tutor confirma si la sesión se impartió o si el
        // estudiante no asistió. Si no lo hace en este plazo, se da por impartida
        // para que el estudiante pueda calificarla.
        public static readonly TimeSpan PlazoConfirmacion = TimeSpan.FromDays(3);

        // Una cita aceptada sigue en curso hasta que termina su hora; las que
        // empezaron antes de este instante ya terminaron.
        public static DateTime InicioDeSesionesEnCurso(DateTime ahora) => ahora - DuracionSesion;

        // Las sesiones aceptadas que empezaron antes de este instante ya
        // terminaron y vencieron su plazo de confirmación.
        public static DateTime InicioDeSesionesSinConfirmarVencidas(DateTime ahora) =>
            ahora - DuracionSesion - PlazoConfirmacion;
    }
}
