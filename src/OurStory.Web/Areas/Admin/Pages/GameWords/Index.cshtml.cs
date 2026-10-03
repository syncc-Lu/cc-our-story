using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OurStory.Services.Games.Draw;
using OurStory.Web.Infrastructure;

namespace OurStory.Web.Areas.Admin.Pages.GameWords;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(DrawWordService words) : PageModel {
    public DrawWordPage Items { get; private set; } = new([], 0, 1);
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    public async Task<IActionResult> OnGetAsync(int p = 1, CancellationToken cancellationToken = default) {
        if (User.UserId() is not { } userId) return Challenge();
        var result = await words.ListAsync(userId, Search, p, cancellationToken);
        if (result is null) return Forbid();
        Items = result;
        return Page();
    }
    public async Task<IActionResult> OnPostChangeAsync(int id, bool delete, CancellationToken cancellationToken) {
        if (User.UserId() is not { } userId) return Challenge();
        if (!await words.RemoveAsync(userId, id, delete, cancellationToken)) return NotFound();
        TempData["Flash"] = delete ? "词条已删除，已经开始的轮次不受影响。" : "词条启用状态已更新，下次选词生效。";
        return RedirectToPage();
    }
}
