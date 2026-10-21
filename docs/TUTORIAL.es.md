# Prioridad: experiencia inicial fiel al cliente

Análisis del 21 de octubre de 2026 sobre cliente Windows `1.32.030.345319` y servidor `693d55f`.
Objetivo solicitado: reproducir el recorrido inicial del juego, empezando por el tutorial y sus misiones.
La fidelidad se comprobará por comportamiento y versión; no se deducirá de poder entrar al mundo.

## Estado actual: integración experimental del primer tramo

Ya existe una implementación optativa de las transiciones hasta «Busca el barco», el NPC y sus operaciones
de interacción/aceptación/entrega, con avance por personaje guardado en SQLite v31. Se habilita mediante
`Tutorial:Opening=true`; el script de laboratorio ofrece `-OpeningTutorial` con guardados separados.
La [guía de prueba](TUTORIAL-PRUEBA.es.md) describe el arranque, recorrido y limitaciones.
La presentación en el cliente real sigue pendiente de validación; la animación inicial tumbado aún no está implementada.
Las secciones de diagnóstico siguientes conservan los hallazgos previos a esta integración.

## Primera implementación: lector y comprobación de definiciones

Se añadió `TutorialData`: carga los estados en el orden del archivo, asigna identificadores desde cero y
conserva equipo, destinos, indicaciones y recompensas declaradas al omitir. Rechaza identificadores duplicados,
referencias anteriores inexistentes/cíclicas y valores inválidos. Sus resultados son inmutables.
El análisis estático del cargador y receptor del cliente confirmó la numeración desde cero y la búsqueda por índice;
las salidas de análisis permanecen locales e ignoradas. `previousstate` es un vínculo usado por el cliente y no
se interpreta como permiso suficiente para avanzar una misión en el servidor.

La comprobación `--check-tutorial` valida archivos y referencias de mapas, equipo, destinos, viajero e indicaciones.
Se ejecuta antes de abrir puertos, registro en archivo o persistencia. No crea personajes ni concede recompensas.
Con la compilación normal actualizada se ejecuta mediante `./scripts/lab.ps1 -Mode tutorial`.
Para compilar y comprobar mientras otra instancia del emulador está encendida:

```powershell
./scripts/lab.ps1 -Mode build -ArtifactsPath data/lab/onboarding-build
./scripts/lab.ps1 -Mode tutorial -ArtifactsPath data/lab/onboarding-build
./scripts/lab.ps1 -Mode test -Only 'tutorial data:' -ArtifactsPath data/lab/onboarding-build
```

Resultados: cinco pruebas nuevas correctas; solución compilada sin advertencias ni errores; comprobación de datos
con cero fallos. La instalación contiene 51 estados contando inicio y fin, cuatro mapas del recorrido, dos piezas
de equipo inicial y cinco destinos. Estos resultados **no acreditan todavía un tutorial jugable**.
Falta identificar el inicio exacto del personaje, enlazar misiones y acciones a los estados e implementar su ejecución.
El [registro de referencia](TUTORIAL-REFERENCIA.es.md) recoge el recorrido que observará el usuario.

## Primera escena observada y corrección de la ficha

El usuario aportó la primera escena original: The Lighthouse, aparición inicial tumbado (la captura posterior muestra
al personaje de pie), chaqueta y zapatos T1 de mercenario, poderes seleccionados, 0 de plata, indicación «El primer paso»
y botones superiores restringidos a inventario/configuración. La ficha recién creada muestra The Lighthouse y 0s.
El inventario restante sigue pendiente. En una segunda entrega, el usuario observa que al entrar en un
círculo blanco este cambia a verde y desaparece junto con el aviso superior; aparece otro círculo más adelante.
El panel Misiones conserva «Primeros pasos» y «Encuentra un camino para salir de la playa.» en la captura posterior.
Se distingue por tanto la retirada del aviso de la finalización de la misión: no se ha demostrado esta última.
La correspondencia entre círculos, volúmenes de activación y estados del protocolo sigue pendiente de verificar.

Una tercera entrega confirma que al pasar al segundo círculo cambia la tarea de «Primeros pasos» a
«Acercate al mirador. Permanece en el círculo por», con contador visible (capturas de 1 s y 0 s).
También se observa el texto del objetivo en el centro y un círculo amarillo/dorado en el mirador.
El usuario informa de que el texto central desaparece después; posteriormente aparece un signo «?» naranja
en el minimapa para hablar con un NPC aún no identificado. La duración total de permanencia y la condición
exacta que habilita al NPC siguen pendientes. No basta una comprobación instantánea de llegada para modelar
la tarea del mirador: habrá que contrastar también el tiempo dentro de la zona y su posible reinicio al salir.

