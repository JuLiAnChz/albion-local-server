# Hoja de trabajo y contribuciones

Actualizada: 21 de octubre de 2026. Rama de integración: `educational-lab`.

## Punto de partida

El cliente Windows `1.32.030.345319` inició sesión contra el emulador, seleccionó LocalHero,
entró a Thetford y mostró movimiento. La evidencia y sus límites están en
[LAB-VALIDATION.es.md](LAB-VALIDATION.es.md). El procedimiento está en
[LABORATORIO.es.md](LABORATORIO.es.md). Aún no se verificó la recuperación tras reiniciar.

## Trabajo por prioridad

Prioridad actual solicitada: **T01, recorrido inicial y tutorial**. La revisión para publicar aportes y la ampliación
de motores de persistencia quedan después de este recorrido. El usuario comprobará en paralelo la recuperación
de su personaje tras reiniciar. Diagnóstico y etapas: [Tutorial](TUTORIAL.es.md).

| ID | Trabajo | Estado | Criterio de cierre | Destino |
|---|---|---|---|---|
| T01 | Reproducir el tutorial del cliente instalado | Diagnóstico realizado; primera prioridad | Primer hito: personaje nuevo en 1008 con objetivo inicial funcional; después misiones, avance guardado y salida verificados | Desarrollo en el fork y aportes pequeños con evidencia |
| C01 | Actualizar SQLite nativo y activar auditoría transitiva | Aislado y validado; listo para revisión local | Rama `fix/sqlite-native-security`, commit `ea91393`: compilación sin advertencias, 6 pruebas correctas, auditoría sin avisos conocidos | [Propuesta preparada](SQLITE-CONTRIBUTION.md) |
| A01 | Preparar proveedores de persistencia | Implementado y validado con SQLite | Fábrica inyectable, contratos completos, SQLite por defecto; suite: 370 correctas, 0 fallos, 4 omitidas | Aporte independiente; [diseño](PERSISTENCIA.es.md) |
| V01 | Comprobar persistencia con el cliente real | Usuario realizará la reapertura; cierre observado en registros | Anotar posición e inventario, salir normalmente, guardar copia consistente con servidor detenido, reiniciar y comprobar recuperación | Informe en el fork; corrección upstream si aparece un fallo reproducible |
| C02 | Hacer portables las pruebas y distinguir omitidas | Implementado; separar cambios | Ruta GameData configurable; selección vacía devuelve 2; informes separan aprobadas, fallidas y omitidas | Contribución candidata |
| C03 | Añadir trazas opcionales del protocolo | Implementado; revisión pendiente | Desactivadas por defecto; pruebas confirman que contraseñas, tokens y valores desconocidos no se muestran | Contribución candidata |
| C04 | Ejercicios sintéticos y guía de preparación | Implementado; separar cambios | Ejecutar desde clon limpio y explicar requisitos, resultados y referencias privadas ausentes | Contribución candidata |
| D01 | Explicar el aviso de servidor no disponible | Pendiente de diagnóstico | Reproducción documentada; identificar origen del estado; demostrar cualquier corrección antes de proponerla | Fork mientras se investiga |
| V02 | Repetir el arranque y la sesión desde un clon limpio | Pendiente | Preparación, compilación, login, selección y Join reproducibles sin depender de herramientas de análisis | Validación del fork |

La recuperación tras reinicio requiere una ventana acordada de prueba: no detener una sesión activa sin avisar.
El movimiento visual por sí solo no acredita que el servidor haya persistido la posición.
Combate, economía, nuevas versiones y cambios masivos de protocolo quedan para etapas posteriores.

## Cómo prepararemos cada aporte

1. Elegir una fila y actualizar su estado. Describir el problema observado y cómo reproducirlo.
2. Crear una rama desde el `upstream/main` actualizado; trasladar únicamente el cambio correspondiente.
3. Revisar el diff y ejecutar las comprobaciones pertinentes en esa rama. Los resultados de la rama de
   integración no sustituyen la validación del aporte aislado.
4. Preparar título y descripción con problema, solución, comprobaciones y límites. Revisarlos juntos antes
   de contactar al mantenedor o publicar una propuesta.
5. Registrar enlace y resultado cuando exista una propuesta. No dar por aceptado un cambio antes de su revisión.

Se conservarán licencia y atribución. No incluir GameData, binarios o código extraído del cliente,
capturas privadas, registros de sesión, bases de datos ni credenciales. El arranque del cliente se conserva
en el fork: el proyecto original excluye de su soporte el procedimiento para dirigirlo a otro servidor.

## Primer aporte propuesto: C01

El commit original `6864c93` se extrajo como cambio mínimo en `ea91393`, rama `fix/sqlite-native-security`,
con una copia de trabajo independiente en `../sqlite-security`. La compilación, las seis pruebas de persistencia
y la auditoría se repitieron correctamente en esa rama. La [descripción revisable](SQLITE-CONTRIBUTION.md)
contiene el resultado actualizado. La propuesta todavía no se ha publicado.

Título propuesto: **Update native SQLite bundle and enable transitive dependency audits**.

Descripción propuesta para revisar:

> Microsoft.Data.Sqlite 10.0.8 resolves SQLitePCLRaw.lib.e_sqlite3 2.1.11, which is flagged for
> CVE-2025-6965. Reference SQLitePCLRaw.bundle_e_sqlite3 2.1.14 explicitly while retaining
> Microsoft.Data.Sqlite 10.0.8. Enable transitive NuGet audits in the laboratory build and verify
> the loaded SQLite version in the persistence checks.
>
> Validation on the isolated branch: solution restore/build and transitive audit completed without
> reported advisories; all six persistence checks passed. The runtime reported SQLite 3.54.0.

La rama aislada ya excluye el script de laboratorio y el cambio de arquitectura; configura la auditoría en
el proyecto del servidor para poder revisarse sin depender de otros aportes.

## Registro de incidencias durante las pruebas

Para cada fallo nuevo, anotar: versión del cliente, commit del servidor, pasos, resultado esperado,
resultado observado, hora del intento y extracto mínimo del registro sin datos de cuenta.
Clasificarlo como reproducido, hipótesis o pendiente; evitar corregir códigos de operación por intuición.

Referencias: [repositorio original](https://github.com/MrGreenCheezz/albion-local-server),
[notas de contribución](LAB-CONTRIBUTING.md). La consulta del 21 de octubre no devolvió incidencias
ni propuestas abiertas; volver a comprobarlo antes de publicar.
