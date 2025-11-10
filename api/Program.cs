// --- PASO 1: USING STATEMENTS (SIEMPRE PRIMERO) ---
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// Agrega los "using" de MercadoPago, QuestPDF y tu PAC
using MercadoPago.Config;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Payment;
using MercadoPago.Resource.Preference;
// using FiscalAPI.SDK; // Asegúrate de agregar el using de tu PAC
// using QuestPDF.Fluent; // Asegúrate de agregar el using de QuestPDF

// --- LEER VARIABLES DE ENTORNO (¡NUEVO!) ---
var mercadopagoAccessToken = Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN");
var pacApiKey = Environment.GetEnvironmentVariable("PAC_API_KEY");
MercadoPagoConfig.AccessToken = mercadopagoAccessToken;


// --- PASO 2: CÓDIGO EJECUTABLE (INSTRUCCIONES DE NIVEL SUPERIOR) ---

// --- CONFIGURACIÓN ---
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseHttpsRedirection();

// --- ENDPOINTS DE NUESTRA API ---

// 1. El Frontend llama a este para iniciar el pago (¡YA NO ES SIMULACIÓN!)
app.MapPost("/api/crear-preferencia-pago", async (PagoRequest request) => {

    var paymentId = Guid.NewGuid().ToString(); // Nuestro ID interno

    try
    {
        var preferenceRequest = new PreferenceRequest
        {
            Items = new List<PreferenceItemRequest>
            {
                new PreferenceItemRequest
                {
                    Title = "Pago de Timbrado de Factura",
                    Quantity = 1,
                    UnitPrice = 29.00m, // Asegúrate de que sea decimal
                    CurrencyId = "MXN",
                }
            },
            ExternalReference = paymentId, // ¡Importante! Es nuestro ID
            NotificationUrl = "https://factura-ya.vercel.app/api/webhook-mercadopago" // ¡Usa tu URL real!
        };

        var client = new PreferenceClient();
        Preference preference = await client.CreateAsync(preferenceRequest);

        return Results.Ok(new { preferenceId = preference.Id, paymentId = paymentId });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creando preferencia: {ex.Message}");
        return Results.Problem("Error al conectar con MercadoPago");
    }
});


// 2. El Frontend llama a este DESPUÉS de crear la preferencia (Sigue igual)
app.MapPost("/api/guardar-datos-temporales", async (FacturaRequest request) => {

    // TODO: Guardar 'request' en una base de datos temporal (Azure Table, etc.)
    // usando request.PaymentId como la llave.
    Console.WriteLine($"Datos guardados para PaymentId: {request.PaymentId}");

    return Results.Ok(new { status = "datos_recibidos" });
});


// 3. MercadoPago llama a este CUANDO el pago se aprueba (¡YA NO ES SIMULACIÓN!)
app.MapPost("/api/webhook-mercadopago", async (MercadoPagoNotificacion notificacion) => {

    if (notificacion.action == "payment.updated")
    {
        // TODO: Validar que la notificación sea legítima de MercadoPago

        // --- --- --- INICIA CORRECCIÓN --- --- ---
        // El ID de la notificación viene como string, pero GetAsync espera un 'long'.
        // Lo convertimos:
        long paymentIdFromNotification = long.Parse(notificacion.data.id);

        var paymentClient = new PaymentClient();
        // Usamos la nueva variable 'long' aquí:
        Payment payment = await paymentClient.GetAsync(paymentIdFromNotification);
        // --- --- --- TERMINA CORRECCIÓN --- --- ---

        var paymentStatus = payment.Status;
        var paymentId = payment.ExternalReference; // Nuestro ID interno

        if (paymentStatus == "approved")
        {
            try
            {
                // 1. Obtener los datos de la factura que guardamos
                // var datosFactura = await ObtenerDatosDeCache(paymentId);

                // 2. Convertir CSD Base64 a bytes
                // var cerBytes = Convert.FromBase64String(datosFactura.CsdCerBase64);
                // var keyBytes = Convert.FromBase64String(datosFactura.CsdKeyBase64);

                // 3. ¡Llamar al PAC (FiscalAPI)!
                // var pac = new ServicioPAC(pacApiKey); // ¡Usa la variable de entorno!
                // var cfdi = await pac.Timbrar(cerBytes, keyBytes, datosFactura.CsdPassword, ...);
                Console.WriteLine($"Timbrando factura para {paymentId}");


                // 4. ¡Generar PDF con QuestPDF!
                // var pdfBytes = GenerarPDFConQuestPDF(cfdi.Xml);
                Console.WriteLine($"Generando PDF para {paymentId}");


                // 5. Guardar XML y PDF en un Blob Storage (Azure, AWS S3)
                // var urlXml = await GuardarEnBlob(cfdi.Uuid + ".xml", cfdi.Xml);
                // var urlPdf = await GuardarEnBlob(cfdi.Uuid + ".pdf", pdfBytes);

                // --- Simulación (SOLO DE GUARDADO, EL PAGO ES REAL) ---
                var urlXml = "https://simulado.com/factura.xml";
                var urlPdf = "https://simulado.com/factura.pdf";
                // --- Fin Simulación ---


                // 6. Actualizar nuestro registro a "LISTA" con las URLs
                // await ActualizarCache(paymentId, "lista", urlXml, urlPdf);
            }
            catch (Exception ex)
            {
                // 7. Manejar error del PAC (ej. CSD inválido)
                // await ActualizarCache(paymentId, "error", ex.Message);
            }
        }
    }

    return Results.Ok(); // Siempre responde OK (200) a MercadoPago
});


// 4. El Frontend llama a este cada 3 segundos (Sigue igual)
app.MapGet("/api/status-factura/{paymentId}", async (string paymentId) => {

    // TODO: Consultar estado en la Cache/DB
    // var estado = await ObtenerEstadoDeCache(paymentId);

    // --- Simulación ---
    var estado = new
    {
        Status = "lista",
        UrlXml = "https://simulado.com/factura.xml",
        UrlPdf = "https://simulado.com/factura.pdf",
        Mensaje = ""
    };
    // --- Fin Simulación ---

    // ... (resto del código igual)
    if (estado.Status == "lista")
    {
        return Results.Ok(new { status = "lista", urlXml = estado.UrlXml, urlPdf = estado.UrlPdf });
    }
    else if (estado.Status == "error")
    {
        return Results.Ok(new { status = "error", mensaje = estado.Mensaje });
    }
    return Results.Ok(new { status = "procesando" });
});

app.Run();

// --- PASO 3: DECLARACIONES DE TIPOS (RECORDS) ---
// ... (resto del código igual)
public record PagoRequest(decimal Importe);
public record FacturaRequest(
    string PaymentId,
    string CsdCerBase64,
    string CsdKeyBase64,
    string CsdPassword,
    string ClienteRfc,
    string ClienteNombre,
    string ClienteCp,
    string ClienteRegimen,
    string UsoCfdi,
    string ConceptoDesc,
    decimal ConceptoImporte
);
public record MercadoPagoNotificacion(string action, MercadoPagoPaymentData data);
public record MercadoPagoPaymentData(string id);