using MangosSuperUI.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

// The wiki controller is deliberately a *partial* from day one. This file — the "Code"
// half — serves the generated C++ code-documentation corpus: the browse tree, a rendered
// page (with its ToC and auto-linked cross-references), and corpus stats for the landing.
//
// Later halves of the wiki plan slot in as sibling partials with no edit to this file:
//   WikiController.Search.cs   -> GET /Wiki/Search   (W2 lexical/alias index)
//   WikiController.Topics.cs   -> GET /Wiki/Topic/{slug}  (W3 flow/"bridge" docs)
//   WikiController.Ask.cs      -> POST /Wiki/Ask     (W4 RAG over the corpus)
//
// Conventional routes (/Wiki/Action); wiki.js hard-codes the URLs, same as the other
// SuperUI feature pages.
public sealed partial class WikiController : Controller
{
    private readonly WikiDocStore _store;
    public WikiController(WikiDocStore store) => _store = store;

    // GET /Wiki  -> the reader shell (nav tree + article + ToC; wiki.js drives it)
    public IActionResult Index() => View();

    // GET /Wiki/Tree  -> folder-mirrored nav tree
    public IActionResult Tree() => Json(_store.Tree());

    // GET /Wiki/Page?path=game/AI/AiBotAI.Movement  -> one rendered page
    public IActionResult Page(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest();
        var page = _store.Page(path);
        return page is null ? NotFound() : Json(page);
    }

    // GET /Wiki/Stats  -> corpus summary for the landing / empty state
    public IActionResult Stats() => Json(_store.Stats());
}
