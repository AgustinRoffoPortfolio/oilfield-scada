using Shared;

namespace Simulator;

/// Traduce nombre de tag a valor del modelo fisico.
/// Es el unico lugar del RTU que conoce el dominio: de aca en adelante,
/// hacia el cable, todo son numeros en direcciones.
public class FieldValues
{
    private readonly Dictionary<string, Func<double>> _readers = new();

    public FieldValues(Oilfield oilfield)
    {
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

    public IEnumerable<string> KnownTags => _readers.Keys;
}