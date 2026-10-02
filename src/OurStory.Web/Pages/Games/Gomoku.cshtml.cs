using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OurStory.Services.Games;
using OurStory.Web.Infrastructure;

namespace OurStory.Web.Pages.Games;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GomokuModel(GomokuService games) : PageModel {
    public async Task<IActionResult> OnGetStateAsync(CancellationToken cancellationToken) {
        if (User.UserId() is not { } id) return new JsonResult(new { ok = false, message = "请先登录，再和对方在线下棋。" }) { StatusCode = 401 };
        var result = await games.GetAsync(id, cancellationToken);
        return new JsonResult(result) { StatusCode = result.Ok ? 200 : 403 };
    }

    // Razor Pages 自动验证防伪令牌，身份和落子颜色均取自服务端。
    public async Task<IActionResult> OnPostOnlineAsync(string action, int version, int row, int col, CancellationToken cancellationToken) {
        if (User.UserId() is not { } id) return new JsonResult(new { ok = false, message = "登录已过期，请重新登录。" }) { StatusCode = 401 };
        var result = await games.ActAsync(id, action, version, row, col, cancellationToken);
        return new JsonResult(result) { StatusCode = result.Ok ? 200 : 409 };
    }
}
