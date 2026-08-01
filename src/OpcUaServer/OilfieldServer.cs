using Opc.Ua;
using Opc.Ua.Server;
using Shared;

namespace OpcUaServer;

/// Servidor estandar del stack, mas nuestro NodeManager registrado.
public class OilfieldServer : StandardServer
{
    private readonly AddressSpaceConfig _config;
    private readonly ITagValueSource _source;
    private readonly string _namespaceUri;

    public OilfieldNodeManager? NodeManager { get; private set; }

    public OilfieldServer(AddressSpaceConfig config, ITagValueSource source, string namespaceUri)
    {
        _config = config;
        _source = source;
        _namespaceUri = namespaceUri;
    }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server, ApplicationConfiguration configuration)
    {
        NodeManager = new OilfieldNodeManager(server, configuration, _config, _source, _namespaceUri);
        return new MasterNodeManager(server, configuration, null,
            new INodeManager[] { NodeManager });
    }
}