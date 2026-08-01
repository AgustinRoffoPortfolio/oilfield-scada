using Shared;

namespace Simulator;

/// Vuelca los valores del modelo fisico a la tabla de registros,
/// en las direcciones que dice el mapa del fabricante.
public class RegisterUpdater
{
    private readonly ModbusMap _map;
    private readonly FieldValues _values;
    private readonly RegisterTable _table;

    public RegisterUpdater(ModbusMap map, FieldValues values, RegisterTable table)
    {
        _map = map;
        _values = values;
        _table = table;
    }

    /// Escribe una vez todos los tags mapeados.
    public void Update()
    {
        foreach (var (tagName, entry) in _map.AllEntries())
        {
            if (!_values.TryGetValue(tagName, out var value)) continue;

            if (entry.Type == RegisterType.Float32)
                _table.WriteFloat(entry.Address, value);
            else
                _table.WriteUInt16(entry.Address, value);
        }
    }

    /// Tags del mapa que el modelo fisico no sabe alimentar.
    public IReadOnlyList<string> MissingTags => _map.AllEntries()
        .Select(e => e.TagName)
        .Where(name => !_values.TryGetValue(name, out _))
        .ToList();
}