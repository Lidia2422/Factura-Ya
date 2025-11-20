// --- PASO 1: USING STATEMENTS (SIEMPRE PRIMERO) ---
using FacturadorAPI;
using FacturadorAPI.Models; // ¡NUESTRO NAMESPACE DE MODELOS!
using FacturadorAPI.Services;
using FiscalApi;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Config;
using MercadoPago.Resource.Payment;
using MercadoPago.Resource.Preference;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
// Agrega aquí los "using" de QuestPDF
// using QuestPDF.Fluent;


// --- PASO 2: CÓDIGO EJECUTABLE (INSTRUCCIONES DE NIVEL SUPERIOR) ---

// --- LEER VARIABLES DE ENTORNO ---
var mercadopagoAccessToken = Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN");
var pacApiKey = Environment.GetEnvironmentVariable("PAC_API_KEY");
var renderExternalUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
var redisUrlCompleta = Environment.GetEnvironmentVariable("REDIS_URL");

MercadoPagoConfig.AccessToken = mercadopagoAccessToken;

// --- Configuración de REDIS ---
var configOptions = ConfigurationOptions.Parse(redisUrlCompleta);
configOptions.AbortOnConnectFail = false;
configOptions.ConnectTimeout = 10000;
configOptions.Ssl = true;
var redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
IDatabase kvDb = redis.GetDatabase();


// --- CONFIGURACIÓN DE LA APLICACIÓN ---
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddFiscalApi(); // Registra el SDK de FiscalAPI
builder.Services.AddScoped<FiscalAPIService>(); // Registra nuestro servicio
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseDefaultFiles(); // Sirve index.html
app.UseStaticFiles(); // Sirve wwwroot

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
            NotificationUrl = $"{renderExternalUrl}/api/webhook-mercadopago"
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
app.MapPost("/api/webhook-mercadopago", async (MercadoPagoNotificacion notificacion, FiscalAPIService fiscalService) => {

    if (notificacion.action == "payment.updated")
    {

        long paymentIdFromNotification = long.Parse(notificacion.data.id);
        var paymentClient = new PaymentClient();
        Payment payment = await paymentClient.GetAsync(paymentIdFromNotification);

        var paymentStatus = payment.Status;
        var paymentId = payment.ExternalReference; // Este es nuestro GUID

        if (paymentStatus == "approved")
        {
            try
            {
                string? facturaJson = await kvDb.StringGetAsync(paymentId);

                if (string.IsNullOrEmpty(facturaJson))
                {
                    // Si no hay datos, es posible que ya se haya procesado o expiró
                    Console.WriteLine($"ADVERTENCIA: No se encontraron datos en Redis para {paymentId}");
                    return Results.Ok();
                }

                var datosFactura = JsonSerializer.Deserialize<FacturaRequest>(facturaJson);

                Console.WriteLine($"Pago aprobado. Iniciando timbrado para: {datosFactura.ClienteNombre}");

                // --- CAMBIO PRINCIPAL AQUÍ ---

                // 1. Llamamos al servicio que devuelve los archivos en Base64
                var (xmlBase64, pdfBase64) = await fiscalService.TimbrarFactura(datosFactura);

                // 2. Guardamos los Base64 en Redis
                // Nota: Usamos los campos 'urlXml' y 'urlPdf' para guardar el contenido B64 por ahora.
                var estadoFinal = JsonSerializer.Serialize(new EstadoFactura(
                    status: "lista",
                    urlXml: xmlBase64,
                    urlPdf: pdfBase64,
                    mensaje: null
                ));

                await kvDb.StringSetAsync($"status_{paymentId}", estadoFinal, TimeSpan.FromDays(1));

                Console.WriteLine($"¡Éxito! Factura timbrada y guardada en Redis para {paymentId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR TIMBRANDO: {ex.Message}");
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
// ¡ESTE BLOQUE DEBE ESTAR VACÍO!
// (Todos los records se movieron a Models.cs)