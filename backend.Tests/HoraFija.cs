using backend.Services.Interfaces;

namespace backend.Tests;

// Reloj de prueba: fija la hora local de la plataforma.
public sealed class HoraFija(DateTime ahora) : IHoraPlataforma
{
    public DateTime Ahora { get; } = ahora;

    public static HoraFija Actual() => new(DateTime.UtcNow);
}
