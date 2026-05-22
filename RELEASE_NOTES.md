# Release Notes

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
