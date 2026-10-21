# Persistencia intercambiable

## Decisión

Conservar los repositorios por área que ya existen y permitir elegir su implementación en el arranque.
SQLite sigue siendo el proveedor predeterminado. PostgreSQL todavía no está implementado.

| Pieza | Responsabilidad |
|---|---|
| `ICharacterRepository` | Cuentas, personajes, copias de lectura y guardados individuales o conjuntos |
| `IMarketStore`, `IMailStore`, `IGuildStore`, `IIslandStore`, `ISocialStore`, `IRoadStore` | Operaciones de persistencia propias de cada área |
| `IGameRepository` | Exigir un proveedor completo al arrancar el servidor y compartir su ciclo de vida |
| `IGameRepositoryFactory` | Abrir el proveedor; el servidor adquiere la responsabilidad de cerrarlo |
| `SqliteRepositoryFactory` | Crear el repositorio SQLite con la ruta y el nivel de cuenta configurados |
| `ServerHost` | Elegir la fábrica predeterminada o usar la recibida, importar guardados antiguos e iniciar el mundo |

Las interfaces de cada área están en sus propios archivos. No exponen conexiones, comandos ni transacciones SQL.
Las reglas del juego usan `Account.LocalId`, independiente del proveedor. El identificador anterior de SQLite
se conserva como alias para compatibilidad con herramientas y pruebas existentes.

## Uso actual

El arranque normal no necesita cambios: `new ServerHost(config)` continúa usando SQLite.
Para sustituir el proveedor desde el punto de composición:

```csharp
IGameRepositoryFactory factory = new SqliteRepositoryFactory();
using var host = new ServerHost(config, repositoryFactory: factory);
host.Start();
```

La fábrica debe devolver una implementación de `IGameRepository`. Un futuro proveedor PostgreSQL se inyectará
por esa misma entrada cuando esté implementado y validado. No existe aún un selector PostgreSQL en configuración.
El código de redes y las reglas del juego no necesitan conocer el tipo de conexión.

`IGameRepository` agrupa capacidades para el arranque; no obliga a cada servicio a consumir todas ellas.
El mundo sigue usando las interfaces pequeñas. Las conversiones opcionales del constructor de `World`
se conservan para las pruebas que crean mundos parciales; el servidor normal recibe un proveedor completo.

## Garantías que debe mantener otro proveedor

- Leer y escribir personajes como copias; el juego modifica su copia en memoria mientras otro hilo guarda instantáneas.
- Mantener la normalización y unicidad de nombres, la pertenencia a cuentas y el identificador local estable.
- Respetar el orden de lectura y la semántica de eliminación: un guardado retrasado no debe recrear personajes borrados.
- Conservar la atomicidad definida por cada contrato: `SaveMany`, importaciones, lotes de mercado y operaciones de otras áreas.
  Esta interfaz no añade una transacción global entre todos los sistemas.
- Permitir el acceso concurrente que ya realizan login y el hilo de guardado. Cambiar de motor no autoriza a compartir
  una conexión sin protección ni a hacer trabajo de red prolongado bajo el bloqueo del mundo.
- Mantener migraciones propias y rechazar esquemas incompatibles. La secuencia SQLite existente no se reescribe.
- Liberar conexiones al cerrar el servidor y cuando falle la importación inicial, después de haber abierto el proveedor.

Las pruebas de `RepositoryContractTest` comprueban normalización, copias, reabrir, eliminación y reversión de lotes.
También verifican que el host use una fábrica suministrada y cierre el proveedor tras un error de importación.
La suite completa continúa comprobando los sistemas que usan cada almacén. Para añadir otro motor habrá que registrar
sus casos de contrato con una base de integración real y ampliar la cobertura de concurrencia y migraciones.

## Siguiente etapa: PostgreSQL

1. Definir configuración de conexión y credenciales locales sin incluirlas en Git ni en los registros.
2. Implementar el proveedor y sus migraciones, respetando nombres, fechas, identificadores y orden de lectura.
3. Ejecutar contratos y pruebas funcionales contra una instancia PostgreSQL temporal.
4. Medir latencia y bloqueo del mundo antes de decidir si se requiere cambiar las interfaces síncronas.
5. Diseñar por separado la migración de datos SQLite → PostgreSQL, con copia, comprobaciones y restauración.

Por ahora no se añaden un repositorio genérico CRUD, un ORM, un contenedor de inyección ni conversiones masivas
a métodos asíncronos. La separación actual permite evaluar esas decisiones con necesidades y mediciones concretas.

## Aportes separados

La corrección de seguridad SQLite está aislada en `fix/sqlite-native-security` (commit `ea91393`), basada en
`upstream/main` `bdb2c60`: dos archivos, actualización del bundle, auditoría y comprobación de la versión nativa.
La preparación de proveedores es un cambio independiente en la rama de integración; no debe mezclarse en esa propuesta.
