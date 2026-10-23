# Validación del laboratorio — 23 de octubre de 2026

## Recorrido real terminado con Hannah

El usuario confirmó que llegó al final y se contrastó con registros y una consulta SQLite de
solo lectura. El 23 de octubre a las 08:44:05 (hora local), `BuyJourney` aceptó el viaje gratuito
de Mountain Fort (`1010`) al destino inicial `0004`; la llegada real quedó registrada a las
08:45:14. El guardado después de salir a las 08:46:33 contiene la misión Q con
`JourneyPhase=completed`, `TutorialState=50`, el personaje en `0004` y ningún `pending_transfer`.
Tiene 100 puntos de aprendizaje (`learning_points=1000000`, unidad fija de 10000).
Los registros posteriores a la llegada muestran consulta del mercado, reparación, apertura y
cierre del banco y montaje de la mula. Esta evidencia confirma el cierre funcional y persistido
del recorrido de isla jugado por Hannah; no implica validación visual de todas las variantes
ni implementación de las misiones siguientes del continente.

El cliente reprodujo y terminó el vídeo de introducción del diario. No reaparece el aviso HAIR
en esta sesión. Sí se registró a las 08:45:15 `SimpleBehaviourObjectView: state not found:` al
cargar la zona de llegada, sin identificar el objeto en el mensaje. El viaje y las interacciones
citadas continuaron; queda como incidencia pendiente del escenario posterior al tutorial.
No se modificó la partida ni se repitieron pruebas automáticas: la verificación es del intento
real del usuario. Queda por confirmar visualmente una reconexión posterior desde el destino.

## Aviso HAIR en la vista previa de un personaje sin cabello

El último registro del cliente y `ReportClientError` muestran `HAIR: Could not set item: HAIR
to meshid:` al entrar. No aparece una excepción del servidor en ese intento. La misión NC de
Hannah sí se entregó: el guardado ya contiene OB activa. La revisión de solo lectura encontró
el peinado femenino 21 conservado en Hannah y la opción masculina sin cabello en Test1.

`CharacterLooks.SelectionJson` enviaba `MeshId:null` para una entrada de cabello/barba sin
recurso, igual que para una categoría sin lista de mallas. El cliente `cpr.b(string)` intenta
resolver los recursos de cada categoría con mallas; `b3g.o(string)` compara sus identificadores
literalmente. Para la opción sin cabello/barba se envía ahora `MeshId:""`. Las categorías solo
de color, como SKIN, conservan null; no se cambian los índices de creación, los arrays enviados
en Join/NewCharacter ni los datos guardados. El aviso coincide con la opción vacía del preview;
falta confirmar en el cliente real que no vuelva a aparecer.

La nueva regresión usa las dos combinaciones guardadas, añade una barba vacía y comprueba que
los arrays originales y el peinado/color de Hannah no cambien. Falló antes de la corrección
(`hair-preview-before.json`). Pasaron las **7 pruebas de creación/entrada**, sin fallos ni
omisiones (`hair-preview-after.json`). La solución y el binario del lanzador compilaron sin
advertencias ni errores. No se modificó la base de datos.

## Completar la fabricación de espada o bastón ante la capitana

El guardado real de Hannah estaba en NC, tarea 2, `ready`, con `JourneySlot=2` y el bastón de
fuego equipado. El servidor exigía que «Completar» repitiese esa ranura de aceptación. El cliente
345319 no lo hace: `c32.ah(long)` construye `QuestGiverRequest` con NPC y acción 3; los selectores
quedan en cero. Evidencia estática: `data/lab/quest-wire-final/c32.txt`, método `ah(long)`.
Eso bloqueaba NB/NC (ranuras 1/2), mientras que NA (arco, ranura 0) funcionaba.

La entrega exige ahora los selectores nativos en cero. Conserva la comprobación de misión lista,
NPC correcto y distancia; la ranura original sigue seleccionando las ofertas al aceptar y en
los datos de la misión. No se modificó el guardado: el estado `ready` ya existente puede entregarse.

La nueva prueba usa operaciones serializadas reales para aceptar cada una de las tres opciones,
prepara los objetos exigidos y comprueba entrega tras reconectar, rechazo de objetivos incompletos,
NPC incorrecto, selectores no nativos y entrega repetida, así como aceptación de la sucesora.
Falló con la validación anterior (`weapon-complete-before.json`). Las pruebas de recorrido que
antes repetían `JourneySlot` en la entrega se corrigieron para reproducir el botón del cliente.
Pasaron las **13 pruebas de recorrido**, incluidos los tres recorridos completos, sin fallos ni
omisiones (`weapon-complete-after.json`). Solución compilada sin advertencias ni errores.
Queda pendiente confirmar el botón dentro de Albion con Hannah.

## Barra de refinado terminaba antes que el servidor

El usuario aclaró que caminaba después del aviso de finalización. El registro de cancelación por
movimiento no demostraba una interrupción voluntaria: el servidor imponía 0,4 s por receta,
ignorando el tiempo que el cliente usa para su barra. Los 48 tablones T2 se programaban a 19,2 s.
La receta del cliente declara `time="0.02083"`; 48 operaciones duran aproximadamente 0,99984 s.

Se contrastó `cl3.aap` → `ctp.af(bdi,int,int,bool)` → `ctp.af(bdi,int,float)`: tiempo de receta
por número de operaciones y factor `cpo.ayz()`. Este resuelve en `a4f.fg()` a
`max(0.1, 1 - CraftTime)`. Se verificaron también las constantes nativas 1 y 0,1.
Referencias locales: `data/lab/craft-timing-native/` y `craft-times-items.xml`.

`Pricing.CraftDuration` reproduce ese cálculo en float; cada vencimiento se calcula desde el
inicio del lote para no acumular redondeos. El servidor usa la receta y la reducción `crafttime`
de los efectos activos al empezar. Las tiendas y recetas sin tiempo siguen siendo instantáneas.
Antes de cancelar una fabricación se producen las unidades cuyo plazo ya venció, evitando que
un movimiento posterior al final, pero anterior al próximo tick, reembolse un lote ya terminado.
La fama agrupada y los reembolsos de unidades realmente pendientes se conservan.

Las **15 pruebas de economía pasaron**, sin fallos ni omisiones (`craft-timing-economy.json`).
La nueva regresión reproduce los 48 tablones y el movimiento después del vencimiento sin ningún
tick intermedio: entrega 48 tablones, ninguna madera devuelta, un final y una ganancia de fama;
los temporizadores antiguos no duplican nada. También verifica T3, reducción y límite mínimo.
Las pruebas de cancelación real y salida siguen comprobando fabricación parcial y persistencia.
Compilación sin advertencias ni errores. Falta confirmar visualmente la sincronía en Albion.
La batería completa pasó con **423 correctas, ningún fallo y 4 omitidas de 427 seleccionadas**
por las referencias privadas ausentes (`craft-timing-final.json`). No se modificó la base de datos.

## Fama de fabricación y refinado agrupada por lote

El usuario pidió una única entrega de fama por cantidad fabricada. El temporizador llamaba a
`AwardCraftFame` después de cada unidad. Ahora se calcula una vez con el total de unidades
producidas cuando termina el lote. Los objetos y los retornos de materiales mantienen su
entrega progresiva; se agrupan la fama, su notificación y la aplicación al tablero y diarios.
Se usa la fórmula existente con la cantidad total, incluidos `amountcrafted` y los multiplicadores.

La cancelación por movimiento o salida liquida una sola vez la fama de las unidades ya hechas;
una cancelación antes de la primera unidad no da fama. El reembolso de lo no fabricado continúa
igual. Las tiendas y recetas sin fama conservan su exclusión. La comprobación de liquidación
única impide duplicar la ganancia si quedan temporizadores pendientes.

