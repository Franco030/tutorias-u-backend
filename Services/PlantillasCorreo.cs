using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace backend.Services
{
    /*
     * Plantillas HTML de correo incrustadas en el ensamblado (Services/Templates).
     * Sintaxis mínima:
     *   {{Clave}}               valor escapado como HTML (los saltos de línea pasan a <br>)
     *   {{{Clave}}}             valor sin escapar (solo para HTML ya generado)
     *   {{#Clave}}...{{/Clave}} el bloque se conserva solo si Clave tiene valor
     */
    public static partial class PlantillasCorreo
    {
        private static readonly ConcurrentDictionary<string, string> Cache = new();

        // Escapa <, >, &, comillas y controles, pero deja legibles los acentos.
        private static readonly HtmlEncoder Codificador = HtmlEncoder.Create(UnicodeRanges.All);

        public static string Renderizar(string nombre, IReadOnlyDictionary<string, string?> valores)
        {
            const string marcador = "\u0001contenido\u0001";
            var contenido = Aplicar(Cargar(nombre), valores);
            var layout = new Dictionary<string, string?>(valores)
            {
                ["Contenido"] = marcador
            };
            // El contenido entra al final: el texto de los usuarios ya escapado
            // no vuelve a pasar por el reemplazo de marcadores.
            return Aplicar(Cargar("Layout"), layout).Replace(marcador, contenido);
        }

        public static string Aplicar(string plantilla, IReadOnlyDictionary<string, string?> valores)
        {
            string? Valor(string clave) => valores.TryGetValue(clave, out var valor) ? valor : null;

            var resultado = Seccion().Replace(plantilla, coincidencia =>
                string.IsNullOrWhiteSpace(Valor(coincidencia.Groups[1].Value))
                    ? string.Empty
                    : coincidencia.Groups[2].Value);

            resultado = SinEscapar().Replace(resultado, coincidencia => Valor(coincidencia.Groups[1].Value) ?? string.Empty);

            return Escapado().Replace(resultado, coincidencia => string.Join(
                "<br>",
                (Valor(coincidencia.Groups[1].Value) ?? string.Empty)
                    .ReplaceLineEndings("\n")
                    .Split('\n')
                    .Select(Codificador.Encode)));
        }

        private static string Cargar(string nombre) => Cache.GetOrAdd(nombre, clave =>
        {
            var recurso = $"backend.Services.Templates.{clave}.html";
            using var stream = typeof(PlantillasCorreo).Assembly.GetManifestResourceStream(recurso)
                ?? throw new InvalidOperationException($"No existe la plantilla de correo {recurso}.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

        [GeneratedRegex(@"\{\{#(\w+)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline)]
        private static partial Regex Seccion();

        [GeneratedRegex(@"\{\{\{(\w+)\}\}\}")]
        private static partial Regex SinEscapar();

        [GeneratedRegex(@"\{\{(\w+)\}\}")]
        private static partial Regex Escapado();
    }
}
