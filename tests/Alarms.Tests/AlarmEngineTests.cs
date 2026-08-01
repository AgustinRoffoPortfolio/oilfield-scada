using Alarms;

namespace Alarms.Tests;

public class AlarmEngineTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    // Tag de referencia: THP, rango 0-50, warn alto 40, alarma alta 45.
    // Con histeresis 2% el margen es 1.0 bar.
    private static TagLimits Thp() => new(
        TagId: 1, Name: "POZO-A/THP", Equipment: "POZO-A", Variable: "THP", Unit: "bar",
        EuMin: 0, EuMax: 50,
        WarnLow: 15, WarnHigh: 40, AlarmLow: 10, AlarmHigh: 45);

    private static AlarmEngine Engine() =>
        new(new AlarmOptions { HysteresisPercent = 2.0, StaleDataSeconds = 30 });

    /// Arma el resultado de una evaluacion con un solo tag y un solo valor.
    private static EvaluationResult Eval(
        TagLimits tag, double value, OpenAlarm? open = null,
        short quality = 0, int ageSeconds = 0)
    {
        var latest = new Dictionary<short, LatestValue>
        {
            [tag.TagId] = new(tag.TagId, Now.AddSeconds(-ageSeconds), value, quality)
        };
        var openByTag = new Dictionary<short, OpenAlarm>();
        if (open is not null) openByTag[open.TagId] = open;

        return Engine().Evaluate([tag], latest, openByTag, Now);
    }

    private static OpenAlarm Open(string severity, string direction, double limit) =>
        new(AlarmId: 100, TagId: 1, severity, direction, limit, Now.AddMinutes(-5), null);

    [Fact]
    public void ValorNormal_NoDisparaNada()
    {
        var r = Eval(Thp(), 30);
        Assert.Empty(r.ToRaise);
        Assert.Empty(r.ToClear);
    }

    [Fact]
    public void CruzarWarnAlto_DisparaWarn()
    {
        var r = Eval(Thp(), 41);
        var raise = Assert.Single(r.ToRaise);
        Assert.Equal("warn", raise.Severity);
        Assert.Equal("high", raise.Direction);
        Assert.Equal(40, raise.LimitValue);
    }

    [Fact]
    public void JustoEnElLimite_Dispara()
    {
        var r = Eval(Thp(), 40);
        Assert.Single(r.ToRaise);
    }

    [Fact]
    public void CruzarAlarmAlto_DisparaAlarmYNoWarn()
    {
        var r = Eval(Thp(), 46);
        var raise = Assert.Single(r.ToRaise);
        Assert.Equal("alarm", raise.Severity);
    }

    [Fact]
    public void ConWarnAbierta_ValorDentroDelMargen_NoNormaliza()
    {
        // Warn alto 40, margen 1.0: a 39.5 sigue abierta.
        var r = Eval(Thp(), 39.5, Open("warn", "high", 40));
        Assert.Empty(r.ToRaise);
        Assert.Empty(r.ToClear);
    }

    [Fact]
    public void ConWarnAbierta_ValorPasadoElMargen_Normaliza()
    {
        // A 38.5 ya bajo de 40 - 1.0: se cierra.
        var r = Eval(Thp(), 38.5, Open("warn", "high", 40));
        Assert.Empty(r.ToRaise);
        var clear = Assert.Single(r.ToClear);
        Assert.Equal(100, clear.Alarm.AlarmId);
        Assert.Equal(38.5, clear.Value);
    }

    [Fact]
    public void ConWarnAbierta_Escala_CierraWarnYAbreAlarm()
    {
        var r = Eval(Thp(), 46, Open("warn", "high", 40));
        Assert.Equal("alarm", Assert.Single(r.ToRaise).Severity);
        Assert.Equal("warn", Assert.Single(r.ToClear).Alarm.Severity);
    }

    [Fact]
    public void ConAlarmAbierta_Desescala_CierraAlarmYAbreWarn()
    {
        // A 42 ya bajo de 45 - 1.0, pero sigue sobre warn alto.
        var r = Eval(Thp(), 42, Open("alarm", "high", 45));
        Assert.Equal("warn", Assert.Single(r.ToRaise).Severity);
        Assert.Equal("alarm", Assert.Single(r.ToClear).Alarm.Severity);
    }

    [Fact]
    public void CruzarWarnBajo_DisparaLow()
    {
        var r = Eval(Thp(), 14);
        var raise = Assert.Single(r.ToRaise);
        Assert.Equal("warn", raise.Severity);
        Assert.Equal("low", raise.Direction);
    }

    [Fact]
    public void DatoViejo_NoSeEvalua()
    {
        var r = Eval(Thp(), 46, ageSeconds: 120);
        Assert.Empty(r.ToRaise);
    }

    [Fact]
    public void DatoViejo_NoNormalizaUnaAlarmaAbierta()
    {
        // Sin dato actual no se puede afirmar que volvio a normal: queda abierta.
        var r = Eval(Thp(), 20, Open("alarm", "high", 45), ageSeconds: 120);
        Assert.Empty(r.ToClear);
    }

    [Fact]
    public void CalidadMala_NoSeEvalua()
    {
        var r = Eval(Thp(), 46, quality: 2);
        Assert.Empty(r.ToRaise);
    }

    [Fact]
    public void TagSinLimites_NoDisparaNunca()
    {
        var status = new TagLimits(2, "POZO-A/Status", "POZO-A", "Status", null,
            null, null, null, null, null, null);
        var latest = new Dictionary<short, LatestValue> { [2] = new(2, Now, 999, 0) };
        var r = Engine().Evaluate([status], latest, new Dictionary<short, OpenAlarm>(), Now);
        Assert.Empty(r.ToRaise);
    }
}