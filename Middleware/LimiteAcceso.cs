using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.Middleware;

/*
 * Límite de intentos de inicio de sesión y registro por IP (10 por minuto,
 * configurable con "LimiteAcceso:PorMinuto"): frena ataques de fuerza bruta a
 * contraseñas y el registro masivo de cuentas. Al pasarse responde 429 con un
 * ProblemDetails en español y la cabecera Retry-After.
 */
public static class LimiteAcceso
{
    public const string Politica = "acceso";

    public static IServiceCollection AddLimiteAcceso(this IServiceCollection services, IConfiguration configuration)
    {
        var porMinuto = configuration.GetValue("LimiteAcceso:PorMinuto", 10);

        // En Azure App Service la IP real del cliente llega en X-Forwarded-For;
        // se toma solo el último valor (el que agrega la plataforma), así un
        // cliente no puede inventarse otra IP para saltarse el límite.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Politica, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.OnRejected = async (contexto, cancellationToken) =>
            {
                var http = contexto.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var reintento))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(reintento.TotalSeconds)).ToString();
                }

                var problemas = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemas.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Demasiados intentos. Espera un minuto e inténtalo de nuevo."
                    }
                });
            };
        });
    }
}
