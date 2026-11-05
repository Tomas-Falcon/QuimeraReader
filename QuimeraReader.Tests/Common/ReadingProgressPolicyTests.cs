using System;
using FluentAssertions;
using QuimeraReader.Application.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class ReadingProgressPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 20, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, 100.0, "Read")]          // última página
    [InlineData("Reading", 99.95, "Read")]     // redondeo de coma flotante
    [InlineData("Reading", 40.0, "Reading")]
    [InlineData("Unread", 10.0, "Reading")]
    [InlineData("NextToRead", 5.0, "Reading")]
    [InlineData("Read", 3.0, "Read")]          // releer no lo cambia solo: lo pide el cliente al pasar de página
    [InlineData("Read", null, "Read")]
    public void StatusAfterProgress_FollowsTheReadingRules(string? current, double? percentage, string expected)
    {
        ReadingProgressPolicy.StatusAfterProgress(current, percentage).Should().Be(expected);
    }

    [Fact]
    public void LateOfflineSync_OlderThanStoredProgress_IsStale()
    {
        // Arrange: en el servidor se leyó a las 20:26; el móvil sube lo que leyó offline a las 19:10
        var stored = new DateTime(2026, 11, 3, 20, 26, 45, DateTimeKind.Unspecified);
        var readAt = ReadingProgressPolicy.ResolveReadAt(new DateTime(2026, 11, 3, 19, 10, 0, DateTimeKind.Utc), Now);

        // Act / Assert
        ReadingProgressPolicy.IsStale(stored, readAt).Should().BeTrue();
    }

    [Fact]
    public void OfflineReading_NewerThanStoredProgress_IsApplied()
    {
        var stored = new DateTime(2026, 11, 3, 18, 0, 0, DateTimeKind.Unspecified);
        var readAt = ReadingProgressPolicy.ResolveReadAt(new DateTime(2026, 11, 3, 19, 10, 0, DateTimeKind.Utc), Now);

        ReadingProgressPolicy.IsStale(stored, readAt).Should().BeFalse();
    }

    [Fact]
    public void LiveReading_WithoutReadAt_UsesNow_AndIsNeverStale()
    {
        var readAt = ReadingProgressPolicy.ResolveReadAt(null, Now);

        readAt.Should().Be(Now);
        ReadingProgressPolicy.IsStale(Now.AddMinutes(-1), readAt).Should().BeFalse();
    }

    [Fact]
    public void ReadAt_InTheFuture_IsClampedToNow()
    {
        // Reloj del dispositivo adelantado: no puede "ganar" a lecturas futuras reales
        ReadingProgressPolicy.ResolveReadAt(Now.AddHours(2), Now).Should().Be(Now);
    }

    [Fact]
    public void BookNeverRead_AcceptsAnyProgress()
    {
        ReadingProgressPolicy.IsStale(null, Now.AddDays(-30)).Should().BeFalse();
    }
}
