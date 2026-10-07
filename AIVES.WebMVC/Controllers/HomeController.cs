using AIVES.BLL.Services.Dashboard;
using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using AIVES.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

namespace AIVES.WebMVC.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDashboardService _dashboard;
        private readonly IExamService _exams;

        public HomeController(IDashboardService dashboard, IExamService exams)
        {
            _dashboard = dashboard;
            _exams = exams;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (User.IsInRole(AppRoles.Admin))
            {
                var adminDashboard = await _dashboard.GetAdminDashboardAsync(cancellationToken);
                return View("Index", adminDashboard);
            }

            if (User.IsInRole(AppRoles.Lecturer))
            {
                var actor = User.ToExamActor();
                var lecturerDashboard = await _dashboard.GetLecturerDashboardAsync(actor, cancellationToken);
                ViewBag.UpcomingExams = (await _exams.ListAsync(actor, cancellationToken))
                    .Where(e => e.StartsAtUtc > DateTime.UtcNow && e.StartsAtUtc <= DateTime.UtcNow.AddDays(7))
                    .OrderBy(e => e.StartsAtUtc)
                    .ToList();
                return View("Index", lecturerDashboard);
            }

            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
            var studentDashboard = await _dashboard.GetStudentDashboardAsync(email, cancellationToken);
            ViewBag.UpcomingExams = (await _exams.ListForCandidateAsync(email, cancellationToken))
                .Where(e => e.StartsAtUtc > DateTime.UtcNow && e.StartsAtUtc <= DateTime.UtcNow.AddDays(7))
                .OrderBy(e => e.StartsAtUtc)
                .ToList();
            return View("Index", studentDashboard);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetLanguage(AppLanguage language, string? returnUrl)
        {
            var culture = language.ToCultureCode();
            Response.Cookies.Append(PresentationDependencyInjection.LanguageCookieName, culture, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index), "Home")!);
        }
    }
}