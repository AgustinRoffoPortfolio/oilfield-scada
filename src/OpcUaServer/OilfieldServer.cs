using Opc.Ua;
using Opc.Ua.Server;
using Shared;

namespace OpcUaServer;

/// Servidor estándar del stack, más nuestro NodeManager registrado.
public class OilfieldServer : StandardServer
{
    private readonly Oilfield _oilfield;
    private readonly string _namespaceUri;

    public OilfieldNodeManager? NodeManager { get; private set; }

    public OilfieldServer(Oilfield oilfield, string namespaceUri)
    {
        _oilfield = oilfield;
        _namespaceUri = namespaceUri;
    }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        NodeManager = new OilfieldNodeManager(server, configuration, _oilfield, _namespaceUri);
        return new MasterNodeManager(server, configuration, null,
            new INodeManager[] { NodeManager });
    }
}