Las dos pruebas específicas pasaron (`craft-batch-target.json`): diez tablas T3 dan una única
ganancia de 120 de fama y una actualización de progreso; una espada mantiene su ganancia
habitual. La nueva prueba cubre cancelar caminando y salir, antes de producir y tras tres
unidades, con reconexión, reembolsos exactos, progreso persistido y temporizadores cancelados.
Compilación sin advertencias ni errores (`craft-batch-build.log`). La comprobación visual de la
notificación agrupada en Albion sigue pendiente. No se modifican partidas existentes.
La batería completa terminó con **422 correctas, ningún fallo y 4 omitidas de 426 seleccionadas**
por las referencias privadas ausentes (`craft-batch-final.json`).

## Entrega de recursos a la capitana: sopas y siguiente misión

### Aviso posterior: el agente no tiene suficiente plata

Test1 también tenía L entregada y las tres sopas. El mensaje corresponde a
`CanAcceptQuestErrorCode.QuestGiverHasNotEnoughSilver` (6), comprobado localmente antes de
enviar la aceptación. `cmh.r(int,bool)` compara el coste de las recompensas con `a5a.aa()`;
`bog.aw(a7m,a7m,bool)` devuelve falta de fondos si el coste supera ese saldo. El saldo del
agente llega en `QuestGiverInfoForPlayer.dr2`, que enviábamos como cero. La siguiente misión
MA/MB/MC recompensa 300 de plata. Evidencia estática: `data/lab/captain-budget-native/`.

Los agentes del tutorial anuncian ahora fondos suficientes para su mayor recompensa de plata
definida en los datos. Es una política del laboratorio privado por personaje, sin agotamiento
compartido; no una implementación de la regeneración económica del servidor original. El
jugador no recibe ese saldo: solo se acredita la recompensa al completar la misión.

La prueba de transición incorpora el saldo enviado, su conservación al reconectar y que aceptar
la nueva misión no aumente la plata del jugador. Falló con saldo cero (`captain-budget-before.json`).
Las **12 pruebas de recorrido pasaron**, incluidos los tres recorridos completos, sin fallos
ni omisiones (`captain-budget-after.json`). La solución compila sin advertencias ni errores.
La capitana declara un máximo de 1000 de plata en el cliente, superior al saldo anunciado de
300, por lo que ese límite no recorta los fondos necesarios. No se modificaron las partidas
de Hannah ni Test1; pueden volver a consultar la oferta con el nuevo servidor.
El binario del lanzador estaba en uso y no pudo sustituirse durante esta sesión: hay que cerrar
el servidor actual y usar el lanzador habitual, que recompila antes de arrancar. Falta confirmar
la aceptación en el cliente real tras ese reinicio.

La revisión de solo lectura del guardado de Hannah encontró la misión L en fase `offered`
y tres `T1_MEAL_SOUP` en el inventario. La entrega sí se confirmó; no se modificó la partida.
Los datos del cliente definen MA/MB/MC como sucesoras ofrecidas por CAPTAIN_TIA.
El servidor espera aceptación explícita; no activa esa misión al entregar L.

La primera prueba confirmó los datos de la oferta, pero no detectaba una identidad reutilizada.
El usuario confirmó que al reabrir seguía viendo las sopas. En el cliente 345319, `cmh.r(ax2)`
compara el GUID entrante con el de la ranura y solo reconstruye la definición si son distintos
(`data/lab/quest-wire-analysis/cmh.txt`). La oferta siguiente reutilizaba el GUID de L.

`TutorialJourney.Offer` deriva ahora un GUID estable por oferta a partir del checkpoint y del
nombre de la misión. Cambia al pasar a otra misión y se conserva al consultar o reconectar;
también funciona con guardados ya entregados, sin migración ni duplicación de recompensas.

La regresión `captain offers camp mission after soup delivery and reconnect` verifica las
tres alternativas: conversación, tres sopas, oferta en ranura cero con identidad distinta de L,
rechazo de entrega repetida, identidad estable al reconectar y aceptación de la misión siguiente.
La nueva condición falló antes del arreglo (`captain-soup-before.json`). Tras el cambio pasaron
las **12 pruebas de recorrido**, incluidos los tres recorridos completos, sin fallos ni omisiones
(`captain-soup-after.json`). Compilación sin advertencias ni errores. Falta la confirmación visual
en Albion; el guardado de Hannah se conserva.

## Aura gris del bosque bloqueaba el ataque básico

El cliente declara `TUTORIAL_MOB_INVISIBILITY` con stun de nueve segundos y protección de larga
duración (87000 segundos), pero cada efecto lleva `expireafterdistance="0.1"`. Las variantes
NODELAY/NOSTUN declaran umbrales distintos para invisibilidad, invencibilidad y bloqueo de ataque.
El motor ignoraba esa condición: terminaba el stun, pero conservaba cannotattack y el aura gris.
Las habilidades de espada seguían permitidas porque cannotattack solo bloquea el ataque básico.

Se registra la posición de aplicación y se desactiva cada subefecto al alcanzar su distancia,
notificando al cliente y recalculando los atributos. Se comprueba en el tick del jugador y antes
de aceptar un ataque. Para los efectos que ya admitían persistencia se conserva también el
origen y su mapa en el JSON existente, sin migración ni reinicio de cuentas.

La regresión usa los hechizos y un zorro reales: verifica bloqueo estando quieto, conservación
a 0,05 metros, retirada tras 0,2 metros y daño de un AttackStart de espada. Comprueba además
que NOSTUN permite atacar tras 0,1 metros mientras conserva las otras protecciones hasta dos.
La prueba específica pasó. La batería completa terminó con **420 correctas, ningún fallo y
4 omitidas de 424 seleccionadas**, por las referencias privadas ausentes. Informes:
`forest-protection-target.json` y `forest-protection-final.json`. Compilación sin advertencias
ni errores. La comprobación visual con el juego sigue pendiente.

## Mercado: recoger una compra individual

La compra de la mula se cobraba y «Tomar todo» la entregaba, pero el clic individual no producía
una petición `AuctionFetchAuction`. El análisis de `MarketPlaceCompleteView.CompleteEntry`
en el cliente 345319 confirmó que compara `BuyerCharacterId` con el personaje local antes de
enviar esa operación. Los DTO de compras pendientes enviaban ambos participantes como null.

Los pendientes privados identifican ahora a su propietario como comprador para la recogida;
las ofertas públicas conservan sus participantes anónimos. No se modifican precios, dinero,
compras existentes ni permisos: el servidor sigue comprobando la propiedad del pendiente.
La prueba del mercado tutorial falló antes del cambio y pasó después: verifica la identidad
necesaria para el botón, recogida individual de la mula, rechazo de una segunda recogida y
compatibilidad con «Tomar todo». **9 pruebas de mercado correctas, sin fallos ni omisiones**,
en `data/lab/market-single-fetch-tests.json`; compilación sin advertencias ni errores.
Queda pendiente la confirmación visual del botón con Albion.

## Puente bloqueado en cuatro bajas: logro simple completado

La partida real de Hannah conservaba E, tarea 3, cuatro bajas y `START` en `Progression.Completed`,
sin entrada en `Levels`. El requisito del tutorial solo consultaba `Levels`: esperaba un nivel
que `DestinyBoard.Apply` elimina precisamente al completar un logro simple. Ahora reconoce los
logros simples completados como nivel uno y sigue comprobando los niveles de las maestrías.
También corrige el requisito posterior de `COMBAT_TRAINEE`.

