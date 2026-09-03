# Release Notes

## 2026-09-02 — Release v1.6.9

- Nuevo botón **Respaldar** junto a cada mundo en la pestaña Mapas: crea un `.zip` con fecha de la carpeta del mundo en `backups/<perfil>/Worlds/`.

## 2026-09-02 — Release v1.6.8

- Consola más limpia: eliminados los warnings `Unknown module` de `--enable-native-access` en Java 25 (ahora solo `ALL-UNNAMED`).
- Los mensajes informativos de java.util.logging (p. ej. ReplayMod "INFORMACIÓN: Loading block connection mappings...") ya no se muestran como `[ERROR]`; se clasifican como `[WARN]`.
- Nuevo: frases melancólicas al estilo de Minecraft en la consola al crear, editar y eliminar perfiles, mundos y servidores.

## 2026-09-02 — Release v1.6.7

- Corregido `NullReferenceException` en `StopGameAsync` al detener el juego justo cuando el proceso terminaba por su cuenta (race condition con el evento `Exited`).
- Migrada la API de Paper a `fill.papermc.io` (la antigua `api.papermc.io` responde 403 desde su migración); restaurada la lista de versiones y la descarga del jar.
- Consola y barra de progreso más informativas durante el lanzamiento de perfiles (fases: validación, reparación, Java, verificación de archivos, inicio del proceso) y al arrancar servidores (Preparación, jar, Java, RAM/puerto).
- Errores de la API de Paper ahora visibles en la consola con el mensaje real en lugar de fallar en silencio.
- Añadido harness de integración en vivo (`TestCml`) que valida el flujo completo: creación e instalación de perfil Fabric, lanzamiento de Minecraft en sesión offline, y creación/arranque/comando/parada de un servidor Paper.

Nota: Ejecuta `dotnet test` para validar la suite de pruebas. Las pruebas de integración largas están marcadas como omitidas por defecto.

## 2026-05-21 — Perfil: Export/Import & Backups

- Añadido soporte para exportar e importar perfiles como paquete `.zip`.
- Añadida gestión de copias de seguridad (crear, listar, restaurar, eliminar) desde la vista de Perfiles.
- Implementadas pruebas automatizadas que cubren el flujo de exportación desde el `ProfilesViewModel`.
- Mejoras de estabilidad en `ProfileService` y reducción de ruido de logs de EF/ASP.NET.

## 2026-05-22 — UX & Release v1.0.2

- Añadidos indicadores de carga en Perfiles y Mods.
- Mejorada la validación de acciones en Perfiles: confirmaciones en eliminación, importación/exportación más robusta y estados de formulario más claros.
- Añadidas confirmaciones de eliminación para mods y copias de seguridad.
- Publicado instalador Windows actualizado `ShoroCraftLauncher_Setup.exe` y paquete `ShoroCraftLauncher_Publish.zip`.

## 2026-05-21 — UI / Release v1.0.1

- Añadidos botones visibles de `Iris + Sodium` e `OptiFine` en el dashboard.
- Mejoras en el estado de progreso: la UI ya no mantiene mensajes de descarga antiguos después del lanzamiento.
- Generado instalador Windows `ShoroCraftLauncher_Setup.exe` con Inno Setup.
- Publicada release `v1.0.1` con instalador y paquete `.zip`.

Nota: Ejecuta `dotnet test` para validar la suite de pruebas. Las pruebas de integración largas están marcadas como omitidas por defecto.
