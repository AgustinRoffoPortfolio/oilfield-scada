using System.Net;
using Opc.Ua;
using Opc.Ua.Configuration;
using OpcUaServer;
using Shared;

// Identidad de la aplicación ante la red OPC UA.
var application = new ApplicationInstance
{
    ApplicationName = "OilfieldScadaServer",
    ApplicationType = ApplicationType.Server
};

// Configuración armada en código, sin archivo XML.
await application.Build(
        applicationUri: $"urn:{Dns.GetHostName()}:OilfieldScada:Server",
        productUri: "https://github.com/AgustinRoffoPortfolio/oilfield-scada")
    .AsServer(new[] { "opc.tcp://localhost:4840/OilfieldScada" })
    .AddUnsecurePolicyNone()
    .AddUserTokenPolicy(UserTokenType.Anonymous)
    .AddSecurityConfiguration(
        subjectName: "CN=OilfieldScadaServer, C=AR, O=Portfolio",
        pkiRoot: "%LocalApplicationData%/OPC Foundation/pki")
    .SetAutoAcceptUntrustedCertificates(true)
    .CreateAsync();

// Crea el certificado propio del servidor la primera vez que corre.
await application.CheckApplicationInstanceCertificatesAsync(silent: true);

// El yacimiento simulado: la misma clase que usa el proyecto Simulator.
var oilfield = new Oilfield();
var server = new OilfieldServer(oilfield);
await application.StartAsync(server);

// Cada segundo: avanzar la física y publicar los valores nuevos.
using var timer = new Timer(_ =>
{
    oilfield.Step(1.0);
    server.NodeManager?.UpdateValues();
}, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

Console.WriteLine("Servidor OPC UA escuchando en opc.tcp://localhost:4840/OilfieldScada");
Console.WriteLine("Enter para detener.");
Console.ReadLine();

await application.StopAsync();
Console.WriteLine("Servidor detenido.");