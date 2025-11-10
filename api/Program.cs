// --- PASO 1: USING STATEMENTS (SIEMPRE PRIMERO) ---
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// Agrega aquí los "using" de MercadoPago, QuestPDF y tu PAC
// using MercadoPago.SDK.Client;
// using MercadoPago.SDK.Config;
// using QuestPDF.Fluent;
// using FiscalAPI.SDK;


// --- PASO 2: CÓDIGO EJECUTABLE (INSTRUCCIONES DE NIVEL SUPERIOR) ---

// --- CONFIGURACIÓN ---
var builder = WebApplication.CreateBuilder(args);

// Habilitar Swagger (para pruebas locales en VS2022)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
var app = builder.Build();

// Configuración de Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configuración de CORS (¡MUY IMPORTANTE!)
app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.UseHttpsRedirection(); // Importante para producción


// --- ENDPOINTS DE NUESTRA API ---

// 1. El Frontend llama a este para iniciar el pago
app.MapPost("/api/crear-preferencia-pago", async (PagoRequest request) => {

    // TODO: Configurar tu SDK de MercadoPago
    // MercadoPagoConfig.AccessToken = "TU_ACCESS_TOKEN_PRIVADO";

    var paymentId = Guid.NewGuid().ToString(); // Nuestro ID interno

    // var preferenceRequest = new PreferenceRequest
    // {
    //     Items = new List<PreferenceItemRequest> { ... item de $29 MXN ... },
    //     ExternalReference = paymentId,
    //     NotificationUrl = "https://factura-ahora.com.mx/api/webhook-mercadopago"
    // };
    // var client = new PreferenceClient();
    // var preference = await client.CreateAsync(preferenceRequest);

    // --- Simulación (mientras no tienes el SDK) ---
    var preferenceId = "ID_DE_PREFERENCIA_SIMULADO";
    // --- Fin Simulación ---

    return Results.Ok(new { preferenceId = preferenceId, paymentId = paymentId });
});


// 2. El Frontend llama a este DESPUÉS de crear la preferencia
app.MapPost("/api/guardar-datos-temporales", async (FacturaRequest request) => {

    // TODO: Guardar 'request' (que tiene los CSD en Base64 y los datos)
    // en una base de datos temporal o caché (Azure Table Storage, etc.)
    // usando request.PaymentId como la llave.

    Console.WriteLine($"Datos guardados para PaymentId: {request.PaymentId}");

    return Results.Ok(new { status = "datos_recibidos" });
});


// 3. MercadoPago llama a este CUANDO el pago se aprueba
app.MapPost("/api/webhook-mercadopago", async (MercadoPagoNotificacion notificacion) => {

    if (notificacion.action == "payment.updated")
    {
        // TODO: Validar que la notificación sea legítima de MercadoPago

        // TODO: Consultar el estado del pago con el notificacion.data.id
        // var paymentClient = new PaymentClient();
        // var payment = await paymentClient.GetAsync(notificacion.data.id);

        // --- Simulación ---
        var paymentStatus = "approved";
        var paymentId = "ID_DE_PAGO_QUE_PUSIMOS_EN_EXTERNAL_REFERENCE";
        // --- Fin Simulación ---

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
                // var pac = new ServicioPAC("TU_API_KEY_PAC");
                // var cfdi = await pac.Timbrar(cerBytes, keyBytes, datosFactura.CsdPassword, ...);
                Console.WriteLine($"Timbrando factura para {paymentId}");


                // 4. ¡Generar PDF con QuestPDF!
                // var pdfBytes = GenerarPDFConQuestPDF(cfdi.Xml);
                Console.WriteLine($"Generando PDF para {paymentId}");


                // 5. Guardar XML y PDF en un Blob Storage (Azure, AWS S3)
                // var urlXml = await GuardarEnBlob(cfdi.Uuid + ".xml", cfdi.Xml);
                // var urlPdf = await GuardarEnBlob(cfdi.Uuid + ".pdf", pdfBytes);

                // --- Simulación ---
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


// 4. El Frontend llama a este cada 3 segundos para ver si ya está la factura
app.MapGet("/api/status-factura/{paymentId}", async (string paymentId) => {

    // TODO: Consultar estado en la Cache/DB
    // var estado = await ObtenerEstadoDeCache(paymentId);

    // --- Simulación ---
    var estado = new
    {
        Status = "lista", // "procesando", "error"
        UrlXml = "https://simulado.com/factura.xml",
        UrlPdf = "https://simulado.com/factura.pdf",
        Mensaje = ""
    };
    // --- Fin Simulación ---

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


// --- EJECUTAR LA API ---
app.Run();


// --- PASO 3: DECLARACIONES DE TIPOS (RECORDS) ---
// (AHORA VAN AL FINAL DEL ARCHIVO)
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

public record MercadoPagoNotificacion(
    string action,
    MercadoPagoPaymentData data
);

public record MercadoPagoPaymentData(string id);