using System;

namespace QuimeraReader.Application.Common;

/// <summary>
/// Decide si una actualización de progreso de lectura debe aplicarse. Gana la lectura más reciente
/// según cuándo se leyó (no cuándo llegó al servidor): así un dispositivo que sincroniza tarde
/// su progreso offline no pisa lo leído después en otro dispositivo.
/// </summary>
public static class ReadingProgressPolicy
{
    /// <summary>
    /// Momento de lectura efectivo en UTC. Sin valor del cliente (lectura en directo) es "ahora";
    /// un reloj de dispositivo adelantado nunca puede fechar una lectura en el futuro.
    /// </summary>
    public static DateTime ResolveReadAt(DateTime? clientReadAt, DateTime utcNow)
    {
        if (!clientReadAt.HasValue) return utcNow;
        var readAt = AsUtc(clientReadAt.Value);
        return readAt > utcNow ? utcNow : readAt;
    }

    /// <summary>
    /// true si el progreso que llega es más antiguo que el ya guardado y debe descartarse.
    /// </summary>
    public static bool IsStale(DateTime? currentLastReadAt, DateTime readAt)
    {
        return currentLastReadAt.HasValue && AsUtc(readAt) < AsUtc(currentLastReadAt.Value);
    }

    // SQLite devuelve las fechas sin Kind; en esta app siempre se guardan en UTC
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
