<div align="center">

# ShoroCraft Launcher

**Launcher profesional de Minecraft para Windows**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightgrey?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

*Un launcher moderno, potente y completamente personalizable.*

</div>

---

## Características Principales

| Módulo | Descripción |
|--------|-------------|
| **Autenticación** | Microsoft OAuth integrado + modo offline |
| **Perfiles** | Múltiples perfiles con Vanilla, Forge, Fabric, Quilt, OptiFine, Iris |
| **Java Automático** | Detección y descarga automática de JRE 8, 17 y 21 |
| **Buscador de Mods** | Búsqueda e instalación directa desde Modrinth API |
| **Resource Packs** | Gestión de texturas con activación/desactivación |
| **Shaders** | Gestión de shader packs compatible con Iris/OptiFine |
| **Noticias** | Feed de noticias de Minecraft en el Dashboard |
| **Consola** | Logs en tiempo real del proceso de Minecraft |
| **Base de Datos** | SQLite local para persistencia de perfiles y mods |

---

## Capturas de Pantalla

> **Dashboard** — Resumen rápido del estado del launcher, noticias y acceso rápido

| Vista | Descripción |
|-------|-------------|
| Dashboard | Métricas: versión instalada, mods activos, RAM, estado |
| Perfiles | Crear y gestionar múltiples configuraciones de Minecraft |
| Mods | Buscar en Modrinth y gestionar mods locales |
| Consola | Logs en tiempo real con colores por nivel |

---

## Inicio Rápido

### Requisitos

- **Windows 10 / Windows 11 (x64)**
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Java 8, 17 o 21 *(se detecta o descarga automáticamente)*

### Compilar desde el código fuente

