# Primera prueba del tutorial local

El modo experimental implementa el tramo observado en el cliente `1.32.030.345319`: playa,
«Primeros pasos», Artesano herido y la misión completa «La información es la clave»: ascenso,
observación del barco y los herejes, descenso, esquiva o desplazamiento y regreso para entregarla.
Incluye también la siguiente misión: acercarse a los recursos, conseguir 6 piedras y 6 troncos
y volver al Artesano herido para entregarla. Después incluye la primera espada, su equipamiento y
selección de habilidad, hasta entregar esa misión y recibir un escudo.

La continuación del escudo hasta abandonar la isla se describe en el
[recorrido completo](TUTORIAL-RECORRIDO.es.md), con sus variantes, precios de laboratorio y
limitaciones de verificación. Esta guía conserva el detalle de la primera prueba.

## Iniciar

Cerrar el otro emulador antes de iniciar este modo: utiliza los mismos puertos locales 5055, 5056 y 5057.
Desde la raíz del clon, con `lab.local.json` apuntando a los datos de la instalación:

```powershell
./scripts/lab.ps1 -Mode server -OpeningTutorial -Trace
```

El modo servidor compila antes de ejecutar. Con `-OpeningTutorial`, la salida predeterminada es
`data/lab/onboarding-build`; así se ejecuta la versión recién compilada. El arranque debe indicar
`new character start cluster 1008` y `opening lab enabled`. Sin `-OpeningTutorial`, el script
usa los ajustes de los archivos y los guardados del laboratorio habitual.

El script activa `Tutorial:Opening=true`, conserva el preset de `config/common.jsonc` y guarda exclusivamente en
`data/lab/tutorial-server/albionlocal.db`. El registro es `data/lab/tutorial-server/server.log`.
La configuración distribuida usa `official` y rates ×1. `-ConfigDir <carpeta>` selecciona los cinco
archivos; `-Mode config` comprueba sus valores sin abrir una partida.
La ruta habitual `data/lab/server` no se abre con este comando. No borrar ni copiar sus guardados para
esta prueba. Una ejecución directa con otra ruta de guardados sí puede migrar la base indicada.

La base nueva usa el esquema **v31**. La migración añade el avance del tutorial y conserva los personajes
anteriores con avance nulo. No se debe abrir una base ya migrada con un binario que solo soporte v30 o anterior.

Abrir el cliente con el lanzador local ya comprobado (`scripts/start-client.ps1`; entorno `dev`).
Usar credenciales de laboratorio y crear un personaje nuevo. El modo tutorial ya no crea LocalHero
automáticamente en una base vacía. Si se reutiliza una base antigua, sus personajes conservan su inicio;
solo los personajes nuevos reciben este recorrido.

Para salir, cerrar el juego y detener el servidor con Ctrl+C para completar el guardado.

Si la ficha muestra «Desconocida», comprobar primero qué instancia está escuchando: una compilación
aislada no actualiza automáticamente la DLL de `src/.../bin`. Se detectó este caso con una instancia
antigua de esquema v29, sin tutorial. El script ahora recompila la salida elegida antes del arranque.
Una cuenta creada en el laboratorio habitual no existe automáticamente en la base separada del tutorial;
registrar una cuenta de laboratorio en este modo y crear un personaje nuevo.

## Recorrido esperado

1. El personaje nuevo tiene The Lighthouse en la ficha y 0s. Aparece en el `startpoint` de 1008,
   con chaqueta y zapatos T1 de mercenario, habilidades elegidas por el sistema existente, sin arma,
   plata ni materiales del kit de laboratorio.
   En su primera entrada se aplica `TUTORIAL_STUN`, definido por el cliente con la animación
   `tutorial_stun`, música `TUTORIAL_MUSIC_STRANDED` y aturdimiento de seis segundos. Esperar a que
   termine antes de caminar. El inicio de la secuencia queda guardado y no se repite al reconectar.
