using backend.Services.Interfaces;

namespace backend.Services
{
    public sealed class HoraPlataforma : IHoraPlataforma
    {
        public const string ZonaPredeterminada = "America/Mexico_City";

        private readonly TimeProvider _tiempo;
        private readonly TimeZoneInfo _zona;

        public HoraPlataforma(TimeProvider tiempo, IConfiguration configuration)
        {
            _tiempo = tiempo;
            var zona = configuration["ZonaHoraria"];
            _zona = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zona) ? ZonaPredeterminada : zona);
        }

        // Sin Kind: se compara directamente con las fechas guardadas en la base.
        public DateTime Ahora => DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(_tiempo.GetUtcNow().UtcDateTime, _zona),
            DateTimeKind.Unspecified);

        public DateTime AUtc(DateTime horaLocal) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(horaLocal, DateTimeKind.Unspecified), _zona);
    }
}
