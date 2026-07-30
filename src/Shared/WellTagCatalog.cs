namespace Shared;

/// Descripción de un tag: nombre, unidad y rango de escala del instrumento.
public record TagDefinition(string Name, string Unit, double Low, double High, string Description);

/// Los tags de todo pozo, definidos UNA sola vez.
/// Los usa tanto la declaración del tipo como cada instancia.
public static class WellTagCatalog
{
    public static readonly TagDefinition[] Analog =
    [
        new("THP",         "bar",   0,     60, "Presion en boca de pozo (tubing)"),
        new("CHP",         "bar",   0,     40, "Presion del casing"),
        new("T_head",      "degC",  0,    120, "Temperatura en boca de pozo"),
        new("Q_oil",       "m3/d",  0,    150, "Caudal de petroleo"),
        new("Q_water",     "m3/d",  0,    250, "Caudal de agua"),
        new("Q_gas",       "Nm3/d", 0,  25000, "Caudal de gas"),
        new("ESP_current", "A",     0,    100, "Corriente del motor de la bomba"),
        new("ESP_freq",    "Hz",    0,     70, "Frecuencia del variador"),
        new("ESP_vib",     "mm/s",  0,     12, "Vibracion de la bomba")
    ];
}