2. El cliente recibe el estado inicial y el seguimiento de «Primeros pasos». Caminar por los dos
   puntos de salida y permanecer un segundo dentro del mirador habilita la tarea de hablar con el sobreviviente.
3. Acercarse al Artesano herido no completa la conversación. Hablar marca el objetivo como cumplido;
   «Completar» entrega la misión. No se inventan recompensas: las tres definiciones locales tienen premios vacíos.
4. El mismo NPC ofrece «La información es la clave». «Aceptar» la activa; seguir los puntos de las
   escaleras lleva a «Busca el barco». Llegar al círculo superior permite continuar al punto de los
   herejes y bajar de la torre. Las posiciones proceden de los marcadores del mapa, no de las capturas.
5. El siguiente objetivo admite dos alternativas: lanzar la esquiva de los zapatos (hechizo cuyo
   nombre contiene `DODGE`) o caminar al círculo indicado. Un lanzamiento rechazado, por ejemplo
   por estar en enfriamiento, no cuenta. Después aparece el objetivo de volver con el artesano.
6. Hablar con él habilita «Completar» y la entrega termina la segunda misión sin añadir recompensas,
   pues su definición tiene premios vacíos. El artesano ofrece ahora la misión de recolección.
7. Aceptar y acercarse al punto de recursos. Se muestran dos objetivos simultáneos: 6 piedras T1
   y 6 troncos T1. Recolectar por los medios normales del juego; una petición rechazada no suma.
   El servidor verifica las cantidades en el inventario y actualiza cada contador. Conseguir solo
   uno de los materiales no permite entregar. Mientras el paso siga pendiente, sacar materiales
   del inventario reduce el contador correspondiente.
8. Con ambos objetivos cumplidos, volver al artesano, hablar y pulsar «Completar». Los materiales
   permanecen en el inventario. No se concede plata. Ahora puede aceptarse la misión de fabricación.
9. Al aceptar la fabricación, se habilita la forja provisional junto al artesano. Antes de ese paso,
   el servidor anuncia permisos sin acceso y rechaza la interacción y la fabricación adelantadas.
   Acercarse al círculo no sustituye hablar/interactuar con la forja.
10. Fabricar la espada T1 mediante la receta normal de la forja, que consume sus recursos. Abrir el
    inventario, equipar la espada y elegir una habilidad de su primera ranura activa. La espada
    recién fabricada deja las habilidades activas sin seleccionar para practicar este paso.
11. Regresar al artesano, hablar y pulsar «Completar»: se recibe un escudo T1. Si no cabe, liberar
    una casilla y volver a completar; el objetivo permanece pendiente. No se concede dos veces.
    La siguiente misión de combate todavía no se ofrece.
12. Salir y volver a entrar debe conservar la etapa. Una permanencia a medias en el mirador empieza
   de nuevo al reconectar; las horas desconectado no cuentan. Entregar o aceptar de nuevo no duplica acciones.

Los personajes que estaban detenidos en «Busca el barco» pueden continuar con el mismo guardado tras
reiniciar el emulador. No se borran cuentas ni se cambia el esquema de la base. Al completar la segunda
misión, reconectar no debe volver a activarla ni repetir la entrega.
Los personajes que ya entregaron la torre también pueden continuar: hablar con el artesano y aceptar
la nueva oferta. La recolección parcial se reconstruye a partir de los materiales guardados; no hace
falta crear otro personaje ni reiniciar la base de datos.
Si ya se entregó la recolección, hablar con el artesano permite aceptar fabricación con ese mismo
personaje. Las etapas guardadas anteriores se conservan; no se reinician las misiones completadas.

## Si solo aparecen iconos y el recuadro de misiones queda cerrado

La visibilidad depende también de la preferencia local `TrackerCollapse`. En la comprobación del
21 de octubre, el perfil activo la tenía en `true`: el cliente mostraba el seguimiento contraído.
Revisar la opción de contraer el seguimiento en Configuración → Interfaz y dejarla desactivada
para probar el recuadro desplegado. No borrar personajes ni cambiar datos de misiones por este motivo.