La nueva regresión falló antes del cambio y pasó después. Usa bajas y fama reales, comprueba
que START desaparece de Levels, reconecta y verifica tanto la continuación normal como un
guardado ya bloqueado con cuatro bajas. Comprueba además COMBAT_TRAINEE tras conceder fama
mediante el sistema de progresión. Los recorridos de integración preparan los logros mediante
`DestinyBoard.SetLevel`, respetando su representación, en lugar de escribir Levels directamente.
Compilación sin advertencias ni errores; **13 pruebas de datos y recorrido correctas, sin fallos**,
en `data/lab/bridge-achievement-tests.json`. No se modificó la partida del usuario: al volver a
entrar se evalúa su logro ya completado y se conservan las bajas.

## Continuación de la isla: comprobación automatizada final

La suite completa final registrada en `data/lab/complete-tutorial-final.json` obtuvo **418 correctas,
0 fallos y 4 omitidas; 422 seleccionadas** el 22 de octubre. Las omisiones corresponden a
comparaciones que requieren referencias privadas ausentes; no equivalen a pruebas correctas.
El registro de ejecución está en `data/lab/complete-tutorial-final.log`. La compilación terminó
con cero advertencias y cero errores.

Esta ejecución incluye los tres recorridos completos de 61 pasos por rama y las cuatro comprobaciones
de obtención, combate, progresión y presupuesto descritas al final del documento. La primera integración
había obtenido **411 correctas, 0 fallos y 4 omitidas; 415 seleccionadas**, conservadas en
`data/lab/complete-tutorial-tests.json` y `.log`. La validación automatizada final tampoco sustituye
la prueba visual con Albion:
la presentación de los diálogos, los indicadores, las animaciones y el recorrido final no se han
confirmado en esta ampliación con el cliente real.

Las comprobaciones utilizan bases temporales del laboratorio. No se han borrado las cuentas ni
el progreso habitual del usuario durante esta ampliación. Las secciones siguientes conservan
el historial de entregas anteriores y sus limitaciones en el momento de cada comprobación.

## Inicio completamente vacío tras el reinicio solicitado

El usuario solicitó retirar todas las cuentas para crear una nueva y recorrer el inicio completo. Con el
servidor detenido se retiraron las bases activas del laboratorio normal y de la comprobación de arranque;
la del modo tutorial aún no existía. Se conserva una copia local ignorada en `data/lab/reset-backups`.
No se borró código, GameData, configuración ni registros. Las bases nuevas se crearán al iniciar cada modo.

Se eliminó la creación automática de LocalHero únicamente para `Tutorial:Opening=true`. Una base vacía
de ese modo no contiene personajes hasta que el usuario crea el suyo. El registro interno reservado
de la cuenta local sigue siendo parte del proveedor; no es una cuenta de usuario registrada.

Compilación sin advertencias ni errores y **6 pruebas del tutorial correctas, 0 fallos**:
`data/lab/clean-start-build.log`, `data/lab/clean-start-tests.json` y `.log`. La prueba nueva construye
el host con una base temporal, comprueba que no crea LocalHero y verifica Login/CreateCharacter como
primer y único personaje con avance inicial. La suite completa inmediatamente anterior se conserva debajo;
no se repitió tras este cambio acotado de arranque.

## Integración optativa del inicio hasta Busca el barco

Se añadió el modo `Tutorial:Opening=true`: creación en el punto inicial resuelto de 1008, dos piezas T1,
tablero propio, NPC y misiones con transición por llegada, permanencia, conversación, entrega y aceptación.
La persistencia usa una propiedad independiente de `Character` y migración SQLite v31; las copias del repositorio
conservan el estado como registro inmutable. Los personajes existentes reciben avance nulo.

La compilación aislada pasó sin advertencias ni errores. **Suite completa final: 384 correctas, 0 fallos,
4 omitidas; 388 seleccionadas**, registrada en `data/lab/opening-full-tests.json` y `.log`.
Las cinco pruebas nuevas cubren creación (también Login/CreateCharacter), equipo y habilidades iniciales,
recorrido, guardas de interacción, entrega/aceptación repetidas, permanencia continua y reconexión,
reapertura SQLite y migración desde v30. Las cuatro omisiones siguen siendo las referencias privadas ausentes.

El primer intento completo en el entorno restringido no pudo completar intercambios de cifrado por UDP;
se interrumpió y repitió con acceso a red local. La repetición detectó una expectativa antigua de 165 manejadores;
se actualizó a 167, incluyendo comprobaciones explícitas de las dos nuevas operaciones y su estado de sesión.
La suite final citada arriba se ejecutó sobre esa corrección y no contiene fallos.

También se comprobó el arranque real del host con puertos aleatorios en loopback y guardados exclusivamente
en `data/lab/opening-startup-smoke`. Inicializó los tres servicios y terminó mediante Ctrl+C, registrando
`stopping`. No se abrió ni migró la base habitual del usuario. El analizador de PowerShell no detectó errores
de sintaxis en el script de laboratorio.

El [procedimiento de prueba](TUTORIAL-PRUEBA.es.md) usa `-OpeningTutorial` y guardados separados en
`data/lab/tutorial-server`. La prueba visual interactiva sigue pendiente: no se declara fidelidad completa.
La animación inicial tumbado aún no se implementó y el mapa de esta entrega es compartido, no una instancia
privada por personaje. La segunda misión se detiene en «Busca el barco», sin completar sus siguientes tareas.

## Primera referencia visual y correcciones de selección

El usuario aportó la escena inicial original de The Lighthouse; está descrita en
[TUTORIAL-REFERENCIA.es.md](TUTORIAL-REFERENCIA.es.md). El análisis estático local confirmó los campos de ubicación
y las unidades de duración en Login y CreateCharacter. Se corrigieron las ubicaciones, el tiempo jugado y los días de
premium de la ficha. El contador de tiempo conectado se añade mediante migración v30 y no modifica la fecha de creación.

La suite completa tras los cambios de ubicación/tiempo/premium obtuvo **378 correctas, 0 fallos, 4 omitidas;
382 seleccionadas**. Después se añadió la ubicación en la respuesta de creación y su caso de regresión; las pruebas
dirigidas se registran en `data/lab/character-entry-tests.json`. Los informes completos están en
`data/lab/character-entry-full.json` y `.log`; compilación en `data/lab/character-entry-build.log`.
La prueba de migración reconstruye un esquema v29 temporal y comprueba preservación de personaje y equipo.
No se ha usado ni migrado la base del usuario para estas comprobaciones.

La aparición inicial, animación, interfaz restringida y avance de misiones continúan pendientes de implementación
y comprobación interactiva. La ficha corregida tampoco se ha confirmado aún visualmente en el cliente real.

## Tutorial: definiciones y referencia manual

En `feature/tutorial-definitions` se añadió un lector inmutable de `tutorial.bin` y la comprobación de solo lectura
`--check-tutorial`. Cinco pruebas nuevas correctas, sin fallos ni omisiones: numeración y ramificaciones,
referencias inválidas/ciclos, parámetros inválidos, aislamiento del XML y referencias de la instalación real.
La solución compila sin advertencias ni errores en `data/lab/onboarding-build`, independiente del servidor encendido.

El comprobador resolvió los cuatro mapas del tutorial y validó equipo, destinos, viajero e indicaciones con cero fallos.
Informes locales: `data/lab/tutorial-data-tests.json`, `data/lab/tutorial-check.log` y `data/lab/onboarding-build.log`.
No se ejecutó de nuevo la suite completa para este cambio de lectura; su resultado anterior se conserva abajo.
La comprobación no acredita ejecución de misiones, inicio interactivo ni recompensas del tutorial.

El usuario confirmó manualmente creación de cuenta, recuperación de posición y poderes. Inventario y otras
partes de la persistencia no se incluyeron en esa prueba. Se preparó una [hoja de referencia](TUTORIAL-REFERENCIA.es.md)
para contrastar los primeros minutos del tutorial original antes de implementar sus condiciones y secuencia.

