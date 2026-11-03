using Azure.Identity;
using Azure.Messaging.ServiceBus;
using ServiceBusThroughputTest.Common;
using ThroughputTest;

namespace ServiceBusThroughputTestLib.Extensions;

static class Extensions
{
    internal static ServiceBusClient CreateClient(Settings settings) =>
        !string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? new ServiceBusClient(settings.ConnectionString)
            : new ServiceBusClient(settings.FullyQualifiedNamespace, new DefaultAzureCredential());

    internal static void RecordFailure(Exception exception, ILogger? logger, Guid? session)
    {
        logger?.AddTrace(exception is ServiceBusException sb ? $"Service Bus failed: {sb.Reason}" : $"Operation failed: {exception.GetType().Name}");
        if (session is not { } id) return;
        logger!.IncrementErrorCount(id);
        if (exception is ServiceBusException { Reason: ServiceBusFailureReason.ServiceBusy })
            logger.IncrementBusyErrorCount(id);
    }
}
