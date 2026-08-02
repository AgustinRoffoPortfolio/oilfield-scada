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

// La PKI vive en el repo (ignorada por Git), no escondida en AppData: el trabajo
// real de OPC UA es mover certificados entre carpetas a mano.
var pkiRoot = Path.GetFullPath(options.PkiRoot);

// La API nueva recibe una coleccion porque un servidor puede tener varios
// certificados (RSA, ECC) y ofrecer el que el cliente soporte. Nosotros usamos uno.
var applicationCertificate = new CertificateIdentifier
{
    // La API nueva no asume el tipo: hay que declararlo porque admite RSA y ECC.
    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
    StoreType = CertificateStoreType.Directory,
    StorePath = Path.Combine(pkiRoot, "own"),
    SubjectName = $"CN={options.ApplicationName}, C=AR, O=Portfolio"
};

// Configuración armada en código, sin archivo XML.
await application.Build(
        applicationUri: $"urn:{Dns.GetHostName()}:OilfieldScada:Server",
        productUri: "https://github.com/AgustinRoffoPortfolio/oilfield-scada")
    .AsServer(new[] { options.EndpointUrl })
    .AddUnsecurePolicyNone()          // endpoint viejo, para comparar mientras desarrollamos
    .AddSignAndEncryptPolicies()      // endpoints firmados y cifrados (Basic256Sha256 y sucesores)
    .AddUserTokenPolicy(UserTokenType.Anonymous)
    .AddSecurityConfiguration(
        new CertificateIdentifierCollection { applicationCertificate },
        pkiRoot: pkiRoot)
    .SetAutoAcceptUntrustedCertificates(options.AutoAcceptUntrustedCertificates)
    .CreateAsync();

Log.Information("PKI en {PkiRoot} (auto-aceptar: {Auto})",
    pkiRoot, options.AutoAcceptUntrustedCertificates);

// Crea el certificado propio del servidor la primera vez que corre.
await application.CheckApplicationInstanceCertificatesAsync(silent: true);

// El address space sale de un archivo, no de catalogos compilados.
var addressSpacePath = AddressSpaceConfig.Resolve(options.AddressSpaceFile);
var addressSpace = AddressSpaceConfig.Load(addressSpacePath);

// El mapa del RTU: que registro corresponde a que tag.
var mapPath = AddressSpaceConfig.Resolve(options.ModbusMapFile);
var modbusMap = ModbusMap.Load(mapPath);

// Los dos archivos los mantiene un humano: el desfasaje se detecta al arrancar.
var (sinRegistro, sinTag) = modbusMap.CrossCheck(addressSpace);

if (sinRegistro.Count > 0)
{
    Log.Warning("{Count} tags configurados sin registro en el mapa: {Tags}",
        sinRegistro.Count, string.Join(", ", sinRegistro));
}

if (sinTag.Count > 0)
{
    Log.Warning("{Count} registros del mapa sin tag configurado: {Tags}",
        sinTag.Count, string.Join(", ", sinTag));
}

// La fuente de datos: un RTU real del otro lado de un socket.
var source = new ModbusTagSource(modbusMap, options.ModbusHost, options.ModbusPort,
    options.ModbusPollIntervalMs, options.ModbusReconnectDelayMs);

using var driverCts = new CancellationTokenSource();
var driverTask = source.RunAsync(driverCts.Token);

Log.Information("Mapa Modbus: {Path} (RTU en {Host}:{Port})",
    mapPath, options.ModbusHost, options.ModbusPort);

var server = new OilfieldServer(addressSpace, source, options.NamespaceUri);
await application.StartAsync(server);

Log.Information("Address space: {Path} ({Devices} equipos, {Tags} tags)",
    addressSpacePath, addressSpace.Devices.Count, server.NodeManager?.TagCount ?? 0);

// Cada ciclo: avanzar la física y publicar los valores nuevos.
var interval = TimeSpan.FromMilliseconds(options.UpdateIntervalMs);
using var timer = new Timer(_ =>
{
    try
    {
        source.Step(interval.TotalSeconds);
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

await driverCts.CancelAsync();
await driverTask;
source.Dispose();

await application.StopAsync();
Log.Information("Servidor detenido.");
await Log.CloseAndFlushAsync();