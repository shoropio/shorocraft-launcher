# Guía de Contribución

¡Gracias por tu interés en contribuir a ShoroCraft Launcher! 🎮

## Antes de empezar

1. Lee el [README](README.md) para entender la arquitectura
2. Revisa los [issues abiertos](../../issues) para no duplicar trabajo
3. Para cambios grandes, abre un issue primero para discutirlo

## Flujo de trabajo

```bash
# 1. Fork y clonar
git clone https://github.com/TU-USUARIO/shorocraft-launcher.git
cd shorocraft-launcher

# 2. Crear rama desde main
git checkout -b feat/nombre-de-tu-feature

# 3. Compilar y verificar que todo funciona
dotnet build
dotnet test

# 4. Hacer tus cambios...

# 5. Commitear siguiendo la convención
git commit -m "feat: agrega soporte para CurseForge API"

# 6. Push y Pull Request
git push origin feat/nombre-de-tu-feature
```

## Convención de commits

Usamos [Conventional Commits](https://www.conventionalcommits.org/):

| Prefijo | Cuándo usarlo |
|---------|---------------|
| `feat:` | Nueva funcionalidad |
| `fix:` | Corrección de bug |
| `style:` | Cambios de UI/estilos (no lógica) |
| `refactor:` | Refactorización sin cambio de comportamiento |
| `docs:` | Solo documentación |
| `chore:` | Mantenimiento, dependencias, configuración |
| `test:` | Añadir o corregir tests |

## Estilo de código

- **C#**: Seguir las convenciones de Microsoft para C#
- **XAML**: Un atributo por línea para elementos con más de 2 atributos
- **Nullable**: El proyecto usa `#nullable enable`. Siempre manejar posibles nulos
- **Async/Await**: Todos los métodos I/O deben ser async
- **Logging**: Usar `ILogger<T>` inyectado, nunca `Console.WriteLine`

## Arquitectura a respetar

```
Core       ← no depende de nada
Data       ← depende de Core
Infrastructure ← depende de Core
App        ← depende de todo
```

Nunca hacer que `Core` o `Data` dependan de `Infrastructure` o `App`.

## Reportar bugs

Al reportar un bug incluye:
1. Versión del launcher
2. Versión de Windows
3. Pasos para reproducir
4. Log de error (en `%LocalAppData%\ShoroCraftLauncher\logs\`)
