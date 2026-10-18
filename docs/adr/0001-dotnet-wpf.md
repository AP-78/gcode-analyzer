# .NET WPF als Plattform

Die App soll nativ unter Windows und offline laufen. Wir bauen sie mit C# / .NET WPF und HelixToolkit für die 3D-Ansicht, ausgeliefert als einzelne selbstständige `.exe`.

## Considered Options

- Python + PySide6 + PyInstaller: Python war schon installiert, aber die Exe ist groß und startet langsam.
- Tauri/Electron + three.js: die besten fertigen G-Code-Viewer, aber im Kern eine Web-App und nicht nativ.
