using Opc.Ua;
using Opc.Ua.Server;
using Shared;
using Range = Opc.Ua.Range;

namespace OpcUaServer;

/// Construye la rama "Oilfield" desde el archivo de configuracion y copia a los
/// nodos los valores que entrega la fuente de datos. No conoce el dominio: no
/// sabe que es un pozo ni que mide un caudalimetro, solo tipos, equipos y tags.
public class OilfieldNodeManager : CustomNodeManager2
{
    private readonly AddressSpaceConfig _config;
    private readonly ITagValueSource _source;

    // Cada nodo apareado con el nombre del tag que lo alimenta. Se arma una vez,
    // al construir el arbol, para no resolver nombres en cada ciclo.
    private readonly List<(AnalogItemState Node, string TagName)> _analogBindings = new();
    private readonly List<(MultiStateDiscreteState Node, string TagName)> _enumBindings = new();

    public OilfieldNodeManager(IServerInternal server, ApplicationConfiguration configuration,
        AddressSpaceConfig config, ITagValueSource source, string namespaceUri)
        : base(server, configuration, namespaceUri)
    {
        _config = config;
        _source = source;
    }

    /// Cantidad de tags publicados. La usa el arranque para loguear.
    public int TagCount => _analogBindings.Count + _enumBindings.Count;

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            // 1) Los tipos declarados en la configuracion. Todos cuelgan de BaseObjectType.
            foreach (var typeConfig in _config.Types)
            {
                var type = CreateObjectType(typeConfig);
                LinkToParent(externalReferences, ObjectTypeIds.BaseObjectType,
                    type.NodeId, ReferenceTypeIds.HasSubtype);
                AddPredefinedNode(SystemContext, type);
            }

