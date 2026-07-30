using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using OpcUaServer;
using Serilog;
using Shared;

// Lee appsettings.json desde la carpeta de salida y lo mapea a OpcUaOptions.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var options = configuration.GetSection("OpcUa").Get<OpcUaOptions>()
    ?? throw new InvalidOperationException("Falta la seccion 'OpcUa' en appsettings.json");

// Logger de toda la aplicación.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // El stack es muy verboso. Sus mensajes salen firmados con el nombre de
    // nuestra subclase, porque crea el logger con el tipo en runtime.
    .MinimumLevel.Override("Opc.Ua", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("OpcUaServer.OilfieldServer", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

// El stack OPC UA no tiene logger propio: usa el que le pasemos por acá.
var telemetry = DefaultTelemetry.Create(builder => builder.AddSerilog(Log.Logger));

// Identidad de la aplicación ante la red OPC UA.
var application = new ApplicationInstance(telemetry)
{
    ApplicationName = options.ApplicationName,
    ApplicationType = ApplicationType.Server
};

// Configuración armada en código, sin archivo XML.
await application.Build(
        applicationUri: $"urn:{Dns.GetHostName()}:OilfieldScada:Server",
        productUri: "https://github.com/AgustinRoffoPortfolio/oilfield-scada")
    .AsServer(new[] { options.EndpointUrl })
    .AddUnsecurePolicyNone()
    .AddUserTokenPolicy(UserTokenType.Anonymous)
    .AddSecurityConfiguration(
        subjectName: $"CN={options.ApplicationName}, C=AR, O=Portfolio",
        pkiRoot: "%LocalApplicationData%/OPC Foundation/pki")
    .SetAutoAcceptUntrustedCertificates(true)
    .CreateAsync();

// Crea el certificado propio del servidor la primera vez que corre.
await application.CheckApplicationInstanceCertificatesAsync(silent: true);

// El yacimiento simulado: la misma clase que usa el proyecto Simulator.
var oilfield = new Oilfield();
var server = new OilfieldServer(oilfield, options.NamespaceUri);
await application.StartAsync(server);

// Cada ciclo: avanzar la física y publicar los valores nuevos.
var interval = TimeSpan.FromMilliseconds(options.UpdateIntervalMs);
using var timer = new Timer(_ =>
{
    try
    {
        oilfield.Step(interval.TotalSeconds);
        server.NodeManager?.UpdateValues();
    }
    catch (Exception ex)
    {
        // Una excepción sin atrapar dentro de un callback de Timer
        // termina el proceso entero.
        Log.Error(ex, "Fallo el ciclo de actualizacion");
    }
}, null, TimeSpan.Zero, interval);

Log.Information("Servidor OPC UA escuchando en {Endpoint}", options.EndpointUrl);
Log.Information("Ciclo de actualizacion: {IntervalMs} ms", options.UpdateIntervalMs);
Log.Information("Enter para detener.");
Console.ReadLine();

await application.StopAsync();
Log.Information("Servidor detenido.");
await Log.CloseAndFlushAsync();