La cuarta entrega identifica al interlocutor por el encabezado «Artesano herido». La tarea pasa a
«Habla con el sobreviviente. 0/1»: acercarse conserva el círculo, mientras que hablar lo retira y marca 1/1.
El diálogo muestra «Primeros pasos», «Misión terminada» y un botón «Completar» aún disponible.
Se requiere distinguir interacción, objetivo cumplido y entrega; el resultado de pulsar ese botón y una
eventual misión posterior siguen pendientes de observar. No se ha confirmado ninguna recompensa.

En la quinta entrega, el usuario comunica la desaparición de «Primeros pasos» y la aparición de «!»
naranja tras la indicación de completar. La captura posterior muestra al mismo NPC ofreciendo
«La información es la clave», objetivo «Sube a la torre.» y botón «Aceptar». Esto confirma la oferta
de una misión distinta; no su aceptación automática. El seguimiento tras aceptar y la condición de
cumplimiento en la torre siguen pendientes. No se han confirmado recompensas de la entrega anterior.

La sexta entrega confirma la aceptación: aparece el seguimiento de «La información es la clave»,
objetivo «Sube a la torre.» sin marcar y un círculo blanco en las escaleras. El diálogo se cierra
y el indicador del NPC pasa de «!» naranja a «?» gris, también visible en el minimapa.
El comportamiento al llegar al círculo o subir sigue pendiente; el cambio de color se registra
como evidencia visual y todavía no como una regla general para todos los estados de misión.

La séptima entrega documenta otro círculo durante el ascenso y el cambio al llegar al último escalón:
«La información es la clave» conserva su título y sustituye «Sube a la torre.» por «Busca el barco.».
La captura superior muestra un círculo blanco y la vista elevada del naufragio. No hay evidencia aún
de una entrega, recompensa o transición especial de cámara; falta observar la condición del nuevo objetivo.

El análisis del cliente identificó tres errores independientes de las misiones en la ficha del emulador:

- Login 10 y 13 son la ubicación de instancia y el mapa base; se enviaba el GUID del personaje y una cadena vacía.
- Login 34 es duración jugada en **milisegundos**; se enviaban ticks de creación, que desbordaban la conversión del cliente.
- Login 35 es premium restante en **días**; se enviaba una fecha, que también producía duraciones incorrectas.

Se corrigieron las unidades y la ubicación, incluida la respuesta CreateCharacter (campos 1 y 2) para que la ficha
pueda resolver su mapa antes de un nuevo login. La etiqueta la resuelve el cliente desde los identificadores del mapa;
no se fuerza el texto The Lighthouse para personajes que estén en otra zona.

El tiempo conectado se guarda en `Character.PlayedMilliseconds` y en una migración nueva SQLite **v30** (`played_ms`).
Se acumula con el reloj del mundo en las instantáneas y al salir; no cuenta dos veces el mismo intervalo ni suma horas
desconectado. Los guardados anteriores comienzan su contador en cero: su tiempo histórico no se puede reconstruir
fiablemente desde la fecha de creación. Esa fecha se conserva.

Las pruebas usan bases temporales. Antes de abrir una base real con la nueva versión, conservar una copia consistente
con el servidor detenido: las versiones antiguas del servidor rechazan el esquema v30. La compilación de desarrollo
está aislada en `data/lab/onboarding-build`; el servidor ya abierto conserva su versión hasta que se reinicie.

Estas correcciones no activan aún la aparición en la playa, la animación, las restricciones de interfaz ni las misiones.
La ficha requiere validación visual en el cliente; el inicio interactivo seguirá desarrollándose con la referencia observada.

## Diagnóstico comprobado

El inicio actual es una preparación de laboratorio:

- `content/starter.json` lleva al mapa 0000 (Thetford), con equipo T4 y materiales.
- `Progression:NewCharacters` tiene `UnlockAll` como valor predeterminado.
- `FairStart` activa un tablero de progreso y equipo T1, pero su archivo también inicia en Thetford;
  no implementa la isla inicial, sus NPC ni la cadena de misiones.
