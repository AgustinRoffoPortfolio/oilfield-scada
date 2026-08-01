namespace OpcUaServer;

/// Configuración del servidor, leída de appsettings.json.
/// Los valores por defecto son los mismos que estaban hardcodeados.
public class OpcUaOptions
{
    public string ApplicationName { get; set; } = "OilfieldScadaServer";
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840/OilfieldScada";
    public string NamespaceUri { get; set; } = "http://oilfield-scada/";
    public int UpdateIntervalMs { get; set; } = 1000;

    public string AddressSpaceFile { get; set; } = "config/addressspace.json";
}