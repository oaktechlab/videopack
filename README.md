# VideoPack

VideoPack prepara un vídeo para publicarlo o archivarlo: le da el formato adecuado, le a?ade la cortinilla y el logo de la marca, y lo exporta en un archivo listo para usar. Es una aplicación de Windows. El vídeo no sale del ordenador.

## Qué resuelve

Montar un vídeo con apertura, cierre y mosca suele exigir un editor y una ronda de ajustes que se repiten en cada pieza. VideoPack concentra ese trabajo en una sola pantalla. Tú eliges el vídeo y el destino; la aplicación se ocupa del encuadre, la calidad y los elementos de marca.

Sirve igual para archivar un máster, publicar en YouTube o preparar una pieza vertical para redes.

## Qué puedes hacer

- **Traer tu vídeo.** Arrástralo a la ventana o selecciónalo. Admite MP4, MOV, MKV, AVI, M4V y WEBM, y muestra su resolución, duración, códec y tama?o.
- **Elegir el destino.** Tres ajustes preparados, o uno personalizado si quieres decidir tú.
  - **Master.** Máxima calidad para archivo, en 4K horizontal.
  - **YouTube.** Calidad de publicación web, en 1080p horizontal.
  - **Redes.** Pieza vertical en 720p, recortada para llenar la pantalla del móvil.
  - **Personalizado.** Resolución, fotogramas por segundo, códecs y bitrate a tu medida. Aparece solo cuando cambias algún parámetro.
- **Decidir el formato.** Horizontal 16:9, vertical 9:16 o cuadrado 1:1.
- **Encuadrar o recortar.** Encuadrar conserva todo el vídeo y a?ade bandas si hace falta. Recortar llena el marco y descarta lo que sobra. La vista previa ense?a el resultado antes de exportar.
- **A?adir la marca.** Cortinilla de entrada, cortinilla de cierre y mosca. La mosca se incrusta solo sobre tu vídeo, en la esquina que elijas, con ocho variantes de logo. La cortinilla de entrada está disponible en formato horizontal; en vertical y cuadrado se usa la cortinilla de cierre vertical.
- **Exportar un MP4.** Eliges carpeta y nombre. Durante la exportación ves el avance y puedes cancelarla. Al terminar puedes abrir el vídeo o la carpeta.

La ayuda integrada recorre estos pasos desde la propia ventana. La interfaz tiene modo claro y modo oscuro, y recuerda el que hayas elegido.

## Por qué resulta práctica

Todo el procesado ocurre en local. No hay cuenta, ni subida, ni servicio externo que reciba el archivo.

Los ajustes preparados evitan elegir bitrates a ciegas, y el modo personalizado sigue ahí cuando el caso se sale de lo habitual. Antes de exportar ves el encuadre, la posición del logo y una estimación del tama?o. VideoPack no sustituye un archivo que ya exista con el mismo nombre, y avisa si no hay espacio suficiente en el disco.

Si algo falla, conserva un registro con la configuración y el detalle del proceso para poder diagnosticarlo. Si cancelas, el archivo a medias se elimina.

## Cómo está hecha

VideoPack está escrita en C# sobre tecnologías de código abierto. La interfaz es propia: no incorpora librerías de controles de terceros. El trabajo de vídeo lo hace FFmpeg, ejecutado como un programa aparte.

### Interfaz

La aplicación usa [.NET 8](https://dotnet.microsoft.com/) y [WPF](https://github.com/dotnet/wpf) (Windows Presentation Foundation), la capa de ventanas de .NET para Windows. .NET se publica bajo licencia MIT.

La ventana, los temas claro y oscuro, y el comportamiento de la pantalla están definidos en XAML dentro del proyecto. El estado de la pantalla (vídeo elegido, formato, mosca, progreso) vive en un view model, y la ventana solo lo muestra. La barra de título oscura usa la API de Windows correspondiente cuando el tema oscuro está activo.

### Motor de vídeo

[FFmpeg](https://ffmpeg.org/) y su herramienta de análisis FFprobe hacen la lectura y la codificación. FFmpeg es software libre bajo la licencia GPL v3. VideoPack no incluye esos ejecutables en el código: la primera preparación descarga la compilación *essentials* de [Gyan.dev](https://www.gyan.dev/ffmpeg/builds/) y guarda sus avisos de licencia en `data/licenses/`.

Por defecto el vídeo se codifica con **libx264** (H.264) y el audio con **AAC**, ambos disponibles en esa compilación. El flujo de exportación es uno solo:

1. FFprobe lee la duración y el audio de las cortinillas.
2. Se compone una secuencia: cortinilla de entrada, si corresponde, tu vídeo y cortinilla de cierre.
3. Cada pieza se escala al formato elegido, encuadrando o recortando, a los fotogramas por segundo de salida.
4. El logo se superpone únicamente sobre tu vídeo, con un margen de seguridad respecto al borde.
5. El audio pasa a estéreo a 48 kHz. Si una pieza no tiene sonido, se genera silencio de la misma duración para que la unión no se desplace.
6. El resultado se escribe como MP4, preparado para empezar a reproducirse sin esperar a descargar el archivo entero.

La codificación informa del avance. Un archivo temporal recibe el resultado y, al terminar bien, pasa a la carpeta de destino.

### Ajustes y recursos

Los tres niveles de calidad están en `data/presets/presets.json`. Rutas de cortinillas, lista de logos, margen y tama?o de la mosca están en `data/presets/appsettings.json`.

Los originales viven en `resources/bumper/` y `resources/logos/`. Al preparar la aplicación se copian a `data/assets/`, que es la carpeta que VideoPack lee al exportar. Para cambiar una cortinilla o un logo, sustituye el archivo conservando el nombre, o actualiza la ruta en la configuración.

### Pruebas

Las pruebas automáticas usan [xUnit](https://xunit.net/) y el SDK de pruebas de .NET. Cubren las reglas de formato, el comando que se envía a FFmpeg y el comportamiento de la pantalla. Una prueba de exportación real queda reservada a cuando FFmpeg ya está preparado en la máquina.

### Licencias

El código de VideoPack se publica bajo la licencia MIT, en el archivo `LICENSE`.

FFmpeg, y los códecs que trae su compilación *essentials*, se distribuyen bajo GPL v3. Al generar la versión portable, esos programas y su licencia viajan junto a VideoPack, en `data/ffmpeg/` y `data/licenses/`. VideoPack los invoca como programas externos.

### Ponerla en marcha

Hace falta Windows de 64 bits y el SDK de .NET 8 o posterior. Con eso, `.\run-dev.ps1` prepara FFmpeg y los recursos, y abre la aplicación. `.\build-portable.ps1` genera la versión portable en `dist/VideoPack/`. Las pruebas se lanzan con `dotnet test tests/VideoPack.Tests/VideoPack.Tests.csproj`.