```bash
# 1. Clonar el repositorio
git clone https://github.com/Shoropio/shorocraft-launcher.git
cd shorocraft-launcher

# 2. Restaurar dependencias
dotnet restore

# 3. Compilar en Release
dotnet build -c Release

# 4. Ejecutar
dotnet run --project src/ShoroCraftLauncher.App
````

También puedes abrir `ShoroCraftLauncher.sln` en **Visual Studio 2022** (v17.8+) y compilar con `Ctrl+Shift+B`.

---

## Arquitectura del Proyecto

El launcher sigue el patrón **MVVM** con **Inyección de Dependencias** centralizada, separando responsabilidades en 4 capas:

```txt
shorocraft-launcher/
├── src/
│   ├── ShoroCraftLauncher.App/            # UI Layer
│   │   ├── Views/                         # Vistas WPF (.xaml)
│   │   ├── ViewModels/                    # ViewModels (MVVM)
│   │   ├── Styles/                        # Tema dark, controles custom
│   │   ├── Converters/                    # Value converters
│   │   └── App.xaml.cs                    # DI Container, bootstrapping
│   │
│   ├── ShoroCraftLauncher.Core/           # Domain Layer
│   │   ├── Interfaces/                    # Contratos de servicios
│   │   ├── Models/                        # Entidades de negocio
│   │   └── Enums/                         # Enumeraciones
│   │
│   ├── ShoroCraftLauncher.Data/           # Data Layer
│   │   ├── LauncherDbContext.cs           # EF Core + SQLite
│   │   └── Repositories/                  # Implementaciones de repositorios
│   │
│   ├── ShoroCraftLauncher.Infrastructure/ # Services Layer
│   │   ├── Services/                      # JavaService, ModService, LauncherService...
│   │   └── Authentication/                # Microsoft OAuth + Offline auth
│   │
│   └── ShoroCraftLauncher.Tests/          # Test Layer
│       └── ...                            # xUnit unit tests
│
├── assets/                                # Recursos visuales
├── docs/                                  # Documentación adicional
├── installer/                             # Configuración del instalador
└── README.md
```

### Dependencias principales

| Paquete                                | Versión | Uso                              |
| -------------------------------------- | ------- | -------------------------------- |
| `CmlLib.Core`                          | 4.x     | Integración nativa con Minecraft |
| `CmlLib.Core.Auth.Microsoft`           | latest  | Autenticación Microsoft OAuth    |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.0   | Persistencia local               |
| `Serilog`                              | 8.x     | Sistema de logging               |
| `Microsoft.Extensions.Hosting`         | 8.0.0   | DI + Configuration               |

---

## Autenticación

El launcher soporta dos modos:

1. **Microsoft (OAuth2)** — Autenticación oficial con cuenta de Mojang/Microsoft. Permite jugar en servidores premium y muestra la skin real.
2. **Offline** — Acceso rápido sin cuenta. Ideal para servidores no premium y modo local.

> La autenticación Microsoft abre el navegador por defecto para validar las credenciales de forma segura. Los tokens se almacenan de forma cifrada localmente.

---

## Gestión Automática de Java

| Versión Minecraft | Java Requerido |
| ----------------- | -------------- |
| 1.0 – 1.16        | Java 8         |
| 1.17 – 1.20.4     | Java 17        |
| 1.20.5+           | Java 21        |

Si no se encuentra la versión adecuada en el sistema, el launcher la descarga automáticamente a su carpeta interna.

---

## Integración con Modrinth

Desde la pestaña **Mods**, puedes:

1. Buscar mods directamente en [Modrinth](https://modrinth.com/) sin salir del launcher
2. Ver el icono, nombre y descripción de cada mod
3. Los resultados se filtran automáticamente por la versión de Minecraft del perfil seleccionado

---

## Desarrollo

### Pruebas

```bash
dotnet test ShoroCraftLauncher.sln
```

Los tests que descargan archivos reales, revisan Java o lanzan Minecraft estan marcados como `Category=Integration` y se omiten por defecto. Para correrlos manualmente:

```powershell
$env:SHOROCRAFT_RUN_INTEGRATION_TESTS="1"
dotnet test ShoroCraftLauncher.sln --filter Category=Integration
```

### Crear un nuevo servicio

1. Definir la interfaz en `ShoroCraftLauncher.Core/Interfaces/`
2. Implementar en `ShoroCraftLauncher.Infrastructure/Services/`
3. Registrar en el contenedor DI en `App.xaml.cs`

### Contribuir

1. Fork el repositorio
2. Crea una rama feature: `git checkout -b feature/mi-feature`
3. Commitea tus cambios: `git commit -m 'feat: agrega mi-feature'`
4. Push a la rama: `git push origin feature/mi-feature`
5. Abre un Pull Request

### Convención de commits

Seguimos [Conventional Commits](https://www.conventionalcommits.org/):

```txt
feat:     Nueva funcionalidad
fix:      Corrección de bug
style:    Cambios de UI/estilos
refactor: Refactorización sin cambio de comportamiento
docs:     Documentación
chore:    Tareas de mantenimiento
test:     Añadir o corregir tests
```

---

## Roadmap

* [x] Dashboard con métricas en tiempo real
* [x] Gestión de perfiles (Vanilla, Forge, Fabric, Quilt)
* [x] Lanzamiento de Minecraft con proceso separado
* [x] Consola de logs en tiempo real
* [x] Gestión de Mods, Resource Packs y Shaders
* [x] Autenticación Microsoft OAuth
* [x] Búsqueda de mods via Modrinth API
* [x] Sistema de noticias en el Dashboard
* [x] Icono de aplicación personalizado
* [ ] Descarga automática de JRE integrada (CmlLib)
* [ ] Instalación de mods desde Modrinth con un click
* [ ] Notificaciones de actualizaciones de Minecraft
* [ ] Soporte para CurseForge API
* [ ] Empaquetado como instalador (.msi)

---

## Licencia

Distribuido bajo la licencia **MIT**. Ver [`LICENSE`](LICENSE) para más información.

---

<div align="center">
  <sub>Hecho con ❤️ por <a href="https://github.com/Shoropio">Shoropio</a></sub>
</div>