## Actualización: aporte SQLite aislado y proveedores de persistencia

La rama `fix/sqlite-native-security`, commit `ea91393`, se preparó directamente sobre `upstream/main`
`bdb2c60` en una copia independiente (`../sqlite-security`). Solo cambia el proyecto del servidor y la prueba
de versión SQLite. Compilación sin advertencias ni errores, seis pruebas de persistencia correctas y auditoría
transitiva de la solución sin avisos conocidos. La biblioteca cargada declara SQLite 3.54.0.
La [descripción preparada](SQLITE-CONTRIBUTION.md) aún no se ha publicado al mantenedor.

En la rama de integración se añadió una fábrica inyectable y el contrato completo `IGameRepository`,
con SQLite predeterminado. Los contratos de cada área se separaron de sus archivos de implementación.
El [diseño de persistencia](PERSISTENCIA.es.md) explica el alcance y la etapa posterior de PostgreSQL.

Resultados posteriores al cambio:

- Compilación de la solución: sin advertencias ni errores.
- Suite completa: **370 correctas, 0 fallos, 4 omitidas; 374 seleccionadas**.
- Cuatro pruebas nuevas: cuentas/copias/reapertura, reversión de guardados conjuntos y mercado,
  fábrica suministrada al host y liberación del proveedor tras una importación inicial fallida.
- Bases de pruebas temporales; la base de la sesión real no se utilizó en estas pruebas.
- Informes locales: `data/lab/repository-contracts.json`, `data/lab/repository-full.json` y
  `data/lab/repository-full.log`. Las cuatro omisiones siguen correspondiendo a referencias privadas ausentes.

PostgreSQL no está implementado ni validado. La sesión gráfica observada antes de este cambio conserva su
valor como evidencia de esa versión anterior; falta repetirla y verificar recuperación tras reiniciar con el cambio.

## Validación inicial