            // 2) La carpeta donde viven las instancias.
            var root = new FolderState(null)
            {
                NodeId = new NodeId("Oilfield", NamespaceIndex),
                BrowseName = new QualifiedName("Oilfield", NamespaceIndex),
                DisplayName = "Oilfield",
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            LinkToParent(externalReferences, ObjectIds.ObjectsFolder,
                root.NodeId, ReferenceTypeIds.Organizes);

            // 3) Los equipos, en el orden del archivo. Tienen que existir ANTES
            //    del AddPredefinedNode, que registra la rama completa de una vez.
            foreach (var device in _config.Devices)
            {
                AddDevice(root, device);
            }

            AddPredefinedNode(SystemContext, root);
        }
    }

    /// Copia los valores actuales de la fuente a los nodos y avisa a los
    /// clientes suscriptos. Se llama desde el timer del programa principal.
    public void UpdateValues()
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;

            foreach (var (node, tagName) in _analogBindings)
            {
                if (_source.TryGetValue(tagName, out var value))
                {
                    node.Value = value;
                    node.StatusCode = StatusCodes.Good;
                }
                else
                {
                    // Sin dato de campo: el valor queda como estaba pero marcado
                    // como malo, que es lo que el cliente tiene que ver.
                    node.StatusCode = StatusCodes.BadNoCommunication;
                }

                node.Timestamp = now;
                node.ClearChangeMasks(SystemContext, false);
            }

            foreach (var (node, tagName) in _enumBindings)
            {
                if (_source.TryGetValue(tagName, out var value))
                {
                    node.Value = (uint)Math.Round(value);
                    node.StatusCode = StatusCodes.Good;
                }
                else
                {
                    node.StatusCode = StatusCodes.BadNoCommunication;
                }

                node.Timestamp = now;
                node.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    // ---------- Declaracion de tipos ----------

    /// Declara un ObjectType con sus tags como componentes obligatorios.
    private BaseObjectTypeState CreateObjectType(TypeConfig typeConfig)
    {
        var type = new BaseObjectTypeState
        {
            NodeId = new NodeId(typeConfig.Name, NamespaceIndex),
            BrowseName = new QualifiedName(typeConfig.Name, NamespaceIndex),
            DisplayName = typeConfig.Name,
            SuperTypeId = ObjectTypeIds.BaseObjectType,
            IsAbstract = false
        };
        type.AddReference(ReferenceTypeIds.HasSubtype, true, ObjectTypeIds.BaseObjectType);

        foreach (var tag in typeConfig.Tags)
        {
            var id = $"{typeConfig.Name}/{tag.Name}";

            // Mandatory: toda instancia del tipo tiene que traer este tag.
            BaseVariableState template = tag.IsEnum
                ? CreateEnumTag(type, id, tag)
                : CreateAnalogTag(type, id, tag);

            template.ModellingRuleId = ObjectIds.ModellingRule_Mandatory;
            type.AddChild(template);
        }

        return type;
    }

    // ---------- Creacion de instancias ----------

    /// Crea un equipo como instancia de su tipo, con todos sus tags.
    private BaseObjectState AddDevice(NodeState parent, DeviceConfig device)
    {
        var typeConfig = _config.TypeOf(device);

        var node = new BaseObjectState(parent)
        {
            NodeId = new NodeId(device.Name, NamespaceIndex),
            BrowseName = new QualifiedName(device.Name, NamespaceIndex),
            DisplayName = device.Name,
            TypeDefinitionId = new NodeId(typeConfig.Name, NamespaceIndex),
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None
        };

        foreach (var tag in typeConfig.Tags)
        {
            AddTag(node, device.Name, tag);
        }

        parent.AddChild(node);
        return node;
    }

    /// Crea un tag dentro de un equipo y lo registra para la actualizacion ciclica.
    private void AddTag(NodeState device, string deviceName, TagConfig tag)
    {
        var tagName = $"{deviceName}/{tag.Name}";

        if (tag.IsEnum)
        {
            var node = CreateEnumTag(device, tagName, tag);
            device.AddChild(node);
            _enumBindings.Add((node, tagName));
        }
        else
        {
            var node = CreateAnalogTag(device, tagName, tag);
            device.AddChild(node);
            _analogBindings.Add((node, tagName));
        }
    }

    // ---------- Fabricas de variables ----------

    /// Crea una variable analogica con unidad de ingenieria y rango de escala.
    private AnalogItemState CreateAnalogTag(NodeState parent, string id, TagConfig tag)
    {
        var variable = new AnalogItemState(parent)
        {
            NodeId = new NodeId(id, NamespaceIndex),
            BrowseName = new QualifiedName(tag.Name, NamespaceIndex),
            DisplayName = tag.Name,
            Description = tag.Description,
            TypeDefinitionId = VariableTypeIds.AnalogItemType,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            DataType = DataTypeIds.Double,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = 0.0,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        variable.EngineeringUnits = new PropertyState<EUInformation>(variable)
        {
            NodeId = new NodeId($"{id}.EngineeringUnits", NamespaceIndex),
            BrowseName = new QualifiedName(BrowseNames.EngineeringUnits),
            DisplayName = BrowseNames.EngineeringUnits,
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypeIds.HasProperty,
            DataType = DataTypeIds.EUInformation,
            ValueRank = ValueRanks.Scalar,
            Value = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = -1,
                DisplayName = tag.Unit ?? "",
                Description = tag.Description
            }
        };

        variable.EURange = new PropertyState<Range>(variable)
        {
            NodeId = new NodeId($"{id}.EURange", NamespaceIndex),
            BrowseName = new QualifiedName(BrowseNames.EURange),
            DisplayName = BrowseNames.EURange,
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypeIds.HasProperty,
            DataType = DataTypeIds.Range,
            ValueRank = ValueRanks.Scalar,
            // Validate() ya garantizo que un tag analogico trae los dos limites.
            Value = new Range { Low = tag.Low!.Value, High = tag.High!.Value }
        };

        return variable;
    }

    /// Tag discreto: entero con etiquetas de texto, tomadas de la configuracion.
    private MultiStateDiscreteState CreateEnumTag(NodeState parent, string id, TagConfig tag)
    {
        var status = new MultiStateDiscreteState(parent)
        {
            NodeId = new NodeId(id, NamespaceIndex),
            BrowseName = new QualifiedName(tag.Name, NamespaceIndex),
            DisplayName = tag.Name,
            Description = tag.Description,
            TypeDefinitionId = VariableTypeIds.MultiStateDiscreteType,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            DataType = DataTypeIds.UInt32,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = (uint)0,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        status.EnumStrings = new PropertyState<LocalizedText[]>(status)
        {
            NodeId = new NodeId($"{id}.EnumStrings", NamespaceIndex),
            BrowseName = new QualifiedName(BrowseNames.EnumStrings),
            DisplayName = BrowseNames.EnumStrings,
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypeIds.HasProperty,
            DataType = DataTypeIds.LocalizedText,
            ValueRank = ValueRanks.OneDimension,
            Value = tag.States!.Select(s => new LocalizedText(s)).ToArray()
        };

        return status;
    }

    /// Registra la referencia inversa desde un nodo que no es nuestro.
    private static void LinkToParent(IDictionary<NodeId, IList<IReference>> externalReferences,
        NodeId parentId, NodeId childId, NodeId referenceType)
    {
        if (!externalReferences.TryGetValue(parentId, out IList<IReference>? refs))
        {
            externalReferences[parentId] = refs = new List<IReference>();
        }
        refs.Add(new NodeStateReference(referenceType, false, childId));
    }
}