# Interfaz y herramientas

La biblioteca reúne originales, favoritos, recientes, fusiones y creaciones. **Abrir
archivo** y arrastrar un archivo siguen siendo las formas directas de empezar. Las
tarjetas por sistema muestran los formatos disponibles; reconocer un formato no
certifica que un juego llegue a una partida controlable.

## Encontrar una acción

**Herramientas > Buscar acción** encuentra opciones por nombre o palabras relacionadas:
volumen, guardar, código, crear, pantalla o IA. También está en la barra lateral de la
biblioteca. El atajo es Ctrl+P, o Ctrl+Shift+P en máquinas que reservan esas teclas para
el juego. Flechas y Enter eligen un resultado; Esc cierra. Las acciones no disponibles
explican qué falta. Buscar no inicia tareas de IA ni cambia la partida.

El menú completo se agrupa bajo **Menú** cuando no cabe. Preferencias conserva tamaños
de texto de 100 a 200 %, alto contraste, controles, pausa por foco y ahorro de energía.
Los paneles se ensanchan al aumentar el texto y usan desplazamiento vertical en pantallas
pequeñas. La interfaz usa una fuente de lectura del sistema; código y registros conservan
tipografía de ancho fijo. No se distribuyen fuentes adicionales.

## Crear

El creador separa máquina, idea y método de creación. La plantilla sirve sin IA; la
selección de proveedor, modelo y conexión sigue disponible. El estudio distingue
originales, juego base y aportes. Una vista previa vacía explica dónde se probará el
resultado; no muestra el arranque de una máquina que todavía no contiene un juego.

Las guías compartidas de IA exigen conservar lo que funciona, diseñar transiciones y
respuesta visual, y separar lo observado de lo supuesto. Los robots comprueban parte
del funcionamiento; sus aprobaciones no certifican diversión, fidelidad ni calidad.

## Referencias de rutinas

Al preparar referencias para escribir una fusión, el estudio toma la captura del
arranque y después observa 12.000 pasos en una **máquina separada**. Registra sitios de
escritura y E/S cuando el sistema ofrece esos monitores. Agrupa candidatos mediante las
llamadas observadas; limita raíces, accesos y resultados. Si ve cambiar instrucciones
en una dirección, no publica una agrupación estable para esa muestra.

Las referencias identifican los archivos por SHA-256 y el conjunto por una huella que
incluye perfil y firmware. Incluyen direcciones y frecuencias, sin bytes de instrucciones.
Un acceso de hardware no demuestra el propósito de una rutina. Interrupciones, saltos
indirectos, bancos y código compartido requieren revisión. Sin monitor de lecturas E/S,
la ausencia de lecturas no se interpreta como evidencia de que no existan.

En **Herramientas > Código y rutinas**, **Copiar referencia** identifica la edición del
original cargado, firmware e instantánea de código. En una fusión el visor declara que
no tiene asociada la edición de la ranura; las referencias del proyecto sí la identifican.
Una cita no autoriza copiar código de originales en una creación autónoma ni añadir
direcciones sin comprobar al perfil.

El mismo hallador está en el ejecutor sin ventana:

```powershell
retro-cli run programa.nes --frames 120 --rutinas observaciones.json --pasos-rutinas 12000
```

Admite direcciones de 16 bits. La muestra avanza únicamente la instancia de diagnóstico,
después de los cuadros pedidos; las capturas de `run` corresponden al momento anterior
a esa muestra. `RoutineFinder.Sample` requiere una máquina descartable y no debe usarse
sobre la partida del jugador. No es una certificación de controles ni un análisis completo.

La revisión nativa se ejecuta con `--hidden --mute --autotest`. Ese modo no guarda
posiciones de ventanas. `--ui-size 640x480` permite comprobar el tamaño mínimo; se acepta
solo en esa instancia oculta de prueba. La instalación y las bibliotecas reales no se
reemplazan para estas comprobaciones.
