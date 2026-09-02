using System;

namespace ShoroCraftLauncher.Infrastructure;

/// <summary>
/// Frases melancólicas al estilo de la legendaria advertencia de Minecraft
/// ("¿Estás seguro de que quieres eliminar este mundo para siempre? ¡Para siempre es mucho tiempo!").
/// Se muestran en consola al crear, editar o eliminar perfiles, mundos y servidores.
/// </summary>
public static class ConsolePhrases
{
    private static readonly Random _random = new();

    private static readonly string[] Delete =
    [
        "\"Para siempre\" es mucho tiempo... el olvido es absoluto.",
        "El sol nunca volverá a salir sobre esas tierras...",
        "Hágase la nada. Este universo acaba de colapsar.",
        "Demoliste un continente entero de recuerdos que nadie más llegó a ver.",
        "Las flores que plantaste ya no crecerán, y los lobos se quedaron esperando en la entrada."
    ];

    private static readonly string[] Create =
    [
        "Un nuevo amanecer ha comenzado. Eres el primer habitante de esta tierra virgen.",
        "Todo gran imperio comenzó siendo un bloque de tierra bajo la lluvia.",
        "Generando cordilleras... sembrando bosques... creando un lugar al que pronto llamarás \"hogar\".",
        "Estás frente a un lienzo en blanco: cada paso que des será la primera línea de una historia sin escribir.",
        "Cuídalo, porque tú eres la única razón por la que este mundo respira."
    ];

    private static readonly string[] Edit =
    [
        "Estás alterando las leyes de este universo; procede con respeto por tu propio pasado.",
        "Puedes cambiarle el nombre, pero la esencia de lo que construiste se quedará en los cimientos antiguos.",
        "Editar es jugar a ser un dios: ten cuidado de no romper la magia mientras intentas hacerla perfecta.",
        "Reescribes la historia de estas tierras... los ríos recuerdan su antiguo cauce.",
        "Cada cambio reescribe la memoria del mundo. Que sea para bien."
    ];

    public static string PickDelete() => Delete[_random.Next(Delete.Length)];
    public static string PickCreate() => Create[_random.Next(Create.Length)];
    public static string PickEdit() => Edit[_random.Next(Edit.Length)];
}
