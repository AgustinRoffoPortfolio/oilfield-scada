using Opc.Ua;
using Opc.Ua.Server;
using Shared;
using Range = Opc.Ua.Range;

namespace OpcUaServer;

/// Construye la rama "Oilfield" y copia a los nodos los valores del simulador.
public class OilfieldNodeManager : CustomNodeManager2
{
    private readonly Oilfield _oilfield;

    // Cada nodo apareado con el sensor que lo alimenta. Se arma una vez,
    // al construir el árbol, para no buscar por nombre en cada ciclo.
    private readonly List<(AnalogItemState Node, Sensor Sensor)> _analogBindings = new();
    private readonly List<(MultiStateDiscreteState Node, Well Well)> _statusBindings = new();

    public OilfieldNodeManager(IServerInternal server, ApplicationConfiguration configuration,
        Oilfield oilfield, string namespaceUri)
        : base(server, configuration, namespaceUri)
    {
        _oilfield = oilfield;
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            // 1) El tipo: qué variables tiene un pozo. Cuelga de BaseObjectType.
            var wellType = CreateWellType();
            LinkToParent(externalReferences, ObjectTypeIds.BaseObjectType,
                wellType.NodeId, ReferenceTypeIds.HasSubtype);
            AddPredefinedNode(SystemContext, wellType);

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

            // 3) Un nodo por cada pozo del simulador. Tienen que existir ANTES
            //    del AddPredefinedNode, que registra la rama completa de una vez.
            foreach (var well in _oilfield.Wells)
            {
                CreateWell(root, well);
            }

            AddPredefinedNode(SystemContext, root);
        }
    }

    /// Copia los valores actuales del simulador a los nodos y avisa a los
    /// clientes suscriptos. Se llama desde el timer del programa principal.
    public void UpdateValues()
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;

            foreach (var (node, sensor) in _analogBindings)
            {
                node.Value = sensor.Value;
                node.Timestamp = now;
                node.StatusCode = StatusCodes.Good;
                node.ClearChangeMasks(SystemContext, false);
            }

            foreach (var (node, well) in _statusBindings)
            {
                node.Value = (uint)well.Status;
                node.Timestamp = now;
                node.StatusCode = StatusCodes.Good;
                node.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    /// Declara WellType con sus tags como componentes obligatorios.
    private BaseObjectTypeState CreateWellType()
    {
        var wellType = new BaseObjectTypeState
        {
            NodeId = new NodeId("WellType", NamespaceIndex),
            BrowseName = new QualifiedName("WellType", NamespaceIndex),
            DisplayName = "WellType",
            SuperTypeId = ObjectTypeIds.BaseObjectType,
            IsAbstract = false
        };
        wellType.AddReference(ReferenceTypeIds.HasSubtype, true, ObjectTypeIds.BaseObjectType);

        foreach (var tag in WellTagCatalog.Analog)
        {
            var variable = CreateAnalogTag(wellType, $"WellType/{tag.Name}", tag);
            // Mandatory: toda instancia de WellType tiene que traer este tag.
            variable.ModellingRuleId = ObjectIds.ModellingRule_Mandatory;
            wellType.AddChild(variable);
        }

        var statusTemplate = CreateStatusTag(wellType, "WellType/Status");
        statusTemplate.ModellingRuleId = ObjectIds.ModellingRule_Mandatory;
        wellType.AddChild(statusTemplate);

        return wellType;
    }

    /// Crea un pozo como instancia de WellType y lo ata a sus sensores.
    private BaseObjectState CreateWell(NodeState parent, Well well)
    {
        var node = new BaseObjectState(parent)
        {
            NodeId = new NodeId(well.Name, NamespaceIndex),
            BrowseName = new QualifiedName(well.Name, NamespaceIndex),
            DisplayName = well.Name,
            TypeDefinitionId = new NodeId("WellType", NamespaceIndex),
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None
        };

        foreach (var tag in WellTagCatalog.Analog)
        {
            var variable = CreateAnalogTag(node, $"{well.Name}/{tag.Name}", tag);
            node.AddChild(variable);
            _analogBindings.Add((variable, SensorFor(well, tag.Name)));
        }

        var status = CreateStatusTag(node, $"{well.Name}/Status");
        node.AddChild(status);
        _statusBindings.Add((status, well));

        parent.AddChild(node);
        return node;
    }

    /// Traduce el nombre del tag al sensor correspondiente del modelo físico.
    private static Sensor SensorFor(Well well, string tagName) => tagName switch
    {
        "THP" => well.WellheadPressure,
        "CHP" => well.CasingPressure,
        "T_head" => well.HeadTemperature,
        "Q_oil" => well.OilRate,
        "Q_water" => well.WaterRate,
        "Q_gas" => well.GasRate,
        "ESP_current" => well.EspCurrent,
        "ESP_freq" => well.EspFrequency,
        "ESP_vib" => well.EspVibration,
        _ => throw new ArgumentException($"Tag sin sensor asociado: {tagName}")
    };

    /// Crea una variable analógica con unidad de ingeniería y rango de escala.
    private AnalogItemState CreateAnalogTag(NodeState parent, string id, TagDefinition tag)
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
                DisplayName = tag.Unit,
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
            Value = new Range { Low = tag.Low, High = tag.High }
        };

        return variable;
    }

    /// Estado del pozo como enumeración con etiquetas de texto.
    private MultiStateDiscreteState CreateStatusTag(NodeState parent, string id)
    {
        var status = new MultiStateDiscreteState(parent)
        {
            NodeId = new NodeId(id, NamespaceIndex),
            BrowseName = new QualifiedName("Status", NamespaceIndex),
            DisplayName = "Status",
            Description = "Estado del pozo",
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
            // Las etiquetas salen del enum del simulador, en orden de valor.
            // Así no pueden desincronizarse del modelo físico.
            Value = Enum.GetNames<WellStatus>()
                .Select(name => new LocalizedText(name.ToUpperInvariant()))
                .ToArray()
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