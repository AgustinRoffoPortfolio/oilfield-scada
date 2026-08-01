using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

/// Tipo de dato de una entrada del mapa. Un float ocupa dos registros;
/// un entero, uno solo.
public enum RegisterType
{
    Float32,
    UInt16
}

/// Una entrada del mapa: en que direccion vive una medicion y con que formato.
public class RegisterEntry
{
    public int Address { get; set; }
    public required string Tag { get; set; }
    public RegisterType Type { get; set; } = RegisterType.Float32;

    /// Cuantos registros de 16 bits ocupa.
    public int Size => Type == RegisterType.Float32 ? 2 : 1;
}

/// El bloque de registros de un equipo.
public class ModbusDeviceMap
{
    public required string Name { get; set; }
    public int StartAddress { get; set; }
    public List<RegisterEntry> Registers { get; set; } = new();
}

/// El mapa completo del RTU. Equivale a la tabla que publica el fabricante:
/// no se genera desde el modelo, se transcribe del manual y se valida.
public class ModbusMap
{
    public byte UnitId { get; set; } = 1;
    public WordOrder WordOrder { get; set; } = WordOrder.HighFirst;
    public int BlockSize { get; set; } = 20;
    public List<ModbusDeviceMap> Devices { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Para que "HighFirst" y "float32" del JSON caigan en los enums.
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static ModbusMap Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontro el mapa Modbus: {path}");

        var map = JsonSerializer.Deserialize<ModbusMap>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"El archivo {path} esta vacio o mal formado");

        map.Validate(path);
        return map;
    }

    private void Validate(string path)
    {
        if (Devices.Count == 0)
            throw new InvalidOperationException($"{path}: el mapa no tiene equipos");

        // Un registro ocupado por dos mediciones distintas es el error mas
        // caro de este archivo: no falla, entrega el valor de otra cosa.
        var occupied = new Dictionary<int, string>();

        foreach (var device in Devices)
        {
            foreach (var entry in device.Registers)
            {
                if (entry.Address < device.StartAddress ||
                    entry.Address >= device.StartAddress + BlockSize)
                {
                    throw new InvalidOperationException(
                        $"{path}: '{device.Name}/{entry.Tag}' en la direccion {entry.Address} " +
                        $"cae fuera de su bloque ({device.StartAddress}..{device.StartAddress + BlockSize - 1})");
                }

                for (var offset = 0; offset < entry.Size; offset++)
                {
                    var address = entry.Address + offset;
                    var owner = $"{device.Name}/{entry.Tag}";

                    if (occupied.TryGetValue(address, out var existing))
                    {
                        throw new InvalidOperationException(
                            $"{path}: la direccion {address} esta asignada a '{existing}' y a '{owner}'");
                    }

                    occupied[address] = owner;
                }
            }
        }
    }

    /// Direccion mas alta usada. Define el tamano de la tabla de registros.
    public int HighestAddress => Devices
        .SelectMany(d => d.Registers)
        .Select(r => r.Address + r.Size - 1)
        .DefaultIfEmpty(0)
        .Max();

    /// Todas las entradas con su nombre completo de tag ("POZO-A/THP").
    public IEnumerable<(string TagName, RegisterEntry Entry)> AllEntries() =>
        Devices.SelectMany(d => d.Registers.Select(r => ($"{d.Name}/{r.Tag}", r)));

    /// Cruza el mapa contra el address space y devuelve las diferencias.
    /// Son dos archivos que un humano mantiene a mano: el desfasaje se detecta
    /// al arrancar, no cuando falta un valor en la pantalla.
    public (List<string> SinRegistro, List<string> SinTag) CrossCheck(AddressSpaceConfig addressSpace)
    {
        var configured = addressSpace.Devices
            .SelectMany(d => addressSpace.TypeOf(d).Tags.Select(t => $"{d.Name}/{t.Name}"))
            .ToHashSet();

        var mapped = AllEntries().Select(e => e.TagName).ToHashSet();

        return (
            SinRegistro: configured.Except(mapped).Order().ToList(),
            SinTag: mapped.Except(configured).Order().ToList());
    }
}