using Opc.Ua;
using Opc.Ua.Server;
using Shared;

namespace OpcUaServer;

/// Servidor estándar del stack, más nuestro NodeManager registrado.
public class OilfieldServer : StandardServer
{
    private readonly Oilfield _oilfield;

    public OilfieldNodeManager? NodeManager { get; private set; }

    public OilfieldServer(Oilfield oilfield) => _oilfield = oilfield;

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        NodeManager = new OilfieldNodeManager(server, configuration, _oilfield);
        return new MasterNodeManager(server, configuration, null,
            new INodeManager[] { NodeManager });
    }
}