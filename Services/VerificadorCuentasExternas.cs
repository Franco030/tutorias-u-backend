using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using backend.Services.Interfaces;
using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;

namespace backend.Services
{
    /*
     * Inicio de sesión con Google y Microsoft. Antes el token de Microsoft solo
     * se leía, sin revisar la firma: cualquiera podía fabricar uno con el correo
     * de otra persona. Ahora se exige que el proveedor lo haya firmado, que no
     * haya vencido y que sea para esta app (ClientId en "Google:ClientId" y
     * "Microsoft:ClientId"). Sin ClientId configurado se rechaza todo.
     */
    public class VerificadorCuentasExternas : IVerificadorCuentasExternas
    {
        // Cuentas de cualquier organización y personales, como en msalConfig.js.
        public const string MetadatosMicrosoft = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

        private const string TokenInfoGoogle = "https://oauth2.googleapis.com/tokeninfo";
        private const string UserInfoGoogle = "https://www.googleapis.com/oauth2/v3/userinfo";

        private readonly HttpClient _http;
        private readonly IConfigurationManager<OpenIdConnectConfiguration> _metadatosMicrosoft;
        private readonly ILogger<VerificadorCuentasExternas> _logger;
        private readonly string? _clientIdGoogle;
        private readonly string? _clientIdMicrosoft;

        public VerificadorCuentasExternas(
            HttpClient http,
            IConfigurationManager<OpenIdConnectConfiguration> metadatosMicrosoft,
            IConfiguration configuration,
            ILogger<VerificadorCuentasExternas> logger)
        {
            _http = http;
            _metadatosMicrosoft = metadatosMicrosoft;
            _logger = logger;
            _clientIdGoogle = configuration["Google:ClientId"];
            _clientIdMicrosoft = configuration["Microsoft:ClientId"];
        }

        public async Task<CuentaExterna> VerificarMicrosoftAsync(string idToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_clientIdMicrosoft))
            {
                _logger.LogError("Falta Microsoft:ClientId: no se puede comprobar el inicio de sesión con Microsoft.");
                throw new UnauthorizedAccessException("El inicio de sesión con Microsoft no está disponible.");
            }

            var resultado = await ValidarConMicrosoftAsync(idToken, cancellationToken);

            // Microsoft cambia sus llaves de vez en cuando: si la del token es
            // nueva, se vuelven a pedir los metadatos y se intenta una vez más.
            if (resultado.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                _metadatosMicrosoft.RequestRefresh();
                resultado = await ValidarConMicrosoftAsync(idToken, cancellationToken);
            }

            if (!resultado.IsValid || resultado.SecurityToken is not JsonWebToken token)
            {
                _logger.LogWarning("Token de Microsoft rechazado: {Motivo}", resultado.Exception?.Message);
                throw new UnauthorizedAccessException("La sesión de Microsoft no es válida o ha expirado.");
            }

