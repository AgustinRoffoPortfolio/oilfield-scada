namespace Shared;

/// Un tag del yacimiento con su nombre completo, igual al NodeId que publica OPC UA.
public record FieldTag(
    string Name, string Equipment, string Variable, string? Unit, double Low, double High);

/// Arma la lista completa de tags recorriendo el yacimiento y los catalogos.
/// Los nombres de los pozos salen del modelo, no de una lista paralela.
public static class FieldTagCatalog
{
    public static IReadOnlyList<FieldTag> Build(Oilfield oilfield)
    {
        var tags = new List<FieldTag>();

        foreach (var well in oilfield.Wells)
        {
            foreach (var t in WellTagCatalog.Analog)
                tags.Add(new FieldTag($"{well.Name}/{t.Name}", well.Name, t.Name, t.Unit, t.Low, t.High));

            // El estado no es analogico: no tiene unidad y su rango es el del enum.
            tags.Add(new FieldTag($"{well.Name}/Status", well.Name, "Status", null, 0, 2));
        }

        foreach (var t in SeparatorTagCatalog.Analog)
            tags.Add(new FieldTag($"Separator/{t.Name}", "Separator", t.Name, t.Unit, t.Low, t.High));

        foreach (var t in PipelineTagCatalog.Analog)
            tags.Add(new FieldTag($"Pipeline/{t.Name}", "Pipeline", t.Name, t.Unit, t.Low, t.High));

        return tags;
    }
}