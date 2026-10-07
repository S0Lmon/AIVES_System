extern alias Razor;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RazorHub = Razor::AIVES.WebRazor.Realtime;

namespace AIVES.Tests;

public sealed class PresenceTrackerTests
{
    [Fact]
    public void SeveralTabsOfOneUserCountAsOneOnlineUser()
    {
        var presence = new RazorHub.PresenceTracker();
        presence.Connect("c1", "u1", "an@fpt.edu.vn");
        presence.Connect("c2", "u1", "an@fpt.edu.vn");
        presence.Connect("c3", "u2", "binh@fpt.edu.vn");

        var online = presence.OnlineUsers();

        Assert.Equal(2, online.Count);
        Assert.Equal(2, online.Single(user => user.UserId == "u1").Connections);
    }

    [Fact]
    public void DisconnectReturnsTheQuestionsTheConnectionWasEditing()
    {
        var presence = new RazorHub.PresenceTracker();
        presence.Connect("c1", "u1", "an");
        presence.Connect("c2", "u2", "binh");
        presence.StartEditing("c1", 7);
        presence.StartEditing("c2", 7);

        Assert.Equal(["an", "binh"], presence.Editors(7));
        Assert.Equal([7], presence.Disconnect("c1"));
        Assert.Equal(["binh"], presence.Editors(7));
        Assert.Empty(presence.Disconnect("unknown"));
    }

    [Fact]
    public void StopEditingReportsWhetherAnythingChanged()
    {
        var presence = new RazorHub.PresenceTracker();
        presence.Connect("c1", "u1", "an");
        presence.StartEditing("c1", 3);

        Assert.True(presence.StopEditing("c1", 3));
        Assert.False(presence.StopEditing("c1", 3));
        Assert.Empty(presence.Editors(3));
    }

    [Fact]
    public void RazorPresentationLayerDoesNotReferenceDataAccess()
    {
        var references = typeof(RazorHub.AivesHub).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToList();
        Assert.Contains("AIVES.BLL", references);
        Assert.DoesNotContain("AIVES.DAL", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
    }
}

/// <summary>
/// Hosts the Razor Pages site in memory and connects real SignalR clients to it. No database
/// is touched: users come from a header-based test scheme and nothing here reads data.
/// </summary>
public sealed class RazorRealtimeTests : IClassFixture<RazorRealtimeTests.RazorApp>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly RazorApp app;

    public RazorRealtimeTests(RazorApp app) => this.app = app;

    [Fact]
    public async Task StaffPagesRedirectAnonymousVisitorsToLogin()
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Questions");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task HubRefusesAnonymousConnections()
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(RazorHub.AivesHub.Path + "/negotiate?negotiateVersion=1", null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangesReachStaffButNotStudents()
    {
        await using var lecturer = await ConnectAsync("lecturer-1", "giangvien@fpt.edu.vn", AppRoles.Lecturer);
        await using var student = await ConnectAsync("student-1", "sinhvien@fpt.edu.vn", AppRoles.Student);
        var lecturerChanges = Listen<RazorHub.EntityChange>(lecturer, "EntityChanged");
        var studentChanges = Listen<RazorHub.EntityChange>(student, "EntityChanged");

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RazorHub.ILiveUpdates>()
                .EntityChangedAsync(RazorHub.LiveEntities.Question, RazorHub.LiveActions.Created, 42, "Giải thích mô hình 3 lớp");

        var change = await ReadAsync(lecturerChanges);
        Assert.Equal("question", change.Entity);
        Assert.Equal("created", change.Action);
        Assert.Equal(42, change.Id);
        Assert.Equal("Giải thích mô hình 3 lớp", change.Title);

        // Question content is exam material, so students must never be sent it.
        await Task.Delay(300);
        Assert.False(studentChanges.TryRead(out _));
    }

    [Fact]
    public async Task EveryoneSeesWhoIsOnline()
    {
        await using var first = await ConnectAsync("presence-a", "a@fpt.edu.vn", AppRoles.Lecturer);
        var updates = Listen<IReadOnlyList<RazorHub.OnlineUser>>(first, "PresenceChanged");
        await using var second = await ConnectAsync("presence-b", "b@fpt.edu.vn", AppRoles.Student);

        var online = await ReadUntilAsync(updates, users => users.Any(user => user.UserId == "presence-b"));
        Assert.Contains(online, user => user.UserId == "presence-a");

        await second.DisposeAsync();
        await ReadUntilAsync(updates, users => users.All(user => user.UserId != "presence-b"));
    }

    [Fact]
    public async Task EditorsOfTheSameQuestionSeeEachOther()
    {
        await using var first = await ConnectAsync("editor-a", "an@fpt.edu.vn", AppRoles.Lecturer);
        await using var second = await ConnectAsync("editor-b", "binh@fpt.edu.vn", AppRoles.Admin);
        var editors = Channel.CreateUnbounded<(int QuestionId, IReadOnlyList<string> Names)>();
        first.On<int, IReadOnlyList<string>>("EditorsChanged", (id, names) => editors.Writer.TryWrite((id, names)));

        await first.InvokeAsync("JoinQuestion", 900);
        await second.InvokeAsync("JoinQuestion", 900);

        var seen = await ReadUntilAsync(editors.Reader, update => update.Names.Count == 2);
        Assert.Equal(900, seen.QuestionId);
        Assert.Equal(["an@fpt.edu.vn", "binh@fpt.edu.vn"], seen.Names);

        await second.InvokeAsync("LeaveQuestion", 900);
        seen = await ReadUntilAsync(editors.Reader, update => update.Names.Count == 1);
        Assert.Equal(["an@fpt.edu.vn"], seen.Names);
    }

    [Fact]
    public async Task StudentsCannotJoinAQuestionEditingGroup()
    {
        await using var student = await ConnectAsync("student-2", "sv2@fpt.edu.vn", AppRoles.Student);

        await Assert.ThrowsAsync<HubException>(() => student.InvokeAsync("JoinQuestion", 1));
    }

    private async Task<HubConnection> ConnectAsync(string userId, string name, string role)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, RazorHub.AivesHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = userId;
                options.Headers[TestAuthHandler.NameHeader] = name;
                options.Headers[TestAuthHandler.RoleHeader] = role;
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static ChannelReader<T> Listen<T>(HubConnection connection, string method)
    {
        var channel = Channel.CreateUnbounded<T>();
        connection.On<T>(method, message => channel.Writer.TryWrite(message));
        return channel.Reader;
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await reader.ReadAsync(timeout.Token);
    }

    private static async Task<T> ReadUntilAsync<T>(ChannelReader<T> reader, Func<T, bool> match)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            var message = await reader.ReadAsync(timeout.Token);
            if (match(message))
                return message;
        }
    }

    public sealed class RazorApp : WebApplicationFactory<Razor::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Nothing in these tests reaches the database; an unreachable server proves it.
                ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Database=None;User Id=none;Password=none;Connect Timeout=1;TrustServerCertificate=True",
                ["Database:MigrateOnStartup"] = "false",
                ["Grading:WorkerEnabled"] = "false",
                ["Authentication:Google:ClientId"] = "",
                ["Logging:LogLevel:Default"] = "Error"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, null);
                // Read identities from test headers, but keep the cookie challenge so anonymous
                // page requests still redirect to the login page.
                services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestAuthHandler.Scheme);
            });
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";
        public const string UserHeader = "X-Test-User";
        public const string NameHeader = "X-Test-Name";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, Request.Headers[NameHeader].ToString()),
                new Claim(ClaimTypes.Role, Request.Headers[RoleHeader].ToString())
            ], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
