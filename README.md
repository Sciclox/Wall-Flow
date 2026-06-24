# WallFlow

Gestor de fondos de pantalla minimalista para Windows, con integración en la bandeja del sistema.

## Características

- **Exploración visual**: Navegación por tus wallpapers con scroll horizontal paginado
- **Establecer wallpaper**: Haz clic en cualquier wallpaper para aplicarlo al escritorio
- **Autoarranque**: Opción para iniciar con Windows desde la bandeja del sistema
- **Atajo de teclado**: `Alt + W` para mostrar/ocultar la ventana
- **Bandeja del sistema**: Icono en el área de notificaciones con menú contextual
- **Overlay transparente**: Interfaz semitransparente sobre el escritorio

## Requisitos

- Windows 10 u 11 (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Instalación

1. Descarga la última versión desde [Releases](https://github.com/Sciclox/Wall-Flow/releases)
2. Extrae el archivo en cualquier carpeta
3. Ejecuta `WallFlow.exe`

### Configuración de directorio

Por defecto busca wallpapers en `C:\Users\%USERNAME%\OneDrive\Imágenes\Wallpapers`.

Para usar un directorio personalizado, crea un archivo `wallflow.txt` junto al ejecutable con la ruta de tu carpeta de wallpapers.

## Uso

| Acción | Resultado |
|--------|-----------|
| Clic en wallpaper | Establece como fondo de pantalla |
| Scroll horizontal | Navega entre páginas de wallpapers |
| `Alt + W` | Muestra/oculta la ventana |
| Clic derecho en ícono de bandeja | Abre menú contextual |
| Clic izquierdo en ícono de bandeja | Activa la ventana |
| `Escape` | Cierra la ventana |

## Compilación desde código

```powershell
git clone https://github.com/Sciclox/Wall-Flow.git
cd Wall-Flow
dotnet build -c Release
```

## Licencia

MIT
