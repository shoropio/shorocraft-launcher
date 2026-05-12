# Changelog

Todos los cambios notables de este proyecto están documentados aquí.
El formato está basado en [Keep a Changelog](https://keepachangelog.com/es/1.0.0/)
y este proyecto sigue [Semantic Versioning](https://semver.org/lang/es/).

---

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
