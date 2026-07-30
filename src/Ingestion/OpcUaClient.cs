using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Serilog;

namespace Ingestion;

/// Cliente OPC UA: se conecta al servidor del yacimiento y se suscribe a sus tags.
public sealed class OpcUaClient(OpcUaOptions options)
{
    private ISession? session;
    private Subscription? subscription;

    /// Arma la identidad de la aplicacion y abre la sesion.
    public async Task ConnectAsync()
    {
        var app = new ApplicationInstance
        {
            ApplicationName = "OilfieldIngestion",
            ApplicationType = ApplicationType.Client
        };

        var config = await app.Build(
                applicationUri: "urn:localhost:OilfieldIngestion",
                productUri: "urn:oilfield-scada:ingestion")
            .AsClient()
            .AddSecurityConfiguration("CN=OilfieldIngestion, O=OilfieldScada")
            .CreateAsync();

        await app.CheckApplicationInstanceCertificatesAsync(silent: true);

        var endpoint = await CoreClientUtils.SelectEndpointAsync(
            config, options.EndpointUrl, false, 15000, default);

        var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config));

        session = await Session.Create(
            config, configured, updateBeforeConnect: false,
            sessionName: "OilfieldIngestion",
            sessionTimeout: 60000, identity: new UserIdentity(), preferredLocales: null);

        Log.Information("Sesion OPC UA abierta contra {Endpoint}", endpoint.EndpointUrl);
    }

    /// Se suscribe a los tags indicados. El callback se dispara con cada valor nuevo.
    public async Task SubscribeAsync(
        IEnumerable<string> tagNames,
        Action<string, DataValue> onValue)
    {
        subscription = new Subscription(session!.DefaultSubscription)
        {
            PublishingInterval = options.PublishingIntervalMs,
            DisplayName = "OilfieldTags"
        };

        foreach (var name in tagNames)
        {
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                DisplayName = name,
                StartNodeId = new NodeId(name, 2), // namespace 2 = http://oilfield-scada/
                AttributeId = Attributes.Value,
                SamplingInterval = options.PublishingIntervalMs,
                QueueSize = 1,
                DiscardOldest = true
            };

            item.Notification += (mi, _) =>
            {
                foreach (var value in mi.DequeueValues())
                    onValue(mi.DisplayName, value);
            };

            subscription.AddItem(item);
        }

        session.AddSubscription(subscription);
        await subscription.CreateAsync();

        Log.Information("Suscripcion creada: {Count} items cada {Interval} ms",
            subscription.MonitoredItemCount, subscription.PublishingInterval);
    }

    public async Task DisconnectAsync()
    {
        if (session is not null)
            await session.CloseAsync();
    }
}