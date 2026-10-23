using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Reloj de prueba: fija la hora local de la plataforma.
public sealed class HoraFija(DateTime ahora) : IHoraPlataforma
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById(HoraPlataforma.ZonaPredeterminada);

    public DateTime Ahora { get; } = ahora;

    public DateTime AUtc(DateTime horaLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(horaLocal, DateTimeKind.Unspecified), Zona);

    public static HoraFija Actual() => new(DateTime.UtcNow);
}
