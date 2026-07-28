# Arquitectura

## Cadena de datos

Simulador → Servidor OPC UA → Ingesta → TimescaleDB
(modelo (expone tags) (cliente (historial)
físico) OPC UA) ↓
Motor de alarmas
↓
WebApp (ASP.NET Core)
↓ SSE
Dashboard (HTML/CSS/JS)


## Proyectos

| Proyecto | Tipo | Rol |
|---|---|---|
| `Shared` | Biblioteca | Modelos y configuración comunes |
| `Simulator` | Consola | Modelo físico de pozos, bombas, separador y ducto |
| `OpcUaServer` | Consola | Publica los tags del simulador vía OPC UA |
| `Ingestion` | Consola | Cliente OPC UA que persiste en TimescaleDB |
| `Alarms` | Biblioteca | Evaluación de umbrales con histéresis |
| `WebApp` | ASP.NET Core | API REST + SSE + dashboard estático |
| `Simulator.Tests` | xUnit | Tests del modelo físico y de las alarmas |

## Decisiones de diseño

- **Frontend sin dependencias.** Sin frameworks ni librerías: gráficos dibujados a mano en Canvas 2D, datos en vivo por Server-Sent Events (API nativa del navegador, con reconexión automática). No hay build step.
- **Procesos separados.** Cada etapa es un ejecutable independiente. Refleja cómo se despliega un SCADA real y permite frenar una parte sin voltear el resto.
- **Solo la base en Docker.** Las apps de .NET corren con `dotnet run`. Containerizar todo queda para una fase posterior.
- **Formato de solución `.slnx`.** Formato nuevo de .NET 10, en XML legible en vez de GUIDs.