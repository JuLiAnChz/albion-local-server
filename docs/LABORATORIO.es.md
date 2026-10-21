# Laboratorio educativo de Albion

Este laboratorio extiende el emulador MIT de [MrGreenCheezz](https://github.com/MrGreenCheezz/albion-local-server).
Primero aprenderás a reproducir conversaciones locales. La conexión del cliente del juego es una comprobación
posterior y separada. Los ejemplos que publicamos son sintéticos; no incluyen archivos ni datos extraídos del juego.

## Preparación desde un clon limpio

Necesitas Git, PowerShell y el SDK .NET 10. Para las pruebas de datos, necesitas tu instalación del cliente de referencia
`1.32.030.345319`. El laboratorio de protocolo también funciona sin GameData.

```powershell
git clone https://github.com/JuLiAnChz/albion-local-server.git
cd albion-local-server
git switch educational-lab
git remote add upstream https://github.com/MrGreenCheezz/albion-local-server.git
./scripts/lab.ps1 -Mode build
./scripts/lab.ps1 -Mode learn -Stage serialization -Trace
```

Ejecuta los comandos desde la raíz del clon. El script cambia temporalmente a esa carpeta para que configuración,
contenido y archivos de salida se resuelvan igual, incluso si lo invocas desde otro directorio.
Si NuGet falla con `NU1301`, comprueba el acceso HTTPS y DNS a `api.nuget.org`: no cambies paquetes para ocultar
un problema de red. En un entorno que restrinja sockets locales, ejecuta las pruebas UDP con permisos de red local.

La compilación mantiene activada la auditoría de NuGet, incluidas las dependencias indirectas. El servidor fija
`SQLitePCLRaw.bundle_e_sqlite3` en `2.1.14`: la resolución original `2.1.11` incluía una biblioteca nativa afectada
por [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). La prueba `persistence: native SQLite`
comprueba la versión del motor realmente cargado, no solo la versión declarada del paquete.
Puedes consultar los avisos actuales con `dotnet list AlbionLocal.slnx package --vulnerable --include-transitive`.

Para trabajar con los datos de tu cliente, copia `lab.local.example.json` como `lab.local.json` y escribe la ruta
de **GameData**, no la del ejecutable ni la del launcher. Ese archivo local está excluido de Git.

```json
{
  "clientGameData": "C:/Games/AlbionOnline/game/Albion-Online_Data/StreamingAssets/GameData"
}
```

También puedes pasar `-ClientGameData 'ruta con espacios'` o usar `ALBION_GAMEDATA`.
La prioridad del script es argumento/variable de entorno, archivo local y, para SelfTest, la ruta histórica del proyecto.
El servidor y la comprobación de contenido requieren una ruta explícita o local configurada.

```powershell
./scripts/lab.ps1 -Mode test
./scripts/lab.ps1 -Mode content
./scripts/lab.ps1 -Mode server -Trace
```

El servidor conserva sus valores predeterminados de loopback: login UDP 5055, juego 5056 y chat 5057.
El script guarda su base y registro en `data/lab/server/`; Ctrl+C lo detiene. No usa tu cuenta oficial:
el emulador administra cuentas locales y puede crearlas en su primer login. Usa credenciales exclusivas del laboratorio.
GameData se lee desde la instalación y no se copia al repositorio. El arranque registra versión y huellas de seis
archivos principales en `data/lab/server/clientdata.json`; los mapas se cargan después, de forma diferida.

## Mapa de conceptos

```text
Datagrama UDP
  -> comandos ENet (secuencias, ACK, fragmentos)
    -> mensaje Photon (inicialización, operación, respuesta o evento)
      -> Protocol18 (tipos y tabla de parámetros)
        -> AlbionSerializer (operación y campos de clases C#)
          -> sesión y manejador (cuentas, personajes, mundo)
```

Un `Dictionary<byte, object?>` es un mapa de claves numéricas a valores de distintos tipos.
`byte[]` representa bytes; `short` es un entero de 16 bits; `int`, de 32. Los atributos `[Param(n)]` enlazan
campos de una clase con claves del mensaje. `using` libera recursos al salir del bloque; `Task` representa trabajo
asíncrono. Encontrarás esos conceptos en los ejercicios y sus pruebas.

Photon distingue solicitudes, respuestas y eventos. En este proyecto las operaciones Albion viajan dentro de la
operación Photon 1: la clave 253 identifica la operación Albion, 252 el evento Albion y 255 correlaciona solicitudes
y respuestas. La clave 251 lleva la base temporal del servidor en Login. El evento del servicio de estado usa una
convención propia; no interpretes su código 1 como el evento Leave del mundo.

Referencia conceptual: [capas binarias de Photon](https://doc.photonengine.com/realtime/v5/reference/binary-protocol).
Las reglas concretas de Albion se verifican contra el código de esta versión, no se deducen de esa guía genérica.

## Ejercicio 1: serialización

```powershell
./scripts/lab.ps1 -Mode learn -Stage serialization -Trace
```

Resultado esperado: tres pruebas aprobadas. El ejemplo imprime:

```text
0103000704686f6c61fd040500ff0954
```

Es el cuerpo de una solicitud Photon, sin las cabeceras UDP/ENet ni el envoltorio de cifrado.
Se lee como `01` (operación Photon), `03` (tres parámetros), seguido de:

| Bytes | Significado |
|---|---|
| `00 07 04 686f6c61` | clave 0, cadena UTF-8 de cuatro bytes: hola |
| `fd 04 0500` | clave 253, Int16, operación Albion 5 |
| `ff 09 54` | clave 255, entero comprimido: identificador 42 |

`0x54` equivale a 84, resultado de codificar 42 con zigzag. El mensaje ilustra el contenedor y **no es una solicitud
Login válida**: sus campos no corresponden al Login del cliente. En `LabExercises.Serialization`, cambia `hola`
por otra cadena y ajusta la comparación esperada. Compila y observa longitud y contenido UTF-8.

Consulta `Protocol18Golden` para enteros positivos/negativos y la diferencia entre serializar un valor y un mensaje.
Has completado el ejercicio cuando puedes explicar cada clave, tipo y valor sin confundir los dos códigos de operación.

## Ejercicio 2: transporte

```powershell
./scripts/lab.ps1 -Mode learn -Stage transport -Trace
```

Resultado esperado: dos pruebas aprobadas. Un servidor de eco y `TestPhotonClient` usan puertos locales libres,
realizan la inicialización y el intercambio de claves, envían 10 KB y comprueban la reconstrucción exacta.
La segunda prueba introduce un 20 % de pérdida simulada; las retransmisiones permiten completar el intercambio.
Las trazas indican que el mensaje va cifrado, sin mostrar su contenido.

En `Program.cs`, localiza `EndToEnd`: cambia el tamaño de `big` de 10000 a 12000 y repite. La igualdad de bytes
debe seguir comprobándose. Explica por qué la confirmación y la fragmentación pertenecen al transporte, y no al
manejador de inventario o movimiento. Restaura el ejercicio antes de preparar una contribución.

## Ejercicio 3: sesión y cifrado

```powershell
./scripts/lab.ps1 -Mode learn -Stage session
```

Resultado esperado: una prueba aprobada. `DhInterop` repite 1500 intercambios sintéticos y verifica que ambos
extremos pueden descifrar mensajes del otro. Estudia cómo cambia el orden de bytes de los enteros grandes antes
de derivar la clave, y cómo se aplica AES al cuerpo del mensaje.

Modifica localmente una copia del mensaje cifrado antes de descifrarlo: la igualdad o la validación de relleno debe
fallar. Restaura el cambio. La propiedad que observas es interoperabilidad con esta implementación, no una auditoría
de seguridad del protocolo. No necesitas claves de sesiones oficiales para este ejercicio.

## Ejercicio 4: de bytes a comportamiento Albion

```powershell
./scripts/lab.ps1 -Mode learn -Stage messages -Trace
```

Resultado esperado: tres pruebas aprobadas. Incluye la serialización Albion, una cadena Login → SelectCharacter → Join
con el cliente de pruebas y un ejemplo mínimo de consulta de estado.

En `LabExercises.StatusHandler`, una operación Photon 1 sin parámetros llega a `LoginSession.StatusQuery`.
El servidor produce un **evento**, no una respuesta: clave 1 con `byte 3` (online), clave 252 con `short 1`.
Este ejemplo usa `FakePeer` para observar el despacho sin UDP; la cadena de login sí usa sockets y cifrado.

Sigue `OnRequest`, `ParseRequest`, la tabla de manejadores y el atributo `[Handles]`. Cambia una comprobación del
ejemplo para esperar estado 2; debe fallar. Restaura el cambio y explica por qué los tipos byte e Int16 importan.
Los nombres ofuscados de los campos generados se conservan para mantener la correspondencia con la referencia.

## Resultados y trazas

```powershell
./scripts/lab.ps1 -Mode learn -Trace
./scripts/lab.ps1 -Mode test -Only 'persistence:'
./scripts/lab.ps1 -Mode test -Only 'lab infrastructure:'
```

Los ejercicios completos seleccionan nueve pruebas y no requieren GameData. Las pruebas de infraestructura añaden
validación de opciones y de que las trazas no exponen valores. La batería completa sí usa GameData.

El script escribe informes JSON en `data/lab/learn-<stage>.json` y `data/lab/test-all.json` (se reemplazan en cada
ejecución). Incluyen fecha UTC, totales, estado y duración por prueba. Un filtro vacío de coincidencias sale con 2;
fallos de pruebas salen con 1; una ejecución sin fallos sale con 0 aunque tenga omisiones.

- **PASS:** se ejecutaron y aprobaron las comprobaciones de esa prueba.
- **FAIL:** falló una comprobación o se produjo una excepción.
- **SKIP:** falta una referencia privada y no se pudo realizar esa comparación; no cuenta como PASS.

El uso directo conserva la ruta GameData histórica si omites la opción:

```powershell
dotnet run --project tests/AlbionLocal.SelfTest -- '--client-gamedata=C:/Games/AlbionOnline/game/Albion-Online_Data/StreamingAssets/GameData' --only='client data:' --report=data/lab/client-data.json
```

`--protocol-trace` es opcional y no cambia el nivel de log. Describe dirección, clase de mensaje, código Photon,
código Albion, cifrado, resultado y claves/tipos. **Omite todos los valores**, también en campos desconocidos,
colecciones y respuestas con tokens. Los eventos muestran su código numérico porque su nombre depende del contexto.
Estas trazas son observaciones de mensajes ya procesados por el servidor; no extraen claves de otros procesos.
Los logs normales con `-v` pertenecen al proyecto original y no tienen el mismo contrato de omisión de valores.

El decodificador `AlbionLocal.CapDecode` acepta PCAP clásico little-endian con Ethernet/IPv4/UDP; no admite PCAPNG
ni reconstrucción IP y marca como cifrados los cuerpos que no puede leer. No recupera conversaciones cifradas
completas por sí solo. Los informes, registros, bases locales y GameData quedan fuera de las contribuciones.

## Paso posterior: cliente real y nuevas versiones

La comprobación inicial y sus límites están en [LAB-VALIDATION.es.md](LAB-VALIDATION.es.md).
Los metadatos de la instalación contienen `+server` y `serverenvironment`; eso es una pista de investigación,
no demuestra que el launcher protegido actual permita cambiar todos los endpoints regionales. No se proporciona
un parche de cliente, una modificación de hosts o una desactivación de protecciones como procedimiento de arranque.

La puerta de aceptación del cliente real es observar primero una conexión local del cliente normal, después login
con una cuenta de laboratorio, selección, Join, movimiento, guardado y recuperación tras reiniciar. Cada paso debe
tener evidencia propia en los registros. El cliente simulado no sustituye esa comprobación y la coincidencia de
versiones no la garantiza. Esta conexión interactiva queda pendiente hasta verificar un procedimiento compatible
de selección del servidor en el launcher/cliente normal.

Si se actualiza el cliente, conserva el informe previo y compara versión, huellas y primer intercambio incompatible.
El generador privado `analysis/tools/gen_protocol_cs.py` no está publicado. No edites masivamente los códigos
generados ni inventes perfiles para versiones que no hemos comprobado.

## Preparar contribuciones

La base conserva licencia y atribución MIT. El remoto `origin` es el fork y `upstream` el proyecto original.
Trabaja en ramas pequeñas y registra el resultado de las comprobaciones. Antes de proponer compatibilidad nueva,
separa cambios de formato, datos y comportamiento. La [nota de contribución](LAB-CONTRIBUTING.md) contiene
la división sugerida y la evidencia mínima para el mantenedor.
