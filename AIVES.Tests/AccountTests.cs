using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Email;
using AIVES.DAL.Data;
using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIVES.Tests;

public sealed class AccountTests
{
    private static AccountService Service(FakeStore store, FakeVerification verification, Dictionary<string, string?>? settings = null) => new(store, verification,
        new FakeSender(), new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), NullLogger<AccountService>.Instance);

    [Fact]
    public async Task NonGmailRegistrationDoesNotCreateAnAccount()
    {
        var store = new FakeStore();
        var result = await Service(store, new()).RegisterAsync(new("test@example.com", "password", "Test"));
        Assert.False(result.Succeeded);
        Assert.False(store.Created);
        Assert.Contains("@gmail.com", result.Errors.Single());
    }

    [Theory]
    [InlineData("student@fpt.edu.vn")]
    [InlineData("Student@FPT.EDU.VN")]
    [InlineData("someone@gmail.com")]
    public async Task ConfiguredDomainsCanRegister(string email)
    {
        var store = new FakeStore();
        var result = await Service(store, new(), new()
        {
            ["Registration:AllowedEmailDomains:0"] = "gmail.com",
            ["Registration:AllowedEmailDomains:1"] = "@fpt.edu.vn"
        }).RegisterAsync(new(email, "password", "Test"));
        Assert.True(result.Succeeded);
        Assert.True(store.Created);
    }

    [Theory]
    [InlineData("user@notgmail.com")]
    [InlineData("user@gmail.com.example.org")]
    [InlineData("user@mail.fpt.edu.vn")]
    [InlineData("fpt.edu.vn")]
    public async Task LookalikeDomainsAreRejected(string email)
    {
        var store = new FakeStore();
        var result = await Service(store, new(), new()
        {
            ["Registration:AllowedEmailDomains"] = "gmail.com, fpt.edu.vn"
        }).RegisterAsync(new(email, "password", "Test"));
        Assert.False(result.Succeeded);
        Assert.False(store.Created);
    }

    [Fact]
    public void AllowedDomainsAcceptACommaSeparatedValueAndDefaultToGmail()
    {
        Assert.Equal(["gmail.com"], Service(new(), new()).AllowedEmailDomains);
        Assert.Equal(["fpt.edu.vn", "gmail.com"], Service(new(), new(), new()
        {
            ["Registration:AllowedEmailDomains"] = " FPT.edu.vn ,@gmail.com,,fpt.edu.vn"
        }).AllowedEmailDomains);
    }

    [Fact]
    public async Task FailedVerificationEmailRollsBackNewAccount()
    {
        var store = new FakeStore();
        var result = await Service(store, new()
        {
            FailSending = true
        }).RegisterAsync(new(" test@gmail.com ", "password", " Test "));
        Assert.False(result.Succeeded);
        Assert.True(store.Created);
        Assert.True(store.Deleted);
        Assert.Equal("test@gmail.com", store.Request!.Email);
        Assert.Equal("Test", store.Request.DisplayName);
    }

    [Fact]
    public async Task InvalidVerificationDoesNotConfirmOrSignIn()
    {
        var store = new FakeStore();
        var result = await Service(store, new()).VerifyEmailAsync("test@gmail.com", "000000");
        Assert.False(result.Succeeded);
        Assert.False(store.Confirmed);
        Assert.False(store.SignedIn);
    }

    private sealed class FakeSender : IAppEmailSender
    {
        public bool IsConfigured => true;
        public Task SendVerificationCodeAsync(string email, string name, string code, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeVerification : IEmailVerificationService
    {
        public bool FailSending
        {
            get; init;
        }
        public Task IssueAndSendAsync(string userId, string email, string name, CancellationToken ct = default)
            => FailSending ? Task.FromException(new InvalidOperationException("SMTP failure")) : Task.CompletedTask;
        public Task<bool> VerifyAsync(string userId, string code, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeStore : IAccountStore
    {
        public bool Created
        {
            get; private set;
        }
        public bool Deleted
        {
            get; private set;
        }
        public bool Confirmed
        {
            get; private set;
        }
        public bool SignedIn
        {
            get; private set;
        }
        public RegisterRequest? Request
        {
            get; private set;
        }
        private static UserDto User => new("user", "test@gmail.com", "Test", false);
        public Task<UserDto?> FindByEmailAsync(string email) => Task.FromResult<UserDto?>(User);
        public Task<UserProfileDto?> GetProfileAsync(string userId) => Task.FromResult<UserProfileDto?>(null);
        public Task<AccountResult> CreateAsync(RegisterRequest request, bool confirmed = false)
        {
            Created = true;
            Request = request;
            return Task.FromResult(AccountResult.Success(User));
        }
        public Task DeleteAsync(string id)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
        public Task ConfirmEmailAsync(string id)
        {
            Confirmed = true;
            return Task.CompletedTask;
        }
        public Task SignInAsync(string id)
        {
            SignedIn = true;
            return Task.CompletedTask;
        }
        public Task<AccountResult> PasswordSignInAsync(string email, string password, bool rememberMe) => throw new NotSupportedException();
        public Task SignOutAsync() => throw new NotSupportedException();
        public AuthenticationProperties ConfigureExternalAuthenticationProperties(string provider, string? redirectUrl) => throw new NotSupportedException();
        public Task<ExternalLoginDto?> GetExternalLoginInfoAsync() => throw new NotSupportedException();
        public Task<ExternalSignInResultDto> ExternalLoginSignInAsync(string provider, string key) => throw new NotSupportedException();
        public Task<AccountResult> AddLoginAsync(string userId, ExternalLoginDto info) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync() => throw new NotSupportedException();
        public Task<AccountResult> SetAssignableRoleAsync(string userId, string role) => throw new NotSupportedException();
        public Task<AccountResult> SetDisabledAsync(string userId, bool disabled) => throw new NotSupportedException();
    }
}
