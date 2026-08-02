namespace Ingestion;

/// Parametros de conexion al servidor OPC UA.
public sealed class OpcUaOptions
{
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840/OilfieldScada";

    /// Cada cuanto el servidor nos empuja los cambios acumulados, en milisegundos.
    public int PublishingIntervalMs { get; set; } = 1000;

    /// Porcentaje del rango de ingenieria que un valor debe cambiar para ser reportado.
    /// 0 desactiva el filtro. Los tags discretos (Status) nunca lo usan.
    public double DeadbandPercent { get; set; } = 0.5;

    /// Cada cuanto la sesion le manda un latido al servidor, en milisegundos.
    public int KeepAliveIntervalMs { get; set; } = 5000;

    /// Cada cuanto se reintenta reconectar tras una caida, en milisegundos.
    public int ReconnectPeriodMs { get; set; } = 5000;
    /// Carpeta de la PKI propia del cliente. En produccion cada aplicacion corre
    /// en su maquina y tiene la suya; aca conviven en el repo, separadas.
    public string PkiRoot { get; set; } = "pki/ingestion";

    /// Si conecta al endpoint cifrado o al inseguro.
    public bool UseSecurity { get; set; } = true;

    /// Aceptar certificados de servidor desconocidos sin intervencion.
    /// En false hay que confiar el certificado a mano, como en campo.
    public bool AutoAcceptUntrustedCertificates { get; set; } = false;
}