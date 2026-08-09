# Changelog

Todos los cambios notables de este proyecto están documentados aquí.
El formato está basado en [Keep a Changelog](https://keepachangelog.com/es/1.0.0/)
y este proyecto sigue [Semantic Versioning](https://semver.org/lang/es/).

---

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
