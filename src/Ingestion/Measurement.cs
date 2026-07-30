namespace Ingestion;

/// Una lectura lista para escribir: cuando, que tag, que valor y con que calidad.
/// Quality: 0 = Good, 1 = Uncertain, 2 = Bad.
public readonly record struct Measurement(
    DateTime Timestamp, short TagId, double Value, short Quality);