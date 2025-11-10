// --- --- --- --- --- --- --- --- --- --- ---
// ¡¡¡ ATENCIÓN AQUÍ !!!
// --- --- --- --- --- --- --- --- --- --- ---
// 1. Esta es tu URL de Vercel. Asegúrate de que sea la correcta.
const TU_API_URL = "https://factura-b9nijoxhn-olga-lidia-felix-medinas-projects.vercel.app/"; // ¡USA TU URL REAL DE VERCEL!
// --- --- --- --- --- --- --- --- --- --- ---

// Reemplaza esto con tu Public Key de MercadoPago (la que es "TEST-...")
const mp = new MercadoPago('TEST-da4036ed-7e39-46b0-b254-6005d0b696de');
// --- Selección de Elementos ---
const botonPagar = document.getElementById('boton-pagar');
const seccionFormulario = document.getElementById('seccion-formulario');
const seccionDescarga = document.getElementById('seccion-descarga');
const seccionProcesando = document.getElementById('seccion-procesando');
const linkPdf = document.getElementById('linkPdf');
const linkXml = document.getElementById('linkXml');

let paymentIdGlobal = null; // Guardaremos nuestro ID interno aquí

// --- Función para convertir archivos a Base64 ---
function fileToBase64(file) {
    return new Promise((resolve, reject) => {
        if (!file) {
            reject(new Error("No se seleccionó ningún archivo."));
            return;
        }
        const reader = new FileReader();
        reader.readAsDataURL(file);
        reader.onload = () => resolve(reader.result.split(',')[1]); // Quita el "data:..."
        reader.onerror = error => reject(error);
    });
}

// --- Event Listener del Botón ---
botonPagar.addEventListener('click', iniciarPago);

async function iniciarPago() {
    botonPagar.disabled = true;
    botonPagar.innerText = "Procesando...";

    try {
        // 1. Crear la "Preferencia de Pago" en MercadoPago
        const prefResponse = await fetch(`${TU_API_URL}/api/crear-preferencia-pago`, {
            method: 'POST',
            body: JSON.stringify({ importe: 29.00 }),
            headers: { 'Content-Type': 'application/json' }
        });
        
        const preferencia = await prefResponse.json();
        paymentIdGlobal = preferencia.paymentId; // Nuestro ID interno

        // 2. Recolectar TODOS los datos (incluyendo archivos en Base64)

        // --- --- --- ¡INICIA HACK DE PRUEBA! --- --- ---
        // Como no podemos descargar los CSD, vamos a saltarnos la lectura
        // de archivos y a "hardcodear" texto falso.
        
        // const [cerBase64, keyBase64] = await Promise.all([
        //     fileToBase64(document.getElementById('archivoCer').files[0]),
        //     fileToBase64(document.getElementById('archivoKey').files[0])
        // ]);

        // Texto "fakecertfile" en Base64
        const cerBase64 = "ZmFrZWNlcnRmaWxl"; 
        // Texto "fakekeyfile" en Base64
        const keyBase64 = "ZmFrZWtleWZpbGU="; 
        
        // --- --- --- ¡TERMINA HACK DE PRUEBA! --- --- ---


        const datosFactura = {
            paymentId: paymentIdGlobal,
            csdCerBase64: cerBase64, // Usa el texto "fake"
            csdKeyBase64: keyBase64, // Usa el texto "fake"
            csdPassword: "12345678a", // Usa una contraseña "fake"
            clienteRfc: document.getElementById('clienteRfc').value,
            clienteNombre: document.getElementById('clienteNombre').value,
            clienteCp: document.getElementById('clienteCp').value,
            clienteRegimen: document.getElementById('clienteRegimen').value,
            usoCfdi: document.getElementById('usoCfdi').value,
            conceptoDesc: document.getElementById('conceptoDesc').value,
            conceptoImporte: parseFloat(document.getElementById('conceptoImporte').value)
        };

        // 3. Enviar los datos a la API para que los "ponga en espera"
        await fetch(`${TU_API_URL}/api/guardar-datos-temporales`, {
            method: 'POST',
            body: JSON.stringify(datosFactura),
            headers: { 'Content-Type': 'application/json' }
        });

        // 4. ¡Abrir el checkout de MercadoPago!
        mp.checkout({
            preference: { id: preferencia.preferenceId },
            render: {
                container: '#wallet_container', // Reemplaza el botón
                label: 'Pagar $29.00 MXN',
            },
            callbacks: {
                onReady: () => {
                    seccionFormulario.style.display = 'none';
                    seccionProcesando.style.display = 'block';
                    iniciarVerificacionDeFactura(paymentIdGlobal);
                },
            }
        });

    } catch (error) {
        console.error(error);
        alert('Error al iniciar el pago. ' + error.message);
        botonPagar.disabled = false;
        botonPagar.innerText = "Pagar y Generar Factura";
    }
}

function iniciarVerificacionDeFactura(paymentId) {
    const interval = setInterval(async () => {
        try {
            const statusResponse = await fetch(`${TU_API_URL}/api/status-factura/${paymentId}`);
            const data = await statusResponse.json();

            if (data.status === 'lista') {
                clearInterval(interval);
                mostrarDescarga(data.urlPdf, data.urlXml);
            } else if (data.status === 'error') {
                clearInterval(interval);
                alert(`Error al timbrar: ${data.mensaje}`);
                seccionProcesando.style.display = 'none';
                seccionFormulario.style.display = 'block'; // Mostrar formulario de nuevo
            }
        } catch (error) {
            console.error('Error verificando status', error);
        }
    }, 3000); // Pregunta cada 3 segundos
}

function mostrarDescarga(urlPdf, urlXml) {
    seccionProcesando.style.display = 'none';
    seccionDescarga.style.display = 'block';
    linkPdf.href = urlPdf;
    linkXml.href = urlXml;
}