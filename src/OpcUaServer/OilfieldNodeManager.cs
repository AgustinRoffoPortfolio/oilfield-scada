using Opc.Ua;
using Opc.Ua.Server;
using Range = Opc.Ua.Range;

namespace OpcUaServer;

/// Construye y administra la rama "Oilfield" del espacio de direcciones.
public class OilfieldNodeManager : CustomNodeManager2
{
    // Acceso rápido a cada tag para escribirle valores en el paso siguiente.
    // Clave: "Well-01/THP".
    private readonly Dictionary<string, AnalogItemState> _analogTags = new();
    private readonly Dictionary<string, MultiStateDiscreteState> _statusTags = new();

    public OilfieldNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "http://oilfield-scada/")
    {
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

            // 3) Los tres pozos del yacimiento. Tienen que existir ANTES
            //    del AddPredefinedNode, que registra la rama completa de una vez.
            foreach (string wellName in new[] { "Well-01", "Well-02", "Well-03" })
            {
                CreateWell(root, wellName);
            }

            AddPredefinedNode(SystemContext, root);
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

    /// Crea un pozo como instancia de WellType, con todos sus tags.
    private BaseObjectState CreateWell(NodeState parent, string wellName)
    {
        var well = new BaseObjectState(parent)
        {
            NodeId = new NodeId(wellName, NamespaceIndex),
            BrowseName = new QualifiedName(wellName, NamespaceIndex),
            DisplayName = wellName,
            TypeDefinitionId = new NodeId("WellType", NamespaceIndex),
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None
        };

        foreach (var tag in WellTagCatalog.Analog)
        {
            string key = $"{wellName}/{tag.Name}";
            var variable = CreateAnalogTag(well, key, tag);
            well.AddChild(variable);
            _analogTags[key] = variable;
        }

        var status = CreateStatusTag(well, $"{wellName}/Status");
        well.AddChild(status);
        _statusTags[wellName] = status;

        parent.AddChild(well);
        return well;
    }

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
            Value = new LocalizedText[]
            {
                new("RUNNING"), new("STOPPED"), new("FAULT")
            }
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