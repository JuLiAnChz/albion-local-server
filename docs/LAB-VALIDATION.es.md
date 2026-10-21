# Validación del laboratorio — 21 de octubre de 2026

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
