using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Serilog;
using Shared;

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
    /// Se suscribe a los tags indicados. El callback se dispara con cada valor nuevo.
    public async Task SubscribeAsync(
        IEnumerable<FieldTag> tags,
        Action<string, DataValue> onValue)
    {
        subscription = new Subscription(session!.DefaultSubscription)
        {
            PublishingInterval = options.PublishingIntervalMs,
            DisplayName = "OilfieldTags"
        };

        foreach (var tag in tags)
        {
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                DisplayName = tag.Name,
                StartNodeId = new NodeId(tag.Name, 2), // namespace 2 = http://oilfield-scada/
                AttributeId = Attributes.Value,
                SamplingInterval = options.PublishingIntervalMs,
                QueueSize = 1,
                DiscardOldest = true
            };

            // Deadband porcentual solo para analogicos: los discretos no tienen
            // rango de ingenieria y queremos cada cambio de estado.
            bool isAnalog = tag.Unit is not null;
            if (isAnalog && options.DeadbandPercent > 0)
            {
                item.Filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.Percent,
                    DeadbandValue = options.DeadbandPercent
                };
            }

            item.Notification += (mi, _) =>
            {
                foreach (var value in mi.DequeueValues())
                    onValue(mi.DisplayName, value);
            };

            subscription.AddItem(item);
        }

        session.AddSubscription(subscription);
        await subscription.CreateAsync();

        // Si el servidor rechazo algun filtro, nos enteramos aca y no en silencio.
        foreach (var item in subscription.MonitoredItems)
        {
            if (ServiceResult.IsBad(item.Status.Error))
                Log.Warning("Item {Tag} con problema: {Error}", item.DisplayName, item.Status.Error);
        }

        Log.Information("Suscripcion creada: {Count} items cada {Interval} ms, deadband {Deadband}%",
            subscription.MonitoredItemCount, subscription.PublishingInterval, options.DeadbandPercent);
    }

    public async Task DisconnectAsync()
    {
        if (session is not null)
            await session.CloseAsync();
    }
}