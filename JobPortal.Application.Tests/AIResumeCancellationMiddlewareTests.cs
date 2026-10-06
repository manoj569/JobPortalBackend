using JobPortal.API.Middleware;
using JobPortal.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumeCancellationMiddlewareTests
{
    [Fact]
    public async Task DisconnectedClientAfterProviderCleanupDoesNotSerializeAnErrorToAnAbortedResponse()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ =>
        {
            cancellation.Cancel();
            throw new AppException("Resume analysis is temporarily unavailable.", 503, "invalid_analysis");
        }, NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task DisconnectDuringErrorSerializationIsHandledWithoutAnotherUnhandledCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = new DisconnectingStream(cancellation);
        var middleware = new GlobalExceptionMiddleware(_ => throw new AppException("Resume analysis is temporarily unavailable.", 503, "invalid_analysis"),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(499, context.Response.StatusCode);
    }

    private sealed class DisconnectingStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }
}
