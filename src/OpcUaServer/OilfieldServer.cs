using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

/// Servidor estándar del stack, más nuestro NodeManager registrado.
public class OilfieldServer : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        var nodeManagers = new INodeManager[] { new OilfieldNodeManager(server, configuration) };
        return new MasterNodeManager(server, configuration, null, nodeManagers);
    }
}