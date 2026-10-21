# Conectar una IA

Abrí **Ayuda > Conectar IA**, elegí una herramienta y seguí sus tres pasos.
La misma ayuda está en Fusionar y Crear. RetroEmu detecta las instalaciones habituales;
no hace falta buscar el ejecutable ni escribir el nombre de un modelo.

## Claude Code

1. Abrí PowerShell en Windows y pegá `irm https://claude.ai/install.ps1 | iex`.
   En Linux: `curl -fsSL https://claude.ai/install.sh | bash`.
2. Abrí una terminal nueva, ejecutá `claude` y completá el acceso con tu cuenta de
   Claude. Necesitás un plan que incluya Claude Code.
3. En RetroEmu elegí Claude Code y pulsá **Actualizar modelos**.

[Instalación y acceso oficiales](https://code.claude.com/docs/en/quickstart).

## Codex CLI, con tu cuenta de ChatGPT

1. En PowerShell, ejecutá estas dos líneas:

   ```powershell
   Invoke-WebRequest https://chatgpt.com/codex/install.ps1 -OutFile "$env:TEMP\codex-install.ps1"
   powershell -ExecutionPolicy Bypass -File "$env:TEMP\codex-install.ps1"
   ```

   En Linux: `curl -fsSL https://chatgpt.com/codex/install.sh | sh`.
2. Abrí una terminal nueva, ejecutá `codex login` y elegí iniciar sesión con ChatGPT.
   Completá el acceso en el navegador.
3. En RetroEmu elegí Codex CLI y pulsá **Actualizar modelos**.

[Instalación oficial](https://developers.openai.com/codex/cli/),
[instalador de Windows](https://learn.chatgpt.com/docs/enterprise/windows-deployment#deploy-codex-cli-on-windows)
y [acceso con ChatGPT](https://developers.openai.com/codex/auth/).

## Antigravity CLI

1. En PowerShell: `irm https://antigravity.google/cli/install.ps1 | iex`.
   En Linux: `curl -fsSL https://antigravity.google/cli/install.sh | bash`.
2. Abrí una terminal nueva y ejecutá `agy`. Elegí el acceso con Google y seguí
   las indicaciones del navegador y la terminal.
3. En RetroEmu elegí Antigravity CLI y pulsá **Actualizar modelos**.

Si todavía usás Gemini CLI, podés iniciar sesión ejecutando `gemini` y usar su
modelo predeterminado. El listado integrado de modelos requiere Antigravity CLI.
[Guía oficial](https://codelabs.developers.google.com/antigravity-cli-hands-on).

## Ollama, en tu PC

1. [Descargá Ollama para tu sistema](https://ollama.com/download) y seguí el instalador.
2. Abrilo, elegí un modelo en su selector y descargalo. Para modelos locales no hace
   falta iniciar sesión. Dejá Ollama abierto.
3. En RetroEmu elegí Ollama, pulsá **Actualizar modelos** y seleccioná uno de los
   descargados. La dirección habitual ya está puesta: `http://localhost:11434`.

[Primeros pasos oficiales](https://docs.ollama.com/quickstart).

## Modelos y esfuerzo

La lista se consulta a la herramienta al abrir su configuración y al pulsar
**Actualizar modelos**. El esfuerzo aparece según las opciones publicadas para el
modelo elegido; al cambiar de modelo se deja de aplicar el esfuerzo del anterior.
Antigravity ofrece sus variantes de cada modelo. Ollama publica los modelos instalados,
pero su listado no informa niveles de esfuerzo.

Consultar opciones no genera una respuesta. Tampoco confirma que quede cuota para
crear un juego. Si una versión no publica su catálogo, podés usar su configuración
predeterminada o actualizarla; RetroEmu no completa la lista con nombres supuestos.

Si acabás de instalar una herramienta y no aparece, pulsá **Volver a detectar** o
reiniciá RetroEmu. **Conexión avanzada** permite indicar una ruta especial y hacer un
pedido breve de prueba; ese pedido sí usa los límites de tu cuenta.

Las herramientas se instalan por separado. RetroEmu no ejecuta instaladores ni guarda
tu acceso. Copiar y pegar desde un chat sigue disponible sin instalar ninguna CLI.
