# Prioridad: experiencia inicial fiel al cliente

Análisis del 21 de octubre de 2026 sobre cliente Windows `1.32.030.345319` y servidor `693d55f`.
Objetivo solicitado: reproducir el recorrido inicial del juego, empezando por el tutorial y sus misiones.
La fidelidad se comprobará por comportamiento y versión; no se deducirá de poder entrar al mundo.

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
La prueba de reabrir el proceso queda a cargo del usuario según lo acordado; no bloquea el análisis del tutorial.
