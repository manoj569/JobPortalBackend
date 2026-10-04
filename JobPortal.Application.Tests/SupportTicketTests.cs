using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.API.Swagger;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Support;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Storage;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class SupportTicketTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");

    [Fact]
    public async Task GuestCreatesDurableTicketWithServerNumberAndBothEmails()
    {
        using var f = new Fixture();
        f.Email.BeforeSend = () => { Assert.Single(f.Db.SupportTickets); return Task.CompletedTask; };
        var created = await f.Service.CreateAsync(null, Request());
        var ticket = Assert.Single(f.Db.SupportTickets);
        Assert.Null(ticket.UserId);
        Assert.Equal("Guest", ticket.Name);
        Assert.Equal("guest@example.test", ticket.Email);
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.Matches("^CH-[A-F0-9]{32}$", created.TicketNumber);
        Assert.Equal(ticket.TicketNumber, created.TicketNumber);
        Assert.Equal(2, f.Email.Messages.Count);
        Assert.Equal("support@example.test", f.Email.Messages[0].Recipient);
        Assert.Contains(created.TicketNumber, f.Email.Messages[0].Message.Message, StringComparison.Ordinal);
        Assert.Equal("guest@example.test", f.Email.Messages[1].Recipient);
        Assert.All(f.Email.Messages, x => Assert.Null(x.Message.ActionUrl));
        Assert.Equal(0, f.Db.Notifications.Count());
    }

    [Theory]
    [InlineData(null, "guest@example.test")]
    [InlineData("   ", "guest@example.test")]
    [InlineData("Guest", null)]
    [InlineData("Guest", "invalid")]
    [InlineData("Guest", "guest@example.test\r\nBcc:attacker@example.test")]
    public async Task GuestNeedsValidNameAndEmail(string? name, string? email)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(null, Request() with { Name = name, Email = email }));
        Assert.Empty(f.Db.SupportTickets);
        Assert.Empty(f.Email.Messages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task InvalidCategoryIsRejected(int category)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(null, Request() with { Category = (SupportCategory)category }));
        Assert.Empty(f.Db.SupportTickets);
    }

    [Fact]
    public async Task TextLimitsAndWhitespaceAreValidatedBeforeStorage()
    {
        using var f = new Fixture();
        foreach (var request in new[] { Request() with { Subject = new string('a', 201) },
            Request() with { Description = new string('a', 5001) }, Request() with { Subject = "  " },
            Request() with { Description = "  " }, Request() with { Name = new string('a', 202) } })
            await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateAsync(null, request));
        Assert.Empty(f.Db.SupportTickets);
    }

    [Fact]
    public async Task AuthenticatedIdentityComesFromAccountRegardlessOfFormNameEmailOrExtraUserId()
    {
        using var f = new Fixture();
        var user = await f.AddUserAsync();
        var controller = Controller(f.Service, user.Id);
        controller.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            { ["UserId"] = Guid.NewGuid().ToString(), ["TicketNumber"] = "CH-SPOOFED" });
        var action = await controller.Create(new CreateSupportTicketForm { Name = "Spoofed", Email = "attacker@example.test",
            Category = SupportCategory.Login, Subject = "Help", Description = "Cannot sign in" }, default);
        Assert.Equal(201, Assert.IsType<ObjectResult>(action.Result).StatusCode);
        var ticket = Assert.Single(f.Db.SupportTickets);
        Assert.Equal(user.Id, ticket.UserId);
        Assert.Equal("Account Owner", ticket.Name);
        Assert.Equal(user.Email, ticket.Email);
        Assert.NotEqual("CH-SPOOFED", ticket.TicketNumber);
        Assert.DoesNotContain(typeof(CreateSupportTicketForm).GetProperties(), p => p.Name is "UserId" or "TicketNumber" or "Status" or "AdminNotes");
    }

    [Fact]
    public async Task AuthenticatedRequestsDoNotRequireGuestIdentityAndStaleIdentityCannotBecomeGuest()
    {
        using var f = new Fixture();
        var user = await f.AddUserAsync();
        await f.Service.CreateAsync(user.Id, Request() with { Name = null, Email = null });
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.CreateAsync(Guid.NewGuid(), Request()));
        var malformed = Controller(f.Service, null);
        malformed.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        await Assert.ThrowsAsync<UnauthorizedException>(() => malformed.Create(new CreateSupportTicketForm(), default));
        Assert.Single(f.Db.SupportTickets);
    }

    [Fact]
    public async Task GuestControllerDoesNotNeedLogin()
    {
        using var f = new Fixture();
        var controller = Controller(f.Service, null);
        await controller.Create(new CreateSupportTicketForm { Name = "Guest", Email = "guest@example.test",
            Category = SupportCategory.Other, Subject = "Help", Description = "Need support" }, default);
        Assert.Null(Assert.Single(f.Db.SupportTickets).UserId);
    }

    [Fact]
    public async Task MultipartCreationRejectsMultipleAttachments()
    {
        using var f = new Fixture();
        using var first = new MemoryStream(Png);
        using var second = new MemoryStream(Png);
        var controller = Controller(f.Service, null);
        controller.Request.ContentType = "multipart/form-data; boundary=test";
        controller.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { new FormFile(first, 0, Png.Length, "Screenshot", "one.png"),
                new FormFile(second, 0, Png.Length, "Screenshot", "two.png") });
        await Assert.ThrowsAsync<BadRequestException>(() => controller.Create(new CreateSupportTicketForm
            { Name = "Guest", Email = "guest@example.test", Category = SupportCategory.Login, Subject = "Help", Description = "Cannot sign in" }, default));
        Assert.Empty(f.Db.SupportTickets);
        Assert.Empty(f.Email.Messages);
    }

    [Fact]
    public async Task ScreenshotIsValidatedStoredPrivatelyAndOwnershipProtected()
    {
        using var f = new Fixture();
        var user = await f.AddUserAsync();
        using var image = new MemoryStream(Png);
        var created = await f.Service.CreateAsync(user.Id, Request(), new(image, Png.Length, "../../untrusted-name.png", "image/png"));
        var ticket = Assert.Single(f.Db.SupportTickets);
        Assert.NotNull(ticket.ScreenshotPath);
        Assert.DoesNotContain("untrusted-name", ticket.ScreenshotPath, StringComparison.Ordinal);
        var result = await f.Service.GetMyScreenshotAsync(user.Id, created.TicketNumber);
        await using var stream = result.Content;
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        Assert.Equal(Png, bytes.ToArray());
        Assert.Equal("image/png", result.ContentType);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetMyScreenshotAsync(Guid.NewGuid(), created.TicketNumber));
        Assert.True((await f.Service.GetMineAsync(user.Id, created.TicketNumber)).HasScreenshot);
    }

    [Theory]
    [InlineData("file.exe", "image/png", true)]
    [InlineData("file.png", "application/octet-stream", true)]
    [InlineData("file.jpg", "image/jpeg", true)]
    [InlineData("file.png", "image/png", false)]
    public async Task InvalidScreenshotMetadataOrBytesAreRejected(string name, string type, bool imageBytes)
    {
        using var f = new Fixture();
        var content = imageBytes ? Png : "<script>not an image</script>"u8.ToArray();
        using var image = new MemoryStream(content);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(null, Request(), new(image, content.Length, name, type)));
        Assert.Empty(f.Db.SupportTickets);
        Assert.Empty(f.Storage.Content);
    }

    [Fact]
    public async Task ScreenshotMetadataAndActualStreamLimitsAreBothEnforced()
    {
        using var f = new Fixture();
        using var small = new MemoryStream(Png);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(null, Request(),
            new(small, SupportScreenshotValidation.MaximumBytes + 1L, "a.png", "image/png")));
        using var oversized = new MemoryStream(new byte[SupportScreenshotValidation.MaximumBytes + 1]);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.CreateAsync(null, Request(), new(oversized, 1, "a.png", "image/png")));
        Assert.Empty(f.Db.SupportTickets);
        Assert.Empty(f.Storage.Content);
    }

    [Fact]
    public async Task OwnerListsAndReadsOnlyOwnTicketsAndNeverReceivesAdminNotes()
    {
        using var f = new Fixture();
        var owner = await f.AddUserAsync();
        var other = await f.AddUserAsync();
        var first = await f.Service.CreateAsync(owner.Id, Request());
        await f.Service.CreateAsync(other.Id, Request());
        await f.Service.CreateAsync(null, Request() with { Email = owner.Email });
        var ticket = f.Db.SupportTickets.Single(x => x.TicketNumber == first.TicketNumber);
        ticket.AdminNotes = "Internal investigation only";
        await f.Db.SaveChangesAsync();
        var mine = await f.Service.GetMineAsync(owner.Id, new SupportTicketPageQuery());
        Assert.Equal(1, mine.TotalCount);
        Assert.Equal(first.TicketNumber, Assert.Single(mine.Items).TicketNumber);
        var detail = await f.Service.GetMineAsync(owner.Id, first.TicketNumber);
        Assert.DoesNotContain("Internal investigation", JsonSerializer.Serialize(detail), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(SupportTicketResponse).GetProperties(), p => p.Name is "AdminNotes" or "UserId" or "ScreenshotPath");
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetMineAsync(other.Id, first.TicketNumber));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetMyScreenshotAsync(other.Id, first.TicketNumber));
    }

    [Fact]
    public async Task AdminSearchSupportsAllFiltersAndPagination()
    {
        using var f = new Fixture();
        await f.Service.CreateAsync(null, Request());
        var created = await f.Service.CreateAsync(null, Request() with { Category = SupportCategory.Payment, Email = "pay@example.test" });
        var ticket = f.Db.SupportTickets.Single(x => x.TicketNumber == created.TicketNumber);
        var admin = new AdminSupportTicketsController(f.Service) { ControllerContext = Controller(f.Service, Guid.NewGuid()).ControllerContext };
        var detail = await admin.Get(ticket.Id, default);
        Assert.Equal(ticket.Id, Assert.IsType<ApiResponse<AdminSupportTicketResponse>>(Assert.IsType<OkObjectResult>(detail.Result).Value).Data.Id);
        await f.Service.UpdateStatusAsync(Guid.NewGuid(), ticket.Id, new(SupportTicketStatus.InProgress));
        var result = await f.Service.SearchAsync(new(Status: SupportTicketStatus.InProgress, Category: SupportCategory.Payment,
            Email: "PAY@example.test", TicketNumber: created.TicketNumber.ToLowerInvariant(),
            FromUtc: ticket.CreatedAtUtc.AddSeconds(-1), ToUtc: ticket.CreatedAtUtc.AddSeconds(1)));
        Assert.Equal(ticket.Id, Assert.Single(result.Items).Id);
        Assert.Equal(1, result.TotalCount);
        var page = await f.Service.SearchAsync(new(PageNumber: 2, PageSize: 1));
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Empty((await f.Service.SearchAsync(new(Email: "missing@example.test"))).Items);
    }

    [Fact]
    public async Task ResolveReplayAndReopenHaveConsistentTimestampsAndRevision()
    {
        using var f = new Fixture();
        await f.Service.CreateAsync(null, Request());
        var ticket = Assert.Single(f.Db.SupportTickets);
        var actor = Guid.NewGuid();
        var resolved = await f.Service.UpdateStatusAsync(actor, ticket.Id, new(SupportTicketStatus.Resolved));
        Assert.Equal(Fixture.Now, resolved.ResolvedAtUtc);
        Assert.Equal(1, ticket.Revision);
        await f.Service.UpdateStatusAsync(actor, ticket.Id, new(SupportTicketStatus.Resolved));
        Assert.Equal(1, ticket.Revision);
        Assert.Equal(Fixture.Now, ticket.ResolvedAtUtc);
        var reopened = await f.Service.UpdateStatusAsync(actor, ticket.Id, new(SupportTicketStatus.InProgress));
        Assert.Null(reopened.ResolvedAtUtc);
        Assert.Equal(2, ticket.Revision);
        var notes = await f.Service.UpdateNotesAsync(actor, ticket.Id, new(" Investigate "));
        Assert.Equal("Investigate", notes.AdminNotes);
        Assert.NotNull(notes.UpdatedAtUtc);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateStatusAsync(actor, ticket.Id, new((SupportTicketStatus)99)));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateNotesAsync(actor, ticket.Id, new(new string('a', 5001))));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPaginationCannotOverflowOrBecomeUnbounded(int page, int size)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SearchAsync(new(page, size)));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.GetMineAsync(Guid.NewGuid(), new SupportTicketPageQuery(page, size)));
    }

    [Fact]
    public async Task InvalidDateRangesAndEnumsAreRejected()
    {
        using var f = new Fixture();
        foreach (var query in new[] { new AdminSupportTicketQuery(Status: (SupportTicketStatus)0),
            new AdminSupportTicketQuery(Category: (SupportCategory)99),
            new AdminSupportTicketQuery(FromUtc: DateTime.SpecifyKind(Fixture.Now, DateTimeKind.Unspecified)),
            new AdminSupportTicketQuery(FromUtc: Fixture.Now, ToUtc: Fixture.Now) })
            await Assert.ThrowsAsync<ValidationException>(() => f.Service.SearchAsync(query));
    }

    [Theory]
    [InlineData(EmailDeliveryResult.Failed, false)]
    [InlineData(EmailDeliveryResult.Disabled, false)]
    [InlineData(EmailDeliveryResult.Failed, true)]
    public async Task EmailFailureAfterSaveNeverMakesCreationFail(EmailDeliveryResult result, bool throws)
    {
        using var f = new Fixture();
        f.Email.Result = result; f.Email.Throws = throws;
        var response = await f.Service.CreateAsync(null, Request());
        Assert.Equal(response.TicketNumber, Assert.Single(f.Db.SupportTickets).TicketNumber);
        Assert.Equal(2, f.Email.Attempts);
    }

    [Fact]
    public async Task SaveFailureRemovesScreenshotAndNeverSendsEmail()
    {
        using var f = new Fixture();
        var service = f.MakeService(new FailingUnitOfWork());
        using var screenshot = new MemoryStream(Png);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(null, Request(), new(screenshot, Png.Length, "a.png", "image/png")));
        Assert.Empty(f.Storage.Content);
        Assert.Empty(f.Email.Messages);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(f.Db.SupportTickets);
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Candidate", false)]
    [InlineData("Employer", false)]
    [InlineData(null, false)]
    public async Task EveryAdminEndpointRequiresExistingAdministratorRole(string? role, bool expected)
    {
        var type = typeof(AdminSupportTicketsController);
        var attributes = type.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        Assert.Equal("Administrator", Assert.Single(attributes).Roles);
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.All(type.GetMethods().Where(m => m.DeclaringType == type && m.GetCustomAttributes<HttpMethodAttribute>().Any()),
            m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
        using var provider = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var policy = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), attributes);
        var principal = role is null ? new ClaimsPrincipal(new ClaimsIdentity()) :
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy!);
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public void OnlyCreationIsAnonymousAndItHasUploadAndRateLimits()
    {
        var type = typeof(SupportTicketsController);
        Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>());
        var actions = type.GetMethods().Where(m => m.DeclaringType == type && m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();
        Assert.Equal(nameof(SupportTicketsController.Create), Assert.Single(actions, m => m.IsDefined(typeof(AllowAnonymousAttribute))).Name);
        var create = type.GetMethod(nameof(SupportTicketsController.Create))!;
        Assert.Equal("SupportTickets", create.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Contains("multipart/form-data", create.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.Equal(6 * 1024 * 1024, ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)
            create.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
    }

    [Fact]
    public async Task PrivateFileStorageGeneratesKeysAndRejectsTraversalAndPublicRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "careerharbor-support-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["SupportSettings:ScreenshotRootPath"] = root }).Build();
        var storage = new LocalSupportScreenshotStorage(config);
        string? key = null;
        try
        {
            using var image = new MemoryStream(Png);
            key = await storage.StoreAsync(image, ".png");
            Assert.Matches("^[a-f0-9]{32}\\.png$", key);
            await using var opened = await storage.OpenReadAsync(key);
            Assert.NotNull(opened);
            using var result = new MemoryStream();
            await opened.CopyToAsync(result);
            Assert.Equal(Png, result.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../private.png"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("..\\private.png"));
        }
        finally
        {
            if (key is not null) await storage.DeleteAsync(key);
            if (Directory.Exists(root)) Directory.Delete(root);
        }
        var publicConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["SupportSettings:ScreenshotRootPath"] = Path.Combine(root, "wwwroot", "screenshots") }).Build();
        Assert.Throws<InvalidOperationException>(() => new LocalSupportScreenshotStorage(publicConfig));
    }

    [Fact]
    public void SwaggerDescribesOptionalAuthAndMultipartScreenshot()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Services.AddControllers().AddApplicationPart(typeof(SupportTicketsController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Support test", Version = "v1" });
            options.OperationFilter<SupportTicketsOperationFilter>();
        });
        using var app = builder.Build();
        var swagger = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = swagger.Paths["/api/support/tickets"].Operations[OperationType.Post];
        Assert.Empty(operation.Security);
        var form = operation.RequestBody.Content["multipart/form-data"].Schema;
        if (form.Reference is { } reference) form = swagger.Components.Schemas[reference.Id];
        var screenshot = form.Properties.Single(p => p.Key.Equals("Screenshot", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal("binary", screenshot.Format);
        Assert.DoesNotContain(form.Required, p => p.Equals("Screenshot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(form.Properties.Keys, p => p.Equals("UserId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("/api/admin/support/tickets/{id}/status", swagger.Paths.Keys);
    }

    [Fact]
    public void PostgreSqlMappingHasNullableOwnershipUniqueNumberAndConcurrencyWithoutShadowFields()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var ticket = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SupportTicket))!;
        Assert.True(ticket.FindProperty(nameof(SupportTicket.UserId))!.IsNullable);
        Assert.True(ticket.FindProperty(nameof(SupportTicket.Revision))!.IsConcurrencyToken);
        Assert.Contains(ticket.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(SupportTicket.TicketNumber));
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(ticket.GetForeignKeys()).DeleteBehavior);
        Assert.DoesNotContain(ticket.GetProperties(), p => p.IsShadowProperty());
        Assert.Equal(3, ticket.GetCheckConstraints().Count());
    }

    [Fact]
    public async Task CompetingAdminUpdatesCannotSilentlyOverwriteEachOther()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var first = new JobPortalDbContext(options);
        using var second = new JobPortalDbContext(options);
        var ticket = new SupportTicket { TicketNumber = "CH-TEST", Name = "Guest", Email = "guest@example.test",
            Category = SupportCategory.Other, Subject = "Help", Description = "Need support" };
        first.SupportTickets.Add(ticket);
        await first.SaveChangesAsync();
        var stale = await second.SupportTickets.SingleAsync();
        ticket.AdminNotes = "First admin update";
        ticket.Revision++;
        await first.SaveChangesAsync();
        stale.Status = SupportTicketStatus.Resolved;
        stale.ResolvedAtUtc = Fixture.Now;
        stale.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.ChangeTracker.Clear();
        var saved = await second.SupportTickets.SingleAsync();
        Assert.Equal("First admin update", saved.AdminNotes);
        Assert.Equal(SupportTicketStatus.Open, saved.Status);
    }

    private static CreateSupportTicketRequest Request() => new("Guest", "guest@example.test", SupportCategory.Login, "Unable to login", "Please help with sign in.");
    private static SupportTicketsController Controller(ISupportTicketService service, Guid? id) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = id is null ? new ClaimsPrincipal(new ClaimsIdentity()) :
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.Value.ToString())], "test"))
        } }
    };

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTime Now = new(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc);
        public JobPortalDbContext Db { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public MemoryStorage Storage { get; } = new();
        public FakeEmail Email { get; } = new();
        public SupportTicketService Service => MakeService(new UnitOfWork(Db));
        public SupportTicketService MakeService(IUnitOfWork unit) => new(new SupportTicketRepository(Db), new UserRepository(Db), unit,
            Storage, Email, Options.Create(new SupportSettings { SupportEmail = "support@example.test" }), new Clock(),
            NullLogger<SupportTicketService>.Instance, new CreateSupportTicketRequestValidator(), new SupportTicketPageQueryValidator(),
            new AdminSupportTicketQueryValidator(), new UpdateSupportTicketStatusRequestValidator(), new UpdateSupportTicketNotesRequestValidator());
        public async Task<User> AddUserAsync()
        {
            var user = new User { FirstName = "Account", LastName = "Owner", Email = Guid.NewGuid().ToString("N") + "@example.test",
                Role = new Role { Name = "Candidate" }, Status = UserStatus.Active };
            Db.Users.Add(user); await Db.SaveChangesAsync(); return user;
        }
        public void Dispose() => Db.Dispose();
        private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    }

    private sealed class MemoryStorage : ISupportScreenshotStorage
    {
        public Dictionary<string, byte[]> Content { get; } = [];
        public async Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        {
            using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, cancellationToken);
            var key = Guid.NewGuid().ToString("N") + extension; Content.Add(key, bytes.ToArray()); return key;
        }
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Content.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) { Content.Remove(storageKey); return Task.CompletedTask; }
    }

    private sealed class FakeEmail : IEmailService
    {
        public List<(string Recipient, Notification Message)> Messages { get; } = [];
        public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent;
        public bool Throws { get; set; }
        public int Attempts { get; private set; }
        public Func<Task>? BeforeSend { get; set; }
        public async Task<EmailDeliveryResult> SendNotificationAsync(User user, Notification notification, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (BeforeSend is not null) await BeforeSend();
            if (Throws) throw new InvalidOperationException("Email provider failed");
            Messages.Add((user.Email, notification)); return Result;
        }
        public Task<EmailDeliveryResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EmailDeliveryResult> SendApplicationStatusAsync(User user, string jobTitle, JobApplicationStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EmailDeliveryResult> SendRegistrationVerificationAsync(User user, string rawToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Save failed");
    }
}
