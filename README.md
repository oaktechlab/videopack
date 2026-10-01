# VideoPack

Aplicación Windows nativa para preparar y exportar un vídeo con cortinillas, mosca, formato y preset de calidad. Interfaz WPF sobre .NET 8; FFmpeg y FFprobe se ejecutan localmente.

## Desarrollo

Requisitos: Windows x64, SDK de .NET 8 o posterior y conexión a internet la primera vez para descargar FFmpeg Essentials.

```powershell
.\run-dev.ps1
```

El script prepara los assets y FFmpeg en `data/`, y después ejecuta la aplicación WPF.

## Compilar y publicar

.\build-portable.ps1
.[0m\build-portable.ps1
```

También se puede ejecutar `build-portable.bat`. El resultado self-contained, x64 y single-file queda en `dist/VideoPack/VideoPack.exe`, junto a `dist/VideoPack/data/`. La primera compilación descarga aproximadamente 115 MB de FFmpeg; conserva las licencias del paquete en `data/licenses/`.

Para preparar únicamente los recursos del modo desarrollo:
.\build-portable.ps1 -PrepareOnly
```powershell
.[0m\build-portable.ps1 -PrepareOnly
```

## Assets

Los originales se encuentran en `resources/bumper/`, `resources/logos/` y `resources/mockup/`. El build los copia a `data/assets/` con las rutas que consume la aplicación. Para sustituirlos, conserva los nombres configurados en `data/presets/appsettings.json` o actualiza las rutas allí.

La configuración apunta a una cortinilla de entrada 16:9, dos versiones de salida y las ocho alternativas de mosca suministradas. La salida cuadrada reutiliza la cortinilla vertical.

## Presets

Los niveles y bitrates se configuran en `data/presets/presets.json`. Los valores internos de códec, audio, margen y tamaño de mosca están en `data/presets/appsettings.json`. Los presets incluidos son MASTER, YOUTUBE y REDES; PERSONALIZADO aparece al cambiar parámetros.

## Pruebas

```powershell
dotnet test tests/VideoPack.Tests/VideoPack.Tests.csproj
```

## Temporales y registros

Los temporales de procesamiento y miniaturas se guardan en `data/temp/`. Los logs de exportación, incluyendo configuración, comando y salida FFmpeg, se guardan en `data/logs/`. Un error conserva el log y el archivo parcial para diagnóstico; una cancelación elimina el parcial.