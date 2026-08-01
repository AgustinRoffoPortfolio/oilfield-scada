using Shared;

namespace OpcUaServer;

/// Fuente de datos respaldada por el simulador corriendo en este mismo proceso.
/// Es la unica clase del servidor que conoce el dominio: sabe que un pozo tiene
/// una bomba y que "THP" es la presion de boca. Cuando entre el driver Modbus,
/// se reemplaza esta clase entera y el resto del servidor no cambia.
public class SimulatorTagSource : ITagValueSource
{
    private readonly Oilfield _oilfield;
    private readonly Dictionary<string, Func<double>> _readers = new();

    public SimulatorTagSource(Oilfield oilfield)
    {
        _oilfield = oilfield;

        foreach (var well in oilfield.Wells)
        {
            Map($"{well.Name}/THP",         () => well.WellheadPressure.Value);
            Map($"{well.Name}/CHP",         () => well.CasingPressure.Value);
            Map($"{well.Name}/T_head",      () => well.HeadTemperature.Value);
            Map($"{well.Name}/Q_oil",       () => well.OilRate.Value);
            Map($"{well.Name}/Q_water",     () => well.WaterRate.Value);
            Map($"{well.Name}/Q_gas",       () => well.GasRate.Value);
            Map($"{well.Name}/ESP_current", () => well.EspCurrent.Value);
            Map($"{well.Name}/ESP_freq",    () => well.EspFrequency.Value);
            Map($"{well.Name}/ESP_vib",     () => well.EspVibration.Value);
            Map($"{well.Name}/Status",      () => (double)(int)well.Status);
        }

        Map("Separator/Sep_P",     () => oilfield.Separator.Pressure.Value);
        Map("Separator/Sep_level", () => oilfield.Separator.Level.Value);

        Map("Pipeline/Pipe_P_in",  () => oilfield.Pipeline.InletPressure.Value);
        Map("Pipeline/Pipe_P_out", () => oilfield.Pipeline.OutletPressure.Value);
        Map("Pipeline/Pipe_Q",     () => oilfield.Pipeline.TotalFlow.Value);
    }

    private void Map(string tagName, Func<double> reader) => _readers[tagName] = reader;

    public void Step(double dtSeconds) => _oilfield.Step(dtSeconds);

    public bool TryGetValue(string tagName, out double value)
    {
        if (_readers.TryGetValue(tagName, out var reader))
        {
            value = reader();
            return true;
        }

        value = 0;
        return false;
    }

    /// Nombres que esta fuente sabe entregar. Sirve para avisar al arrancar
    /// si la configuracion pide tags que nadie alimenta.
    public IEnumerable<string> KnownTags => _readers.Keys;
}