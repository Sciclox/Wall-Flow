<h1 align="center">WallFlow</h1>

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge)
![License](https://img.shields.io/badge/license-GPLv3-blue?style=for-the-badge)
![Platform](https://img.shields.io/badge/platform-WPF-512BD4?style=for-the-badge)

> Gestor de fondos de pantalla minimalista para Windows con integración en la bandeja del sistema.

<p align="center">
  <img src="assets/screenshot.png" alt="WallFlow Screenshot" width="800"/>
</p>

---

## 🚗 Getting Started

### 📦 Installation

**Windows 10+ — Portable Version**

#### PowerShell (recomendado)
```powershell
# Instalación automática (descarga, extrae, acceso directo)
powershell -c "iwr -Uri 'https://github.com/Sciclox/Wall-Flow/releases/latest/download/install.ps1' -OutFile install.ps1; .\install.ps1"
```

#### Manual
| Archivo | Descripción |
|---------|-------------|
| [`WallFlow.exe`](https://github.com/Sciclox/Wall-Flow/releases/latest/download/WallFlow.exe) | Ejecutable portable (self-contained, no requiere .NET) |
| [`WallFlow-Setup.exe`](https://github.com/Sciclox/Wall-Flow/releases/latest/download/WallFlow-Setup.exe) | Instalador con Inno Setup |
| [`WallFlow-v1.0.0.zip`](https://github.com/Sciclox/Wall-Flow/releases/latest/download/WallFlow-v1.0.0.zip) | Zip con el ejecutable |

---

### ⚙️ Configuración

Por defecto busca wallpapers en:

```
C:\Users\%USERNAME%\OneDrive\Imágenes\Wallpapers
```

Para usar un directorio personalizado, crea un archivo `wallflow.txt` junto al ejecutable con la ruta de tu carpeta de wallpapers.

---

## ✨ Características

| | |
|---|---|
| 🖼️ **Exploración visual** | Navegación por tus wallpapers con scroll horizontal paginado |
| 🎯 **Establecer wallpaper** | Haz clic en cualquier wallpaper para aplicarlo al instante |
| 🔄 **Autoarranque** | Inicia con Windows con icono visible en apps de inicio |
| ⌨️ **Atajo de teclado** | `Alt + W` para mostrar/ocultar la ventana |
| 🎛️ **Bandeja del sistema** | Icono en el área de notificaciones con menú contextual animado |
| 🌫️ **Overlay transparente** | Interfaz semitransparente sobre el escritorio con efecto blur |

<p align="center">
  <img src="assets/contextmenu.png" alt="Context Menu" width="600"/>
</p>

---

## ⌨️ Uso

| Acción | Resultado |
|--------|-----------|
| Clic en wallpaper | Establece como fondo de pantalla |
| Scroll horizontal | Navega entre páginas de wallpapers |
| `Alt + W` | Muestra/oculta la ventana |
| Clic derecho en ícono de bandeja | Abre menú contextual |
| Clic izquierdo en ícono de bandeja | Activa la ventana |
| `Escape` | Cierra la ventana |

---

## 🔧 Compilación desde código

```powershell
git clone https://github.com/Sciclox/Wall-Flow.git
cd Wall-Flow/WallFlow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 📄 Licencia

GNU General Public License v3.0
