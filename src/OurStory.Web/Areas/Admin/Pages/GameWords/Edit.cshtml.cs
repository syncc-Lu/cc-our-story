using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OurStory.Services.Games.Draw;
using OurStory.Web.Infrastructure;

namespace OurStory.Web.Areas.Admin.Pages.GameWords;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditModel(DrawWordService words, DrawPairAccess access) : PageModel {
    [BindProperty] public InputModel Input { get; set; } = new();
    public int WordId { get; private set; }
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) {
        if (User.UserId() is not { } userId || await access.GetAsync(userId, cancellationToken) is null) return Forbid();
        WordId = id;
        if (id != 0) {
            var word = await words.GetAsync(userId, id, cancellationToken);
            if (word is null) return NotFound();
            Input = new() { Answer = word.Answer, Aliases = word.Aliases, Category = word.Category, Difficulty = word.Difficulty, Enabled = word.Enabled };
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken) {
        if (User.UserId() is not { } userId) return Challenge();
        WordId = id;
        if (!ModelState.IsValid) return Page();
        var error = await words.SaveAsync(userId, id, new(Input.Answer, Input.Aliases ?? "", Input.Category, Input.Difficulty, Input.Enabled), cancellationToken);
        if (error is not null) { ModelState.AddModelError(string.Empty, error); return Page(); }
        TempData["Flash"] = "词条已保存，下一次选词时生效。";
        return Redirect("/admin/game-words");
    }
    public sealed class InputModel {
        [Required, StringLength(48)] public string Answer { get; set; } = "";
        [StringLength(500)] public string? Aliases { get; set; }
        [Required, StringLength(20)] public string Category { get; set; } = "专属回忆";
        [Range(1, 3)] public int Difficulty { get; set; } = 1;
        public bool Enabled { get; set; } = true;
    }
}
