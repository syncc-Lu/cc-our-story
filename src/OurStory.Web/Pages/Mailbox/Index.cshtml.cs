using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OurStory.Core.Entities;
using OurStory.Services.Mailbox;
using OurStory.Web.Infrastructure;

namespace OurStory.Web.Pages.Mailbox;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(MailboxService mailbox) : PageModel {
    [BindProperty] public string MessageText { get; set; } = string.Empty;
    public List<PrivateMessage> Messages { get; private set; } = [];
    public int CurrentUserId => User.UserId() ?? 0;
    public int CurrentPage { get; private set; }
    public bool HasNext { get; private set; }
    public string? Error { get; private set; }
    public string? Flash { get; private set; }

    public async Task<IActionResult> OnGetAsync(int p = 1, CancellationToken cancellationToken = default) {
        if (User.UserId() is not { } userId || !await mailbox.CanAccessAsync(userId, cancellationToken)) return Forbid();
        CurrentPage = Math.Clamp(p, 1, 1000000);
        Messages = await mailbox.ConversationAsync(userId, CurrentPage, cancellationToken);
        HasNext = Messages.Count > MailboxService.PageSize;
        Messages = Messages.Take(MailboxService.PageSize).Reverse().ToList();
        Flash = TempData["MailboxFlash"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) {
        if (User.UserId() is not { } userId || !await mailbox.CanAccessAsync(userId, cancellationToken)) return Forbid();
        Error = await mailbox.SendAsync(userId, MessageText, cancellationToken);
        if (Error is not null) return await OnGetAsync(1, cancellationToken);
        TempData["MailboxFlash"] = "留言已发布，只有你们两个人能看到。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken) {
        if (User.UserId() is not { } userId) return Forbid();
        if (!await mailbox.DeleteAsync(userId, id, cancellationToken)) return NotFound();
        TempData["MailboxFlash"] = "你的留言已删除。";
        return RedirectToPage();
    }
}