- `CreateCharacter` usa uno de esos dos kits. El personaje automático LocalHero procede del arranque del servidor.
- La categoría `TUTORIAL` del Diario y sus logros son un sistema diferente del recorrido inicial de la isla.

No encontré implementaciones en el servidor de `SetNextTutorialState` ni de `QuestGiverRequest`,
ni emisiones de `TutorialUpdate`, `NewTutorialBlocker`, `NewQuestGiverObject` o `FullQuestInfo`.
Sí existen sus declaraciones generadas de protocolo: conocer la forma de un mensaje no implementa su lógica.
Las misiones locales de campamentos y las de expediciones tienen implementación, pero no sustituyen esta cadena.

## Datos locales disponibles

Se utilizó el lector GameData existente, sin modificar la instalación ni exportar los datos al repositorio.
El diagnóstico temporal y su salida quedan ignorados bajo `data/lab/TutorialInspection/` y
`data/lab/tutorial-inspection.log`.

- `tutorial.bin`: `startcluster=1008`; grupo de mapas 1009, 1010 y 1011;
  49 elementos `state`, además de un `startstate` y un `endstate`.
- Existen `quests.bin`, `questgiverobjects.bin`, `questgiverreferences.bin` y `questgivertypes.bin`.
  Sus estructuras incluyen tareas, requisitos, acciones previas y recompensas.
- El resolvedor actual cargó 1008, 1009, 1010 y 1011 con cero problemas de resolución.
  Es una comprobación de los datos del mapa, no de su funcionamiento interactivo como tutorial.
- 1008 contiene puntos de interés, recursos, apariciones de enemigos y un punto de reaparición.
  Ese punto de reaparición no demuestra dónde debe aparecer un personaje recién creado.

