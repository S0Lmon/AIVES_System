using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Email;
using AIVES.DAL.Data;
using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIVES.Tests;

public sealed class GoogleAccountTests
{
    private static AccountService Service(Store store) => new(store, new Verification(), new Sender(),
        new ConfigurationBuilder().Build(), NullLogger<AccountService>.Instance);

    [Fact]
    public async Task MissingProviderInformationCannotSignIn()
    {
        var store = new Store { Info = null };
        Assert.False((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.SignedIn);
    }
    [Fact]
    public async Task MissingGoogleEmailCannotCreateAnAccount()
    {
        var store = new Store { Info = new("Google", "key", null, "Test") };
        Assert.False((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.Created);
    }
    [Fact]
    public async Task ExistingExternalLoginDoesNotCreateAnotherUser()
    {
        var store = new Store { ExistingLoginSucceeded = true };
        Assert.True((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.Created);
    }
    [Fact]
    public async Task NewGoogleUserIsCreatedLinkedAndSignedIn()
    {
        var store = new Store();
        Assert.True((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.True(store.Created);
        Assert.True(store.Linked);
        Assert.True(store.SignedIn);
    }
    [Fact]
    public async Task ExistingEmailUserIsLinkedWithoutDuplicateCreation()
    {
        var store = new Store { User = new("user", "test@gmail.com", "Test", false) };
        Assert.True((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.Created);
        Assert.True(store.Linked);
        Assert.True(store.SignedIn);
    }
    [Fact]
    public async Task FailedLinkDoesNotSignIn()
    {
        var store = new Store { LinkSucceeded = false };
        Assert.False((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.SignedIn);
    }
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task BlockedExternalLoginCannotFallBackToDirectSignIn(bool locked, bool notAllowed, bool twoFactor)
    {
        var store = new Store { SignInResult = new(false, locked, notAllowed, twoFactor) };
        Assert.False((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.Created);
        Assert.False(store.Linked);
        Assert.False(store.SignedIn);
    }
    [Fact]
    public async Task AlreadyAssociatedLoginErrorCannotCreateSession()
    {
        var store = new Store { LinkSucceeded = false, LinkError = "LoginAlreadyAssociated" };
        Assert.False((await Service(store).CompleteGoogleLoginAsync()).Succeeded);
        Assert.False(store.SignedIn);
    }
    private sealed class Sender : IAppEmailSender
    {
        public bool IsConfigured => true;
        public Task SendVerificationCodeAsync(string email, string name, string code, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Verification : IEmailVerificationService
    {
        public Task IssueAndSendAsync(string id, string email, string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> VerifyAsync(string id, string code, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Store : IAccountStore
    {
        public ExternalLoginDto? Info { get; init; } = new("Google", "provider-key", "test@gmail.com", "Test");
        public UserDto? User
        {
            get; init;
        }
        public bool ExistingLoginSucceeded
        {
            get; init;
        }
        public ExternalSignInResultDto? SignInResult
        {
            get; init;
        }
        public string LinkError { get; init; } = "Link failed";
        public bool LinkSucceeded { get; init; } = true;
        public bool Created
        {
            get; private set;
        }
        public bool Linked
        {
            get; private set;
        }
        public bool SignedIn
        {
            get; private set;
        }
        public Task<ExternalLoginDto?> GetExternalLoginInfoAsync() => Task.FromResult(Info);
        public Task<ExternalSignInResultDto> ExternalLoginSignInAsync(string provider, string key) => Task.FromResult(SignInResult ?? new(ExistingLoginSucceeded));
        public Task<UserDto?> FindByEmailAsync(string email) => Task.FromResult(User);
        public Task<UserProfileDto?> GetProfileAsync(string userId) => Task.FromResult<UserProfileDto?>(null);
        public Task<AccountResult> CreateAsync(RegisterRequest request, bool confirmed = false)
        {
            Assert.True(confirmed);
            Created = true;
            return Task.FromResult(AccountResult.Success(new("user", request.Email, request.DisplayName, true)));
        }
        public Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync() => throw new NotSupportedException();
        public Task<AccountResult> SetAssignableRoleAsync(string userId, string role) => throw new NotSupportedException();
        public Task<AccountResult> AddLoginAsync(string userId, ExternalLoginDto info)
        {
            Linked = true;
            return Task.FromResult(LinkSucceeded ? AccountResult.Success() : AccountResult.Failure(LinkError));
        }
        public Task ConfirmEmailAsync(string id) => Task.CompletedTask;
        public Task SignInAsync(string id)
        {
            SignedIn = true;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public Task<AccountResult> PasswordSignInAsync(string email, string password, bool rememberMe) => throw new NotSupportedException();
        public Task SignOutAsync() => throw new NotSupportedException();
        public AuthenticationProperties ConfigureExternalAuthenticationProperties(string provider, string? redirectUrl) => throw new NotSupportedException();
    }
}
