namespace Ingestion;

/// Datos de conexion a TimescaleDB.
/// La contrasena NO sale del JSON: viene de la variable de entorno Database__Password.
public sealed class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "oilfield";
    public string Username { get; set; } = "scada";
    public string Password { get; set; } = "";

    /// Cadena de conexion en el formato que espera Npgsql.
    public string ConnectionString =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}