La documentación pública oficial distingue la salida del tutorial hacia las localidades iniciales y la
experiencia posterior al tutorial: [mundo de Albion](https://albiononline.com/news/guide-world-albion),
[experiencia de nuevos jugadores](https://albiononline.com/news/dev-talk-new-player-experience).
Estos artículos dan contexto; la referencia de implementación será esta versión instalada y la evidencia verificable.

## Brechas y orden de implementación

### Primer tramo solicitado: playa hasta «Busca el barco»

El usuario propone detener aquí la toma de referencias e integrar el recorrido observado por etapas.
No necesita obtener coordenadas manualmente de cada círculo o NPC. La inspección de los datos instalados
identificó el miembro `startpoint`, el interlocutor `WOUNDEDCRAFTER` y los marcadores referenciados por
las misiones `MISSIONS_T2_TUTORIAL_TEST_A` y `MISSIONS_T2_TUTORIAL_TEST_B`. La primera contiene los dos
puntos de salida de la playa, el mirador con `seconds=1` y la interacción con el artesano. La segunda
contiene los marcadores de ascenso y la parte alta. Esta correspondencia es compatible con lo observado;
no equivale todavía a verificar sus mensajes y numeración de tareas en el cliente real.

El resolvedor ya transforma posiciones de plantillas al mapa: movimiento en el plano `(x,z)`, altura `y`
separada y rotación de cada instancia aplicada. No deben usarse píxeles de las capturas ni sumarse de nuevo
el origen o la posición de la plantilla. El radio del marcador describe su área; por sí solo no acredita
que su entrada complete la tarea, como demuestra la conversación con el NPC.

`--check-tutorial -v` ahora muestra punto inicial, interlocutores y marcadores con coordenadas, altura y radio.
Comprueba además un único `startpoint`, la existencia de las dos definiciones iniciales y todas sus referencias
de `locationmarkerid` en el mapa inicial. Conserva todas las posiciones si un identificador tiene varios
marcadores. No abre puertos ni guardados. Los resultados y salidas con datos de la instalación permanecen
ignorados en `data/lab/opening-foundation-*.log`.

Validación de esta ampliación: compilación aislada con cero advertencias y errores, comprobación con cero fallos
y cinco pruebas del lector correctas. Es validación de definiciones, **no un tutorial jugable implementado**.

El alcance del primer tramo interactivo será:

1. Personaje nuevo en el punto inicial de 1008, equipo inicial y aviso de movimiento.
2. Puntos de salida de la playa, retirada del aviso y permanencia en el mirador.
3. Artesano herido: interacción, objetivo cumplido, entrega explícita y siguiente oferta.
4. Aceptación de «La información es la clave», ascenso y cambio a «Busca el barco».
5. Conservación del avance al desconectar y pruebas que impidan saltar pasos o repetir entregas.

La frontera de esta etapa es la aparición del objetivo «Busca el barco», no completar toda la segunda misión:
sus tareas posteriores están en los datos pero todavía no se han contrastado con el recorrido del usuario.
Se desarrollará como opción explícita de laboratorio para personajes nuevos. Los personajes existentes no
se trasladarán ni perderán su equipo. Antes de dar instrucciones de prueba habrá que verificar los mensajes
de misión, NPC y tutorial, su numeración y el inicio/animación; cambiar solo mapa y posición no cierra la etapa.
La colaboración pendiente del usuario será una prueba visual de este mismo tramo cuando esté preparado;
no hacen falta más capturas de fases posteriores para comenzar.

| Etapa | Trabajo necesario | Evidencia para cerrarla |
|---|---|---|
| T01.1 | Leer y validar la definición del tutorial, sus estados y referencias; identificar el punto inicial y la numeración que espera el cliente | Pruebas del lector y de referencias rotas; informe por versión; ninguna numeración inventada |
| T01.2 | Añadir un inicio tutorial para personajes nuevos y sincronizar su primer estado, indicaciones y objetivo | Un personaje nuevo entra en 1008 y ve el primer objetivo correcto en el cliente real |
| T01.3 | Implementar transiciones verificadas por el servidor y persistir el avance | Una acción válida avanza una vez; una petición prematura no salta etapas; reconectar conserva el objetivo |
| T01.4 | Incorporar NPC, aceptación/entrega de misiones, recursos, fabricación y combate requeridos, siguiendo los datos | Cada misión se reproduce y compara por separado, con recompensas correctas y sin duplicación |
| T01.5 | Completar recorrido entre mapas y salida a la localidad elegida; definir el comportamiento de omitir tutorial | Se llega al destino esperado, no se repiten premios y la salida se conserva tras reiniciar |

El primer entregable será **un personaje nuevo en el inicio correcto con su primer objetivo funcional**.
Cambiar únicamente el mapa a 1008 no cumple ese entregable. No activar un tutorial incompleto como inicio
predeterminado ni mover personajes existentes mientras se desarrolla.

## Diseño a respetar

- Leer definiciones desde GameData local; publicar lectores, lógica y pruebas sintéticas, no los archivos del juego.
- Verificar el significado y la numeración de los parámetros del protocolo antes de emitirlos.
  `SetNextTutorialState` contiene un timestamp; su recepción por sí sola no demuestra que el jugador cumplió el objetivo.
- Separar definiciones inmutables del tutorial del avance mutable de cada personaje. Usar evidencia de movimiento,
  inventario, fabricación, interacción y combate para validar las transiciones correspondientes.
- Guardar avance y recompensas relacionadas de forma atómica mediante los contratos de persistencia;
  una migración nueva debe mantener los guardados anteriores y evitar conceder recompensas dos veces.
- Confirmar aislamiento y comportamiento de los mapas para varios jugadores antes de declarar soporte multijugador del tutorial.
- Reutilizar recolección, inventario, fabricación, combate y viajes donde sus reglas coincidan con este recorrido.
  No confundir un sistema ya implementado con uno ya verificado dentro del tutorial.
- El preset `official` puede servir de base para reglas y tasas, pero no añade el tutorial ni demuestra fidelidad completa.

## Pendientes de evidencia

Coordenadas y equipo iniciales exactos; enumeración de estados/hints; campos de login/selección/Join que indiquen
avance; semántica de los diálogos y acciones de NPC; reglas de muerte/reconexión; recompensas y salida/omisión.
Contrastar con los datos, código disponible y una referencia de juego observada; mantener explícitas las hipótesis.

## Persistencia: comprobación independiente

El último cierre visto en `server.log` es 13:04:42: parada del servidor y salida de LocalHero en (-244.26, 160.23).
Eso demuestra que se ejecutó la salida, pero no confirma la recuperación al volver a entrar.
El usuario confirmó después haber probado correctamente creación de cuenta y recuperación de posición y poderes.
Ese resultado se registra como prueba manual comunicada por el usuario; inventario y otros datos no fueron comprobados
en esa prueba. No bloquea el trabajo del tutorial.
