namespace backend.Services.Interfaces
{
    public sealed record AdjuntoCorreo(string Nombre, string TipoContenido, byte[] Contenido);

    public sealed record CorreoPendiente(
        string Para,
        string Asunto,
        string Html,
        IReadOnlyList<AdjuntoCorreo>? Adjuntos = null);

    public interface IColaCorreos
    {
        // Agrega el correo a la cola y regresa de inmediato: el envío ocurre en segundo plano.
        void Encolar(CorreoPendiente correo);
    }
}
