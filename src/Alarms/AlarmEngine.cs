namespace Alarms;

/// Que hacer con una alarma tras evaluar un valor.
public sealed record RaiseAction(TagLimits Tag, string Severity, string Direction,
                                 double LimitValue, double Value, DateTime Ts);

public sealed record ClearAction(OpenAlarm Alarm, double Value, DateTime Ts);

public sealed record EvaluationResult(
    IReadOnlyList<RaiseAction> ToRaise,
    IReadOnlyList<ClearAction> ToClear);

/// Decide que alarmas disparar y cuales normalizar. No toca la base: recibe el estado
/// y devuelve las acciones. Asi se puede testear sin infraestructura.
public sealed class AlarmEngine(AlarmOptions options)
{
    private readonly AlarmOptions _opt = options;

    public EvaluationResult Evaluate(
        IReadOnlyList<TagLimits> tags,
        IReadOnlyDictionary<short, LatestValue> latest,
        IReadOnlyDictionary<short, OpenAlarm> openByTag,
        DateTime now)
    {
        var toRaise = new List<RaiseAction>();
        var toClear = new List<ClearAction>();

        foreach (var tag in tags)
        {
            openByTag.TryGetValue(tag.TagId, out var open);

            // Sin dato, con dato viejo o con calidad mala no se evalua: no hay contra
            // que comparar. Las alarmas ya abiertas se dejan como estan, porque no se
            // puede afirmar que el valor volvio a normal.
            if (!latest.TryGetValue(tag.TagId, out var value)) continue;
            if (value.Quality != 0) continue;
            if ((now - value.Ts).TotalSeconds > _opt.StaleDataSeconds) continue;

            var target = Classify(tag, value.Value, open);

            if (target is null)
            {
                if (open is not null) toClear.Add(new ClearAction(open, value.Value, value.Ts));
                continue;
            }

            // Ya esta abierta la misma condicion: nada que hacer.
            if (open is not null &&
                open.Severity == target.Severity &&
                open.Direction == target.Direction) continue;

            // Cambio de severidad o de lado: se cierra la anterior y se abre la nueva.
            if (open is not null) toClear.Add(new ClearAction(open, value.Value, value.Ts));

            toRaise.Add(new RaiseAction(tag, target.Severity, target.Direction,
                                        target.Limit, value.Value, value.Ts));
        }

        return new EvaluationResult(toRaise, toClear);
    }

    private sealed record Target(string Severity, string Direction, double Limit);

    /// En que condicion cae el valor. Se evalua de mayor a menor severidad: solo existe
    /// la alarma mas grave, no una de warn y otra de alarm sobre el mismo tag.
    private Target? Classify(TagLimits tag, double value, OpenAlarm? open)
    {
        var margin = Margin(tag);

        if (Crossed(tag.AlarmHigh, "alarm", "high") is { } ah) return ah;
        if (Crossed(tag.AlarmLow, "alarm", "low") is { } al) return al;
        if (Crossed(tag.WarnHigh, "warn", "high") is { } wh) return wh;
        if (Crossed(tag.WarnLow, "warn", "low") is { } wl) return wl;
        return null;

        // El umbral se afloja por el margen solo si esa condicion ya esta abierta:
        // se dispara en el limite exacto, se normaliza un poco mas adentro.
        Target? Crossed(double? limit, string severity, string direction)
        {
            if (limit is null) return null;

            var isOpen = open is not null &&
                         open.Severity == severity &&
                         open.Direction == direction;
            var effective = direction == "high"
                ? limit.Value - (isOpen ? margin : 0)
                : limit.Value + (isOpen ? margin : 0);

            var crossed = direction == "high" ? value >= effective : value <= effective;
            return crossed ? new Target(severity, direction, limit.Value) : null;
        }
    }

    /// Margen de histeresis en unidades de ingenieria del tag.
    /// Sin rango declarado no hay margen posible: se usa cero.
    private double Margin(TagLimits tag)
    {
        if (tag.EuMin is null || tag.EuMax is null) return 0;
        var span = tag.EuMax.Value - tag.EuMin.Value;
        return span <= 0 ? 0 : span * _opt.HysteresisPercent / 100.0;
    }
}