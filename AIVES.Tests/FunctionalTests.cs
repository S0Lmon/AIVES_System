using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AIVES.DAL.Data;
using AIVES.DTO;
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
    /// <summary>Registers, gives the account <paramref name="role"/> before verification signs it in, and returns the email.</summary>
    private async Task<string> SignIn(HttpClient browser, string role = AppRoles.Lecturer)
    {
        var email = Email();
        await Register(browser, email);
        if (role != AppRoles.Default)
        {
            using var scope = app.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
            var user = await store.FindByEmailAsync(email);
            Assert.True((await store.SetAssignableRoleAsync(user!.Id, role)).Succeeded);
        }
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

    [SqlFact]
    public async Task HealthEndpointIsAnonymousAndChecksTheDatabase()
    {
        using var browser = app.Browser();
        var response = await browser.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
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
    public async Task NewAccountsAreStudentsAndCannotReachLecturerPages()
    {
        using var browser = app.Browser();
        var email = await SignIn(browser, AppRoles.Student);
        using (var scope = app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
            var profile = await store.GetProfileAsync((await store.FindByEmailAsync(email))!.Id);
            Assert.Equal([AppRoles.Student], profile!.Roles);
        }

        foreach (var page in new[] { "/Question", "/Question/Create", "/Question?tab=ai", "/Rubric", "/Catalog", "/Admin", "/Admin/Users" })
        {
            var response = await browser.GetAsync(page);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
        }

        // Writes are refused too, not just hidden: a valid antiforgery token does not get past the role check.
        var subjectName = "Student subject " + Guid.NewGuid().ToString("N")[..8];
        var write = await Post(browser, "/", "/Catalog/CreateSubject", new() { ["Name"] = subjectName, ["Description"] = string.Empty });
        Assert.StartsWith("/Account/AccessDenied", write.Headers.Location!.PathAndQuery);
        using (var scope = app.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Subjects.AnyAsync(subject => subject.Name == subjectName));

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Account/AccessDenied")).StatusCode);
        var home = await Html(await browser.GetAsync("/"));
        Assert.DoesNotContain("href=\"/Question?tab=bank\"", home);
        Assert.DoesNotContain("href=\"/Admin/Users\"", home);
    }

    [SqlFact]
    public async Task LecturersReachTheBanksButNotTheUsersPage()
    {
        using var browser = app.Browser();
        await SignIn(browser);
        foreach (var page in new[] { "/Question", "/Rubric", "/Catalog" })
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(page)).StatusCode);
        Assert.Contains("href=\"/Question?tab=bank\"", await Html(await browser.GetAsync("/")));

        var users = await browser.GetAsync("/Admin/Users");
        Assert.StartsWith("/Account/AccessDenied", users.Headers.Location!.PathAndQuery);
    }

    [SqlFact]
    public async Task AdminPromotesAStudentWhoThenReachesTheQuestionBank()
    {
        using var admin = app.Browser();
        await SignInAsAdmin(admin);
        using var student = app.Browser();
        var studentEmail = await SignIn(student, AppRoles.Student);
        string studentId;
        using (var scope = app.Services.CreateScope())
            studentId = (await scope.ServiceProvider.GetRequiredService<IAccountStore>().FindByEmailAsync(studentEmail))!.Id;

        var page = await Html(await admin.GetAsync("/Admin/Users"));
        Assert.Contains(studentEmail, page);

        var promoted = await Post(admin, "/Admin/Users", "/Admin/SetRole", new() { ["userId"] = studentId, ["role"] = AppRoles.Lecturer });
        Assert.Equal(HttpStatusCode.Redirect, promoted.StatusCode);
        Assert.Contains($"{studentEmail} is now Lecturer.", await Html(await admin.GetAsync("/Admin/Users")));
        using (var scope = app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
            Assert.Equal([AppRoles.Lecturer], (await store.GetProfileAsync(studentId))!.Roles);
        }

        // A fresh sign-in carries the new role (an open session picks it up at the next stamp check).
        await Post(student, "/", "/Account/Logout", new());
        var login = await Post(student, "/Account/Login", "/Account/Login", new() { ["Email"] = studentEmail, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/Question")).StatusCode);
    }

    [SqlFact]
    public async Task UsersPageRefusesAdminRoleAndSelfChanges()
    {
        using var admin = app.Browser();
        var adminId = await SignInAsAdmin(admin);
        using var other = app.Browser();
        var otherEmail = await SignIn(other, AppRoles.Student);
        string otherId;
        using (var scope = app.Services.CreateScope())
            otherId = (await scope.ServiceProvider.GetRequiredService<IAccountStore>().FindByEmailAsync(otherEmail))!.Id;

        await Post(admin, "/Admin/Users", "/Admin/SetRole", new() { ["userId"] = otherId, ["role"] = AppRoles.Admin });
        Assert.Contains("That role cannot be assigned here.", await Html(await admin.GetAsync("/Admin/Users")));
        await Post(admin, "/Admin/Users", "/Admin/SetRole", new() { ["userId"] = adminId, ["role"] = AppRoles.Student });
        Assert.Contains("You cannot change your own role.", await Html(await admin.GetAsync("/Admin/Users")));

        using var scope2 = app.Services.CreateScope();
        var store = scope2.ServiceProvider.GetRequiredService<IAccountStore>();
        Assert.Equal([AppRoles.Student], (await store.GetProfileAsync(otherId))!.Roles);
        Assert.Contains(AppRoles.Admin, (await store.GetProfileAsync(adminId))!.Roles);
    }

    [SqlFact]
    public async Task AccountsWithoutARoleBecomeStudentsAtStartup()
    {
        string id;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AIVES.DAL.Entities.ApplicationUser>>();
            var email = Email();
            var legacy = new AIVES.DAL.Entities.ApplicationUser { UserName = email, Email = email, DisplayName = "Legacy", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(legacy)).Succeeded);
            Assert.Empty(await users.GetRolesAsync(legacy));
            id = legacy.Id;
        }

        // The same initialisation the migrate container runs on every deploy.
        await AIVES.BLL.DependencyInjection.InitializeAivesAsync(app.Services, app.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), isDevelopment: false);

        using var check = app.Services.CreateScope();
        Assert.Equal([AppRoles.Student], (await check.ServiceProvider.GetRequiredService<IAccountStore>().GetProfileAsync(id))!.Roles);
    }

    /// <summary>Creates a confirmed administrator directly and signs it in; returns its user id.</summary>
    private async Task<string> SignInAsAdmin(HttpClient browser)
    {
        var email = Email();
        string id;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AIVES.DAL.Entities.ApplicationUser>>();
            var user = new AIVES.DAL.Entities.ApplicationUser { UserName = email, Email = email, DisplayName = "Admin Tester", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
            id = user.Id;
        }
        var login = await Post(browser, "/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return id;
    }

    [SqlFact]
    public async Task UniversityEmailRegistersVerifiesAndSignsInWhileOtherDomainsAreRejected()
    {
        using var browser = app.Browser();
        var form = await Html(await browser.GetAsync("/Account/Register"));
        // The form names the allowed domains from appsettings.json.
        Assert.Contains("@gmail.com, @fpt.edu.vn", form);

        var email = "student." + Guid.NewGuid().ToString("N")[..8] + "@fpt.edu.vn";
        await Register(browser, email);
        var verified = await Post(browser, "/Account/VerifyEmail?email=" + email, "/Account/VerifyEmail", new()
        {
            ["Email"] = email,
            ["Code"] = app.Mail.Codes[email]
        });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Profile")).StatusCode);

        var rejectedEmail = "someone." + Guid.NewGuid().ToString("N")[..8] + "@yahoo.com";
        var rejected = await Post(browser, "/Account/Register", "/Account/Register", Registration(rejectedEmail));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("Please use an email address ending in @gmail.com, @fpt.edu.vn.", await Html(rejected));
        Assert.False(app.Mail.Codes.ContainsKey(rejectedEmail));
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
        // The panel resolves subject and topic names from catalogue ids and asks for one question per run.
        var (subjectId, topicId) = await CreateCatalogTopic("Three layers");
        var fields = new Dictionary<string, string> { ["SubjectId"] = subjectId.ToString(), ["TopicId"] = topicId.ToString(), ["UseMaterials"] = "false" };
        var success = await Post(browser, "/Question/Create", page, fields);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var html = await Html(success);
        Assert.Contains("Generated question 1", html);
        Assert.DoesNotContain("Generated question 2", html);
        Assert.Contains("First follow up", html);
        Assert.Contains("Expected test answer", html);
        Assert.DoesNotContain("Q1.ToString", html);
        Assert.Contains("ai-result-index\">Q01", html);
        Assert.DoesNotContain("ai-result-index\">Q02", html);
        // Each result carries the values the panel copies into the question form.
        Assert.Contains("data-content=\"Generated question 1 for Three layers\"", html);
        Assert.Contains("ai-result-actions", html);
        Assert.Contains("ai-use", html);
        var (failSubjectId, failTopicId) = await CreateCatalogTopic("simulate-failure");
        fields["SubjectId"] = failSubjectId.ToString();
        fields["TopicId"] = failTopicId.ToString();
        var failureHtml = await Html(await Post(browser, "/Question/Create", page, fields));
        Assert.Contains("Could not generate questions right now. Please try again later.", failureHtml);
        Assert.DoesNotContain("AI test failure", failureHtml);
    }

    /// <summary>Adds a subject with one topic straight to the database; names are unique per call.</summary>
    [SqlFact]
    public async Task LecturerSchedulesAnExamAndStudentsSeeOnlyTheirOwnSlot()
    {
        using var lecturer = app.Browser();
        await SignIn(lecturer);
        using var student = app.Browser();
        var studentEmail = await SignIn(student, AppRoles.Student);
        var (subjectId, topicId) = await CreateCatalogTopic("Exam topic");
        var contents = await AddQuestions(subjectId, topicId, 6);

        // Times are entered in Vietnam time (UTC+7) and shown the same way.
        var day = DateTime.UtcNow.AddDays(2).Date;
        var created = await Post(lecturer, "/Exam/Create", "/Exam/Create", new()
        {
            ["Title"] = "Viva schedule test",
            ["SubjectId"] = subjectId.ToString(),
            ["TopicId"] = topicId.ToString(),
            ["StartsAtLocal"] = day.ToString("yyyy-MM-dd") + "T09:00",
            ["SlotMinutes"] = "10",
            ["MainQuestionCount"] = "2",
            ["MaxFollowUpQuestions"] = "1",
            ["CandidateEmails"] = studentEmail.ToUpperInvariant() + "\nnot.registered@fpt.edu.vn"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var detailsUrl = created.Headers.Location!.OriginalString;
        Assert.Matches(@"^/Exam/Details/\d+$", detailsUrl);

        var details = await Html(await lecturer.GetAsync(detailsUrl));
        Assert.Contains("Viva schedule test", details);
        Assert.Contains("09:00 – 09:10", details);
        Assert.Contains("09:10 – 09:20", details);
        Assert.Contains("Functional Tester", details);        // the registered candidate's name
        Assert.Contains("not.registered@fpt.edu.vn", details);
        Assert.Contains("No account yet", details);
        Assert.Equal(4, contents.Count(content => details.Contains(content)));
        Assert.Contains("Viva schedule test", await Html(await lecturer.GetAsync("/Exam")));

        // The student sees their slot, never the questions, and cannot open the lecturer pages.
        var mine = await Html(await student.GetAsync("/MyExams"));
        Assert.Contains("Viva schedule test", mine);
        Assert.Contains(day.ToString("yyyy-MM-dd") + " 09:00 – 09:10", mine);
        Assert.Contains("1 of 2", mine);
        Assert.DoesNotContain(contents, content => mine.Contains(content));
        Assert.StartsWith("/Account/AccessDenied", (await student.GetAsync(detailsUrl)).Headers.Location!.PathAndQuery);

        // Another lecturer cannot see it.
        using var otherLecturer = app.Browser();
        await SignIn(otherLecturer);
        Assert.Equal(HttpStatusCode.NotFound, (await otherLecturer.GetAsync(detailsUrl)).StatusCode);
        Assert.DoesNotContain("Viva schedule test", await Html(await otherLecturer.GetAsync("/Exam")));

        // The owner deletes it.
        var deleted = await Post(lecturer, detailsUrl, detailsUrl.Replace("Details", "Delete"), new());
        Assert.Equal("/Exam", deleted.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await lecturer.GetAsync(detailsUrl)).StatusCode);
        Assert.DoesNotContain("Viva schedule test", await Html(await student.GetAsync("/MyExams")));
    }

    [SqlFact]
    public async Task ExamFormExplainsWhyItCannotBeSaved()
    {
        using var lecturer = app.Browser();
        await SignIn(lecturer);
        var (subjectId, topicId) = await CreateCatalogTopic("Small pool");
        await AddQuestions(subjectId, topicId, 2);

        var response = await Post(lecturer, "/Exam/Create", "/Exam/Create", new()
        {
            ["Title"] = "Too many questions",
            ["SubjectId"] = subjectId.ToString(),
            ["TopicId"] = topicId.ToString(),
            ["StartsAtLocal"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd") + "T10:00",
            ["SlotMinutes"] = "15",
            ["MainQuestionCount"] = "3",
            ["MaxFollowUpQuestions"] = "2",
            ["CandidateEmails"] = "a@fpt.edu.vn"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Html(response);
        Assert.Contains("Only 2 active questions match this subject and topic, but each candidate needs 3.", html);
        Assert.Contains("value=\"Too many questions\"", html); // the form keeps what was typed
    }

    /// <summary>Adds active questions to a subject/topic and returns their texts.</summary>
    private async Task<List<string>> AddQuestions(int subjectId, int topicId, int count)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var contents = Enumerable.Range(1, count).Select(i => $"Exam pool question {i} {Guid.NewGuid():N}").ToList();
        foreach (var (content, index) in contents.Select((content, index) => (content, index)))
            db.Questions.Add(new AIVES.DAL.Entities.Question { Content = content, ExpectedAnswer = "Answer", BloomLevelId = index % 4 + 1, SubjectId = subjectId, TopicId = topicId });
        await db.SaveChangesAsync();
        return contents;
    }

    private async Task<(int SubjectId, int TopicId)> CreateCatalogTopic(string topicName)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subject = new AIVES.DAL.Entities.Subject { Name = $"Subject {Guid.NewGuid():N}" };
        subject.Topics.Add(new AIVES.DAL.Entities.Topic { Name = topicName });
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return (subject.Id, subject.Topics.Single().Id);
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

    [SqlFact]
    public async Task QuestionBankExposesBankCreateAiAndBulkTabs()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var html = await Html(await browser.GetAsync("/Question"));
        // The tag helpers render these into hrefs, so match the query they produce.
        Assert.Contains("href=\"/Question?tab=bank\"", html);
        Assert.Contains("tab=create", html);
        Assert.Contains("tab=ai", html);
        Assert.Contains("tab=bulk", html);

        var ai = await Html(await browser.GetAsync("/Question?tab=ai"));
        // One question per run, aimed at a catalogue subject/topic rather than free text.
        Assert.Contains("name=\"SubjectId\"", ai);
        Assert.Contains("name=\"TopicId\"", ai);
        Assert.DoesNotContain("name=\"QuestionCount\"", ai);
        Assert.Contains("action=\"/Question/GenerateReview\"", ai);

        var bulk = await Html(await browser.GetAsync("/Question?tab=bulk"));
        Assert.Contains("action=\"/Question/GenerateBulk\"", bulk);
        // A total plus a lowest/highest range per Bloom level.
        Assert.Contains("name=\"TotalAmount\"", bulk);
        Assert.Contains("data-plan-min", bulk);
        Assert.Contains("data-plan-max", bulk);
    }

    [SqlFact]
    public async Task RubricBankExposesBankCreateAndAiTabsWithAMatrixEditor()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var html = await Html(await browser.GetAsync("/Rubric"));
        Assert.Contains("href=\"/Rubric?tab=bank\"", html);
        Assert.Contains("tab=create", html);
        Assert.Contains("tab=ai", html);

        var create = await Html(await browser.GetAsync("/Rubric?tab=create"));
        // Rows are criteria and columns are levels, both editable on the same grid.
        Assert.Contains("data-matrix-editor", create);
        Assert.Contains("name=\"Columns[0].Name\"", create);
        Assert.Contains("name=\"Rows[0].Criterion\"", create);
        Assert.Contains("name=\"Rows[0].Cells[0].Descriptor\"", create);

        var ai = await Html(await browser.GetAsync("/Rubric?tab=ai"));
        Assert.Contains("action=\"/Rubric/Generate\"", ai);
        Assert.Contains("name=\"CriterionCount\"", ai);
        Assert.Contains("name=\"LevelCount\"", ai);
    }

    [SqlFact]
    public async Task AMatrixRubricCanBeCreatedAndEditedFromTheEditor()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var values = new Dictionary<string, string>
        {
            ["Name"] = "Oral defence matrix",
            ["Description"] = "Two rows across two columns",
            ["Columns[0].Name"] = "Below",
            ["Columns[0].Points"] = "1",
            ["Columns[1].Name"] = "Exceeds",
            ["Columns[1].Points"] = "4",
            ["Rows[0].Criterion"] = "Correctness",
            ["Rows[0].Cells[0].Descriptor"] = "Major errors present",
            ["Rows[0].Cells[0].Points"] = "1",
            ["Rows[0].Cells[1].Descriptor"] = "Accurate and complete",
            ["Rows[0].Cells[1].Points"] = "4",
            ["Rows[1].Criterion"] = "Reasoning",
            ["Rows[1].Cells[0].Descriptor"] = "Answer is asserted, not shown",
            ["Rows[1].Cells[0].Points"] = "1",
            ["Rows[1].Cells[1].Descriptor"] = "Each claim is justified",
            ["Rows[1].Cells[1].Points"] = "4"
        };

        var created = await Post(browser, "/Rubric?tab=create", "/Rubric/Create", values);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var bank = await Html(await browser.GetAsync("/Rubric?tab=bank"));
        Assert.Contains("Oral defence matrix", bank);
        // Two rows x two columns, and the total is the sum of each row's best cell.
        Assert.Contains("2 rows × 2 columns", bank);
        Assert.Contains(">8<", bank);

        // Deleting has to actually remove the row, columns and cells, not just report success.
        // Scope to this rubric's own row: the same name also appears in the success alert
        // above the table, and the first delete form on the page belongs to another rubric.
        var row = Regex.Match(bank, "(?s)<tr[^>]*>((?:(?!</tr>).)*?Oral defence matrix(?:(?!</tr>).)*?)</tr>").Groups[1].Value;
        var id = Regex.Match(row, "name=\"id\" value=\"(\\d+)\"").Groups[1].Value;
        Assert.NotEmpty(id);
        var deleted = await Post(browser, "/Rubric?tab=bank", "/Rubric/Delete", new() { ["id"] = id });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        var after = await Html(await browser.GetAsync("/Rubric?tab=bank"));
        Assert.False(after.Contains("Oral defence matrix"),
            $"Rubric {id} survived the delete. Page said: {Regex.Match(after, "alert-(?:success|danger)[^>]*>([^<]*)").Groups[1].Value}");
    }

    [SqlFact]
    public async Task ARubricWithoutRowsIsRejectedAndNothingIsStored()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var before = await Html(await browser.GetAsync("/Rubric?tab=bank"));
        var response = await Post(browser, "/Rubric?tab=create", "/Rubric/Create", new()
        {
            ["Name"] = "Empty grid",
            ["Description"] = "No rows",
            ["Columns[0].Name"] = "Only",
            ["Columns[0].Points"] = "2"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await Html(response);
        Assert.Contains("at least one criterion row", page);

        var after = await Html(await browser.GetAsync("/Rubric?tab=bank"));
        Assert.DoesNotContain("Empty grid", after);
        Assert.Equal(CountOccurrences(before, "rubric"), CountOccurrences(after, "rubric"));
    }

    private static int CountOccurrences(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle)).Count;

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
    public async Task CreatingASubjectOpensItsOwnPageWithTopicCreationInFront()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        const string subjectName = "Functional catalogue subject";
        var created = await Post(browser, "/Catalog?tab=subject", "/Catalog/CreateSubject", new()
        {
            ["Name"] = subjectName,
            ["Description"] = "Created by the functional test"
        });

        // Creating a subject lands on that subject rather than back on the list.
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var location = created.Headers.Location!.ToString();
        Assert.Contains("tab=subject", location);
        Assert.Contains("open=", location);

        var openId = Regex.Match(location, @"open=(?<id>\d+)").Groups["id"].Value;
        Assert.NotEmpty(openId);

        var page = await Html(await browser.GetAsync($"/Catalog?tab=subject&open={openId}"));
        Assert.Contains(subjectName, page);
        Assert.Contains("Add a topic", page);
        // The topic form is the subject page's main event, so it is rendered open, not hidden.
        Assert.Matches(@"<form[^>]*action=""/Catalog/CreateTopic""[^>]*>", page);
        Assert.Contains($"name=\"SubjectId\" value=\"{openId}\"", page);
        // A subject with no topics yet says so rather than showing an empty table.
        Assert.Contains("No topics under this subject yet", page);
    }

    [SqlFact]
    public async Task AddingATopicKeepsTheSubjectPageOpenAndListsTheTopic()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        const string subjectName = "Subject with a topic";
        var created = await Post(browser, "/Catalog?tab=subject", "/Catalog/CreateSubject", new()
        {
            ["Name"] = subjectName,
            ["Description"] = string.Empty
        });
        var openId = Regex.Match(created.Headers.Location!.ToString(), "open=(?<id>\\d+)").Groups["id"].Value;

        const string topicName = "Functional catalogue topic";
        var topic = await Post(browser, $"/Catalog?tab=subject&open={openId}", "/Catalog/CreateTopic", new()
        {
            ["SubjectId"] = openId,
            ["Name"] = topicName,
            ["Description"] = "Created by the functional test"
        });

        Assert.Equal(HttpStatusCode.Redirect, topic.StatusCode);
        Assert.Contains($"open={openId}", topic.Headers.Location!.ToString());

        var page = await Html(await browser.GetAsync($"/Catalog?tab=subject&open={openId}"));
        Assert.Contains(topicName, page);
        Assert.Contains("The topic was added.", page);
        // Topics are numbered down the page and each row carries its own edit form.
        Assert.Contains($"/Catalog/UpdateTopic/", page);
        Assert.Contains($"/Catalog/DeleteTopic/", page);
    }

    [SqlFact]
    public async Task QuestionCreateSavesSubjectAndTopicAsRealReferences()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var subject = await Post(browser, "/Catalog?tab=subject", "/Catalog/CreateSubject", new()
        {
            ["Name"] = "Referenced subject",
            ["Description"] = string.Empty
        });
        var subjectId = Regex.Match(subject.Headers.Location!.ToString(), "open=(?<id>\\d+)").Groups["id"].Value;
        await Post(browser, $"/Catalog?tab=subject&open={subjectId}", "/Catalog/CreateTopic", new()
        {
            ["SubjectId"] = subjectId,
            ["Name"] = "Referenced topic",
            ["Description"] = string.Empty
        });

        var create = await Html(await browser.GetAsync("/Question?tab=create"));
        // Both pickers render a real select, so the form still posts plain ids without JavaScript.
        Assert.Contains("data-catalog-picker", create);
        Assert.Contains("name=\"SubjectId\"", create);
        Assert.Contains("name=\"TopicId\"", create);
        Assert.Contains("Referenced subject", create);

        var bank = await Html(await browser.GetAsync("/Question"));
        // The bank list filters by subject and topic through the same pickers.
        Assert.Contains("name=\"subjectId\"", bank);
        Assert.Contains("name=\"topicId\"", bank);
        Assert.Contains("bankFilterSubject", bank);
    }

    [SqlFact]
    public async Task AQuestionCanBeSavedWithoutARubric()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        // The form offers "No rubric", so leaving it blank has to save rather than fail validation.
        var values = Question();
        values["RubricId"] = string.Empty;
        values["Content"] = "A question stored without any rubric attached";

        var created = await Post(browser, "/Question?tab=create", "/Question/Create", values);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Contains("tab=bank", created.Headers.Location!.ToString());

        var bank = await Html(await browser.GetAsync("/Question?tab=bank"));
        Assert.Contains("A question stored without any rubric attached", bank);
    }

    [SqlFact]
    public async Task TheBankPagesOpenWithTheTabStripAndNoPageHeading()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        foreach (var page in new[] { "/Question", "/Rubric" })
        {
            var html = await Html(await browser.GetAsync(page));
            // The header block was removed; the tab strip is now the first thing on the page.
            Assert.DoesNotContain("page-heading", html);
            Assert.Contains("bank-tabs", html);
        }

        var catalog = await Html(await browser.GetAsync("/Catalog"));
        Assert.DoesNotContain("page-heading", catalog);
        Assert.Contains("cat-tabs", catalog);

        // Stat cards sit under the tab strip rather than above it.
        Assert.True(catalog.IndexOf("cat-tabs", StringComparison.Ordinal)
            < catalog.IndexOf("cat-stats", StringComparison.Ordinal),
            "catalog stats should render below the tabs");

        var question = await Html(await browser.GetAsync("/Question?tab=bank"));
        Assert.True(question.IndexOf("bank-tabs", StringComparison.Ordinal)
            < question.IndexOf("stat-strip", StringComparison.Ordinal),
            "question stats should render below the tabs");
    }

    [SqlFact]
    public async Task TheRubricMatrixNumbersItsRows()
    {
        using var browser = app.Browser();
        await SignIn(browser);

        var html = await Html(await browser.GetAsync("/Rubric?tab=create"));
        // Row headers carry 1, 2, 3... beside the criterion name, matching the level columns.
        var numbers = Regex.Matches(html, @"data-matrix-row-number>(?<n>\d+)<")
            .Select(match => match.Groups["n"].Value).ToList();
        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Count).Select(n => n.ToString()), numbers);
        Assert.Contains("Rows[0].Criterion", html);
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
