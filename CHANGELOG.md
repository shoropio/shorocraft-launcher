# Changelog

Todos los cambios notables de este proyecto están documentados aquí.
El formato está basado en [Keep a Changelog](https://keepachangelog.com/es/1.0.0/)
y este proyecto sigue [Semantic Versioning](https://semver.org/lang/es/).

---

## [1.4.1] - 2026-08-17

### ✨ Nuevo
- Botón "Fabric + Iris + Sodium" en el dashboard: crea un perfil Fabric con Iris (shaders) y Sodium (rendimiento) preinstalados con un solo clic. El botón se deshabilita y muestra "Iris + Sodium instalado" cuando los mods ya están presentes en el perfil activo.
- Reporte de código de salida al cerrar el juego: si Minecraft termina con error (código ≠ 0), el status bar muestra un mensaje con el código y sugiere revisar la consola.

### 🔧 Correcciones
- Sidebar colapsable ahora funciona correctamente: el ancho de la columna cambia entre 64 px (colapsado) y 240 px (expandido) vía code-behind, reemplazando el DataTrigger roto en `ColumnDefinition` (que no tiene `DataContext` por heredar de `DefinitionBase`).
- Fondo blanco intermitente al buscar shaders: los Grids raíz y de contenido en `ShaderPacksView` ahora tienen `BackgroundBrush` explícito para evitar destellos durante la carga.
- Fondo blanco en Mods, Resource Packs y Maps: mismas correcciones de `BackgroundBrush` en las vistas afectadas.
- Navegación por pestañas (Instalados/Popular/Recomendado) ahora resalta visualmente la pestaña activa con color primario.
- Padding del sidebar ajustado para evitar desbordamiento al colapsar: header, nav items y panel de usuario ahora caben en 64 px.
- Se deshabilita el botón "Fabric + Iris + Sodium" cuando los mods ya están instalados en el perfil seleccionado.
- **Iris/Sodium crash en MC 26.2**: formato de versión incorrecto al buscar en Modrinth (launcher usaba `26.2`, Modrinth espera `1.21.2`). Ahora `ToModrinthVersion()` convierte `26.x` → `1.21.x` en búsqueda y resolución de descarga.
- **Versión del juego en la barra superior**: ahora resuelve `latest` a la versión real (ej. `26.2`) en lugar de mostrar el literal `latest`.
- **Fondo blanco en shaders al buscar**: eliminado `IsEnabled` del Grid de contenido (causaba overlay blanco por estado disabled); ahora se deshabilitan botones individuales durante carga.

### 🏗️ Refactorización
- `LauncherPaths`: clase estática para centralizar rutas de `%LocalAppData%\ShoroCraftLauncher` (reemplaza concatenaciones repetidas en `ProfileService` y `ServerService`).
- `TestPaths`: helper para aislamiento de tests con directorios temporales deterministas y scopes de data root.
- `GameExited` ahora propagsa el código de salida del proceso (de `Action` a `Action<int>`).
- `Inno Setup`: ruta de fuente del instalador corregida a `publish/` y versión fija a `1.4.0`.

---

## [1.4.0] - 2026-08-11

### ✨ Nuevo
- Sistema de descargas reanudables para redes lentas o inestables: las descargas grandes (jar de servidor, cliente y librerías de Minecraft, instalador de actualización, modpacks, mods y shaders) se guardan en un archivo `.part` y, si la conexión se interrumpe, se reanudan desde donde iban mediante peticiones HTTP `Range` en lugar de empezar de cero.
- Reintentos automáticos (hasta 5 intentos) con espera progresiva y timeout de inactividad por lectura (60 s) que aborta descargas que se quedan sin datos y las retoma.
- Verificación opcional de tamaño y hash SHA-1 al completar la descarga (usada en la importación de modpacks).

### 📦 Empaquetado
- Firma Authenticode del instalador y del ejecutable con un certificado self-signed (SHA-256 + timestamp DigiCert). El desinstalador también queda firmado (`SignedUninstaller`). En equipos sin el certificado en el almacén de confianza el editor aparecerá como "desconocido". Script de firma en `installer/sign.ps1` y configuración en `installer/setup.iss` (definición vía `ISCC /Ssigntool=...`).

## [1.3.1] - 2026-08-09

### 🔧 Correcciones
- Al iniciar un servidor, el launcher detecta y detiene procesos Java huérfanos de sesiones anteriores (vía `server.pid`) que retenían `logs/latest.log` y `session.lock`, evitando el fallo de arranque del servidor.
- Los servidores en ejecución se detienen correctamente al cerrar el launcher, evitando que queden procesos huérfanos.
- La consola (principal y de servidores) tiene padding superior y lateral de 12 px para que el texto no quede pegado a los bordes.
- Los botones del panel de servidores quedan pegados bajo la lista (sin pegarse al fondo ni necesitar scroll).

## [1.3.0] - 2026-08-08

### ✨ Nuevo
- Módulo de servidores: nueva sección "Servidores" en la barra lateral para crear y gestionar servidores Vanilla y Paper desde el launcher.
- El launcher descarga automáticamente el jar del servidor (con barra de progreso), genera `eula.txt` y `server.properties`, e inicia el proceso Java con consola interactiva: comandos, detener, reiniciar, despertar y copiar la consola.
- La pausa automática por servidor vacío se deshabilita (`pause-when-empty-seconds=0`) para que la consola siempre responda.

## [1.2.1] - 2026-08-08

### 🔧 Correcciones
- El botón "Buscar actualizaciones" en Ajustes → Mantenimiento ahora consulta el repositorio GitHub de verdad y ofrece instalar la nueva versión; antes siempre respondía "No hay actualizaciones disponibles" (implementación simulada).

## [1.2.0] - 2026-08-08

### ✨ Nuevo
- Notificaciones de actualizaciones de Minecraft: banner en el dashboard que avisa cuando el perfil Vanilla usa una versión anterior a la más reciente, con opciones "Instalar" y "Ignorar"; la última versión notificada se guarda para no repetir el aviso.

## [1.1.1] - 2026-08-08

### 🔧 Correcciones
- La versión mostrada en Ajustes y enviada al juego (`minecraft.launcher.version`) ahora se lee del ensamblado, por lo que refleja la versión real del launcher en lugar del valor fijo "1.0.0".

## [1.1.0] - 2026-08-07

### ✨ Nuevo
- Actualizador automático del launcher: detecta nueva versión, descarga el instalador y reinicia la aplicación con un banner en el dashboard.
- Mundos por perfil: extracción de mundos `.zip`/`.mcworld` directamente a la carpeta `saves`, escaneo de mundos existentes en disco, preview de `icon.png` y botón "Respaldo de mundos".
- Importación de modpacks Modrinth (`.mrpack`): descarga y verificación SHA-1 de mods, respeta `env.client`, aplica `overrides/` y registra los mods en la base de datos.
- Soporte de NeoForge: instalación del loader, detección de versión más reciente, filtrado de mods por loader en CurseForge y botón "Instalar NeoForge".
- Cuenta Microsoft online: restauración automática de la sesión al arrancar (login silencioso) y botón "Cerrar sesión".

### 🔧 Correcciones
- Corregido crash de arranque (`AccessViolationException`) causado por el P/Invoke a `CredEnumerateW` al enumerar credenciales de Windows; el logout ahora usa `JELoginHandler.Signout()`.
- El cierre del cuadro de inicio de sesión Microsoft ya no se registra como error: se muestra el mensaje "Inicio de sesión Microsoft cancelado" sin stack trace.
- Reducido el ruido en consola al fallar el login silencioso (sin sesión guardada).

## [1.0.2] - 2026-05-22

### ✨ Nuevo
- Mejoras de UX en Perfiles y Mods: estados de carga más claros, confirmaciones de eliminación y mensajes de éxito/errores más comprensibles.
- Indicadores de carga en la vista de Perfiles y en la gestión de Mods.
- Release empaquetado de nuevo con instalador Windows actualizado.

### 🔧 Correcciones
- Bloqueo de acciones mientras se ejecuta una operación para evitar estados inconsistentes.
- Mensajes de error más claros para exportación/importación de perfiles y búsqueda/instalación de mods.
- Confirmaciones añadidas al eliminar perfiles, mods y copias de seguridad.

## [1.0.1] - 2026-05-21

### ✨ Nuevo
- Interfaz de usuario actualizada: botones de Iris+Sodium y OptiFine disponibles directamente en el dashboard.
- Instalador Windows generado y empaquetado con Inno Setup.

### 🔧 Correcciones
- Mejorado el manejo del estado de descarga/progreso para que no quede información obsoleta tras iniciar el juego.
- Ajustado el flujo de release para subir assets `.exe` y `.zip` a GitHub.

## [1.0.0] - 2026-05-12

### ✨ Nuevo

#### Autenticación
- Integración completa con Microsoft OAuth2 via `CmlLib.Core.Auth.Microsoft`
- Modo offline con UUID persistente
- Visualización de skin del jugador en la barra lateral via Crafatar

#### Dashboard
- Panel con métricas en tiempo real (versión instalada, mods activos, RAM, estado)
- Feed de últimas noticias de Minecraft con imagen, fecha y resumen
- Sección de "Inicio rápido" para instalar versiones y loaders directamente

#### Gestión de Perfiles
- Creación y edición de perfiles: Vanilla, Forge, Fabric, Quilt, OptiFine, Iris
- Configuración de RAM asignable con slider moderno
- Argumentos JVM personalizados por perfil
- Selección de carpeta de juego y Java path

#### Gestión de Mods
- Barra de búsqueda integrada con Modrinth API
- Visualización de resultados con icono, nombre y descripción del mod
- Gestión de mods locales (.jar): agregar, activar/desactivar, eliminar
- Vista dual: mods locales / resultados de búsqueda online

#### Gestión de Recursos
- Resource Packs: agregar y activar/desactivar texturas (.zip)
- Shader Packs: gestión compatible con Iris/OptiFine

#### Java Automático
- Detección automática de JRE instalado (Java 8, 17, 21)
- Infraestructura preparada para descarga automática de JRE faltante
- Selección automática del Java correcto según versión de Minecraft

#### Consola
- Vista de logs en tiempo real del proceso de Minecraft
- Coloreado por nivel: INFO (verde), WARN (amarillo), ERROR (rojo)
- Scroll automático al final

#### UX / Diseño
- Tema dark mode con azul neón (#00BFFF) y glassmorphism
- Icono de aplicación: bloque de tierra (Grass Block) estilo Minecraft
- Estilo `SearchTextBox` con placeholder text y bordes redondeados
- Slider de RAM con estilo moderno (`ModernSlider`)
- Toggle switches para activar/desactivar mods

### 🏗️ Arquitectura
- Patrón MVVM con separación estricta de capas (Core/Data/Infrastructure/App)
- Inyección de Dependencias con `Microsoft.Extensions.Hosting`
- Base de datos SQLite con Entity Framework Core
- Logging con Serilog (archivo rotativo + consola)
- Repositorio genérico para acceso a datos

### 🔧 Correcciones
- Corregido error `Track.DecreaseRepeatButton` en el Slider (usaba `Button` en lugar de `RepeatButton`)
- Corregido error `StaticResource SearchTextBox` no encontrado al abrir la vista de Mods
- Corregido desbordamiento de `Grid.Row="3"` con solo 3 RowDefinitions declaradas
- Corregido error de rutas relativas `Assets/icon.png` cambiando a rutas absolutas `/Assets/icon.png`
- Corregido `CS8601` (posible asignación nula) en `ModService.cs`

---

## [0.9.0] - 2026-05-11 *(Pre-release)*

### ✨ Nuevo
- Arquitectura inicial MVVM con 5 proyectos (App, Core, Data, Infrastructure, Tests)
- Vistas principales: Dashboard, Perfiles, Mods, Texturas, Shaders, Consola, Ajustes
- Lanzamiento de Minecraft con `CmlLib.Core`
- Gestión de proceso del juego con logs capturados en tiempo real
- Inicio de sesión offline funcional
