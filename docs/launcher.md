# ShoroCraft Launcher

Documentación del launcher de Minecraft para Windows.

- **Stack**: .NET 8, WPF, MVVM, EF Core + SQLite, CmlLib, Serilog
- **Plataforma**: Windows 10 / 11 x64
- **Última release**: `v1.4.0`

---

## Índice

- [Resumen](#resumen)
- [Características](#características)
- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Compilar desde código](#compilar-desde-código)
- [Arquitectura](#arquitectura)
- [Persistencia y rutas](#persistencia-y-rutas)
- [Descargas reanudables](#descargas-reanudables)
- [Autenticación](#autenticación)
- [Java](#java)
- [Pruebas](#pruebas)
- [Desarrollo](#desarrollo)
- [Proceso de release](#proceso-de-release)
- [Dependencias](#dependencias)
- [Licencia](#licencia)

---

## Resumen

ShoroCraft Launcher es un launcher de Minecraft orientado a modding que permite gestionar perfiles (Vanilla, Forge, Fabric, Quilt, OptiFine e Iris), mods (Modrinth y CurseForge), resource packs, shaders, mundos por perfil y servidores Vanilla/Paper, todo desde una interfaz WPF oscura.

## Características

| Módulo | Descripción |
| ------ | ----------- |
| Autenticación | Microsoft OAuth y modo offline con nombre de usuario configurable |
| Perfiles | Perfiles para Vanilla, Forge, Fabric, Quilt, OptiFine e Iris |
| Java | Detección y descarga automática de JRE 8, 17 y 21 |
| Mods | Búsqueda e instalación desde Modrinth y CurseForge |
| Resource Packs | Gestión de paquetes de texturas |
| Shaders | Gestión de shader packs compatible con Iris y OptiFine |
| Mundos | Extracción de `.zip`/`.mcworld`, escaneo y respaldo de mundos por perfil |
| Modpacks | Importación de modpacks Modrinth (`.mrpack`) |
| Noticias | Feed de noticias en el dashboard |
| Consola | Logs en tiempo real del proceso de Minecraft |
| Servidores | Creación y gestión de servidores Vanilla y Paper con consola interactiva (ver [`servers.md`](servers.md)) |
| Actualizaciones | Detección de actualizaciones de Minecraft y del propio launcher |
| Descargas reanudables | Reanudación con HTTP `Range`, reintentos y verificación SHA-1 en descargas grandes |
| Datos | Persistencia local con SQLite |

## Requisitos

- Windows 10 o Windows 11 x64
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Java 8, 17 o 21 (detectado o descargado automáticamente por el launcher)

## Instalación

- **Instalador**: descarga `ShoroCraftLauncher_Setup.exe` desde [GitHub Releases](https://github.com/shoropio/shorocraft-launcher/releases) e instálalo (Inno Setup).
- **Portable**: descarga `ShoroCraftLauncher_Publish.zip` y extrae su contenido en una carpeta.

Los datos de usuario se guardan en `%LocalAppData%\ShoroCraftLauncher`.

## Compilar desde código

```bash
git clone https://github.com/shoropio/shorocraft-launcher.git
cd shorocraft-launcher
dotnet restore
dotnet build -c Release
dotnet run --project src/ShoroCraftLauncher.App
```

También puedes abrir `ShoroCraftLauncher.sln` en Visual Studio 2022 v17.8 o superior.

## Arquitectura

El proyecto usa MVVM e inyección de dependencias, separado en capas:

```txt
shorocraft-launcher/
|-- src/
|   |-- ShoroCraftLauncher.App/             UI WPF, views, viewmodels y estilos
|   |-- ShoroCraftLauncher.Core/            modelos, enums e interfaces
|   |-- ShoroCraftLauncher.Data/            EF Core, SQLite y repositorios
|   |-- ShoroCraftLauncher.Infrastructure/  servicios de Minecraft, Java, auth, logs y descargas
|   |   `-- Downloading/                    ResumableDownloadService (HTTP Range + `.part`)
|   `-- ShoroCraftLauncher.Tests/           pruebas con xUnit
|-- assets/
|-- docs/
|-- installer/                              script de Inno Setup (setup.iss)
|-- dist/                                   instalador y zip generados
`-- README.md
```

### Capas

| Capa | Responsabilidad | Referencias a otras capas |
| ---- | --------------- | ------------------------- |
| **Core** | Modelos, enums e interfaces (sin dependencias) | — |
| **Data** | EF Core, SQLite, repositorios e inicialización de la BD | Core |
| **Infrastructure** | Implementaciones: Minecraft, Java, autenticación, servidores, logging | Core, Data |
| **App** | Vistas WPF, viewmodels, estilos, DI | Core, Data, Infrastructure |
| **Tests** | Pruebas unitarias/integración con xUnit y Moq | Core, Infrastructure |

El arranque se configura en `App.xaml.cs` (`CreateHostBuilder`), donde se registran todos los servicios como singletons y los viewmodels como transients.

## Persistencia y rutas

Toda la persistencia es local, bajo `%LocalAppData%\ShoroCraftLauncher`:

| Ruta | Contenido |
| ---- | --------- |
| `%LocalAppData%\ShoroCraftLauncher\data\launcher.db` | Base de datos SQLite (perfiles, mods, ajustes, servidores…) |
| `%LocalAppData%\ShoroCraftLauncher\logs\launcher-*.log` | Logs del launcher (Serilog, retención de 7 días) |
| `%LocalAppData%\ShoroCraftLauncher\servers\{nombre}` | Carpetas de servidores (ver [`servers.md`](servers.md)) |
| `%LocalAppData%\ShoroCraftLauncher\backups\{perfil}` | Backups de mundos, scripts y configs |

## Descargas reanudables

Las descargas grandes (jar de servidor, cliente y librerías de Minecraft, instalador de actualización, modpacks, mods y shaders) usan `ResumableDownloadService` (`src/ShoroCraftLauncher.Infrastructure/Downloading/`):

- El archivo se descarga a `<destino>.part`; si la conexión se interrumpe, el parcial se conserva y el siguiente intento continúa desde el byte donde quedó con una petición HTTP `Range` (respuesta `206 Partial Content`).
- Si el servidor no soporta `Range` (responde `200`), la descarga se reinicia; si responde `416` (rango inválido), el `.part` se descarta y se vuelve a empezar.
- Reintenta hasta 5 veces con espera progresiva; un timeout de inactividad de 60 s aborta descargas que se quedan sin datos y las retoma desde donde iban.
- Al completar, verifica el tamaño y, opcionalmente, el hash SHA-1 (modpacks) antes de mover el archivo a su destino.
- Se registra como singleton en `App.xaml.cs`; las librerías de Minecraft y los assets se guardan en rutas estables, por lo que sus `.part` sobreviven entre sesiones y se reanudan en el siguiente arranque.

## Autenticación

1. **Microsoft OAuth**: autenticación oficial con cuenta Microsoft (`CmlLib.Core.Auth.Microsoft`), con restauración silenciosa de la sesión al arrancar y cierre de sesión.
2. **Offline**: acceso local con un nombre de usuario configurable.

## Java

| Versión de Minecraft | Java requerido |
| -------------------- | -------------- |
| 1.0 a 1.16 | Java 8 |
| 1.17 a 1.20.4 | Java 17 |
| 1.20.5 o superior | Java 21 |

Si no se encuentra la versión adecuada, el launcher intenta descargarla a su carpeta interna.

## Pruebas

```bash
dotnet test ShoroCraftLauncher.sln
```

Los tests que descargan archivos reales, revisan instalaciones de Java o lanzan Minecraft están marcados como `Category=Integration` y se omiten por defecto.

Para ejecutarlos manualmente:

```powershell
$env:SHOROCRAFT_RUN_INTEGRATION_TESTS="1"
dotnet test ShoroCraftLauncher.sln --filter Category=Integration
```

## Desarrollo

### Ramas

- `master`: estable, solo recibe merges de releases.
- `development`: integración; el trabajo diario se commitea aquí.

### Agregar un servicio

1. Define la interfaz en `src/ShoroCraftLauncher.Core/Interfaces/`.
2. Implementa el servicio en `src/ShoroCraftLauncher.Infrastructure/Services/` (los de descargas, en `Downloading/`).
3. Registra la dependencia en `App.xaml.cs`.

### Convención de commits

El proyecto usa [Conventional Commits](https://www.conventionalcommits.org/):

```txt
feat:     Nueva funcionalidad
fix:      Corrección de bug
style:    Cambios de UI o estilos
refactor: Refactorización sin cambio de comportamiento
docs:     Documentación
chore:    Tareas de mantenimiento
test:     Pruebas
release:  Commit de publicación (vX.Y.Z)
```

## Proceso de release

1. Bump de versión en `src/ShoroCraftLauncher.App/ShoroCraftLauncher.App.csproj` (`<Version>`) y en `DbInitializer.cs` (`launcher_version`).
2. Actualiza `CHANGELOG.md` con una sección `## [x.y.z] - fecha`.
3. Commit `release: vX.Y.Z` en `development` y merge fast-forward a `master`.
4. Push de ambas ramas.
5. Publica (framework-dependent, multi-file):

   ```bash
   dotnet publish src/ShoroCraftLauncher.App/ShoroCraftLauncher.App.csproj -c Release -r win-x64 --self-contained false -o src/ShoroCraftLauncher.App/bin/Release/net8.0-windows/win-x64/publish
   ```

6. Compila el instalador (Inno Setup) con `installer/setup.iss`:

   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\setup.iss
   ```

   Genera `dist\ShoroCraftLauncher_Setup.exe`.
7. Crea el zip portable:

   ```powershell
   Compress-Archive -Path src\ShoroCraftLauncher.App\bin\Release\net8.0-windows\win-x64\publish\* -DestinationPath dist\ShoroCraftLauncher_Publish.zip
   ```

8. Publica la release en GitHub:

   ```bash
   gh release create vX.Y.Z --title "vX.Y.Z" --notes "..." dist/ShoroCraftLauncher_Setup.exe dist/ShoroCraftLauncher_Publish.zip
   ```

## Dependencias

| Paquete | Uso |
| ------- | --- |
| `CmlLib.Core` | Integración con Minecraft |
| `CmlLib.Core.Auth.Microsoft` | Autenticación Microsoft |
| `Microsoft.EntityFrameworkCore.Sqlite` | Persistencia local |
| `Serilog` + sinks | Logging |
| `Microsoft.Extensions.Hosting` / DI / Http | DI, ciclo de vida y HTTP |
| `Microsoft.Web.WebView2` | Vistas embebidas |
| `xUnit` / `Moq` | Pruebas |

## Licencia

Distribuido bajo licencia MIT. Consulta [`LICENSE`](../LICENSE).

---

- Documentación del módulo de servidores: [servers.md](servers.md)
