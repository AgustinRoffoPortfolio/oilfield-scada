using System.Text.Json;

namespace Shared;

/// Un tag tal como viene en el archivo de configuracion.
/// Si trae States es una enumeracion; si no, es un analogico con unidad y rango.
public class TagConfig
{
    public required string Name { get; set; }
    public string? Unit { get; set; }
    public double? Low { get; set; }
    public double? High { get; set; }
    public string Description { get; set; } = "";
    public string[]? States { get; set; }

    public bool IsEnum => States is { Length: > 0 };
}

/// Un tipo de equipo: el molde del que salen las instancias.
public class TypeConfig
{
    public required string Name { get; set; }
    public List<TagConfig> Tags { get; set; } = new();
}

/// Una instancia concreta de equipo en el campo.
public class DeviceConfig
{
    public required string Name { get; set; }
    public required string Type { get; set; }
}

/// El address space completo, leido de disco.
public class AddressSpaceConfig
{
    public List<TypeConfig> Types { get; set; } = new();
    public List<DeviceConfig> Devices { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // El JSON usa minusculas y las propiedades de C# van en PascalCase.
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// Lee y valida el archivo. Falla temprano y con mensaje claro:
    /// un error de configuracion tiene que doler al arrancar, no a las tres horas.
    public static AddressSpaceConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontro el archivo de configuracion: {path}");

        var config = JsonSerializer.Deserialize<AddressSpaceConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"El archivo {path} esta vacio o mal formado");

        config.Validate(path);
        return config;
    }

    /// Busca el archivo subiendo desde la carpeta de salida hasta la raiz del repo.
    /// Cada app corre desde su propio bin/, pero la configuracion del campo es una sola.
    public static string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) return relativePath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // El separador del JSON es "/" y en Windows queda mezclado con "\".
            var candidate = Path.GetFullPath(Path.Combine(dir.FullName, relativePath));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"No se encontro '{relativePath}' subiendo desde {AppContext.BaseDirectory}");
    }

    private void Validate(string path)
    {
        if (Types.Count == 0)
            throw new InvalidOperationException($"{path}: no hay ningun tipo definido");
        if (Devices.Count == 0)
            throw new InvalidOperationException($"{path}: no hay ningun equipo definido");

        foreach (var duplicate in Types.GroupBy(t => t.Name).Where(g => g.Count() > 1))
            throw new InvalidOperationException($"{path}: tipo duplicado '{duplicate.Key}'");

        foreach (var duplicate in Devices.GroupBy(d => d.Name).Where(g => g.Count() > 1))
            throw new InvalidOperationException($"{path}: equipo duplicado '{duplicate.Key}'");

        foreach (var type in Types)
        {
            if (type.Tags.Count == 0)
                throw new InvalidOperationException($"{path}: el tipo '{type.Name}' no tiene tags");

            foreach (var duplicate in type.Tags.GroupBy(t => t.Name).Where(g => g.Count() > 1))
                throw new InvalidOperationException(
                    $"{path}: tag duplicado '{duplicate.Key}' en el tipo '{type.Name}'");

            foreach (var tag in type.Tags.Where(t => !t.IsEnum))
            {
                if (tag.Low is null || tag.High is null)
                    throw new InvalidOperationException(
                        $"{path}: el tag '{type.Name}/{tag.Name}' necesita low y high");
                if (tag.Low >= tag.High)
                    throw new InvalidOperationException(
                        $"{path}: el tag '{type.Name}/{tag.Name}' tiene low >= high");
            }
        }

        foreach (var device in Devices)
        {
            if (!Types.Any(t => t.Name == device.Type))
                throw new InvalidOperationException(
                    $"{path}: el equipo '{device.Name}' referencia un tipo inexistente '{device.Type}'");
        }
    }

    /// Busca el tipo de un equipo. Validate() ya garantizo que existe.
    public TypeConfig TypeOf(DeviceConfig device) => Types.First(t => t.Name == device.Type);
}