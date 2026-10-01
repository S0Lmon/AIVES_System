using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AIVES.DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIVES.Tests;

public sealed class FunctionalTests(FunctionalApp app) : IClassFixture<FunctionalApp>
{
    [SqlTheory]
    [InlineData("locked")]
    [InlineData("unconfirmed")]
    [InlineData("two-factor")]
    public async Task DirectSignInRejectsRestrictedAccounts(string restriction)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AIVES.DAL.Entities.ApplicationUser>>();
        var user = new AIVES.DAL.Entities.ApplicationUser
        {
            UserName = Email(),
            Email = Email(),
            DisplayName = "Restricted Test",
            EmailConfirmed = restriction != "unconfirmed",
            LockoutEnabled = true,
            LockoutEnd = restriction == "locked" ? DateTimeOffset.UtcNow.AddMinutes(10) : null,
            TwoFactorEnabled = restriction == "two-factor"
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SignInAsync(user.Id));
    }
    private const string Password = "Functional123!";
    private static string Email() => "aives.test." + Guid.NewGuid().ToString("N") + "@gmail.com";
    private static async Task<string> Html(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static async Task<HttpResponseMessage> Post(HttpClient browser, string page, string action, Dictionary<string, string> values)
    {
        var form = await browser.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var match = Regex.Match(await form.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The form must contain an antiforgery token.");
        values["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await browser.PostAsync(action, new FormUrlEncodedContent(values));
    }

    private static Dictionary<string, string> Registration(string email) => new()
    {
        ["DisplayName"] = "Functional Tester",
        ["Email"] = email,
        ["Password"] = Password,
        ["ConfirmPassword"] = Password
    };
    private async Task Register(HttpClient browser, string email)
    {
        var response = await Post(browser, "/Account/Register", "/Account/Register", Registration(email));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/VerifyEmail", response.Headers.Location!.OriginalString);
    }
    private async Task<string> SignIn(HttpClient browser)
    {
        var email = Email();
        await Register(browser, email);
        var response = await Post(browser, "/Account/VerifyEmail?email=" + email, "/Account/VerifyEmail", new()
        {
            ["Email"] = email,
            ["Code"] = app.Mail.Codes[email]
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return email;
    }
    private static Dictionary<string, string> Question(string content = "Explain the three layers clearly") => new()
    {
        ["Content"] = content,
        ["Context"] = "Software engineering",
        ["BloomLevelId"] = "1",
        ["RubricId"] = "1",
        ["ExpectedAnswer"] = "Presentation, BLL, DAL",
        ["DisplayOrder"] = "2",
        ["IsActive"] = "true"
    };

    [SqlTheory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/VerifyEmail?email=test@gmail.com")]
    public async Task PublicPagesRender(string route)
    {
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(route)).StatusCode);
    }

    [SqlTheory]
    [InlineData("/")]
    [InlineData("/Home/Privacy")]
    [InlineData("/Question")]
    [InlineData("/Question/Create")]
    [InlineData("/AiExamRoom/QuestionGenerator")]
    public async Task AnonymousUsersAreRedirectedToLogin(string route)
    {
        using var browser = app.Browser();
        var response = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [SqlTheory]
    [InlineData("Login")]
    [InlineData("Register")]
    [InlineData("VerifyEmail")]
    [InlineData("ResendCode")]
    [InlineData("ExternalLogin")]
    public async Task AccountPostsRejectMissingAntiforgeryToken(string action)
    {
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Account/" + action, new FormUrlEncodedContent([]))).StatusCode);
    }

    [SqlTheory]
    [InlineData("Email", "invalid-email")]
    [InlineData("Email", "test@example.com")]
    [InlineData("DisplayName", "")]
    [InlineData("Password", "short")]
    [InlineData("ConfirmPassword", "different")]
    public async Task InvalidRegistrationIsRejected(string field, string value)
    {
        using var browser = app.Browser();
        var fields = Registration(Email());
        fields[field] = value;
        var response = await Post(browser, "/Account/Register", "/Account/Register", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation", await Html(response));
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == fields["Email"]));
    }

    [SqlFact]
    public async Task RegisterVerifyLogoutLoginAndExternalReturnUrlWork()
    {
        using var browser = app.Browser();
        var email = await SignIn(browser);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
        var logout = await Post(browser, "/Question", "/Account/Logout", new());
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Question")).StatusCode);
        var login = await Post(browser, "/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email,
            ["Password"] = Password,
            ["RememberMe"] = "true",
            ["ReturnUrl"] = "https://example.com/untrusted"
        });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("AIVES.Auth=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [SqlFact]
    public async Task UnverifiedAccountCannotLoginAndDuplicateEmailCannotRegister()
    {
        using var browser = app.Browser();
        var email = Email();
        await Register(browser, email);
        var login = await Post(browser, "/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email,
            ["Password"] = Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("has not been verified", await Html(login));
        var duplicate = await Post(browser, "/Account/Register", "/Account/Register", Registration(email));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync(u => u.Email == email));
    }

    [SqlFact]
    public async Task FiveWrongPasswordsLockTheAccount()
    {
        using var browser = app.Browser();
        var email = await SignIn(browser);
        await Post(browser, "/Question", "/Account/Logout", new());
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await Post(browser, "/Account/Login", "/Account/Login", new()
            {
                ["Email"] = email,
                ["Password"] = "WrongPassword123"
            })).StatusCode);
        var locked = await Post(browser, "/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email,
            ["Password"] = Password
        });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("temporarily locked", await Html(locked));
    }

    [SqlFact]
    public async Task WrongOtpAndImmediateResendAreRejectedThenNewCodeReplacesOldCode()
    {
        using var browser = app.Browser();
        var email = Email();
        await Register(browser, email);
        var page = "/Account/VerifyEmail?email=" + email;
        var wrong = await Post(browser, page, "/Account/VerifyEmail", new()
        {
            ["Email"] = email,
            ["Code"] = "000000"
        });
        Assert.Contains("code is incorrect", await Html(wrong));
        await Post(browser, page, "/Account/ResendCode", new()
        {
            ["email"] = email
        });
        Assert.Contains("wait a minute", await Html(await browser.GetAsync(page)));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var code = await db.EmailVerificationCodes.SingleAsync(c => c.User!.Email == email);
            code.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        await Post(browser, page, "/Account/ResendCode", new()
        {
            ["email"] = email
        });
        using var check = app.Services.CreateScope();
        var codes = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().EmailVerificationCodes.Where(c => c.User!.Email == email).ToListAsync();
        Assert.Equal(2, codes.Count);
        Assert.Single(codes, c => !c.IsConsumed);
        var verified = await Post(browser, page, "/Account/VerifyEmail", new()
        {
            ["Email"] = email,
            ["Code"] = app.Mail.Codes[email]
        });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
    }

    [SqlFact]
    public async Task QuestionCrudAndBloomFilterWorkThroughHttp()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var content = "Functional question " + Guid.NewGuid().ToString("N");
        var created = await Post(browser, "/Question/Create", "/Question/Create", Question(content));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        int id;
        DateTime createdAt;
        using (var scope = app.Services.CreateScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Questions.SingleAsync(q => q.Content == content);
            id = record.Id;
            createdAt = record.CreatedDate;
        }
        Assert.Contains(content, await Html(await browser.GetAsync("/Question")));
        Assert.Contains("Remember", await Html(await browser.GetAsync("/Question/Details/" + id)));
        Assert.Contains("Functional rubric", await Html(await browser.GetAsync("/Question/Details/" + id)));
        Assert.Contains(content, await Html(await browser.GetAsync("/Question/ByBloomLevel/1")));
        Assert.DoesNotContain(content, await Html(await browser.GetAsync("/Question/ByBloomLevel/2")));
        var edit = Question(content + " updated");
        edit["Id"] = id.ToString();
        edit["BloomLevelId"] = "2";
        edit["IsActive"] = "false";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Question/Edit/" + id, "/Question/Edit/" + id, edit)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Questions.SingleAsync(q => q.Id == id);
            Assert.Equal(createdAt, record.CreatedDate);
            Assert.False(record.IsActive);
            Assert.Equal(2, record.BloomLevelId);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Question/Delete/" + id, "/Question/Delete/" + id, new())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Question/Details/" + id)).StatusCode);
    }

    [SqlFact]
    public async Task OptionalQuestionFieldsCanBeLeftEmpty()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var fields = Question();
        fields["Context"] = "";
        fields["ExpectedAnswer"] = "";
        var response = await Post(browser, "/Question/Create", "/Question/Create", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [SqlFact]
    public async Task NewQuestionFormUsesTheDeclaredDefaults()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var html = await Html(await browser.GetAsync("/Question/Create"));
        Assert.Matches("<input[^>]*checked=\"checked\"[^>]*id=\"IsActive\"", html);
        Assert.Matches("<input[^>]*id=\"DisplayOrder\"[^>]*value=\"0\"", html);
    }

    [SqlTheory]
    [InlineData("Content", "short")]
    [InlineData("BloomLevelId", "99999")]
    [InlineData("RubricId", "99999")]
    [InlineData("DisplayOrder", "-1")]
    public async Task InvalidQuestionIsNotSaved(string field, string value)
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var content = "Invalid question " + Guid.NewGuid().ToString("N");
        var fields = Question(content);
        fields[field] = value;
        Assert.Equal(HttpStatusCode.OK, (await Post(browser, "/Question/Create", "/Question/Create", fields)).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Questions.AnyAsync(q => q.Content == fields["Content"]));
    }

    [SqlTheory]
    [InlineData("/Question/Details/999999")]
    [InlineData("/Question/Edit/999999")]
    [InlineData("/Question/Delete/999999")]
    [InlineData("/Question/Details")]
    public async Task UnknownQuestionReturns404(string route)
    {
        using var browser = app.Browser();
        await SignIn(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(route)).StatusCode);
    }

    [SqlFact]
    public async Task AuthenticatedPostsStillRequireAntiforgery()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        foreach (var route in new[] { "/Question/Create", "/Question/Edit/1", "/Question/Delete/1", "/Account/Logout", "/Question/GenerateQuestions" })
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(route, new FormUrlEncodedContent([]))).StatusCode);
    }

    [SqlFact]
    public async Task AiPanelReturnsStructuredQuestionsAndHandlesFailure()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var page = "/Question/GenerateQuestions";
        var fields = new Dictionary<string, string> { ["Subject"] = "Software engineering", ["Topic"] = "Three layers", ["QuestionCount"] = "2", ["Difficulty"] = "Balanced" };
        var success = await Post(browser, "/Question/Create", page, fields);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var html = await Html(success);
        Assert.Contains("Generated question 1", html);
        Assert.Contains("Generated question 2", html);
        Assert.Contains("First follow up", html);
        Assert.Contains("Expected test answer", html);
        Assert.DoesNotContain("Q1.ToString", html);
        Assert.Contains("ai-result-index\">Q01", html);
        Assert.Contains("ai-result-index\">Q02", html);
        // Each result carries the values the panel copies into the question form.
        Assert.Contains("data-content=\"Generated question 1 for Three layers\"", html);
        Assert.Contains("ai-result-actions", html);
        Assert.Contains("ai-use", html);
        fields["Topic"] = "simulate-failure";
        var failureHtml = await Html(await Post(browser, "/Question/Create", page, fields));
        Assert.Contains("Could not generate questions right now. Please try again later.", failureHtml);
        Assert.DoesNotContain("AI test failure", failureHtml);
    }

    [SqlFact]
    public async Task AiPanelIsDisabledOnTheExamRoomAndSendsUsersToTheQuestionPage()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var response = await browser.GetAsync("/AiExamRoom/QuestionGenerator");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Question/Create", response.Headers.Location!.OriginalString);
    }

    [SqlFact]
    public async Task CreatePageCarriesTheCompactAiPanel()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var html = await Html(await browser.GetAsync("/Question/Create"));

        Assert.Contains("data-slide=\"aiPanel\"", html);
        Assert.Contains("slide-panel-compact", html);
        Assert.Contains("id=\"aiPanelForm\"", html);
        // The exam room nav entry is retired while its flow is reworked.
        Assert.DoesNotContain("asp-controller=\"AiExamRoom\"", html);
    }

