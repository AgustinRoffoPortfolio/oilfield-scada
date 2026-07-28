using Shared;

var well = new Well("PAD-01/POZO-A", seed: 42);
var stepInterval = TimeSpan.FromSeconds(1);
int elapsed = 0;

Console.WriteLine($"Simulador — {well.Name} — Ctrl+C para salir");
Console.WriteLine("  Freq   Q_oil  Q_water     Q_gas      I     Vib     THP    CHP   T_head   WC%  Status");
Console.WriteLine("  (Hz)   (m3/d)  (m3/d)    (Nm3/d)    (A)  (mm/s)  (bar)  (bar)    (C)");

while (true)
{
    well.Step(stepInterval.TotalSeconds);

    if (elapsed == 5)
    {
        well.StartPumpDegradation(minutesToFailure: 2.0);
        Console.WriteLine("  >>> FALLA INYECTADA: degradación de bomba <<<");
    }

    if (elapsed == 20)
    {
        well.EspVibration.Freeze();
        Console.WriteLine("  >>> FALLA INYECTADA: sensor de vibración congelado <<<");
    }

    string frozenMark = well.EspVibration.IsFrozen ? "*" : " ";

    Console.WriteLine(
        $"  {well.EspFrequency.Value,5:F2} {well.OilRate.Value,7:F1} {well.WaterRate.Value,7:F1} " +
        $"{well.GasRate.Value,10:F0} {well.EspCurrent.Value,6:F1} {well.EspVibration.Value,6:F2}{frozenMark} " +
        $"{well.WellheadPressure.Value,6:F2} {well.CasingPressure.Value,6:F2} {well.HeadTemperature.Value,8:F1} " +
        $"{well.WaterCut * 100,5:F1}  {well.Status}  wear={well.PumpWear * 100,5:F1}%");

    elapsed++;
    await Task.Delay(stepInterval);
}