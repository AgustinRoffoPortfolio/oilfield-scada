using Opc.Ua;
using Opc.Ua.Server;

namespace OpcUaServer;

/// Construye y administra la rama "Oilfield" del espacio de direcciones.
public class OilfieldNodeManager : CustomNodeManager2
{
    public OilfieldNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "http://oilfield-scada/")
    {
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            // Carpeta raíz de nuestro modelo.
            var root = new FolderState(null)
            {
                NodeId = new NodeId("Oilfield", NamespaceIndex),
                BrowseName = new QualifiedName("Oilfield", NamespaceIndex),
                DisplayName = "Oilfield",
                TypeDefinitionId = ObjectTypeIds.FolderType
            };

            // La colgamos de la carpeta estándar "Objects".
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? refs))
            {
                externalReferences[ObjectIds.ObjectsFolder] = refs = new List<IReference>();
            }
            refs.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));

            // Variable de prueba, valor fijo por ahora.
            var ping = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Oilfield/Ping", NamespaceIndex),
                BrowseName = new QualifiedName("Ping", NamespaceIndex),
                DisplayName = "Ping",
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                Value = 42.0,
                Timestamp = DateTime.UtcNow
            };
            root.AddChild(ping);

            AddPredefinedNode(SystemContext, root);
        }
    }
}