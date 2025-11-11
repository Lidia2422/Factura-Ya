// --- PASO 1: USING STATEMENTS (SIEMPRE PRIMERO) ---
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic; // Asegura que List<T> funcione
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using MercadoPago.Config;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Payment;
using MercadoPago.Resource.Preference;
// Agrega aquí los "using" de QuestPDF y tu PAC
// using QuestPDF.Fluent;
// using FiscalAPI.SDK; 


// --- PASO 2: CÓDIGO EJECUTABLE (INSTRUCCIONES DE NIVEL SUPERIOR) ---

// --- LEER VARIABLES DE ENTORNO ---
var mercadopagoAccessToken = Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN");
var pacApiKey = Environment.GetEnvironmentVariable("PAC_API_KEY");

// Leemos la URL completa que Upstash nos da
var redisUrlCompleta = Environment.GetEnvironmentVariable("REDIS_URL");

MercadoPagoConfig.AccessToken = mercadopagoAccessToken;

// ¡AQUÍ ESTÁ LA CORRECCIÓN!
// Agregamos dos parámetros:
// 1. abortConnect=false: Permite que el programa siga intentando arrancar aunque la conexión falle inicialmente.
// 2. connectTimeout=5000: Damos 5 segundos para que se conecte antes de fallar.

var configOptions = ConfigurationOptions.Parse(redisUrlCompleta);

// 2. Añadimos nuestras opciones de seguridad y timeout
configOptions.AbortOnConnectFail = false;
configOptions.ConnectTimeout = 10000; // Le damos 10 segundos (más seguro)
configOptions.Ssl = true; // Nos aseguramos de que Ssl esté activado

// 3. Conectamos usando el OBJETO de configuración, no el string
var redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
IDatabase kvDb = redis.GetDatabase();


// --- CONFIGURACIÓN DE LA APLICACIÓN ---
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

// app.UseHttpsRedirection(); // <-- ¡LA COMENTAMOS O ELIMINAMOS!

// --- --- --- ¡NUEVAS LÍNEAS! --- --- ---
// Esto le dice a la API que sirva el index.html como página principal
app.UseDefaultFiles();
// Esto le dice a la API que sirva los archivos de la carpeta "wwwroot" (index.html, script.js)
app.UseStaticFiles();
// --- --- --- --- --- --- --- --- --- ---
// --- ENDPOINTS DE NUESTRA API ---

// 1. /api/crear-preferencia-pago
app.MapPost("/api/crear-preferencia-pago", async (PagoRequest request) => {
    var paymentId = Guid.NewGuid().ToString();
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
            ExternalReference = paymentId,
            NotificationUrl = "https://factura-ya.vercel.app/api/webhook-mercadopago"
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

// 2. /api/guardar-datos-temporales
app.MapPost("/api/guardar-datos-temporales", async (FacturaRequest request) => {

    var facturaJson = JsonSerializer.Serialize(request);
    await kvDb.StringSetAsync(request.PaymentId, facturaJson, TimeSpan.FromDays(1));
    await kvDb.StringSetAsync($"status_{request.PaymentId}", "procesando", TimeSpan.FromDays(1));

    Console.WriteLine($"Datos guardados para PaymentId: {request.PaymentId}");
    return Results.Ok(new { status = "datos_recibidos" });
});

// 3. /api/webhook-mercadopago
app.MapPost("/api/webhook-mercadopago", async (MercadoPagoNotificacion notificacion) => {

    if (notificacion.action == "payment.updated")
    {
        long paymentIdFromNotification = long.Parse(notificacion.data.id);
        var paymentClient = new PaymentClient();
        Payment payment = await paymentClient.GetAsync(paymentIdFromNotification);

        var paymentStatus = payment.Status;
        var paymentId = payment.ExternalReference;

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

// 4. /api/status-factura/{paymentId}
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
// ¡ESTE BLOQUE DEBE IR AQUÍ AL FINAL, DESPUÉS DE app.Run()!
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