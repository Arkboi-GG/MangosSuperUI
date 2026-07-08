using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

// The Search half of the wiki controller (W2) — the first sibling partial slotting in
// exactly as WikiController.Code.cs's header promised, with no edit to that file:
// services arrive via action-level injection instead of widening the shared constructor.
//
// GET  /Wiki/Search?q=...&take=20  ->  WikiSearchResponse
// POST /Wiki/Reindex               ->  force a full rebuild (after upgrades / logic changes)
// GET  /Wiki/IndexStatus           ->  the indexer's live status
//
// { ready:false } means the docs_* index isn't reachable or hasn't finished its first
// build. The index is self-healing: the search path kicks WikiIndexer, which rebuilds
// in the background whenever the docs folder changes — the client shows live progress
// instead of erroring.
public sealed partial class WikiController
{
    public async Task<IActionResult> Search(
        string? q,
        [FromServices] WikiSearchStore search,
        int take = 20,
        CancellationToken ct = default)
    {
        return Json(await search.SearchAsync(q ?? "", Math.Clamp(take, 1, 50), ct));
    }

    [HttpPost]
    public IActionResult Reindex([FromServices] WikiIndexer indexer)
        => Json(new { started = indexer.ForceReindex(), status = indexer.Status });

    public IActionResult IndexStatus([FromServices] WikiIndexer indexer)
        => Json(indexer.Status);
}