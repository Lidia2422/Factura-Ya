// --- PASO 1: USING STATEMENTS (SIEMPRE PRIMERO) ---
using System;
using System.Text.Json; // Importante para serializar
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis; // El paquete que instalamos
using MercadoPago.Config;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Payment;
using MercadoPago.Resource.Preference;
// Agrega aquí los "using" de QuestPDF y tu PAC
// using QuestPDF.Fluent;
// using FiscalAPI.SDK;


// --- LEER VARIABLES DE ENTORNO ---
var mercadopagoAccessToken = Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN");
var pacApiKey = Environment.GetEnvironmentVariable("PAC_API_KEY");
var kvUrl = Environment.GetEnvironmentVariable("KV_REST_API_URL");
var kvToken = Environment.GetEnvironmentVariable("KV_REST_API_TOKEN");

MercadoPagoConfig.AccessToken = mercadopagoAccessToken;

// --- CONFIGURAR CONEXIÓN A BASE DE DATOS KV (REDIS) ---
// ¡AQUÍ ESTÁ LA CORRECCIÓN! ( ? por , )
var redisConnectionString = $"{kvUrl},ssl=true,password={kvToken}";
var redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
IDatabase kvDb = redis.GetDatabase();


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

// 1. El Frontend llama a este para iniciar el pago
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
                    UnitPrice = 29.00m,
                    CurrencyId = "MXN",
                }
            },
            ExternalReference = paymentId, // ¡Importante! Es nuestro ID
            NotificationUrl = "https://factura-ya.vercel.app/api/webhook-mercadopago" // ¡Revisa que sea tu URL!
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

// 2. El Frontend llama a este DESPUÉS de crear la preferencia
app.MapPost("/api/guardar-datos-temporales", async (FacturaRequest request) => {

    var facturaJson = JsonSerializer.Serialize(request);
    await kvDb.StringSetAsync(request.PaymentId, facturaJson, TimeSpan.FromDays(1));
    await kvDb.StringSetAsync($"status_{request.PaymentId}", "procesando", TimeSpan.FromDays(1));

    Console.WriteLine($"Datos guardados para PaymentId: {request.PaymentId}");
    return Results.Ok(new { status = "datos_recibidos" });
});

// 3. MercadoPago llama a este CUANDO el pago se aprueba
app.MapPost("/api/webhook-mercadopago", async (MercadoPagoNotificacion notificacion) => {

    if (notificacion.action == "payment.updated")
    {
        long paymentIdFromNotification = long.Parse(notificacion.data.id);
        var paymentClient = new PaymentClient();
        Payment payment = await paymentClient.GetAsync(paymentIdFromNotification);

        var paymentStatus = payment.Status;
        var paymentId = payment.ExternalReference; // Nuestro ID interno

        if (paymentStatus == "approved")
        {
            try
            {
                string? facturaJson = await kvDb.StringGetAsync(paymentId);
                if (string.IsNullOrEmpty(facturaJson))
                {
                    throw new Exception($"No se encontraron datos para PaymentId {paymentId}");
                }
                var datosFactura = JsonSerializer.Deserialize<FacturaRequest>(facturaJson);

                var cerBytes = Convert.FromBase64String(datosFactura.CsdCerBase64);
                var keyBytes = Convert.FromBase64String(datosFactura.CsdKeyBase64);

                // TODO: Reemplazar esto con la llamada real a FiscalAPI
                Console.WriteLine($"Timbrando factura para {paymentId}");
                var cfdiXmlSimulado = "<xml>Factura Timbrada</xml>";

                // TODO: Reemplazar esto con la llamada real a QuestPDF
                Console.WriteLine($"Generando PDF para {paymentId}");
                var pdfBytesSimulados = new byte[0];

                // TODO: Guardar XML y PDF en un Blob Storage (Vercel Blob)
                var urlXml = "https://simulado.com/factura.xml";
                var urlPdf = "https://simulado.com/factura.pdf";

                var estadoFinal = JsonSerializer.Serialize(new EstadoFactura("lista", urlXml, urlPdf, null));
                await kvDb.StringSetAsync($"status_{paymentId}", estadoFinal, TimeSpan.FromDays(1));
            }
            catch (Exception ex)
            {
                var estadoError = JsonSerializer.Serialize(new EstadoFactura("error", null, null, ex.Message));
                await kvDb.StringSetAsync($"status_{paymentId}", estadoError, TimeSpan.FromDays(1));
            }
        }
    }
    return Results.Ok();
});

// 4. El Frontend llama a este cada 3 segundos
app.MapGet("/api/status-factura/{paymentId}", async (string paymentId) => {

    string? estadoJson = await kvDb.StringGetAsync($"status_{paymentId}");

    if (string.IsNullOrEmpty(estadoJson) || estadoJson == "procesando")
    {
        return Results.Ok(new { status = "procesando" });
    }

    var estado = JsonSerializer.Deserialize<EstadoFactura>(estadoJson);
    return Results.Ok(estado);
});

// --- EJECUTAR LA API ---
app.Run();

// --- PASO 3: DECLARACIONES DE TIPOS (RECORDS) ---
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

public record EstadoFactura(
    string status,
    string? urlXml,
    string? urlPdf,
    string? mensaje
);