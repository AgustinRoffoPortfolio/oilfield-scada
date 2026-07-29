using System.Net;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

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

// StandardServer: el servidor base del stack. Todavía no expone tags nuestros.
var server = new StandardServer();
await application.StartAsync(server);

Console.WriteLine("Servidor OPC UA escuchando en opc.tcp://localhost:4840/OilfieldScada");
Console.WriteLine("Enter para detener.");
Console.ReadLine();

await application.StopAsync();

Console.WriteLine("Servidor detenido.");