using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Subjects;

/// <summary>
/// One subject and its topics. Each form posts to its own named handler
/// (OnPostUpdate, OnPostCreateTopic, OnPostDeleteTopic) and validates only its own input.
/// </summary>
public sealed class DetailsModel(ICatalogService catalog, ILiveUpdates live) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public SubjectDto Subject { get; private set; } = null!;
    public IReadOnlyList<TopicDto> Topics { get; private set; } = [];

    public CatalogEntryInput SubjectForm { get; set; } = new();
    public CatalogEntryInput TopicForm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
            return NotFound();
        SubjectForm = new CatalogEntryInput { Name = Subject.Name, Description = Subject.Description };
        return Page();
    }

    /// <summary>GET ?handler=Topics — the topic table, refreshed by SignalR.</summary>
    public async Task<IActionResult> OnGetTopicsAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Partial("_TopicRows", this) : NotFound();

    public async Task<IActionResult> OnPostUpdateAsync([Bind(Prefix = nameof(SubjectForm))] CatalogEntryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return await RedisplayAsync(cancellationToken, subjectForm: input);

        try
        {
            await catalog.UpdateSubjectAsync(Id, new SubjectInput(input.Name.Trim(), input.Description?.Trim() ?? string.Empty), cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            return await RedisplayAsync(cancellationToken, subjectForm: input);
        }

        await live.EntityChangedAsync(LiveEntities.Subject, LiveActions.Updated, Id, input.Name);
        TempData["Success"] = "Đã lưu môn học.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostCreateTopicAsync([Bind(Prefix = nameof(TopicForm))] CatalogEntryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return await RedisplayAsync(cancellationToken, topicForm: input);

        try
        {
            var topic = await catalog.CreateTopicAsync(new TopicInput(Id, input.Name.Trim(), input.Description?.Trim() ?? string.Empty), cancellationToken);
            await live.EntityChangedAsync(LiveEntities.Topic, LiveActions.Created, topic.Id, topic.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            return await RedisplayAsync(cancellationToken, topicForm: input);
        }

        TempData["Success"] = "Đã thêm chủ đề.";
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostDeleteTopicAsync(int topicId, CancellationToken cancellationToken)
    {
        var topic = (await catalog.GetTopicsAsync(Id, cancellationToken)).FirstOrDefault(item => item.Id == topicId);
        if (topic is null)
            return RedirectToPage(new { Id });

        try
        {
            await catalog.DeleteTopicAsync(topicId, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            TempData["Error"] = L10n.T(ex.Message);
            return RedirectToPage(new { Id });
        }

        await live.EntityChangedAsync(LiveEntities.Topic, LiveActions.Deleted, topicId, topic.Name);
        TempData["Success"] = $"Đã xoá chủ đề \"{topic.Name}\".";
        return RedirectToPage(new { Id });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var subject = (await catalog.GetSubjectsAsync(cancellationToken)).FirstOrDefault(item => item.Id == Id);
        if (subject is null)
            return false;
        Subject = subject;
        Topics = await catalog.GetTopicsAsync(Id, cancellationToken);
        return true;
    }

    private async Task<IActionResult> RedisplayAsync(CancellationToken cancellationToken,
        CatalogEntryInput? subjectForm = null, CatalogEntryInput? topicForm = null)
    {
        if (!await LoadAsync(cancellationToken))
            return NotFound();
        SubjectForm = subjectForm ?? new CatalogEntryInput { Name = Subject.Name, Description = Subject.Description };
        TopicForm = topicForm ?? new CatalogEntryInput();
        return Page();
    }
}
