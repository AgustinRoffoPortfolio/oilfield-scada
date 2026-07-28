using Shared;

var well = new Well("PAD-01/POZO-A", seed: 42);
var stepInterval = TimeSpan.FromSeconds(1);

Console.WriteLine($"Simulador — {well.Name} — Ctrl+C para salir");
Console.WriteLine("  Freq   Q_oil  Q_water     Q_gas      I    Vib     THP    CHP   T_head   WC%  Status");
Console.WriteLine("  (Hz)   (m3/d)  (m3/d)    (Nm3/d)    (A) (mm/s)  (bar)  (bar)    (C)");

while (true)
{
    well.Step(stepInterval.TotalSeconds);
    Console.WriteLine(
        $"  {well.EspFrequency,5:F2} {well.OilRate,7:F1} {well.WaterRate,7:F1} " +
        $"{well.GasRate,10:F0} {well.EspCurrent,6:F1} {well.EspVibration,6:F2} " +
        $"{well.WellheadPressure,6:F2} {well.CasingPressure,6:F2} {well.HeadTemperature,8:F1} " +
        $"{well.WaterCut * 100,5:F1}  {well.Status}");
    await Task.Delay(stepInterval);
}