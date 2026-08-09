# ShoroCraft Launcher

Launcher de Minecraft para Windows, construido con .NET 8, WPF, MVVM, SQLite y CmlLib.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12.0-68217A?logo=csharp&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?logo=windows&logoColor=white)
![MVVM](https://img.shields.io/badge/MVVM-Arquitectura-6D28D9)
![SQLite](https://img.shields.io/badge/SQLite-EF%20Core-0F9D58?logo=sqlite&logoColor=white)
![CmlLib](https://img.shields.io/badge/CmlLib-Minecraft-4E8C1E)
![Serilog](https://img.shields.io/badge/Serilog-Logging-9C4A2B?logo=serilog&logoColor=white)
![xUnit](https://img.shields.io/badge/xUnit-Tests-4E7C9F?logo=xunit&logoColor=white)
![Inno Setup](https://img.shields.io/badge/Inno%20Setup-Instalador-0080AA)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightgrey?logo=windows&logoColor=white)
![Release](https://img.shields.io/github/v/release/shoropio/shorocraft-launcher?label=release)
![License](https://img.shields.io/github/license/shoropio/shorocraft-launcher)

## Caracteristicas

| Modulo | Descripcion |
| ------ | ----------- |
| Autenticacion | Microsoft OAuth y modo offline con nombre de usuario configurable |
| Perfiles | Perfiles para Vanilla, Forge, Fabric, Quilt, OptiFine e Iris |
| Java | Deteccion y descarga automatica de JRE 8, 17 y 21 |
| Mods | Busqueda e instalacion desde Modrinth API |
| Resource Packs | Gestion de paquetes de texturas |
| Shaders | Gestion de shader packs compatible con Iris y OptiFine |
| Noticias | Feed de noticias en el dashboard |
| Consola | Logs en tiempo real del proceso de Minecraft |
| Datos | Persistencia local con SQLite |

## Requisitos

## Export / Import y Backups

El launcher soporta exportar e importar perfiles, y gestionar copias de seguridad:

- Exportar: desde la vista de Perfiles, usa el botón "Exportar" para guardar un paquete `.zip` del perfil.
- Importar: usa "Importar" para restaurar o añadir un perfil desde un paquete `.zip`.
- Copias de seguridad: crea backups de `Worlds`, `Scripts` o `Configs` en `%LocalAppData%\ShoroCraftLauncher\backups\{perfil}` y restaura/elimina desde la misma vista.

Estas operaciones también están disponibles programáticamente vía `IProfileService`.


- Windows 10 o Windows 11 x64
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Java 8, 17 o 21, detectado o descargado automaticamente por el launcher

## Compilar

```bash
git clone https://github.com/shoropio/shorocraft-launcher.git
cd shorocraft-launcher
dotnet restore
dotnet build -c Release
dotnet run --project src/ShoroCraftLauncher.App
```

Tambien puedes abrir `ShoroCraftLauncher.sln` en Visual Studio 2022 v17.8 o superior.

## Arquitectura

El proyecto usa MVVM e inyeccion de dependencias. Las capas principales son:

```txt
shorocraft-launcher/
|-- src/
|   |-- ShoroCraftLauncher.App/             UI WPF, views, viewmodels y estilos
|   |-- ShoroCraftLauncher.Core/            modelos, enums e interfaces
|   |-- ShoroCraftLauncher.Data/            EF Core, SQLite y repositorios
|   |-- ShoroCraftLauncher.Infrastructure/  servicios de Minecraft, Java, auth y logs
|   `-- ShoroCraftLauncher.Tests/           pruebas con xUnit
|-- assets/
|-- docs/
|-- installer/
`-- README.md
```

## Lanzamiento

La versión más reciente está disponible en GitHub Releases. La release `v1.1.1` incluye el instalador Windows (`ShoroCraftLauncher_Setup.exe`) y el paquete publicado (`ShoroCraftLauncher_Publish.zip`).

## Dependencias

| Paquete | Uso |
| ------- | --- |
| `CmlLib.Core` | Integracion con Minecraft |
| `CmlLib.Core.Auth.Microsoft` | Autenticacion Microsoft |
| `Microsoft.EntityFrameworkCore.Sqlite` | Persistencia local |
| `Serilog` | Logging |
| `Microsoft.Extensions.Hosting` | DI y configuracion |

## Autenticacion

El launcher soporta dos modos:

1. Microsoft OAuth: autenticacion oficial con cuenta Microsoft para servidores premium.
2. Offline: acceso local usando un nombre de usuario configurado por el usuario.

## Java

| Version Minecraft | Java requerido |
| ----------------- | -------------- |
| 1.0 a 1.16 | Java 8 |
| 1.17 a 1.20.4 | Java 17 |
| 1.20.5 o superior | Java 21 |

Si no se encuentra la version adecuada, el launcher intenta descargarla en su carpeta interna.

## Modrinth

Desde la vista Mods puedes buscar mods en Modrinth, ver informacion basica y filtrar resultados por version de Minecraft y loader.

## Pruebas

```bash
dotnet test ShoroCraftLauncher.sln
```

Los tests que descargan archivos reales, revisan instalaciones de Java o lanzan Minecraft estan marcados como `Category=Integration` y se omiten por defecto.

Para correrlos manualmente:

```powershell
$env:SHOROCRAFT_RUN_INTEGRATION_TESTS="1"
dotnet test ShoroCraftLauncher.sln --filter Category=Integration
```

## Desarrollo

Para agregar un servicio:

1. Define la interfaz en `ShoroCraftLauncher.Core/Interfaces/`.
2. Implementa el servicio en `ShoroCraftLauncher.Infrastructure/Services/`.
3. Registra la dependencia en `App.xaml.cs`.

## Commits

El proyecto usa Conventional Commits:

```txt
feat:     Nueva funcionalidad
fix:      Correccion de bug
style:    Cambios de UI o estilos
refactor: Refactorizacion sin cambio de comportamiento
docs:     Documentacion
chore:    Tareas de mantenimiento
test:     Pruebas
```

## Roadmap

- [x] Dashboard con metricas
- [x] Gestion de perfiles
- [x] Lanzamiento de Minecraft
- [x] Consola de logs en tiempo real
- [x] Gestion de mods, resource packs y shaders
- [x] Autenticacion Microsoft OAuth
- [x] Modo offline con nombre de usuario configurable
- [x] Busqueda de mods via Modrinth API
- [x] Noticias en el dashboard
- [x] Instalacion de mods desde Modrinth con un click
- [ ] Notificaciones de actualizaciones de Minecraft
- [x] Soporte para CurseForge API
- [x] Empaquetado como instalador (Inno Setup / .exe)

## Licencia

Distribuido bajo licencia MIT. Consulta [LICENSE](LICENSE).

## Créditos

- Esta aplicación usa bibliotecas .NET de código abierto:
- © 2026 Shoropio Corporation. Todos los derechos reservados.

- [CmlLib.Core](https://github.com/AlphaBs/CmlLib.Core) y [CmlLib.Core.Auth.Microsoft](https://github.com/AlphaBs/CmlLib.Core.Auth.Microsoft) — integración con Minecraft y autenticación Microsoft
- [Entity Framework Core](https://github.com/dotnet/efcore) y SQLite — persistencia local
- [Serilog](https://github.com/serilog/serilog) — logging
- [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) — DI y configuración
- [WebView2](https://developer.microsoft.com/microsoft-edge/webview2) — vistas embebidas
- [xUnit](https://github.com/xunit/xunit) y [Moq](https://github.com/devlooped/moq) — pruebas