En este equipo se desactivó únicamente esa preferencia, con el cliente cerrado. Se conservó una copia
del perfil en `data/lab/client-profile-backups/tracker-20261021-232209.profile`. Es una preferencia del
perfil de Albion y también se aplica al jugar en servidores oficiales. Volver a enviar el progreso
de la misión no cambia esa preferencia guardada por el cliente.

Tras repetirse el problema al comenzar de nuevo, `Iniciar Albion local.cmd` pasa ahora
`-ExpandQuestTracker` a `scripts/start-client.ps1`. Antes de abrir el juego, el lanzador desactiva
`TrackerCollapse` si encuentra un único perfil y guarda una copia previa en
`data/lab/client-profile-backups`. Conserva las demás preferencias. Si hay varios perfiles no
elige uno automáticamente: muestra una indicación para ajustar la opción desde el juego.
Ejecutar el script sin ese parámetro conserva el comportamiento anterior.

**Validado visualmente el 22 de octubre de 2026:** el usuario confirmó que el recuadro aparece
automáticamente al entrar con Barbaro tras desactivar `TrackerCollapse`, sin pulsar el icono.

## Qué falta comprobar o implementar

La primera prueba real encontró círculos que avanzaban con el seguimiento de misiones estancado y
un diálogo bloqueado. Se corrigieron tres puntos del intercambio con el cliente:

- La misión y su progreso se envían antes de `JoinFinished`: `OnEnterGame` consulta la misión activa
  para abrir el panel. Al caminar se actualiza ese modelo con `QuestProgressInfo`, sin reconstruirlo
  con `FullQuestInfo` en cada paso.
- Los campos 4 y 5 de `QuestProgressInfo` identifican el paso y la misión interna para el texto flotante;
  no son índices anterior/actual. Cada paso implementado contiene una sola misión (índice 0).
- El cliente observado abre el NPC mediante `RegisterToObject` (45). Esa interacción ahora valida
  NPC y distancia, completa la conversación y envía el progreso; la entrega continúa siendo explícita.

La segunda prueba confirmó los círculos, pero encontró el panel aún oculto y «Completar» bloqueado.
El NPC necesita además el campo 6 de `QuestGiverInfoForPlayer` (`dr5`, `cmh+0x41`); anuncia que puede
recibir la misión cuando solo falta hablar con él y mientras sigue pendiente de entrega. La misión se vuelve a sincronizar
al recibir op515, después de restaurarse el HUD, sin sugerencias del diario general durante este tramo.
Estas dos correcciones de presentación siguen pendientes de repetir con el cliente real.

La prueba posterior confirmó la animación, pero encontró cambios de aspecto y «Completar» activo solo
al reabrir el diálogo. `Join` enviaba valores predeterminados para cuerpo y personalización: ahora usa
los elegidos al crear el personaje, también para los observadores y al reconectar. El NPC anuncia su
disponibilidad en etapa 3, antes del primer clic, porque el cliente calcula los botones al abrir el diálogo.
Hablar sigue siendo obligatorio y la entrega se rechaza hasta etapa 4; no entrega la misión por proximidad.
El usuario confirmó después las misiones y el diálogo; el aspecto y el panel seguían pendientes.

La revisión siguiente distinguió los campos de creación: `Template` guarda históricamente el retrato,
y `Appearance` el cuerpo, no el anillo del retrato. Join usa campos 3/5 para esos valores y 6/7 para
personalización y colores; los observadores y el listado de personajes reciben la misma asignación.
No hace falta borrar cuentas: los valores ya estaban guardados correctamente.

