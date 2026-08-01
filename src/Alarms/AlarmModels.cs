namespace Alarms;

/// Una fila del catalogo de tags, con sus umbrales.
/// Los limites pueden ser null: significa "sin limite de ese lado".
public sealed record TagLimits(
    short TagId,
    string Name,
    string Equipment,
    string Variable,
    string? Unit,
    double? EuMin,
    double? EuMax,
    double? WarnLow,
    double? WarnHigh,
    double? AlarmLow,
    double? AlarmHigh);

/// Ultimo valor conocido de un tag.
public sealed record LatestValue(
    short TagId,
    DateTime Ts,
    double Value,
    short Quality);

/// Una alarma abierta en la base, tal como se guardo al dispararse.
public sealed record OpenAlarm(
    long AlarmId,
    short TagId,
    string Severity,
    string Direction,
    double LimitValue,
    DateTime RaisedAt,
    DateTime? AckedAt);