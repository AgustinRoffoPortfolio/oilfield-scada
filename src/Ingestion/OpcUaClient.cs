using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Serilog;

namespace Ingestion;

/// Cliente OPC UA: se conecta al servidor del yacimiento y lee sus tags.
public sealed class OpcUaClient(OpcUaOptions options)
{
    private ISession? session;

    /// Arma la identidad de la aplicacion y abre la sesion.
    public async Task ConnectAsync()
    {
        var app = new ApplicationInstance
        {
            ApplicationName = "OilfieldIngestion",
            ApplicationType = ApplicationType.Client
        };

        // Configuracion minima: sin seguridad (Fase 6), certificado autofirmado propio.
        var config = await app.Build(
                applicationUri: "urn:localhost:OilfieldIngestion",
                productUri: "urn:oilfield-scada:ingestion")
            .AsClient()
            .AddSecurityConfiguration("CN=OilfieldIngestion, O=OilfieldScada")
            .CreateAsync();

        await app.CheckApplicationInstanceCertificatesAsync(silent: true);

        // El servidor puede anunciarse con el nombre de la maquina en vez de localhost.
        var endpoint = await CoreClientUtils.SelectEndpointAsync(
            config, options.EndpointUrl, false, 15000, default);

        var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config));

        session = await Session.Create(
            config, configured, updateBeforeConnect: false,
            sessionName: "OilfieldIngestion",
            sessionTimeout: 60000, identity: new UserIdentity(), preferredLocales: null);

        Log.Information("Sesion OPC UA abierta contra {Endpoint}", endpoint.EndpointUrl);
    }

    /// Lee un solo valor, para verificar que la conexion sirve.
    public DataValue ReadTag(string nodeId)
    {
        var id = new NodeId(nodeId, 2); // namespace 2 = http://oilfield-scada/
        return session!.ReadValue(id);
    }

    public async Task DisconnectAsync()
    {
        if (session is not null)
            await session.CloseAsync();
    }
}