namespace FacturadorAPI.Models
{
    public record PagoRequest(decimal Importe);

    public record FacturaRequest(
        string PaymentId,

        // --- CSD (Certificado de Sello Digital) ---
        string CsdCerBase64,
        string CsdKeyBase64,
        string CsdPassword,

        // --- Datos del Emisor (¡NUEVOS!) ---
        string EmisorRfc,
        string EmisorNombre,
        string EmisorRegimenFiscal,
        string EmisorCp,

        // --- Datos del Receptor (Cliente) ---
        string ClienteRfc,
        string ClienteNombre,
        string ClienteCp,
        string ClienteRegimen,
        string UsoCfdi,

        // --- Datos del CFDI (¡NUEVOS!) ---
        string FormaPago,
        string MetodoPago,

        // --- Datos del Concepto (MVP: 1 concepto) ---
        string ConceptoClaveProdServ,
        string ConceptoClaveUnidad,
        string ConceptoObjetoImp,
        string ConceptoDesc,
        decimal ConceptoImporte       // (Este es el Subtotal, sin IVA)
    );
    public record MercadoPagoNotificacion(string action, MercadoPagoPaymentData data);
    public record MercadoPagoPaymentData(string id);

    public record EstadoFactura(
        string status,
        string? urlXml,
        string? urlPdf,
        string? mensaje
    );
}
