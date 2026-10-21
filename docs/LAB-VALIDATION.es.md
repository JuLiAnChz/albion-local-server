# Validación del laboratorio — 21 de octubre de 2026

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

## Cliente real: puerta de viabilidad pendiente

**No se ha demostrado una sesión del cliente gráfico contra el servidor local.** Todos los resultados de sesiones
anteriores corresponden al cliente de laboratorio. La coincidencia de versión permite comprobar la base existente;
no convierte automáticamente la instalación normal en un cliente de servidor privado.

Se inspeccionaron los archivos de configuración de texto disponibles y, sin modificarlos ni publicarlos, los
metadatos de la instalación. Aparecen las cadenas `+server`, `serverenvironment`, `GetCommandLineArgs` y
`ServerAddress`. El launcher protegido instalado referencia el ejecutable del juego, pero esa evidencia estática
no prueba qué argumentos acepta ni qué endpoints de estado, login y regiones cambia en esta versión de Windows.
Una [publicación de primera mano en el foro del juego](https://forum.albiononline.com/index.php/Thread/219095-Game-Startup-Settings-Are-Broken/)
muestra esos argumentos en otro entorno; no valida el flujo de esta instalación.

No se lanzó el cliente gráfico ni se modificaron el launcher, la instalación, hosts o las protecciones. No existe
todavía un primer mensaje incompatible del cliente real que podamos atribuir al servidor. El siguiente hito requiere
verificar interactivamente la selección local de servidor mediante el flujo normal del cliente, observar una conexión
a loopback y usar credenciales locales. Después se comprobarán login, selección, mapa, movimiento y persistencia.

Hasta superar esa puerta, la conexión gráfica y cualquier compatibilidad con una versión diferente quedan pendientes.
El laboratorio reproducible, las pruebas y la documentación pueden utilizarse y revisarse de forma independiente.

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
