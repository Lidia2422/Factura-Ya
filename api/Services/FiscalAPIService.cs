// Contenido para /Services/FiscalAPIService.cs

// --- [IMPORTANTE] ---
// (Asegúrate de que estos 'using' estén al inicio del archivo)
using FacturadorAPI.Models;
using Fiscalapi.Abstractions;
using Fiscalapi.Models;
using Fiscalapi.Common;
using Fiscalapi.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
// ---

namespace FacturadorAPI.Services
{
    public class FiscalAPIService
    {
        private readonly IFiscalApiClient _pacClient;

        public FiscalAPIService(IFiscalApiClient pacClient)
        {
            _pacClient = pacClient;
        }

        // [CORRECCIÓN 1: Cambiamos el tipo de retorno]
        public async Task<(string xmlBase64, string pdfBase64)> TimbrarFactura(FacturaRequest datosFactura)
        {
            // --- 1. Calcular Impuestos (IVA 16%) ---
            var ivaImporte = Math.Round(datosFactura.ConceptoImporte * 0.16m, 2);
            var total = datosFactura.ConceptoImporte + ivaImporte;

            // --- 2. Mapear 'FacturaRequest' al objeto 'Invoice' del SDK ---
            var invoice = new Invoice
            {
                // (El mapeo de 'Invoice' que ya teníamos está bien)
                VersionCode = "4.0",
                Series = "F",
                Date = DateTime.Now,
                PaymentFormCode = datosFactura.FormaPago,
                CurrencyCode = "MXN",
                TypeCode = "I",
                ExpeditionZipCode = datosFactura.EmisorCp,
                PaymentMethodCode = datosFactura.MetodoPago,
                Subtotal = datosFactura.ConceptoImporte,
                Total = total,
                Issuer = new InvoiceIssuer { /* ... (mapeo del emisor) ... */ },
                Recipient = new InvoiceRecipient { /* ... (mapeo del receptor) ... */ },
                Items = new List<InvoiceItem> { /* ... (mapeo del item) ... */ }
            };

            // --- 3. Llamada al PAC (Paso 1: CREAR) ---
            try
            {
                var createResponse = await _pacClient.Invoices.CreateAsync(invoice);

                if (createResponse.Succeeded)
                {
                    string newInvoiceId = createResponse.Data.Id;

                    // --- 4. [CORRECCIÓN FINAL] ---
                    // Obtenemos los archivos (que devuelven un 'FileResponse')

                    var xmlResponse = await _pacClient.Invoices.GetXmlAsync(newInvoiceId);

                    var pdfRequest = new CreatePdfRequest { InvoiceId = newInvoiceId };
                    var pdfResponse = await _pacClient.Invoices.GetPdfAsync(pdfRequest);


                    if (xmlResponse.Succeeded && pdfResponse.Succeeded)
                    {
                        // [CORRECCIÓN 2: Accedemos a la propiedad .Base64File]
                        // El 'FileResponse' (xmlResponse.Data) tiene el archivo
                        string xmlB64 = xmlResponse.Data.Base64File;
                        string pdfB64 = pdfResponse.Data.Base64File;

                        return (xmlB64, pdfB64);
                    }
                    else
                    {
                        string xmlError = xmlResponse.Succeeded ? "OK" : xmlResponse.Message;
                        string pdfError = pdfResponse.Succeeded ? "OK" : pdfResponse.Message;
                        throw new InvalidOperationException($"Factura creada (ID: {newInvoiceId}) pero falló al obtener archivos. XML: {xmlError}, PDF: {pdfError}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Error del PAC (Create): {createResponse.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error al llamar a FiscalAPI: {ex.Message}");
            }
        }
    }
}