Para el panel se encontró una condición adicional en el cliente: `QuestTrackerHudGui.UpdateHud` activa
su presentación automática cuando `ciy.bv()` identifica una instancia de tutorial. El nombre simple
`1008` no cumple esa condición. Creación, listado, selección y Join usan
`@TUTORIALSINGLE@<id-personaje>` como identidad del mapa cargado por el cliente, con `1008` como mapa
base. Las referencias de misión, variables y NPC deben usar el mapa base `1008`: el cliente las busca
en su catálogo de mapas, no en el registro de instancias. Se corrigió la regresión que enviaba el alias
también en esas referencias y provocaba `ArgumentOutOfRangeException` al visualizar las misiones.
La misma identidad selecciona ahora una instancia privada en el servidor por personaje. Los objetos,
cofres, recursos y enemigos pertenecen a esa copia; los personajes de otras copias no se ven.
El mapa base y las referencias de misión siguen siendo `1008`, para conservar sus textos y marcadores.

Al intentar crear un nombre ocupado se responde con 482 (`NAME_NOT_UNIQUE`), que el cliente traduce
a su mensaje específico. Un nombre vacío usa 483 (`NAME_NOT_ALLOWED`). Ambos casos se prueban sin
reemplazar el personaje existente. Reiniciar el emulador y el juego antes de repetir estos casos.

La selección de personajes usa además un formato de aspecto distinto de Join: el campo 18 de Login
contiene un diccionario JSON con cada parte (`FACE`, `SKIN`, `HAIR`, etc.), `MeshId` y `ColorPosition`.
Ahora se reconstruye desde las elecciones guardadas y las definiciones de `characters.bin`; antes se
enviaba vacío y aparecía el cuerpo básico sin pelo. Se envían también los tipos del equipo puesto,
tanto al crear como al volver a iniciar sesión. Este ajuste se prueba con los personajes existentes,
sin borrarlos ni crear una cuenta nueva; comprobar pelo, cara, colores y equipo antes de pulsar Jugar.

Estas correcciones tienen pruebas de mensajes y sesión. Repetir la comprobación visual tras reiniciar
el emulador: con el personaje existente se puede retomar el diálogo; crear otro personaje para verificar
la apertura inicial del panel y todo el recorrido. No hace falta borrar la cuenta ni sus personajes.

Las pruebas automatizadas comprueban sesiones, guardados, referencias y transiciones; **no sustituyen la prueba
visual con el juego**. Confirmar textos, círculos, indicadores del NPC, apertura/cierre del diálogo,
contador y restricciones de botones. Si algo difiere, anotar la última acción y el estado visible;
el registro del servidor incluye cada transición con la etiqueta `tutorial`.

La llegada usa el hechizo nativo encontrado en `spells.bin`, con su duración exacta sin escalado de
control de masas de combate ni cambios de vida, muerte o durabilidad. No se utiliza el derribo de combate.
Falta confirmar visualmente la postura, el levantamiento, la música y los detalles de cámara con el juego.
Cada personaje del modo de apertura tiene una instancia privada del mapa 1008. Reconectar reutiliza
su copia mientras siga cargada; si se descarga por inactividad o se reinicia el servidor, el escenario
se reconstruye. El avance de misiones se conserva en la base de datos; el estado de destrucción de
objetos no se persiste entre reconstrucciones del escenario.

Para comprobar el aislamiento, destruir el cofre de la torre con un personaje y entrar con otro sin
reiniciar el emulador: el segundo debe tener su propio cofre. Con dos personajes conectados a la vez,
no deben verse ni recibir los cambios del escenario del otro. No hace falta borrar las cuentas.
El objetivo `HERETICS` sigue completándose al entrar en el círculo, sin exigir destruir el cofre:
el usuario confirmó ese comportamiento en el juego original y la definición usa `gotolocation`.

El servidor valida los objetivos a partir del movimiento aceptado por el emulador y de la interacción con
el NPC correcto a un máximo de siete unidades (límite del laboratorio, no medida del servidor original).
No añade un sistema nuevo de detección de trampas o validación completa de navegación.

La continuación incluye combate, recolección, fabricación y las misiones hasta el viaje fuera de la isla.
Las misiones posteriores de las ciudades iniciales quedan fuera de este alcance.
Los personajes ajenos a este modo no se trasladan automáticamente.
