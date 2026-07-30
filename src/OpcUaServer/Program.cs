using System.Net;
using Microsoft.Extensions.Configuration;
using Opc.Ua;
using Opc.Ua.Configuration;
using OpcUaServer;
using Shared;

// Lee appsettings.json desde la carpeta de salida y lo mapea a OpcUaOptions.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var options = configuration.GetSection("OpcUa").Get<OpcUaOptions>()
    ?? throw new InvalidOperationException("Falta la sección 'OpcUa' en appsettings.json");

// Identidad de la aplicación ante la red OPC UA.
var application = new ApplicationInstance
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
    oilfield.Step(interval.TotalSeconds);
    server.NodeManager?.UpdateValues();
}, null, TimeSpan.Zero, interval);

Console.WriteLine($"Servidor OPC UA escuchando en {options.EndpointUrl}");
Console.WriteLine($"Ciclo de actualizacion: {options.UpdateIntervalMs} ms");
Console.WriteLine("Enter para detener.");
Console.ReadLine();

await application.StopAsync();
Console.WriteLine("Servidor detenido.");