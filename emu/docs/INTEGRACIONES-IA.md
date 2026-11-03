# Integraciones de IA: comprobaciones y límites

Revisión del 2026-11-03, incorporada en 0.4.1. Estos cambios no están en la entrega 0.4.0.
Las pruebas no generan respuestas con una IA real,
no instalan herramientas ni cambian cuentas, modelos o permisos globales.

## Qué se comprueba

| Integración | Confirmación necesaria para aceptar una respuesta | Evidencia automatizada |
|---|---|---|
| Claude Code | Envelope JSON `type=result`, sin error y resultado no vacío | Proceso local simulado, stdin Unicode largo, permisos, salida inválida, cancelación y plazo |
| Codex CLI | Un solo `turn.completed`, ningún `turn.failed` y archivo final propio legible y no vacío | Proceso local simulado, archivo final ausente/parcial, esquema, modelo/esfuerzo, stdin largo y cancelación |
| Antigravity CLI | Un único resultado final `SUCCESS`; se conserva el transporte stdin stream-json de D-138 | Proceso local simulado, envoltorios personalizados, permisos, progreso, duplicados, esquema y limpieza |
| Gemini CLI anterior | Envelope JSON con `response` no vacía y sin `error` | Proceso local simulado, stdin largo, JSON inválido, errores y plazos |
| Claude API / SDK Anthropic | Mensaje validado por el SDK, `end_turn` o `stop_sequence` y texto no vacío | Transporte HTTP simulado, sin reintentos en la prueba |
| OpenAI Responses API | Respuesta `completed` y mensajes finales del asistente `completed` | Transporte HTTP simulado, estados pendientes/fallidos, parciales, rechazo y esquema |
| Gemini API | Candidato con `finishReason=STOP` y texto útil | Transporte HTTP simulado, final ausente, truncamiento, filtros y esquema |
| Ollama `/api/chat` | `done=true`, motivo final vacío o `stop`, mensaje del asistente y texto no vacío | Transporte HTTP simulado, final parcial, errores HTTP, dirección inválida y uso informado |
| Copiar y pegar / carpeta de agente | Validación local de la respuesta y del proyecto; lectura pendiente si el archivo está bloqueado | Talleres con respuestas propias, recuperación y lectura durante una escritura exclusiva |

Los cuatro transportes HTTP cubren éxito con texto Unicode, JSON dañado, forma incorrecta,
respuesta vacía, estados HTTP 400/401/404/429/503, desconexión, cuerpo cortado, UTF-8 inválido,
cancelación, plazo durante la lectura y tamaño excesivo con o sin longitud declarada.
Un error posterior a un éxito no reutiliza sus contadores. Solo se informa consumo recibido;
un fallo de transporte sin métricas deja ese dato desconocido.

Las pruebas CLI usan procesos propios con pipes reales, sin una cuenta de proveedor. Cubren
el caso en que el padre termina pero un hijo mantiene la salida abierta: el plazo sigue
limitando la espera. Los diagnósticos distinguen permisos y cuota de un error ordinario al
generar. La actividad termina también al fallar o cancelar. Los asesores adjuntan capturas
solo cuando el proveedor declara admitirlas y limpian sus imágenes temporales tras un error.
Un CLI que cierra stdin antes de recibir el pedido completo tampoco puede aprobar su salida,
aunque termine con código cero y escriba un resultado final aparentemente válido.

Los archivos de prueba están en `emu/tests/Fusion.Tests` (`AiBoundaryTests`,
`HttpProviderResilienceTests`, `CliPipeBoundaryTests`, `AdvisorCapabilityTests`, junto con
las pruebas existentes de proveedores/protocolos), `Studio.Tests/AgentWorkspaceTests` y
los recorridos de talleres de `App.Tests`. Los resultados privados de esta campaña quedan
en `emu/out/mantenimiento/t138-r8-ia`; no se suman las repeticiones como casos distintos.

## Cómo se limita un fallo

- HTTP: hasta 16 MiB de cuerpo por respuesta, antes de que el SDK lo interprete; el plazo
  incluye la lectura del cuerpo. Una salida ilegible o incompleta no se aplica como respuesta.
- CLI: hasta 16 Mi caracteres por canal; el archivo final de Codex admite hasta 16 MiB.
  El panel muestra los últimos 12.000 caracteres, no todo el texto retenido durante el pedido.
  El plazo incluye enviar stdin, esperar el proceso y leer ambos canales.
- Se solicita detener el árbol del proceso propio cuando sigue activo. Si el padre ya terminó
  y dejó un hijo separado, cancelar la lectura evita la espera infinita pero no garantiza
  retirar ese proceso huérfano. Si el sistema rechaza detener la herramienta, se avisa.
- Codex recibe `--sandbox read-only`. Antigravity conserva modo plan; Claude conserva sus
  permisos de lectura. Estas opciones no sustituyen las políticas ni el aislamiento del SO.
- Un lanzador Windows `.cmd`/`.bat` rechaza argumentos con comillas, `%`, `!` o caracteres
  de control para evitar expansión accidental. Ante ese aviso, usá el ejecutable directo
  de la herramienta o una ruta/modelo sin esos caracteres. La consigna viaja por stdin.
- Los reintentos propios del SDK de Anthropic se conservan en producción; el transporte de
  prueba los desactiva para observar cada fallo. No se agregan reintentos con permisos mayores.

## Qué sigue sin certificar

No se comprobó una generación real de cada cuenta/modelo ni la calidad del juego que pueda
producir. Una lista de modelos o una ayuda de CLI no confirma acceso, cuota, conectividad ni
compatibilidad futura. El comportamiento depende también de la versión instalada, sus
extensiones y políticas. Un error se puede manejar sin perder la consigna; eso no garantiza
que el servicio externo pueda responder siempre. El modo manual sigue disponible.

Las API actuales de RetroEmu declaran solo texto; se rechaza un adjunto explícito incompatible.
Las CLI declaran imágenes, pero el modelo elegido y sus permisos todavía pueden rechazarlas.
Los resultados de procesos Windows simulados no prueban ejecución real de esas CLI en Linux.

## Contratos oficiales consultados

- [Codex: modo no interactivo](https://developers.openai.com/codex/noninteractive/).
- [OpenAI: salidas estructuradas en Responses](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses).
- [Claude Code: ejecución sin interacción](https://code.claude.com/docs/en/headless).
- [Claude API: motivos de finalización](https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons).
- [Antigravity: protocolo headless](https://antigravity.google/docs/cli/headless/).
- [Gemini CLI: ejecución sin interacción](https://geminicli.com/docs/cli/headless/).
- [Gemini API: generateContent](https://ai.google.dev/api/generate-content).
- [Ollama: chat](https://docs.ollama.com/api/chat).