            // preferred_username primero: en cuentas de organización es el UPN,
            // que debe ser de un dominio verificado; "email" puede no estarlo.
            var email = Reclamo(token, "preferred_username") ?? Reclamo(token, "email");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                throw new UnauthorizedAccessException("No se pudo obtener el correo de la cuenta de Microsoft.");
            }

            return new CuentaExterna(
                email,
                Reclamo(token, "name") ?? email,
                null,
                Reclamo(token, "oid") ?? Reclamo(token, "sub"));
        }

        private async Task<TokenValidationResult> ValidarConMicrosoftAsync(string idToken, CancellationToken cancellationToken)
        {
            OpenIdConnectConfiguration metadatos;
            try
            {
                metadatos = await _metadatosMicrosoft.GetConfigurationAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudieron obtener las llaves de Microsoft.");
                throw new UnauthorizedAccessException("No se pudo comprobar la sesión de Microsoft. Intenta de nuevo.");
            }

            var parametros = new TokenValidationParameters
            {
                ValidAudience = _clientIdMicrosoft,
                IssuerSigningKeys = metadatos.SigningKeys,
                // Con "common" el emisor lleva la organización de cada cuenta:
                // https://login.microsoftonline.com/{tenantid}/v2.0
                IssuerValidator = (emisor, token, _) =>
                {
                    var organizacion = token is JsonWebToken jwt && jwt.TryGetPayloadValue("tid", out string tid) ? tid : null;
                    if (organizacion is not null && emisor == metadatos.Issuer.Replace("{tenantid}", organizacion))
                    {
                        return emisor;
                    }
                    throw new SecurityTokenInvalidIssuerException($"Emisor no reconocido: {emisor}");
                },
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            return await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parametros);
        }

        private static string? Reclamo(JsonWebToken token, string nombre) =>
            token.TryGetPayloadValue(nombre, out string valor) && !string.IsNullOrWhiteSpace(valor) ? valor : null;

        public async Task<CuentaExterna> VerificarGoogleAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_clientIdGoogle))
            {
                _logger.LogError("Falta Google:ClientId: no se puede comprobar el inicio de sesión con Google.");
                throw new UnauthorizedAccessException("El inicio de sesión con Google no está disponible.");
            }

            // El botón de Google del frontend entrega un token de acceso ("ya29.").
            return token.StartsWith("ya29.")
                ? await VerificarTokenDeAccesoGoogleAsync(token, cancellationToken)
                : await VerificarIdTokenGoogleAsync(token);
        }

        private async Task<CuentaExterna> VerificarIdTokenGoogleAsync(string idToken)
        {
            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(
                    idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = [_clientIdGoogle] });
                return new CuentaExterna(payload.Email, payload.Name ?? payload.Email, payload.Picture, payload.Subject);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning("Token de Google rechazado: {Motivo}", ex.Message);
                throw new UnauthorizedAccessException("La sesión de Google no es válida o ha expirado.");
            }
        }

        private async Task<CuentaExterna> VerificarTokenDeAccesoGoogleAsync(string accessToken, CancellationToken cancellationToken)
        {
            // Un token de acceso de Google sirve para cualquier app que lo haya
            // pedido: tokeninfo dice para cuál se emitió ("aud").
            using var info = await LeerJsonAsync(
                new HttpRequestMessage(HttpMethod.Get, $"{TokenInfoGoogle}?access_token={Uri.EscapeDataString(accessToken)}"),
                cancellationToken);
            var datos = info.RootElement;

            if (Texto(datos, "aud") != _clientIdGoogle)
            {
                _logger.LogWarning("Token de acceso de Google emitido para otra app ({Aud}).", Texto(datos, "aud"));
                throw new UnauthorizedAccessException("La sesión de Google no es válida o ha expirado.");
            }

            var email = Texto(datos, "email");
            var verificado = datos.TryGetProperty("email_verified", out var valor)
                && (valor.ValueKind == JsonValueKind.True || (valor.ValueKind == JsonValueKind.String && valor.GetString() == "true"));
            if (string.IsNullOrWhiteSpace(email) || !verificado)
            {
                throw new UnauthorizedAccessException("La cuenta de Google no tiene un correo verificado.");
            }

            // Nombre y foto (tokeninfo no los incluye).
            var perfil = new HttpRequestMessage(HttpMethod.Get, UserInfoGoogle);
            perfil.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var usuario = await LeerJsonAsync(perfil, cancellationToken);

            return new CuentaExterna(
                email,
                Texto(usuario.RootElement, "name") ?? email,
                Texto(usuario.RootElement, "picture"),
                Texto(datos, "sub"));
        }

        private async Task<JsonDocument> LeerJsonAsync(HttpRequestMessage solicitud, CancellationToken cancellationToken)
        {
            using (solicitud)
            {
                HttpResponseMessage respuesta;
                try
                {
                    respuesta = await _http.SendAsync(solicitud, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "No se pudo contactar a Google.");
                    throw new UnauthorizedAccessException("No se pudo comprobar la sesión de Google. Intenta de nuevo.");
                }

                using (respuesta)
                {
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        throw new UnauthorizedAccessException("La sesión de Google no es válida o ha expirado.");
                    }
                    return JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cancellationToken));
                }
            }
        }

        private static string? Texto(JsonElement json, string propiedad) =>
            json.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;
    }
}
