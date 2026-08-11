# Servidores

Documentación del módulo de servidores de ShoroCraft Launcher.

- **Tipos soportados**: Vanilla y Paper
- **Agregado en**: v1.3.0
- **Última corrección**: v1.3.1 (procesos Java huérfanos y padding de consola)

---

## Índice

- [Resumen](#resumen)
- [Ubicación y estructura](#ubicación-y-estructura)
- [Crear un servidor](#crear-un-servidor)
- [Configuración generada](#configuración-generada)
- [Flujo de inicio](#flujo-de-inicio)
- [Consola interactiva](#consola-interactiva)
- [Ciclo de vida y estados](#ciclo-de-vida-y-estados)
- [Procesos huérfanos (server.pid)](#procesos-huérfanos-serverpid)
- [Java](#java)
- [Referencia de API](#referencia-de-api)
- [Solución de problemas](#solución-de-problemas)

---

## Resumen

Desde la sección **Servidores** (barra lateral) puedes crear servidores Vanilla o Paper, iniciarlos y gestionarlos sin salir del launcher:

- Descarga automática del jar del servidor con barra de progreso.
- Generación de `eula.txt` y `server.properties`.
- Consola interactiva en tiempo real: enviar comandos, detener, despertar (`list`), copiar y limpiar.
- Detección de procesos Java huérfanos y parada automática de servidores al cerrar el launcher.

## Ubicación y estructura

Cada servidor vive en una carpeta propia:

```txt
%LocalAppData%\ShoroCraftLauncher\servers\{nombre}/
|-- server.jar            # jar del servidor (Vanilla o Paper)
|-- eula.txt              # eula=true (aceptada automáticamente)
|-- server.properties     # configuración del servidor
|-- server.pid            # PID del proceso Java (se crea al iniciar y se borra al salir)
|-- logs/                 # logs propios del servidor (latest.log)
|-- world/                # mundo por defecto (o el level-name configurado)
`-- ...                   # resto de archivos generados por Minecraft
```

El nombre de la carpeta se sanitiza (`SanitizeFolderName`): los caracteres inválidos se reemplazan por `_`.

## Crear un servidor

Desde la vista Servidores:

1. Escribe un **nombre**.
2. Elige el **tipo**: Vanilla o Paper.
3. Elige la **versión de Minecraft** (las listas se consultan en línea).
4. Define la **RAM máxima** en MB (mínimo por defecto: 1024 MB).
5. Pulsa **Crear**.

La creación es síncrona en local: crea la carpeta, escribe `eula.txt` y `server.properties` y registra el servidor en la base de datos (`MinecraftServers`).

## Configuración generada

### `eula.txt`

```txt
#EULA aceptada automáticamente por ShoroCraft Launcher
eula=true
```

### `server.properties`

```properties
server-port=25565
level-name=world
motd=A ShoroCraft server
online-mode=false
max-players=20
view-distance=10
pause-when-empty-seconds=0
```

Nota: al iniciar un servidor existente, el launcher se asegura de que `pause-when-empty-seconds=0` esté presente (para que la consola siempre responda).

## Flujo de inicio

`ServerService.StartAsync` hace lo siguiente:

1. **Limpieza de huérfanos**: si existe `server.pid` y el proceso sigue vivo (y es `java`/`javaw`), lo mata (todo el árbol de procesos) y espera a que termine.
2. Crea la carpeta si falta y regenera `eula.txt` si no existe.
3. Asegura `pause-when-empty-seconds=0` en `server.properties`.
4. **Descarga el jar** si `server.jar` no existe:
   - **Vanilla**: resuelve la URL del jar desde el manifiesto de Mojang.
   - **Paper**: usa la API pública `https://api.papermc.io/v2/projects/paper` y toma el último build de la versión.
5. **Resuelve Java**: usa `JavaPath` guardado; si no existe, busca el Java recomendado para la versión y, si no lo encuentra, lo descarga (con progreso).
6. Lanza el proceso con salida/entrada redirigida y el directorio de trabajo en la carpeta del servidor:

   ```txt
   java -Xms{MinRam}M -Xmx{MaxRam}M -jar server.jar nogui
   ```

7. Escribe el PID del proceso en `server.pid`.

## Consola interactiva

- **Comandos**: se escriben en el campo inferior y se envían por la entrada estándar del proceso (`SendCommandAsync`).
- **Despertar**: envía `list` (útil si el servidor entró en pausa por inactividad).
- **Copiar consola**: copia todas las líneas al portapapeles.
- **Limpiar**: vacía el historial mostrado.
- El historial está limitado a las últimas **2000 líneas** por servidor y se conserva en memoria mientras el launcher está abierto.

## Ciclo de vida y estados

### Estados (`ServerStatus`)

| Estado | Descripción |
| ------ | ----------- |
| `Stopped` | Servidor detenido |
| `Starting` | En proceso de arranque |
| `Running` | En ejecución |
| `Stopping` | Deteniéndose (tras enviar `stop`) |
| `Error` | Falló el arranque |

### Detención

`StopAsync` envía `stop` por la entrada estándar y espera hasta **15 segundos**; si no termina, mata el proceso (todo el árbol). Al salir del proceso se borra `server.pid` y se actualiza el estado a `Stopped`.

### Cierre del launcher

Al cerrar la aplicación, `App.OnExit` invoca `ServerService.StopAllAsync`, que detiene todos los servidores en ejecución de forma paralela. Esto evita dejar procesos Java vivos que bloqueen archivos en el siguiente arranque.

## Procesos huérfanos (server.pid)

Si el launcher se cierra de forma forzada (crash, tarea terminada) los procesos Java pueden quedar vivos y retener `logs\latest.log` y el lock del mundo (`session.lock`), lo que hacía fallar el siguiente arranque con:

```txt
java.nio.file.FileSystemException: El proceso no tiene acceso al archivo porque está siendo utilizado por otro proceso
java.io.IOException: ... DirectoryLock.create ... Failed to start the minecraft server
```

Desde v1.3.1:

- El launcher escribe el PID en `server.pid` al iniciar y lo borra al detener el servidor.
- Antes de iniciar, lee `server.pid`; si el proceso existe y es `java`/`javaw`, lo mata (con `Kill(entireProcessTree: true)`) y espera, liberando los locks.
- El PID se valida para no matar procesos que no sean de Java.

## Java

| Versión de Minecraft | Java requerido |
| -------------------- | -------------- |
| 1.0 a 1.16 | Java 8 |
| 1.17 a 1.20.4 | Java 17 |
| 1.20.5 o superior | Java 21 |

El launcher busca el Java recomendado para la versión elegida; si no está instalado, lo descarga automáticamente con barra de progreso.

## Referencia de API

### `IServerService`

```csharp
IReadOnlyList<MinecraftServer> Servers { get; }
event Action? ServersChanged;
event Action<string>? LogOutput;
event Action<double, string>? ProgressChanged;
event Action<ServerStatus>? StatusChanged;

Task LoadAsync();
Task<List<string>> GetAvailableVanillaVersionsAsync();
Task<List<string>> GetAvailablePaperVersionsAsync();
Task<MinecraftServer> CreateServerAsync(string name, ServerType type, string minecraftVersion, int maxRamMB, string? worldName = null);
Task DeleteServerAsync(MinecraftServer server);
Task<ServerLaunchResult> StartAsync(MinecraftServer server);
Task StopAsync(MinecraftServer server);
Task StopAllAsync();
Task SendCommandAsync(MinecraftServer server, string command);
bool IsRunning(MinecraftServer server);
IReadOnlyList<string> GetLogHistory(MinecraftServer server);
```

### `ServerLaunchResult`

```csharp
public class ServerLaunchResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### Enums

```csharp
public enum ServerType { Vanilla, Paper }

public enum ServerStatus { Stopped, Starting, Running, Stopping, Error }
```

### Implementación y repositorio

- Servicio: `ShoroCraftLauncher.Infrastructure/Services/ServerService.cs`
- Modelo: `ShoroCraftLauncher.Core/Models/MinecraftServer.cs`
- Interfaz: `ShoroCraftLauncher.Core/Interfaces/IServerService.cs`
- Repositorio: `ShoroCraftLauncher.Data/Repositories/ServerRepository.cs`
- Vista: `ShoroCraftLauncher.App/Views/ServersView.xaml`
- ViewModel: `ShoroCraftLauncher.App/ViewModels/ServersViewModel.cs`

## Solución de problemas

### El servidor no arranca por archivos bloqueados

Suele deberse a un proceso Java huérfano de una sesión anterior. El launcher lo mata automáticamente desde v1.3.1. Si el huérfano es anterior a esa versión (sin `server.pid`), termínalo manualmente:

```powershell
Get-Process java,javaw | Where-Object { $_.Path } | Stop-Process -Force
```

O desde el Administrador de tareas: termina los procesos `java.exe` / `javaw.exe` relacionados con el servidor.

### El servidor entra en pausa

Si `pause-when-empty-seconds` aparece con un valor distinto de 0, vuelve a iniciar el servidor desde el launcher (lo corrige automáticamente) o usa **Despertar** para enviar `list`.

### Logs

- Logs del servidor: `%LocalAppData%\ShoroCraftLauncher\servers\{nombre}\logs\latest.log`
- Logs del launcher: `%LocalAppData%\ShoroCraftLauncher\logs\launcher-*.log`

---

- Documentación general del launcher: [launcher.md](launcher.md)
