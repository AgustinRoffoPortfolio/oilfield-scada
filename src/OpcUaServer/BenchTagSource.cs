using Serilog;

namespace OpcUaServer;

/// Envuelve la fuente real y rellena los tags que ella no conoce con una senal
/// sintetica. Existe solo para el benchmark de escala: permite publicar miles de
/// tags sin extender el RTU, que es trabajo de modelo fisico y no aporta a la medicion.
///
/// LIMITACION DELIBERADA: los valores sinteticos no tienen modelo fisico detras.
/// El benchmark mide la cadena OPC UA -> ingesta -> base, no la fidelidad del proceso.
public sealed class BenchTagSource : ITagValueSource
{
    private readonly ITagValueSource _inner;
    private readonly Dictionary<string, (double Low, double High, int Index)> _synthetic;
    private double _elapsed;

    /// <param name="inner">La fuente real (Modbus). Tiene prioridad siempre.</param>
    /// <param name="syntheticTags">Tags a rellenar, con su rango de ingenieria.</param>
    public BenchTagSource(ITagValueSource inner, IEnumerable<(string Name, double Low, double High)> syntheticTags)
    {
        _inner = inner;
        _synthetic = syntheticTags
            .Select((t, i) => (t.Name, t.Low, t.High, Index: i))
            .ToDictionary(t => t.Name, t => (t.Low, t.High, t.Index));

        Log.Warning("MODO BENCHMARK: {Count} tags con valores sinteticos, sin modelo fisico",
            _synthetic.Count);
    }

    public void Step(double dtSeconds)
    {
        _inner.Step(dtSeconds);
        _elapsed += dtSeconds;
    }

    public bool TryGetValue(string tagName, out double value)
    {
        // La fuente real manda: si el RTU conoce el tag, ese es el valor bueno.
        if (_inner.TryGetValue(tagName, out value)) return true;

        if (!_synthetic.TryGetValue(tagName, out var spec)) return false;

        // Senoidal desfasada por indice, para que no cambien todos a la vez y el
        // deadband filtre de forma pareja. Periodo de 60 s.
        var phase = spec.Index * 0.017;
        var normalized = 0.5 + 0.5 * Math.Sin(_elapsed * 2 * Math.PI / 60.0 + phase);

        value = spec.Low + normalized * (spec.High - spec.Low);
        return true;
    }
}