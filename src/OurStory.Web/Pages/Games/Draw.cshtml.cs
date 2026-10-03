using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OurStory.Services.Games.Draw;
using OurStory.Web.Infrastructure;

namespace OurStory.Web.Pages.Games;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(32768)]
public sealed class DrawModel(DrawGameService games, DrawCoordinator coordinator) : PageModel {
    private static JsonResult Reply(DrawResponse result) => new(result) { StatusCode = result.Forbidden ? 403 : result.Ok ? 200 : 409 };

    public async Task<IActionResult> OnGetStateAsync(int? version, bool wait, string? epoch, int since, CancellationToken cancellationToken) {
        if (User.UserId() is not { } id) return Unauthorized();
        var changed = coordinator.NextChange;
        var result = await games.GetAsync(id, epoch ?? "", since, cancellationToken);
        if (!result.Ok || !wait || (result.Game?.Version ?? 0) != version) return Reply(result);
        var delay = TimeSpan.FromSeconds(20);
        if (result.Game is { Stage: "drawing", EndsAt: { } end } state) {
            var halfTime = end.AddSeconds(-state.RoundSeconds / 2.0);
            var next = halfTime > state.ServerNow ? halfTime : end;
            delay = TimeSpan.FromMilliseconds(Math.Clamp((next - state.ServerNow).TotalMilliseconds, 1, 20000));
        }
        try { await changed.WaitAsync(delay, cancellationToken); }
        catch (TimeoutException) { }
        return Reply(await games.GetAsync(id, epoch ?? "", since, cancellationToken));
    }

    // Razor Pages 防伪验证同时保护选词、画画和猜词；所有角色与时限在业务层重新校验。
    public async Task<IActionResult> OnPostActAsync([FromBody] DrawCommand? command, CancellationToken cancellationToken) {
        if (User.UserId() is not { } id) return Unauthorized();
        if (command is null || !ModelState.IsValid) return BadRequest(new { ok = false, message = "请求内容无效。" });
        return Reply(await games.ActAsync(id, command, cancellationToken));
    }
}
