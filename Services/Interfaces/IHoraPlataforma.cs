namespace backend.Services.Interfaces
{
    /*
     * Hora de la plataforma. Las citas y la disponibilidad se guardan en la hora
     * local de la universidad (sin zona), así que toda comparación con "ahora"
     * debe hacerse en esa misma hora local y no en UTC.
     */
    public interface IHoraPlataforma
    {
        DateTime Ahora { get; }

        // Para lo que sale de la plataforma con zona explícita (archivos .ics).
        DateTime AUtc(DateTime horaLocal);
    }
}
