namespace WebApp;

/// Ultimo valor conocido de un tag, con su metadata de catalogo.
public sealed record TagReading
{
    public string Name { get; init; } = "";
    public string Equipment { get; init; } = "";
    public string Variable { get; init; } = "";
    public string? Unit { get; init; }
    public double? EuMin { get; init; }
    public double? EuMax { get; init; }
    public double? WarnLow { get; init; }
    public double? WarnHigh { get; init; }
    public double? AlarmLow { get; init; }
    public double? AlarmHigh { get; init; }

    public double? Value { get; init; }
    public short? Quality { get; init; }
    public DateTime? Ts { get; init; }
}