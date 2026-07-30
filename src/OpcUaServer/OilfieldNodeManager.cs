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
            // 1) Los tipos. Los tres cuelgan de BaseObjectType.
            var types = new[]
            {
                CreateWellType(),
                CreateObjectType("SeparatorType", SeparatorTagCatalog.Analog),
                CreateObjectType("PipelineType", PipelineTagCatalog.Analog)
            };

            foreach (var type in types)
            {
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

            // 3) Las instancias, en el orden en que fluye el fluido.
            //    Tienen que existir ANTES del AddPredefinedNode, que registra
            //    la rama completa de una vez.
            foreach (var well in _oilfield.Wells)
            {
                CreateWell(root, well);
            }
            CreateSeparator(root, _oilfield.Separator);
            CreatePipeline(root, _oilfield.Pipeline);

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

    // ---------- Declaración de tipos ----------

    /// Declara un ObjectType con sus tags analógicos como componentes obligatorios.
    private BaseObjectTypeState CreateObjectType(string typeName, TagDefinition[] tags)
    {
        var type = new BaseObjectTypeState
        {
            NodeId = new NodeId(typeName, NamespaceIndex),
            BrowseName = new QualifiedName(typeName, NamespaceIndex),
            DisplayName = typeName,
            SuperTypeId = ObjectTypeIds.BaseObjectType,
            IsAbstract = false
        };
        type.AddReference(ReferenceTypeIds.HasSubtype, true, ObjectTypeIds.BaseObjectType);

        foreach (var tag in tags)
        {
            var variable = CreateAnalogTag(type, $"{typeName}/{tag.Name}", tag);
            // Mandatory: toda instancia del tipo tiene que traer este tag.
            variable.ModellingRuleId = ObjectIds.ModellingRule_Mandatory;
            type.AddChild(variable);
        }

        return type;
    }

    /// WellType es un ObjectType común más el tag de estado.
    private BaseObjectTypeState CreateWellType()
    {
        var wellType = CreateObjectType("WellType", WellTagCatalog.Analog);

        var statusTemplate = CreateStatusTag(wellType, "WellType/Status");
        statusTemplate.ModellingRuleId = ObjectIds.ModellingRule_Mandatory;
        wellType.AddChild(statusTemplate);

        return wellType;
    }

    // ---------- Creación de instancias ----------

    /// Crea un equipo como instancia de un tipo y ata cada tag a su sensor.
    /// El delegado sensorFor traduce nombre de tag a sensor del modelo físico.
    private BaseObjectState CreateInstance(NodeState parent, string name, string typeName,
        TagDefinition[] tags, Func<string, Sensor> sensorFor)
    {
        var node = new BaseObjectState(parent)
        {
            NodeId = new NodeId(name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name,
            TypeDefinitionId = new NodeId(typeName, NamespaceIndex),
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            EventNotifier = EventNotifiers.None
        };

        foreach (var tag in tags)
        {
            var variable = CreateAnalogTag(node, $"{name}/{tag.Name}", tag);
            node.AddChild(variable);
            _analogBindings.Add((variable, sensorFor(tag.Name)));
        }

        parent.AddChild(node);
        return node;
    }

    private void CreateWell(NodeState parent, Well well)
    {
        var node = CreateInstance(parent, well.Name, "WellType", WellTagCatalog.Analog,
            tagName => tagName switch
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
            });

        var status = CreateStatusTag(node, $"{well.Name}/Status");
        node.AddChild(status);
        _statusBindings.Add((status, well));
    }

    private void CreateSeparator(NodeState parent, Separator separator)
    {
        CreateInstance(parent, "Separator", "SeparatorType", SeparatorTagCatalog.Analog,
            tagName => tagName switch
            {
                "Sep_P" => separator.Pressure,
                "Sep_level" => separator.Level,
                _ => throw new ArgumentException($"Tag sin sensor asociado: {tagName}")
            });
    }

    private void CreatePipeline(NodeState parent, Pipeline pipeline)
    {
        CreateInstance(parent, "Pipeline", "PipelineType", PipelineTagCatalog.Analog,
            tagName => tagName switch
            {
                "Pipe_P_in" => pipeline.InletPressure,
                "Pipe_P_out" => pipeline.OutletPressure,
                "Pipe_Q" => pipeline.TotalFlow,
                _ => throw new ArgumentException($"Tag sin sensor asociado: {tagName}")
            });
    }

    // ---------- Fábricas de variables ----------

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