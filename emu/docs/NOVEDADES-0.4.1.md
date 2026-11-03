# RetroEmu 0.4.1

## Español

Respuestas de IA mejor comprobadas y más formas de continuar cuando una conexión falla.

Esta entrega reúne las correcciones de Antigravity y la revisión de las integraciones de Claude, Codex, Gemini y Ollama. Conserva las interfaces, colores, idiomas, partidas y notas de 0.4.0.

**Novedades** aparece en el primer inicio y después de actualizar, en tu idioma. Al venir de 0.4.0 muestra estos cambios; si salteaste versiones, reúne también las novedades intermedias. El historial sigue disponible desde Ayuda o la terminal.

- **Corrección · Antigravity recibe la consigna completa.** Mejora el envío de consignas largas, acentos y saltos de línea. Si una acción necesita permiso, el aviso explica cómo revisarla o continuar copiando y pegando, sin habilitar permisos generales.
- **Corrección · Una respuesta completa antes de continuar.** Las integraciones de Claude, Codex, Antigravity, Gemini y Ollama comprueban que la respuesta haya terminado. Un avance, un texto cortado o una salida dañada no se aceptan como resultado; tampoco si la herramienta cerró la entrada antes de recibir todo el pedido.
- **Corrección · Esperas y cancelación más fiables.** El plazo cubre tanto el envío como la lectura de la respuesta. Una conexión cortada o una salida demasiado grande muestran un aviso para que puedas reintentar o usar el modo manual.
- **Mejora · Capturas cuando la integración las admite.** Los asesores de juego, variables y experimentos adjuntan capturas solo si la integración declara admitirlas. Las conexiones de texto pueden continuar con la información escrita.
- **Corrección · El taller espera mientras el agente guarda.** Si el agente mantiene ocupado el archivo de código mientras escribe, el taller espera una lectura válida y conserva la versión anterior, en lugar de interrumpirse por ese bloqueo temporal.
- **Mejora · Avisos y consumo más claros.** Los avisos distinguen mejor permisos, acceso, cuota y otros errores, en español e inglés. Cuando un pedido falla sin informar consumo, no se muestran los contadores de la respuesta anterior como si fueran de ese intento.
- **Corrección · Novedades legibles con texto grande.** En ventanas pequeñas, el cartel aprovecha más espacio para que puedas leer y desplazar las novedades con texto grande, manteniendo accesibles el idioma, el historial y el cierre.

### Para empezar

- **Windows x64:** extraé el ZIP y abrí `RetroEmu.exe`. Requiere Windows 10 o posterior y OpenGL 3.3. Incluye .NET; jugar no requiere SDK ni herramientas de IA.
- **Linux x64:** extraé el TAR.GZ y ejecutá `sh RetroEmu.sh --check`. Requiere glibc 2.38 o posterior, X11/XWayland, OpenGL 3.3 y las bibliotecas del sistema indicadas por el comprobador.
- **Actualizar:** Ayuda > Actualizaciones. Se conserva la misma carpeta con tus juegos, firmware, proyectos y ajustes. Si la interfaz falla, Windows ofrece `RetroEmu.exe --repair`.
- **IA opcional:** las herramientas se configuran por separado; copiar y pegar sigue disponible. Esta actualización no habilita permisos generales ni instala proveedores automáticamente.

### Alcance de la revisión

Las correcciones se comprobaron con respuestas HTTP simuladas y procesos propios con canales reales. No se ejecutaron generaciones reales para certificar cada cuenta, modelo o proveedor. La cuota, conectividad, disponibilidad y calidad de una respuesta externa siguen dependiendo del servicio. Linux gráfico, Windows 10, dispositivos físicos y la PC que presentó avisos de audio necesitan sus propias pruebas.

La [matriz de integraciones](INTEGRACIONES-IA.md) explica la cobertura y los límites. Las [novedades de 0.4.0](NOVEDADES-0.4.0.md) describen las funciones que esta entrega conserva. Los juegos se agregan por separado; no se promete compatibilidad universal.

## English

Better-checked AI answers and more ways to continue when a connection fails.

This release brings together the Antigravity fixes and the review of Claude, Codex, Gemini and Ollama integrations. It keeps the interfaces, colours, languages, saves and notes from 0.4.0.

**What's new** appears on first launch and after updates, in your selected language. Updating from 0.4.0 shows these changes; skipping versions also collects the intermediate release notes. The history remains available from Help or the terminal.

- **Fix · Antigravity receives the complete prompt.** Long prompts, accents and line breaks are sent more reliably. If an action needs permission, the message explains how to review it or continue by copying and pasting, without enabling blanket permissions.
- **Fix · A complete answer before continuing.** Claude, Codex, Antigravity, Gemini and Ollama integrations check that the answer has finished. Progress, cut-off text or damaged output is not accepted as the result, nor is an answer from a tool that closed its input before receiving the whole request.
- **Fix · More reliable time limits and cancellation.** Time limits cover both sending the request and reading the answer. A broken connection or oversized output shows a message so you can retry or use manual mode.
- **Improvement · Screenshots when the integration supports them.** Play, variable and experiment advisors attach screenshots only when the integration declares support for them. Text connections can continue using the written information.
- **Fix · The workshop waits while the agent saves.** If an agent keeps the source file busy while writing, the workshop waits for a valid read and preserves the previous version instead of failing on that temporary lock.
- **Improvement · Clearer messages and usage reports.** Messages distinguish permissions, sign-in, quota and other errors more clearly, in Spanish and English. When a request fails without reporting usage, the previous answer's counters are not shown as belonging to that attempt.
- **Fix · Readable news with large text.** In small windows, the panel uses more space so you can read and scroll the news with large text, while keeping language, history and close controls accessible.

### Getting started

- **Windows x64:** extract the ZIP and open `RetroEmu.exe`. Requires Windows 10 or later and OpenGL 3.3. .NET is included; playing needs no SDK or AI tools.
- **Linux x64:** extract the TAR.GZ and run `sh RetroEmu.sh --check`. Requires glibc 2.38 or later, X11/XWayland, OpenGL 3.3 and the system libraries listed by the checker.
- **Updating:** Help > Updates. Your installation folder, games, firmware, projects and settings are preserved. If the main interface fails, Windows offers `RetroEmu.exe --repair`.
- **Optional AI:** configure tools separately; copying and pasting remains available. This update does not enable blanket permissions or automatically install providers.

### Scope of verification

The fixes were checked with simulated HTTP responses and own test processes using real pipes. No real AI generation was run to certify every account, model or provider. Quota, connectivity, availability and answer quality still depend on the external service. Linux desktop, Windows 10, physical devices and the PC that showed audio warnings need their own checks.

The [English user guide](USER-GUIDE-EN.md) covers these limits. [What was added in 0.4.0](NOVEDADES-0.4.0.md#english) describes the features retained by this release. Add games separately; universal compatibility is not claimed.
