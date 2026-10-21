# Primera prueba del tutorial local

El modo experimental implementa el tramo observado en el cliente `1.32.030.345319`: playa,
«Primeros pasos», Artesano herido, entrega, aceptación de «La información es la clave» y ascenso
hasta que aparece «Busca el barco». Ese objetivo es el límite de esta entrega: todavía no se completa.

## Iniciar

Cerrar el otro emulador antes de iniciar este modo: utiliza los mismos puertos locales 5055, 5056 y 5057.
Desde la raíz del clon, con `lab.local.json` apuntando a los datos de la instalación:

```powershell
./scripts/lab.ps1 -Mode build -ArtifactsPath data/lab/onboarding-build
./scripts/lab.ps1 -Mode server -OpeningTutorial -ArtifactsPath data/lab/onboarding-build -Trace
```

El script activa `Tutorial:Opening=true`, usa el preset `official` y guarda exclusivamente en
`data/lab/tutorial-server/albionlocal.db`. El registro es `data/lab/tutorial-server/server.log`.
La ruta habitual `data/lab/server` no se abre con este comando. No borrar ni copiar sus guardados para
esta prueba. Una ejecución directa con otra ruta de guardados sí puede migrar la base indicada.

La base nueva usa el esquema **v31**. La migración añade el avance del tutorial y conserva los personajes
anteriores con avance nulo. No se debe abrir una base ya migrada con un binario que solo soporte v30 o anterior.

Abrir el cliente con el lanzador local ya comprobado (`scripts/start-client.ps1`; entorno `dev`).
Usar credenciales de laboratorio. Crear un personaje nuevo: el personaje automático LocalHero, si aparece,
conserva el inicio normal del laboratorio y no sirve para comprobar este recorrido.

Para salir, cerrar el juego y detener el servidor con Ctrl+C para completar el guardado.

## Recorrido esperado

1. El personaje nuevo tiene The Lighthouse en la ficha y 0s. Aparece en el `startpoint` de 1008,
   con chaqueta y zapatos T1 de mercenario, habilidades elegidas por el sistema existente, sin arma,
   plata ni materiales del kit de laboratorio.
2. El cliente recibe el estado inicial y el seguimiento de «Primeros pasos». Caminar por los dos
   puntos de salida y permanecer un segundo dentro del mirador habilita la tarea de hablar con el sobreviviente.
3. Acercarse al Artesano herido no completa la conversación. Hablar marca el objetivo como cumplido;
   «Completar» entrega la misión. No se inventan recompensas: ambas definiciones locales tienen premios vacíos.
4. El mismo NPC ofrece «La información es la clave». «Aceptar» la activa; seguir los puntos de las
   escaleras lleva a la tarea «Busca el barco». Llegar al círculo de esa tarea no avanza más esta versión.
5. Salir y volver a entrar debe conservar la etapa. Una permanencia a medias en el mirador empieza
   de nuevo al reconectar; las horas desconectado no cuentan. Entregar o aceptar de nuevo no duplica acciones.

## Qué falta comprobar o implementar

Las pruebas automatizadas comprueban sesiones, guardados, referencias y transiciones; **no sustituyen la prueba
visual con el juego**. Confirmar textos, círculos, indicadores del NPC, apertura/cierre del diálogo,
contador y restricciones de botones. Si algo difiere, anotar la última acción y el estado visible;
el registro del servidor incluye cada transición con la etiqueta `tutorial`.

La animación de aparecer tumbado todavía no está implementada. No se emite un estado de inconsciencia
sin verificar su semántica, pues podría bloquear el movimiento. Los detalles de cámara y la aparición
temporal de texto quedan pendientes de contraste visual. El modo se prepara para una prueba local individual;
el mapa 1008 es compartido por las conexiones de esta instancia, no una isla privada por personaje.

El servidor valida los objetivos a partir del movimiento aceptado por el emulador y de la interacción con
el NPC correcto a un máximo de siete unidades (límite del laboratorio, no medida del servidor original).
No añade un sistema nuevo de detección de trampas o validación completa de navegación.

La lógica deliberadamente no ejecuta las tareas posteriores de la segunda misión, el resto de la isla,
omitir tutorial ni recompensas futuras. Los personajes que ya existían no se trasladan automáticamente.
