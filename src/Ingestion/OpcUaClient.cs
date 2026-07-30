using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Serilog;
using Shared;

namespace Ingestion;

/// Cliente OPC UA: se conecta al servidor del yacimiento, se suscribe a sus tags
/// y se reconecta solo si la comunicacion se corta.
public sealed class OpcUaClient(OpcUaOptions options)
{
    private ISession? session;
    private Subscription? subscription;
    private SessionReconnectHandler? reconnectHandler;
    private bool shuttingDown;

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

        // Latido: si el servidor no responde en este intervalo, saltan las alarmas.
        session.KeepAliveInterval = options.KeepAliveIntervalMs;
        session.KeepAlive += OnKeepAlive;

        Log.Information("Sesion OPC UA abierta contra {Endpoint}", endpoint.EndpointUrl);
    }

    /// Se dispara con cada latido. Si viene mal, arranca la reconexion.
    private void OnKeepAlive(ISession sender, KeepAliveEventArgs e)
    {
        if (shuttingDown || !ServiceResult.IsBad(e.Status)) return;
        if (reconnectHandler is not null) return; // ya hay un reintento en curso

        Log.Warning("Comunicacion OPC UA perdida ({Status}). Reintentando cada {Period} ms",
            e.Status, options.ReconnectPeriodMs);

        reconnectHandler = new SessionReconnectHandler(reconnectAbort: true);
        reconnectHandler.BeginReconnect(sender, options.ReconnectPeriodMs, OnReconnectComplete);
    }

    /// Se dispara cuando el reintento termino, con exito o no.
    private void OnReconnectComplete(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, reconnectHandler)) return;

        // El handler puede devolver la misma sesion revivida o una nueva.
        var recovered = reconnectHandler?.Session;
        if (recovered is not null && !ReferenceEquals(recovered, session))
        {
            session = recovered;
            subscription = session.Subscriptions.FirstOrDefault();
        }

        reconnectHandler?.Dispose();
        reconnectHandler = null;

        Log.Information("Comunicacion OPC UA restablecida. Items activos: {Count}",
            subscription?.MonitoredItemCount ?? 0);
    }

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
        shuttingDown = true;
        reconnectHandler?.Dispose();
        reconnectHandler = null;

        if (session is not null)
        {
            session.KeepAlive -= OnKeepAlive;
            await session.CloseAsync();
        }
    }
}