[Fact]
    public void VietnameseTextTableBuildsWithoutDuplicateKeys()
    {
        // A repeated key throws inside the collection initializer, so the whole table
        // fails to build and every page silently falls back to English. Touching the
        // field here surfaces that failure directly.
        var appText = typeof(AIVES.DTO.Localization.L10n).Assembly.GetType("AIVES.DTO.Localization.AppText")!;
        var table = (IDictionary<string, string>)appText
            .GetField("Vietnamese", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.NotEmpty(table);
        Assert.Equal(table.Count, table.Keys.Distinct(StringComparer.Ordinal).Count());

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("vi");
            Assert.Equal("Danh mục", AIVES.DTO.Localization.L10n.T("Catalog"));
            Assert.Equal("Môn học", AIVES.DTO.Localization.L10n.T("Subject"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [SqlFact]
public async Task DefaultLanguageIsEnglishAndVietnameseCanBeSelected()
{
    using var browser = app.Browser();
    var english = await Html(await browser.GetAsync("/Account/Login?culture=en"));
    Assert.Contains("Resume your academic workspace", english);
    Assert.Contains("Sign in", english);
    Assert.DoesNotContain("Tiếp tục phiên làm việc học thuật", english);

    var vietnamese = await Html(await browser.GetAsync("/Account/Login?culture=vi"));
    Assert.Contains("Tiếp tục phiên làm việc học thuật", vietnamese);
    Assert.Contains("Đăng nhập", vietnamese);
    Assert.DoesNotContain("Resume your academic workspace", vietnamese);
}

[SqlFact]
public async Task SettingLanguagePersistsThroughTheCookieForAuthenticatedPages()
{
    using var browser = app.Browser();
    await SignIn(browser);

    var before = await Html(await browser.GetAsync("/"));
    Assert.Contains("Question bank", before);
    Assert.DoesNotContain("Ngân hàng câu hỏi", before);

    var form = await browser.GetAsync("/");
    var token = Regex.Match(await form.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    var response = await browser.PostAsync("/Home/SetLanguage", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["language"] = "Vi",
        ["returnUrl"] = "/",
        ["__RequestVerificationToken"] = token
    }));

    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

    var after = await Html(await browser.GetAsync("/"));
    Assert.Contains("Ngân hàng câu hỏi", after);
    Assert.DoesNotContain("Question bank", after);
}

    [SqlFact]
    public async Task InvalidAiCountDoesNotCallGenerator()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        var calls = app.Generator.Calls;
        var response = await Post(browser, "/Question/Create", "/Question/GenerateQuestions", new()
        {
            ["Subject"] = "Test",
            ["Topic"] = "Test",
            ["QuestionCount"] = "11",
            ["Difficulty"] = "Balanced"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(calls, app.Generator.Calls);
    }

[SqlFact]
public async Task ProfilePageShowsTheSignedInAccountAndLogoutReturnsToLogin()
{
    using var browser = app.Browser();
    var email = await SignIn(browser);

    var profile = await Html(await browser.GetAsync("/Profile"));
    Assert.Contains(email, profile);
    Assert.Contains("Sign out", profile);

    var form = await browser.GetAsync("/Profile");
    var token = Regex.Match(await form.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    var signedOut = await browser.PostAsync("/Profile/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token
    }));

    Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
    Assert.Equal("/Account/Login", signedOut.Headers.Location!.ToString());

    var after = await browser.GetAsync("/Profile");
    Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
}

[SqlFact]
public async Task AnonymousUsersAreSentToLoginFromProfile()
{
    using var browser = app.Browser();
    var response = await browser.GetAsync("/Profile");
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    Assert.Contains("Login", response.Headers.Location!.ToString());
}

[SqlFact]
public async Task UnconfiguredGoogleLoginAndInvalidCallbackReturnToLogin()
    {
        using var browser = app.Browser();
        var response = await Post(browser, "/Account/Login", "/Account/ExternalLogin", new());
        Assert.Equal("/Account/Login", response.Headers.Location!.OriginalString);
        Assert.Equal("/Account/Login", (await browser.GetAsync("/Account/ExternalLoginCallback?remoteError=denied")).Headers.Location!.OriginalString);
    }
}

public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION")))
            Skip = "Set AIVES_TEST_SQL_CONNECTION to run HTTP tests with an isolated SQL database.";
    }
}
