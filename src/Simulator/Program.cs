using Shared;

var well = new Well("PAD-01/POZO-A", seed: 42);
var stepInterval = TimeSpan.FromSeconds(1);

Console.WriteLine($"Simulador — {well.Name} — Ctrl+C para salir");
Console.WriteLine("  Freq(Hz)  Q_oil(m3/d)     I(A)  THP(bar)");

while (true)
{
    well.Step(stepInterval.TotalSeconds);
    Console.WriteLine($"  {well.EspFrequency,8:F2}  {well.OilRate,10:F1}  {well.EspCurrent,7:F1}  {well.WellheadPressure,8:F2}");
    await Task.Delay(stepInterval);
}