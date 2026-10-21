using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace backend.Middleware;

// Todas las respuestas de error salen como application/problem+json (RFC 7807).
// Los controladores ponen su propio "title"; aquí solo se traducen los títulos
// genéricos en inglés que genera ASP.NET Core (401/403 del JWT, 404 de ruta,
// validación del modelo, excepciones no controladas).
public static class ProblemDetailsEspanol
{
    private const string TituloValidacion = "Revisa los datos enviados.";

    private static readonly Dictionary<int, string> TitulosPorEstado = new()
    {
        [StatusCodes.Status400BadRequest] = "La solicitud no es válida.",
        [StatusCodes.Status401Unauthorized] = "Tu sesión no es válida o expiró.",
        [StatusCodes.Status403Forbidden] = "No tienes permiso para realizar esta acción.",
        [StatusCodes.Status404NotFound] = "El recurso solicitado no existe.",
        [StatusCodes.Status405MethodNotAllowed] = "Método no permitido.",
        [StatusCodes.Status409Conflict] = "La solicitud entra en conflicto con el estado actual.",
        [StatusCodes.Status415UnsupportedMediaType] = "Tipo de contenido no soportado.",
        [StatusCodes.Status500InternalServerError] = "Ocurrió un error inesperado en el servidor."
    };

    private static readonly HashSet<string> TitulosGenericos =
    [
        "One or more validation errors occurred.",
        "An error occurred while processing your request."
    ];

    public static IServiceCollection AddProblemDetailsEspanol(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = contexto =>
            Traducir(contexto.ProblemDetails));

    public static void Traducir(ProblemDetails problema)
    {
        var estado = problema.Status ?? StatusCodes.Status500InternalServerError;
        problema.Status = estado;

        var esGenerico = string.IsNullOrWhiteSpace(problema.Title) ||
                         problema.Title == ReasonPhrases.GetReasonPhrase(estado) ||
                         TitulosGenericos.Contains(problema.Title);
        if (!esGenerico)
        {
            return;
        }

        if (problema is ValidationProblemDetails)
        {
            problema.Title = TituloValidacion;
        }
        else if (TitulosPorEstado.TryGetValue(estado, out var titulo))
        {
            problema.Title = titulo;
        }
        else if (estado >= 500)
        {
            problema.Title = TitulosPorEstado[StatusCodes.Status500InternalServerError];
        }
        else
        {
            problema.Title = TitulosPorEstado[StatusCodes.Status400BadRequest];
        }
    }
}
