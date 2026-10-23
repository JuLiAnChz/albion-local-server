# Recorrido del tutorial y comprobación en el juego

Este recorrido sigue `quests.bin`, `tutorial.bin`, `questgivertypes.bin` y los cuatro mapas de la instalación de referencia. Las coordenadas y los radios se leen de los mapas; no es necesario medirlos en capturas. La verificación automática complementa la prueba manual: no demuestra por sí sola que todas las pantallas y animaciones se vean como en el servidor original.

Conserva tu cuenta y personaje. Cierra el servidor, vuelve a iniciarlo con el lanzador del tutorial y entra al juego con el lanzador local. Los personajes existentes conservan su avance; las etapas anteriores de playa, torre, recolección y primera espada mantienen sus identificadores guardados.

## Playa y primera fabricación

1. Crear un personaje: aparecer en The Lighthouse, levantarse y seguir los primeros círculos.
2. Hablar con el artesano herido y entregar «Primeros pasos». Acercarse solamente al NPC no sustituye la conversación.
3. Aceptar la misión de la torre, subir, observar el barco y regresar. El objetivo de la zona del cofre es de ubicación: no exige destruirlo.
4. Aceptar la primera recolección, obtener seis piedras y seis troncos y entregarla. El aviso superior anterior debe desaparecer.
5. Usar la forja provisional, fabricar la espada, abrir el inventario, equiparla, elegir su habilidad y entregar la misión. La recompensa es un escudo.

## Escudo, combate y llegada a The Cove

6. Equipar el escudo y permanecer tres segundos en el marcador de la puerta.
7. Derrotar un hereje; el siguiente objetivo pide cuatro herejes y el desbloqueo `START` del tablero del destino. Las bajas deben provenir de combate real.
8. Seguir el marcador de la capitana Tia y el cambio de mapa. The Cove es el centro del resto del tutorial.
9. Hablar con Tia y entregar la misión de bienvenida, cuya recompensa declarada es tres unidades de plata.

## Mercado, mula y herramientas

10. Interactuar con el mercado, comprar y recoger una mula, equiparla y montarla.
11. Regresar al artesano de The Cove y aceptar la fabricación de herramientas. Es otro objeto de NPC que comparte el tipo de misiones del primer artesano.
12. Usar el fabricante de herramientas, obtener un pico y un hacha de principiante y entregar. Las recompensas declaradas son una hoz y un cuchillo de principiante.

El mercado del laboratorio ofrece la mula por tres unidades de plata y cada pieza de equipo de nivel dos por setenta y cinco, incluidos escudo y libro. Así, las trescientas unidades de recompensa permiten comprar cuatro piezas. Son precios locales para permitir el recorrido con sus recompensas; los archivos del cliente no demuestran los precios ni las existencias del servidor oficial. Comprar y recoger son operaciones separadas, y la compra descuenta plata real del personaje.

## Recolección de nivel dos y reparación del barco

13. Entrar en Forgotten Woods, abrir el mapa y seguir el punto informativo del bosque.
14. Recolectar seis troncos y seis pieles de nivel dos. Regresar al artesano y entregar.
15. Entrar en la zona de montaña, seguir el punto informativo y recolectar seis minerales y seis fibras de nivel dos. Regresar y entregar.
16. Refinar seis barras, cuatro cueros, seis tablas y cuatro telas de nivel dos.
17. Hablar con el reparador del barco y confirmar expresamente la entrega de los cuatro materiales. La mera proximidad no autoriza consumirlos.
18. Cumplir la espera de reparación de diez segundos y regresar a Tia. La recompensa declarada es tres sopas de principiante.

Los identificadores visuales `T2_HIDE` y `T2_FIBER` aparecen en las misiones pero no tienen círculos estáticos en estos cuatro mapas. Esta ausencia queda registrada en los datos del laboratorio; ambos objetivos dependen de recolectar los objetos reales.

## Tablero del destino y elección de equipo

19. Abrir el tablero del destino, volver al bosque y seguir los marcadores de los campamentos. Hay tres variantes de misión y alternativas explícitas de recorrido; no son tres misiones que deban completarse acumulativamente.
20. Desbloquear `COMBAT_TRAINEE`, regresar a Tia y entregar. La recompensa declarada de cada variante es 300 unidades de plata.
21. Seguir la rama de arma presentada por el juego: arco, espada o bastón de fuego. Los requisitos de fabricación cambian según esa rama.
22. Interactuar con el mercado y adquirir casco, armadura y zapatos de nivel dos. Las ramas con arma de una mano añaden escudo o libro.
23. Alcanzar el poder medio de equipo requerido por la variante y seleccionar las habilidades de cabeza, armadura y zapatos. Regresar a Tia para recibir la capa.

El cálculo de poder de equipo usa los valores reales de cada pieza, incluidos calidad, durabilidad y bonificaciones del tablero; `COMBAT_TRAINEE` aporta una bonificación importante que el valor base del objeto no refleja. Mantiene el promedio de seis posiciones utilizado por el emulador (ambas manos, cabeza, pecho, pies y capa), con las armas de dos manos ocupando ambas posiciones. La fórmula del servidor original requiere contraste manual: los archivos del cliente sí declaran los umbrales de 400 y 240, pero no demuestran el denominador del servidor. El seguimiento recibe el poder numérico calculado, separado de la indicación de objetivo cumplido.

## Fuerte y salida de la isla

24. Volver a la montaña, seguir el punto del fuerte y salir de su círculo cuando ese objetivo lo indique: `triggeronleave` es una condición de salida, no de entrada.
25. Derrotar diez herejes de nivel dos.
26. Seguir el objetivo de munición: el cliente declara la alternativa entre la función de finalizar el tutorial y permanecer dos segundos en el marcador correspondiente.
27. Ir a la señal de fuego y seguir los puntos de llegada del barco, salida y encuentro final. La recompensa declarada es 100 puntos de aprendizaje.
28. Cumplir el requisito de tiempo de juego de 900 segundos y usar el planificador de viaje para abandonar la isla.

El laboratorio interpreta ese requisito como quince minutos de juego acumulado del personaje, sin sumar tiempo desconectado. La duración procede del cliente; la elección de contar desde la creación del personaje requiere contraste manual con el servidor original. Las siguientes misiones se ofrecen para aceptarlas; el significado exacto de `nextquest` en el servidor original también queda pendiente de contraste visual.

El planificador oficial de esta instalación está en Mountain Fort (`1010`). Ofrece cinco destinos de comienzo: `1006`, `4006`, `0004`, `3002` y `2002`. Las misiones posteriores de las ciudades iniciales quedan fuera del recorrido de la isla.

## Qué observar al comprobarlo

En cada entrega comprueba que el primer clic en el NPC habilite la conversación correcta, que «Completar» se habilite cuando corresponde y que la recompensa se conceda una sola vez. Al cambiar de objetivo deben actualizarse el seguimiento, el texto y los marcadores. Reconectar durante una misión debe conservar el progreso.

Con dos personajes, cada uno debe tener sus propios enemigos, cofres y recursos. No deben verse entre sí en los mapas privados del tutorial, y destruir un objeto con uno no debe eliminarlo del escenario del otro.

Si aparece un bloqueo, anota el personaje, el nombre y objetivo visible de la misión, el mapa y la acción exacta que realizaste. Conserva la partida para contrastar ese estado con el registro del servidor; no hace falta borrar la base de datos para investigar.