Base original: `bdb2c60` de MrGreenCheezz/albion-local-server. La carpeta descargada se comparó con el clon:
no se encontraron diferencias de contenido al normalizar los saltos de línea. La descarga original se conservó.
El fork de trabajo es [JuLiAnChz/albion-local-server](https://github.com/JuLiAnChz/albion-local-server),
rama `educational-lab`; el clon tiene remotos `origin` y `upstream`.

## Entorno y resultados observados

Windows, SDK .NET `10.0.401`, cliente instalado mediante Steam con primera línea de version.txt
`albiononline-win32-full-1.32.030.345319`. Se mantuvieron las versiones originales de paquetes.

| Comprobación | Resultado |
|---|---|
| Compilación de la solución | Correcta, 0 errores y 0 advertencias |
| Batería completa con GameData configurado | 369 seleccionadas: 365 PASS, 0 FAIL, 4 SKIP |
| Laboratorio completo, tras los últimos ajustes de trazas | 9 PASS, 0 FAIL, 0 SKIP |
| Opciones y ocultación de valores de las trazas | 2 PASS |
| Filtro sin coincidencias y argumento vacío | Salida 2 en ambos casos |
| Comprobación de contenido | 1427 de 1427 clusters aprobados en las ocho categorías |
| Arranque del programa de servidor | Lectura de GameData, SQLite esquema 29 y tres puertos en 127.0.0.1 |
| Parada del servidor | Mensaje `stopping` tras Ctrl+C; no se dejó el servidor ejecutándose |

Las cuatro omisiones corresponden a `items.tsv`, `gamedata.json`, `spells.json` y `journal-ids.tsv`.
Son referencias privadas no publicadas, no fallos reparados ni comprobaciones aprobadas. Otras pruebas sí
comprueban las tablas cargadas y su comportamiento. El resultado no implica cobertura completa del juego.

La batería completa incluye las pruebas existentes de login → selección → Join con movimiento y reentrada,
retransmisión con pérdida del 20 %, serialización, cifrado, guardado y persistencia. El ejemplo mínimo de estado
añadido permite seguir bytes → manejador → evento sin necesitar archivos del juego.

La comprobación de contenido valida `load`, `critical`, `exits`, `distribution`, `mobcounts`, `realestates`,
`bounds` y `resolve`. Leyó 1427 clusters y 159078 tiles; terminó en aproximadamente 29 segundos en esta ejecución.
Ese tiempo es orientativo, no una garantía ni una condición nueva de aprobación.

Se verificó el arranque con los seis archivos principales: 12237 elementos, 9326 hechizos y 704 logros cargados.
Los servicios escucharon en UDP `127.0.0.1:5055`, `:5056` y `:5057`. La base y las huellas quedaron en
`data/lab/server/`, excluido de Git.

Los primeros intentos dentro del entorno restringido fallaron al resolver NuGet y al completar UDP local.
La restauración y las pruebas con acceso de red local funcionaron sin cambiar esas implementaciones. El archivo
`data/lab/learn-all.log` se reemplazó con la ejecución final correcta. Los datos de prueba fueron temporales.

## Evidencia local y repetición

Los archivos generados no se publican en el fork:

- `data/lab/test-all.json` y `test-all.log`: batería completa y omisiones individuales.
- `data/lab/learn-all.json` y `learn-all.log`: ejercicios y trazas finales.
- `data/lab/infrastructure.json`: comprobaciones de opciones y ocultación.
- `data/lab/empty.json`: selección sin coincidencias.
- `data/lab/content.log`: comprobación de mapas.
- `data/lab/server/clientdata.json` y `server.log`: versión, huellas y arranque/parada.

```powershell
./scripts/lab.ps1 -Mode build
./scripts/lab.ps1 -Mode learn -Trace
./scripts/lab.ps1 -Mode test
./scripts/lab.ps1 -Mode content
```

La batería completa se ejecutó antes de los últimos ajustes del texto de trazas y su clasificación de eventos.
Después se recompiló y se repitieron los nueve ejercicios afectados, con resultado correcto.
Los comandos siguientes permiten inspeccionar las omisiones y repetir únicamente la persistencia:

```powershell
(Get-Content data/lab/test-all.json -Raw | ConvertFrom-Json).results | Where-Object Status -eq skipped
./scripts/lab.ps1 -Mode test -Only 'persistence:'
```

## Cliente real: sesión local verificada

El 21 de octubre se abrió la instalación Windows `1.32.030.345319` mediante `Albion-Online_BE.exe`, con BattlEye,
usando `0 1 +server 127.0.0.1 +serverenvironment dev`. La línea de argumentos quedó confirmada en el registro
del cliente. `scripts/start-client.ps1` reproduce ese arranque y conserva registros separados en `data/lab/`.
La inspección estática local identificó las entradas Local (USA/Europe/Asia) de `dev`, todas con login en
`127.0.0.1:5055`. No se publicaron archivos del cliente ni salidas de esa inspección.

Evidencia observada en `data/lab/server/server.log` (hora local):

- 12:54:03: conexión real `Albion.LoginServer`, cifrado establecido y operación Login aceptada;
  el cliente declaró `1.32.455 build 345319`. La cuenta de laboratorio heredó un personaje existente.
- 12:54:37: SelectCharacter de LocalHero, redirección a `127.0.0.1:5056` y registro en `Albion.ChatServer`.
- 12:54:41: conexión `Albion.GameServer`, cifrado, Join de LocalHero en cluster 0000, objetos e inventario enviados.
- La interfaz mostró el mapa Thetford y un desplazamiento corto del personaje. El movimiento usa predicción
  del cliente y su manejador es silencioso; la observación visual no demuestra persistencia tras reinicio.

El aviso de disponibilidad siguió mostrando Local (USA) como Desconocido/no disponible, pero no bloqueó
el login ni el acceso al mundo. Queda pendiente corregir o explicar esa comprobación de estado y probar
guardado/recuperación tras reiniciar. La consulta `Albion.StatusServer` de las 12:33 correspondía al cliente
sintético de diagnóstico y no se cuenta como evidencia del juego real.

No se modificaron el launcher, la instalación, hosts ni protecciones. Los intentos con `live` y `local`
no produjeron login local; no deben usarse como procedimiento validado. La compatibilidad con otras versiones
y con sistemas adicionales del juego no se ha demostrado.

## Corrección posterior: dependencia SQLite vulnerable

La validación inicial anterior conservaba los paquetes del proyecto original. Al revisar el aviso del IDE se confirmó
que `Microsoft.Data.Sqlite 10.0.8` resolvía indirectamente `SQLitePCLRaw.lib.e_sqlite3 2.1.11`, afectado por
[CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). La compilación sin advertencias inicial no
era evidencia de una auditoría de seguridad: el script desactivaba NuGetAudit. Ese ajuste se corrigió.

Se mantuvo Microsoft.Data.Sqlite y se añadió una referencia explícita a `SQLitePCLRaw.bundle_e_sqlite3 2.1.14`.
El bundle, core, proveedor y biblioteca nativa resuelven ahora `2.1.14`. La compilación del script activa
`NuGetAudit=true` y `NuGetAuditMode=all`.

Verificaciones posteriores a la corrección:

- Restauración auditada y compilación de toda la solución: correctas, sin advertencias.
- Auditoría NuGet de toda la solución, incluidas dependencias transitivas: ningún aviso conocido reportado.
- Persistencia: 6 PASS, 0 FAIL; la prueba nueva consulta `sqlite_version()` y observó el motor nativo `3.54.0`.
- Mensajes, incluida la cadena login → selección → Join: 3 PASS, 0 FAIL.

Los informes locales son `data/lab/security-audit.json`, `data/lab/persistence-security.json` y
`data/lab/learn-messages.json`. No se repitió toda la batería de 369 pruebas: sus cifras anteriores corresponden
a la instantánea inicial. La nueva prueba de versión aumenta la selección completa a 370.
Un primer intento de la migración de persistencia falló por permisos del entorno restringido para renombrar su
archivo temporal; al repetir con esos permisos, las seis pruebas aprobaron.

## Diagnóstico de ubicación desconocida al crear personaje (2026-10-21)

La instancia observada ejecutaba la DLL habitual anterior a la corrección de entrada, con esquema v29
y guardados en `data/lab/server`. La integración del tutorial se había compilado en otra salida; esa
compilación no actualizaba la DLL habitual. No era una prueba de la integración actual del tutorial.

El script ahora compila el servidor seleccionado antes de iniciarlo, muestra su ruta y avisa cuando se
usa el modo habitual. `-OpeningTutorial` selecciona por defecto la salida aislada de onboarding.
Se conservó la base habitual y se detuvo la instancia antigua después de comprobar su desconexión.

- Cuatro pruebas de entrada y ubicación: PASS, sin fallos ni omisiones; informe local
  `data/lab/location-regression-tests.json`.
- Acceso local `Iniciar emulador tutorial.cmd`: compilación correcta, arranque de los tres puertos,
  base tutorial v31, cero personajes y mapa inicial 1008; detenido después con Ctrl+C.
- Sigue pendiente confirmar visualmente The Lighthouse en la ficha del personaje nuevo con el cliente real.

## Primera prueba visual del tutorial y correcciones (2026-10-21)

El usuario observó círculos que desaparecían, seguimiento estático y ausente hasta pulsar su icono,
falta de texto flotante y conversación bloqueada. El registro del servidor confirma etapas 0 → 1 → 2 → 3,
seguidas de `RegisterToObject`, sin `QuestGiverRequest`: la apertura real del NPC no llegaba al sistema.

Se añadió esa ruta de interacción, conservando la entrega explícita y validando NPC/distancia. Se adelantó
la inicialización de misión a `JoinFinished` y se dejó de reconstruir el modelo completo en cada paso.
La lectura estática de `ct2.i(ax3)` y `QuestTrackerHudGui.OnQuestProgressChanged` confirma que los campos
4/5 de progreso son índices paso/misión para el texto, corregidos a paso actual/0.

Compilación de la solución: cero advertencias y errores. Ocho pruebas de tutorial correctas, incluidas
dos regresiones nuevas para orden de entrada, conservación del modelo, texto e interacción op45;
informe local `data/lab/tutorial-feedback-tests.json`. La primera ampliación de pruebas se interrumpió
tras fallos de comunicación UDP dentro del entorno restringido; se repite fuera de esa restricción.
La repetición completa fuera de esa restricción terminó con **387 PASS, 0 FAIL y 4 SKIP, de 391**;
informe `data/lab/tutorial-feedback-full.json`. Las omisiones corresponden a referencias privadas ausentes.
La animación inicial de levantarse continúa sin implementar: las rutas identificadas de levantarse por
derribo no demuestran el mecanismo de aparición en la playa. Las correcciones de interfaz requieren otra
prueba visual y no se consideran confirmadas únicamente por las pruebas de mensajes.

## Segunda prueba: llegada, panel y entrega (2026-10-21)

El usuario confirmó el funcionamiento de los círculos, pero el panel siguió oculto y «Completar»
inhabilitado. El registro confirma que op45 sí pasó de etapa 3 a 4. La lectura estática de
`QuestDetailsGui` y `QuestEntryBase.CalculateState` identificó la condición adicional: el campo
`dr5` de `QuestGiverInfoForPlayer` (`cmh+0x41`). Se activa exclusivamente en etapa 4 y se limpia al entregar.

El cliente restaura el estado del panel después de entrar. Se vuelve a sincronizar la misión y su
progreso al recibir op515, sustituyendo las sugerencias automáticas del diario general durante el tutorial.
No se modifican las preferencias ni los archivos del cliente. La apertura automática requiere contraste visual.

Se encontró en `spells.bin` el hechizo de sistema `TUTORIAL_STUN`: animación `tutorial_stun`, música
`TUTORIAL_MUSIC_STRANDED`, un único efecto stun de seis segundos. Se emite después de `JoinFinished`,
se publica el efecto con su duración de datos (sin el factor 0.7 del combate) y se guarda `IntroStarted`
en el JSON de avance existente. No cambia el esquema SQLite ni causa daño, muerte, desgaste o recompensas.
El servidor rechaza caminar mientras el efecto está activo y no progresa por círculos durante la llegada.

Compilación sin advertencias ni errores y diez pruebas del tutorial correctas, incluidas duración,
bloqueo de movimiento, no repetición al reconectar, preparación tardía del panel e indicador de entrega;
informe local `data/lab/tutorial-arrival-tests.json`. La validación visual de estos cambios queda pendiente.
La batería completa final, con acceso UDP local, terminó con **389 PASS, 0 FAIL y 4 SKIP, de 393**;
informe `data/lab/tutorial-arrival-full.json`. Las omisiones siguen siendo comparaciones con referencias privadas ausentes.

## Tercera prueba: apariencia y primer diálogo (2026-10-21)

El usuario confirmó la animación, pero observó un aspecto distinto y la necesidad de reabrir el diálogo
para habilitar «Completar». Los registros confirman la entrega y aceptación al reabrir, hasta etapa 8.

`Join` enviaba plantilla/variante predeterminadas y dos arrays de ceros en lugar de los campos guardados
de creación. Se conservan ahora plantilla, variante, personalización y ropa en Join. NewCharacter para
observadores también conserva la variante y deja de excluir plantillas actuales distintas de 0/1.
La lectura estática de `cx2.ai7` confirma campos Join 3/4 y 6/7 hacia `b3d`.

El cliente calcula el botón al abrir el diálogo; la notificación posterior no reconstruye todos sus botones.
El indicador `dr5` ahora anuncia disponibilidad desde etapa 3, sin dar por cumplida la conversación.
La prueba verifica que entregar antes de hablar sigue rechazado. La interacción real op45 cumple la tarea;
la entrega explícita solo se acepta después, en etapa 4.

Compilación sin advertencias ni errores. Once pruebas del tutorial correctas, incluida creación con
plantilla 10 y aspecto no predeterminado, Join, efecto inicial, observadores, guardado y reconexión;
informe `data/lab/tutorial-look-dialogue-tests.json`. Pendiente repetir estos dos casos visualmente.
La batería completa terminó con **390 PASS, 0 FAIL y 4 SKIP, de 394**;
informe `data/lab/tutorial-look-dialogue-full.json`. Las cuatro omisiones corresponden a referencias privadas ausentes.

## Cuarta prueba: nombre repetido, cuerpo y panel automático (2026-10-21)

El usuario confirmó que las misiones y la entrega funcionan como esperaba. Seguían pendientes el
aspecto, el panel derecho y el error específico al intentar crear un nombre ocupado.

La corrección anterior interpretó mal los nombres históricos del modelo persistente: `Template` es
el retrato de creación (`h8`), y `Appearance` es el cuerpo (`h9`). La lectura de CreateCharacterDialog,
`c33.i` y `cx2.ai7` distingue retrato/anillo/cuerpo. Ahora Join envía retrato en 3 y cuerpo en 5;
NewCharacter usa 2/4 y el listado de login 14/17. El anillo sigue en cero; las dos listas de
personalización y colores se conservan. No se cambia el esquema ni los valores almacenados.

CreateCharacter devuelve 482 (`0x1E2`, `NAME_NOT_UNIQUE`) para nombres ocupados y 483 para nombre vacío.
El cliente tiene ramas específicas para esos códigos; 1 disparaba el mensaje genérico de conexión.
La prueba anterior de unicidad entre cuentas se actualizó para comprobar el código específico.

La presentación automática del seguimiento tiene una condición adicional: QuestTrackerHudGui llama
a su notificación de progreso bajo `ciy.bv()`, verdadera para tipos de instancia 5/6. `ciy.a` obtiene
ese tipo mediante `a4l.b(instanceId)`, que reconoce prefijos `@` más el nombre del enum; `1008` es NONE.
Se usa `@TUTORIALSINGLE@<id-personaje>` únicamente como identidad del cliente en este modo. Selección
incluye además mapa base 1008; creación, listado, Join y variables de misión/NPC coinciden en las
identidades correspondientes. El servidor conserva su simulación compartida en 1008: esto no implementa
aislamiento por personaje ni acredita soporte multijugador del tutorial.

Las nuevas pruebas verifican error de duplicado sin reemplazo, persistencia de las elecciones de aspecto,
y coherencia de identidades en creación, selección, login, Join, referencias de misión y reconexión.
La prueba visual sigue pendiente: reiniciar emulador y cliente, intentar nombre repetido y crear otro
personaje con aspecto identificable para comprobar el panel desde el primer objetivo. No se borró la base.

Validación final: compilación con **0 advertencias y 0 errores** (`data/lab/tutorial-feedback-build.log`).
Batería completa con UDP local: **392 PASS, 0 FAIL, 4 SKIP, de 396**, informe
`data/lab/tutorial-feedback-verified-full.json`; las cuatro omisiones son referencias privadas ausentes.
La primera ejecución detectó una aserción antigua que esperaba error 1 para nombre duplicado; tras
actualizarla a 482 se repitió la batería completa. Las pruebas no acreditan todavía la presentación visual.

## Selección de personajes: cuerpo básico sin pelo ni equipo (2026-10-21)

La captura del usuario muestra el defecto en la selección, antes de entrar al mundo. Login enviaba
el retrato y el cuerpo, pero dejaba vacíos el JSON de personalización y los tipos del equipo.
La lectura de `LoginInProcessDialog.CreateCharacterRecord` identifica campo 18 hacia `cpr.b(string)`;
ese método deserializa un diccionario de `ItemSetting` con `MeshId` y `ColorPosition`. `b3g.o(string)`
busca por recurso mesh, no por uniquename ni por índice numérico. `CharacterLooks` conserva ahora la
lista completa de recursos de cada parte y convierte las dos listas guardadas a ese JSON.

La lectura de `CharacterSelectionDialog.Update3dCharacterDisplayForSelectCharacter` confirma el orden
de equipo del registro: cabeza, armadura, capa, mano principal, secundaria, zapatos. Se completan Login
37–42 y CreateCharacter 15–20 a partir del equipo guardado y los índices del catálogo de ítems.
No se cambia apariencia, equipo, progreso ni esquema de la base. No se modifican archivos del cliente.

La prueba nueva recorre creación y nuevo login, comprueba pelo y colores concretos de una mujer,
los seis tipos de equipo en ambos mensajes y los 22 presets masculinos/femeninos del catálogo. Incluye
valores ausentes o índices inválidos de guardados antiguos para que no interrumpan el login.

Compilación con **0 advertencias y 0 errores** (`data/lab/selection-preview-build.log`). Pasaron las
**6 pruebas de entrada y selección** (`selection-preview-tests.json`) y las **12 del tutorial**
(`selection-preview-tutorial-tests.json`), sin fallos ni omisiones en estos grupos. No se repitió
la batería completa tras este cambio acotado; la comprobación visual de selección queda pendiente.

## Regresión de referencias de misión con instancia tutorial (2026-10-21)

El usuario confirmó el modelo de selección, pero perdió misiones y círculos. Su último registro
(`Test`, 22:33) contiene `ArgumentOutOfRangeException` en `MissionVisualizationManager.OnBaseQuestChanged`,
tanto al entrar como al recibir progreso. El servidor sí avanzó etapas 0→3. El alias de instancia había
sido extendido incorrectamente a los datos de la misión; la prueba anterior solo exigía coincidencia
entre mensajes y no comprobaba que esos nombres pudieran resolverse en el catálogo del cliente.

La lectura estática confirma que `ct2.i(apm)` resuelve FullQuestInfo campo 3 mediante `a0.abs` y luego
busca el GUID del NPC en ese mapa; `bop.o8` hace lo mismo para los pares de dynamicquestgiver. Estos
campos deben usar mapa base 1008. Se restauró ese mapa en FullQuestInfo y en las variables de las dos
misiones, incluidas las ofertas. Selección y Join mantienen su identidad de instancia tutorial, la
condición `ciy.bv()` usada por el panel. No se cambiaron cuentas ni progreso persistente.

La prueba ahora diferencia identidad de entrada y referencias de contenido: el alias no existe en
world.bin, mientras cada referencia de misión debe encontrar su mapa y su NPC. Comprueba creación,
oferta de la segunda misión, aceptación y reconexión. Pasaron **12 pruebas del tutorial y 6 de entrada**,
sin fallos ni omisiones; compilación con **0 advertencias y 0 errores**. Informes locales:
`tutorial-reference-repair-tests.json`, `tutorial-reference-entry-tests.json` y `tutorial-reference-repair-build.log`.
Falta repetir la prueba visual, especialmente la apertura automática del panel. El personaje Test puede
retomar «Habla con el sobreviviente»; no es necesario borrar la base ni recrearlo.

## Panel todavía cerrado: preferencia del perfil activo (2026-10-21)

Tras la prueba limpia con Barbaro, el registro del servidor no contiene errores del cliente al
construir misiones. La revisión del perfil local activo encontró `Values.TrackerCollapse=true`.
El código nativo de `glc.e()` consulta esa opción junto al estado de introducción; los controles
CombinedTrackerHudEntryGui/CombinedTrackerInteractions la usan para resolver la presentación
contraída. La identificación de tutorial por sí sola no cambia esta preferencia.

Con el juego cerrado se cambió exclusivamente `TrackerCollapse` de true a false en el perfil activo,
mediante sustitución del valor, conservando el resto del documento. Copia previa ignorada por Git:
`data/lab/client-profile-backups/tracker-20261021-232209.profile`. Se verificó el valor escrito.
Esta intervención cambia una preferencia del perfil de usuario, también usada en servidores oficiales;
no cambia archivos de instalación, ejecutables, misiones, personajes ni base de datos. La explicación
anterior que atribuía toda la apertura a los mensajes del servidor era incompleta.

El 22 de octubre de 2026 el usuario confirmó, tras reiniciar ambos y entrar con Barbaro:
«Ahora aparece el recuadro», como respuesta a la comprobación de apertura sin pulsar el icono.
Queda validada visualmente la apertura automática del panel con `TrackerCollapse=false` en este perfil.
Esta confirmación no acredita el resto del tutorial aún no implementado ni otras preferencias/perfiles.

## Continuación de la torre hasta su entrega (2026-10-22)

Se leyó completa `MISSIONS_T2_TUTORIAL_TEST_B` en quests.bin: siete pasos, no solo los tres iniciales.
Después de LIGHTHOUSETOP siguen HERETICS, LIGHTHOUSEDOWN, una alternativa entre `usespell
spellnamepart="DODGE"` y llegar a LIGHTHOUSEUP, y la conversación con el artesano. La misión siguiente
es `MISSIONS_T2_TUTORIAL_TEST_C`, recolección de seis piedras y seis maderas; queda fuera de esta entrega.

El lector exige los siete pasos y valida que la alternativa contenga exactamente esas dos condiciones.
Se conservan los identificadores de etapas ya guardadas: 8 sigue siendo «Busca el barco». Nuevas etapas:
9 herejes, 10 descenso, 11 esquivar o caminar, 12 hablar con el artesano, 13 listo para entregar y 14
entregado (frontera actual). La progresión y el identificador de misión existentes se conservan; no
cambia el esquema SQLite v31 ni se borran cuentas. Los estados USESPELL y HARDCLEARPREDB se toman
de las preacciones de la definición. Los textos y marcadores los representa el cliente.

La alternativa de habilidad se notifica desde `SpellCasts.Finish`, después de validar y finalizar el
lanzamiento normal. Un intento rechazado por enfriamiento no avanza. La alternativa de caminar usa
el radio y posición del marcador resuelto, como el resto del recorrido. La lectura de cb0.wr/cbq.n9
confirma progreso OR como contador/objetivo (`[[0,1]]` pendiente), no listas anidadas de sus hijos.

Hablar y entregar siguen siendo acciones distintas. El indicador de entrega se anuncia antes del
primer clic, como en la primera misión. La definición mantiene premios vacíos: no se inventan pagos.
Entregar la torre limpia la misión; reconectar no la vuelve a activar. No se ofrece la tercera misión
mientras no esté implementada. La guía de prueba refleja esta nueva frontera.

La compilación se hizo en `data/lab/tower-build` porque el usuario mantenía abierto el emulador con
los binarios de onboarding-build. El lanzador habitual recompila al reiniciar el servidor. Compilación
correcta con **0 advertencias y 0 errores** tras restaurar dependencias con auditoría disponible.
Pasaron **14 pruebas del tutorial**, incluidas continuación de un guardado en etapa 8, ruta completa,
esquiva validada, rechazo por enfriamiento, entrega pendiente tras reconectar y entrega no repetible.
Informes locales: `tower-continuation-build.log`, `tower-continuation-tests.json`. Falta comprobar
visualmente este nuevo tramo con el juego; no se considera demostrada su fidelidad solo por estas pruebas.

La batería completa final terminó con **395 PASS, 0 FAIL y 4 SKIP, de 399**, con acceso UDP local;
informe `data/lab/tower-continuation-full.json`. Las cuatro omisiones siguen correspondiendo a
referencias privadas ausentes. El primer intento de compilación no pudo reemplazar los binarios en
uso por el emulador; se resolvió con la salida separada indicada, sin detener la sesión del usuario.

## Escenario privado del tutorial (2026-10-22)

El usuario confirmó visualmente el recorrido hasta la entrega de «La información es la clave».
Después detectó que el cofre destruido por Barbaro no aparecía al entrar con Test2 sin reiniciar.
El servidor todavía reutilizaba una sola simulación del mapa 1008 para todos los personajes.

Join selecciona ahora una copia por GUID de personaje, con la misma identidad `TUTORIALSINGLE`
en cliente y servidor. El mapa base y las variables de misión conservan 1008; no cambian los guardados
ni las referencias que el cliente busca en su catálogo. Cada copia tiene sus propios objetos, vidas,
recursos, temporizadores y visibilidad. La descarga de una copia elimina únicamente su registro.
La comprobación de vigencia de santuarios utiliza también la identidad de instancia.

El usuario corrigió expresamente la condición del cofre: en el original basta llegar al círculo.
Se conserva `gotolocation` para HERETICS; no se añadió ninguna condición de destrucción.
El progreso de misiones persiste; la destrucción de objetos sigue siendo estado del mapa en memoria
y se reconstruye tras descargarlo o reiniciar. No se borró la base de datos.

Compilación: **0 advertencias, 0 errores**. Las **15 pruebas de apertura pasaron**. La nueva prueba
conecta dos personajes simultáneamente, comprueba objetos e identificadores separados, ausencia de
eventos NewCharacter entre ellos, rechazo de un ataque dirigido al cofre ajeno, destrucción sin afectar
el cofre ni los eventos de la otra copia, reconexión a la copia propia, descarga y reconstrucción aisladas.
La ruta existente sigue verificando que el círculo permite avanzar sin destruir el cofre.
Informes locales: `data/lab/tutorial-isolation-build.log`, `data/lab/tutorial-isolation-tests.json`.
La batería completa terminó con **396 PASS, 0 FAIL y 4 SKIP, de 400 pruebas**;
informe `data/lab/tutorial-isolation-full.json`. Las omisiones son las referencias privadas ausentes.
Queda pendiente la comprobación visual de este aislamiento con dos personajes en el juego.

## Primera misión de recolección (2026-10-22)

Se añadió `MISSIONS_T2_TUTORIAL_TEST_C`, leída de los datos locales: acercamiento a T1_RESOURCES,
6 T1_ROCK y 6 T1_WOOD en paralelo, conversación con el artesano y entrega explícita. La definición
tiene recompensas vacías y no declara consumo de materiales; se conservan los recursos sin añadir plata.
El objetivo anterior del cofre continúa siendo exclusivamente de ubicación, por confirmación del usuario.

La etapa 14 existente sigue significando torre entregada y ahora ofrece la nueva misión. Se agregan
15 (acercamiento), 16 (materiales), 17 (conversación), 18 (entrega pendiente) y 19 (recolección entregada).
No hay migración de esquema ni reinicio de cuentas. Se mantiene el aislamiento de escenario por personaje.

El lector admite objetivos acquireitem paralelos, conserva su orden y valida la cadena y cantidades.
El servidor calcula los contadores desde las pilas llevadas en el inventario, incluidos materiales
previos, sin aceptar contadores del cliente. Mientras el paso está pendiente, retirar recursos reduce
el contador. Cuando ambos objetivos se satisfacen se conserva el checkpoint de conversación.
Los mensajes usan un estado por misión (`[[piedras],[troncos]]`) y solo se reenvían si cambia el inventario
o se restaura el seguimiento; no se emite FullQuestInfo de nuevo en cada avance de contador.

La nueva prueba recorre las operaciones reales HarvestStart y su finalización: petición inválida sin
avance, piedra sin madera, contadores sin emisiones repetidas, reconexión parcial, lectura desde una
segunda conexión SQLite de checkpoint e inventario, retirada/restauración de recursos, conversación,
entrega tras reconectar, materiales conservados y rechazo de segunda entrega o misión no implementada.
La primera ejecución dirigida pasó las 16 pruebas de apertura. Compilación final: 0 advertencias y
0 errores, en `data/lab/gathering-build.log`. La batería final se guarda en `data/lab/gathering-full.json`.
Resultado final: **397 PASS, 0 FAIL y 4 SKIP, de 401 pruebas**. Las cuatro omisiones corresponden
a las referencias privadas ausentes; la prueba de recolección con comprobación SQLite pasó también.
Queda pendiente contrastar visualmente esta misión y sus indicaciones con el juego.

## Primera forja, espada y escudo (2026-10-22)

El usuario confirmó visualmente la primera recolección y pidió continuar, señalando que la forja
provisional del original no permitía interactuar al llegar. La misión local siguiente,
`MISSIONS_T2_TUTORIAL_TEST_D`, contiene el desbloqueo `BLOCKFORGE=0` al aceptarse. Sus pasos son
interactuar con T1_FORGE_TUTORIAL, adquirir T1_MAIN_SWORD, abrir inventario, equipar la espada,
seleccionar mainhand1 y hablar con el artesano. La recompensa declarada es un T1_OFF_SHIELD.

Se conserva la etapa 19 existente como recolección entregada, ahora con oferta de fabricación.
Se añaden 20 (interacción), 21 (espada), 22 (inventario), 23 (equipar), 24 (selección de habilidad),
25 (conversación), 26 (entrega pendiente) y 27 (entregada). No cambia el esquema ni se borran cuentas.

La forja anuncia permisos sin acceso antes de la aceptación y los actualiza al aceptarse. El servidor
rechaza también registros y acciones de fabricación adelantados. La interacción se valida contra la
forja de la propia instancia y su distancia, no contra el círculo. No se añadió una representación
general de los objetos físicos TutorialBlocker ni de todas las acciones applybuff de quests;
el bloqueo de esta estación se implementa mediante permisos y comprobaciones del servidor.
Su apariencia y apertura exactas quedan pendientes de contraste con el juego.

La espada se fabrica con el sistema de estaciones existente, su receta y sus recursos. acquireitem
cuenta la espada realmente presente; la petición de fabricación sola no completa nada. La primera
espada creada durante ese objetivo deja las habilidades activas sin seleccionar. OpenInventory se
recibe mediante UseFunction=16, y la selección de habilidad cuenta solo tras validar op270 para la
primera ranura activa de la espada equipada. UseFunction deja de ser stub e ignora sin respuesta
las funciones que este tramo no utiliza, como antes.

El escudo necesita espacio en la bolsa; de lo contrario la entrega queda pendiente. La recompensa y
el checkpoint entregado se guardan en el mismo snapshot del personaje mediante SaveNow. Reconectar
no vuelve a concederlo. El lector valida la recompensa y la cadena; no se publican datos propietarios.
Cuando hay dos settutorialstate consecutivos se envían en su orden, conservando el último al reconectar.

Pasaron las **17 pruebas del tutorial**: incluyen registro real en la forja, fabricación real, movimiento
de inventario, apertura mediante UseFunction, habilidad inválida rechazada, habilidad válida, bolsa llena,
reconexión y una sola recompensa. Las pruebas globales de registro de opcodes se actualizaron para
contar el manejador nuevo y comprobar que UseFunction sigue sin emitir OperationResponse fuera del
tutorial. Informes locales: `first-craft-build.log`, `first-craft-tests.json`, `first-craft-full.json`.
La misión de combate siguiente aún no se ofrece. Falta la prueba visual del tramo de fabricación.
Compilación final: **0 advertencias y 0 errores**. Batería completa final: **398 PASS, 0 FAIL y
4 SKIP, de 402 pruebas**. Las omisiones siguen siendo las referencias privadas ausentes.

## Cierre de la indicación de recolección (2026-10-22)

El usuario señaló que el aviso superior permanecía tras entregar la recolección. La transición
18→19 conservaba FIRSTGATHER y lo reenviaba. Ahora guarda y emite HARDCLEARPREDB, cuyo triggerhint
es CLEAR. La aproximación a la forja conserva ese estado limpio; interactuar activa FIRSTCRAFT.
Al reconectar se reparan guardados de etapas 19/20 que aún conservan FIRSTGATHER, sin cambiar la
etapa, objetos o misiones completadas. No se borran cuentas ni se requiere repetir la recolección.

Compilación con 0 advertencias y 0 errores. **18 pruebas del tutorial pasaron**, incluida una regresión
que verifica cierre inmediato, reconexión, reparación de guardados en ambas etapas y activación de la
indicación de fabricación. Informes locales: `gathering-hint-build.log`, `gathering-hint-tests.json`.
Queda pendiente la confirmación visual del cierre con el cliente.

## Recorridos completos de la continuación de la isla (2026-10-22)

Se agregaron tres recorridos de integración desde la entrega de la primera espada hasta la salida
de la isla: campamento A y arco, campamento B y espada, campamento C y bastón de fuego. Cada ruta
correlaciona FullQuestInfo con QuestProgressInfo a través de todas sus reconexiones y comprueba
los **61 pasos declarados** de esa rama. No escribe JourneyTask para avanzar: las condiciones deben
ser evaluadas por el sistema de misiones. Las ofertas usan la ranura de NPC correspondiente;
un selector de recompensa distinto de cero o una entrega dirigida a otra ranura se rechazan.

Las compras, recogidas, conversaciones, canjes de materiales, transiciones entre mapas y salida
mediante BuyJourney usan sus manejadores reales. Las tres salidas verificadas llegan a `1006`,
`4006` y `0004`, incluida una nueva conexión al destino. Las ciudades contienen varios puntos de
viaje: las pruebas seleccionan el GUID conectado por una ruta de world.xml, no simplemente el
primer punto de la plantilla. El lector conserva los cinco destinos declarados; los otros dos no se
recorren en estas tres pruebas.

El recorrido es una prueba del servidor con entradas controladas, no una partida del cliente real:
proporciona resultados de inventario para fabricación y recolección, fija niveles del tablero y
usa daño controlado contra enemigos reales del mapa. Las pruebas de obtención complementarias
comprueban recursos, despellejado, herramientas, recetas, enemigos que reaparecen, fama necesaria
para START/COMBAT_TRAINEE y presupuesto de todas las alternativas de equipo. La presentación y
la secuencia visual de todo el recorrido siguen pendientes de la prueba manual del usuario.

La condición de poder medio ahora utiliza el cálculo real de cada objeto (calidad, durabilidad y
bonificaciones del tablero), manteniendo el promedio de seis posiciones empleado por el emulador.
Los valores base de las plantillas omitían la bonificación de COMBAT_TRAINEE y bloqueaban el arco.
El mensaje de seguimiento transmite el poder numérico, como espera el contador del cliente, y no
un booleano. Las pruebas rechazan equipo que permanece en la bolsa y un arco sin armadura; después
comprueban que las tres ramas cumplen sus umbrales declarados con equipo de nivel dos.

Compilación: **0 advertencias, 0 errores**. Comprobación específica: **12 PASS, 0 FAIL** en datos y
recorridos, más **4 PASS, 0 FAIL** en obtención y presupuesto. Informes locales:
`route-build.log`, `route-tests.json` y `obtainability-final-tests.json`. No se modificó ni borró
la base de datos del usuario. La batería global final obtuvo **418 PASS, 0 FAIL y 4 SKIP, de 422
seleccionadas**, registrada en `complete-tutorial-final.json` y `.log`; las omisiones corresponden
a referencias privadas ausentes.
