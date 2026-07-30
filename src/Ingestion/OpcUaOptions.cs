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
}