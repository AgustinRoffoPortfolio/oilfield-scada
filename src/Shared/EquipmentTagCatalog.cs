namespace Shared;

/// Tags del separador de producción.
public static class SeparatorTagCatalog
{
    public static readonly TagDefinition[] Analog =
    [
        new("Sep_P",     "bar", 0,  20, "Presion del separador"),
        new("Sep_level", "%",   0, 100, "Nivel de liquido del separador")
    ];
}

/// Tags del tramo de ducto.
public static class PipelineTagCatalog
{
    public static readonly TagDefinition[] Analog =
    [
        new("Pipe_P_in",  "bar",  0,  20, "Presion de entrada al ducto"),
        new("Pipe_P_out", "bar",  0,  20, "Presion de salida del ducto"),
        new("Pipe_Q",     "m3/d", 0, 800, "Caudal total transportado")
    ];
}