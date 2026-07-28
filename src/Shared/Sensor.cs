namespace Shared;

/// <summary>
/// Un sensor de campo: toma el valor físico real y devuelve el valor que
/// reporta el instrumento, con su ruido de medición y sus fallas.
/// </summary>
public class Sensor
{
    private readonly Random random;
    private readonly double noiseAmplitude;
    private double driftPerSecond;

    public string Name { get; }
    public string Unit { get; }

    /// <summary>Valor que reporta el instrumento. Es lo único que ve el SCADA.</summary>
    public double Value { get; private set; }

    /// <summary>Valor físico real. Solo lo conoce el simulador; sirve para comparar.</summary>
    public double TrueValue { get; private set; }

    /// <summary>Si está congelado, el transmisor repite el último valor leído.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>Error acumulado por descalibración, en unidades de ingeniería.</summary>
    public double Drift { get; private set; }

    public Sensor(string name, string unit, double noiseAmplitude, int seed)
    {
        Name = name;
        Unit = unit;
        this.noiseAmplitude = noiseAmplitude;
        random = new Random(seed);
    }

    /// <summary>Actualiza la medición a partir del valor físico real.</summary>
    public void Update(double trueValue, double dt)
    {
        TrueValue = trueValue;

        // La descalibración avanza aunque el sensor esté congelado:
        // es un problema del instrumento, no de la lectura.
        Drift += driftPerSecond * dt;

        // Un transmisor colgado no vuelve a leer: sigue publicando lo último,
        // sin ruido. Esa quietud perfecta es la única pista de que falló.
        if (IsFrozen) return;

        Value = trueValue + Drift + Noise();
    }

    /// <summary>Congela el sensor en su último valor.</summary>
    public void Freeze() => IsFrozen = true;

    /// <summary>Devuelve el sensor a su funcionamiento normal.</summary>
    public void Unfreeze() => IsFrozen = false;

    /// <summary>Arranca una descalibración progresiva del instrumento.</summary>
    /// <param name="unitsPerMinute">Cuánto se aparta de la realidad por minuto.</param>
    public void StartDrift(double unitsPerMinute) => driftPerSecond = unitsPerMinute / 60.0;

    /// <summary>Recalibra el instrumento y detiene la deriva.</summary>
    public void Calibrate()
    {
        driftPerSecond = 0;
        Drift = 0;
    }

    private double Noise() => (random.NextDouble() - 0.5) * 2 * noiseAmplitude;
}