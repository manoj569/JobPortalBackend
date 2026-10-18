using JobPortal.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task ClientCancellationUses499()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new OperationCanceledException(cancellation.Token),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task InternalDependencyTimeoutIs503AndNot499()
    {
        var context = new DefaultHttpContext();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new TimeoutException(),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }
}
