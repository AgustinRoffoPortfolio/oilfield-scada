# Seguridad OPC UA

## Qué problema resuelve

Sin seguridad, un servidor OPC UA acepta a cualquiera que sepa la dirección y manda
los valores en texto plano. En una red industrial eso significa que quien pinche el
cable lee la producción del yacimiento, y que cualquier máquina de la red puede
abrir una sesión y escribir en los nodos.

OPC UA lo resuelve con certificados digitales. Cada aplicación —el servidor y cada
cliente— tiene el suyo, y cumple dos funciones a la vez: identificar quién es y
cifrar el canal.

## Confianza mutua y manual

Lo distintivo de OPC UA frente a HTTPS es que no hay autoridad certificante. Los
certificados son autofirmados: el `Subject` y el `Issuer` son el mismo. Nadie los
respalda, así que la confianza se otorga a mano, y **de los dos lados**:

- El servidor tiene que confiar en el cliente.
- El cliente tiene que confiar en el servidor.

Confiar en alguien es literalmente mover su archivo `.der` de la carpeta `rejected`
a la carpeta `trusted`. No hay más que eso.

Este es el motivo por el cual un cliente OPC UA nuevo nunca conecta a la primera en
una planta real, y es la causa número uno de llamados al integrador.

## Organización de la PKI en este repo

En producción cada aplicación corre en su propia máquina y tiene su propia PKI. Acá
conviven en el repo, separadas por aplicación:

pki/
├── server/ # PKI del servidor OPC UA
│ ├── own/ # su certificado y su clave privada
│ ├── trusted/ # clientes autorizados a conectarse
│ └── rejected/ # clientes que intentaron y fueron rechazados
└── ingestion/ # PKI del cliente de ingesta, misma estructura


La carpeta `pki/` está en `.gitignore`: contiene claves privadas y nunca va al repo.
Se regenera sola en el primer arranque de cada aplicación.

La ruta se configura en el `appsettings.json` de cada app (`OpcUa.PkiRoot`), igual
que el resto de la configuración.

## Dar de alta un cliente nuevo

Con el servidor corriendo:

1. El cliente intenta conectar y falla. Su certificado queda en
   `pki/server/rejected/certs`.
2. Se lo mueve a `pki/server/trusted/certs`.
3. El cliente intenta de nuevo y ahora falla del otro lado, porque él no conoce al
   servidor. El certificado del servidor queda en la carpeta `rejected` del cliente.
4. Se lo mueve a la carpeta `trusted` del cliente.
5. Tercer intento: conecta.

No hace falta reiniciar el servidor: relee el almacén en cada intento.

```powershell
Move-Item pki\server\rejected\certs\*.der pki\server\trusted\certs\
Move-Item pki\ingestion\rejected\certs\*.der pki\ingestion\trusted\certs\
```

Con un cliente gráfico como UaExpert el paso 4 se resuelve con el botón
`Trust Server Certificate` del diálogo de validación, que hace lo mismo contra su
propio almacén.

## Interpretar los errores

La diferencia entre `request` y `response` en el mensaje dice de qué lado está el
problema. Es lo más útil para diagnosticar:

| Mensaje | Quién rechazó | Qué falta |
|---|---|---|
| `BadCertificateUntrusted` sobre `OpenSecureChannel request` | el servidor | mover el certificado del cliente a `pki/server/trusted/certs` |
| `BadCertificateUntrusted` sobre `OpenSecureChannel response` | el cliente | mover el certificado del servidor a la carpeta `trusted` del cliente |
| `BadSecurityChecksFailed` con `Error received from remote host` | el servidor | igual que el primero: el prefijo indica que el error vino del otro extremo |
| `The receiver's certificate thumbprint is not valid` | el cliente | tiene guardado un certificado viejo; el del servidor se regeneró |
| `SocketException 10061` | nadie | no es un problema de certificados: el servidor no está corriendo |

El último es importante: si la conexión TCP no se establece, el error no tiene nada
que ver con la seguridad y no hay que tocar certificados.

## Configuración

Endpoints que ofrece el servidor:

| Política | Modo | Uso |
|---|---|---|
| `None` | `None` | texto plano; se mantiene para comparar durante el desarrollo |
| `Basic256Sha256` | Sign & Encrypt | la más común en equipos industriales |
| `Aes128_Sha256_RsaOaep` | Sign & Encrypt | recomendada por la OPC Foundation |
| `Aes256_Sha256_RsaPss` | Sign & Encrypt | la más fuerte de las tres |

El cliente de ingesta no elige una política fija: `SelectEndpointAsync` con
`useSecurity: true` pide el endpoint más fuerte que el servidor ofrezca, y hoy
resuelve en `Aes256_Sha256_RsaPss`. Un cliente gráfico como UaExpert usa la que el
operador seleccionó al agregar el servidor.

Opciones relevantes de `appsettings.json`:

- `OpcUa.PkiRoot` — carpeta de la PKI de esa aplicación.
- `OpcUa.UseSecurity` (ingesta) — `false` vuelve al endpoint `None`.
- `OpcUa.AutoAcceptUntrustedCertificates` — en `true` acepta cualquier certificado
  sin intervención. Está en `false` en las dos aplicaciones: es lo correcto en
  producción y es lo que hace que el mecanismo se pueda demostrar.

## Qué falta para producción

- Los certificados son autofirmados. Una instalación real usa una autoridad
  certificante propia (o un Global Discovery Server) que los emite y los revoca.
- No hay listas de revocación: un certificado comprometido se saca borrando el
  archivo.
- La autenticación de usuario es anónima. El siguiente paso sería usuario y
  contraseña o certificado de usuario, que es distinto del certificado de aplicación.