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

    public string ModbusMapFile { get; set; } = "config/modbusmap.json";
    public string ModbusHost { get; set; } = "localhost";
    public int ModbusPort { get; set; } = 5502;
    public int ModbusPollIntervalMs { get; set; } = 1000;
    public int ModbusReconnectDelayMs { get; set; } = 5000;
}