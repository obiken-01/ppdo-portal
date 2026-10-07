using Microsoft.Extensions.Logging;
using Moq;
using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Asserts the PPDO-193 "AIP write without rowVersion" warning (<see cref="AipUnguardedWrite"/>),
/// which is PPDO-121's entry condition. Matched on the message prefix, the text App Insights is
/// searched for, so a rewording fails here before it silently empties that search.
/// </summary>
internal static class UnguardedWriteWarning
{
    public static void Verify<T>(Mock<ILogger<T>> logger, Times times) =>
        logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.StartsWith(AipUnguardedWrite.MessagePrefix)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
