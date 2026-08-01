namespace Shared;

/// Un tag del yacimiento con su nombre completo, igual al NodeId que publica OPC UA.
public record FieldTag(
    string Name, string Equipment, string Variable, string? Unit, double Low, double High);

/// Arma la lista completa de tags desde el archivo de configuracion.
/// Es la MISMA fuente de la que el servidor OPC UA arma su arbol de nodos:
/// asi la tabla de la base y el address space no pueden divergir.
public static class FieldTagCatalog
{
    public static IReadOnlyList<FieldTag> Build(AddressSpaceConfig config)
    {
        var tags = new List<FieldTag>();

        foreach (var device in config.Devices)
        {
            foreach (var tag in config.TypeOf(device).Tags)
            {
                // Los enums no tienen unidad; su rango es el de los indices validos.
                var low = tag.IsEnum ? 0 : tag.Low!.Value;
                var high = tag.IsEnum ? tag.States!.Length - 1 : tag.High!.Value;

                tags.Add(new FieldTag(
                    $"{device.Name}/{tag.Name}", device.Name, tag.Name, tag.Unit, low, high));
            }
        }

        return tags;
    }
}