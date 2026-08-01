namespace OpcUaServer;

/// De donde salen los valores que el servidor publica.
/// Hoy es el simulador en memoria; en el paso Modbus sera el driver de campo.
/// El servidor no sabe cual de las dos esta del otro lado.
public interface ITagValueSource
{
    /// Avanza la fuente de datos un ciclo.
    void Step(double dtSeconds);

    /// Valor actual de un tag por su nombre completo ("POZO-A/THP").
    /// Devuelve false si la fuente no lo conoce o no tiene dato valido.
    bool TryGetValue(string tagName, out double value);
}