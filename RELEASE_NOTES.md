# Release Notes

## 2026-05-21 — Perfil: Export/Import & Backups

- Añadido soporte para exportar e importar perfiles como paquete `.zip`.
- Añadida gestión de copias de seguridad (crear, listar, restaurar, eliminar) desde la vista de Perfiles.
- Implementadas pruebas automatizadas que cubren el flujo de exportación desde el `ProfilesViewModel`.
- Mejoras de estabilidad en `ProfileService` y reducción de ruido de logs de EF/ASP.NET.

Nota: Ejecuta `dotnet test` para validar la suite de pruebas. Las pruebas de integración largas están marcadas como omitidas por defecto.
