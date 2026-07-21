/* MangosSuperUI :: Addon Builder -- addon-codegen.js
 *
 * Turns a project model into a real, loadable vanilla addon:
 *
 *     <AddonName>.toc
 *     <AddonName>.xml     layout, in Blizzard's own dialect
 *     <AddonName>.lua     behaviour stub + selected snippets
 *
 * Layout goes in XML on purpose. It validates against
 * Interface\FrameXML\UI.xsd, it is what a visual designer maps onto cleanly,
 * and it keeps generated Lua down to behaviour a human will actually edit.
 *
 * Every emitted Lua line obeys the 1.12 / Lua 5.0 rules: 5-argument SetPoint,
 * this/arg1/event globals rather than self parameters, no string.match, no
 * # length operator, no table.getn.
 */
(function (root) {
    "use strict";

    var M = root.AddonModel;

    function esc(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function pad(n) {
        var s = "", i;
        for (i = 0; i < n; i += 1) { s += "\t"; }
        return s;
    }

    /* ---------------------------------------------------------------- */
    /* TOC                                                              */
    /* ---------------------------------------------------------------- */

    function generateToc(p) {
        var out = [];
        out.push("## Interface: " + (p.interfaceVersion || "11200"));
        out.push("## Title: " + (p.title || p.addonName));
        if (p.notes) { out.push("## Notes: " + p.notes); }
        if (p.author) { out.push("## Author: " + p.author); }
        out.push("## Version: " + (p.version || "1.0"));
        if (p.savedVariables && p.savedVariables.length) {
            out.push("## SavedVariables: " + p.savedVariables.join(", "));
        }
        if (p.savedVariablesPerCharacter && p.savedVariablesPerCharacter.length) {
            out.push("## SavedVariablesPerCharacter: " +
                p.savedVariablesPerCharacter.join(", "));
        }
        out.push("");
        out.push(p.addonName + ".xml");
        out.push(p.addonName + ".lua");
        out.push("");
        return out.join("\n");
    }

    /* ---------------------------------------------------------------- */
    /* XML                                                              */
    /* ---------------------------------------------------------------- */

    function emitSize(w, depth) {
        if (w.width == null && w.height == null) { return []; }
        return [
            pad(depth) + "<Size>",
            pad(depth + 1) + '<AbsDimension x="' + (w.width || 0) +
            '" y="' + (w.height || 0) + '"/>',
            pad(depth) + "</Size>"
        ];
    }

    function emitAnchors(w, depth) {
        if (!w.anchors || !w.anchors.length) { return []; }
        var out = [pad(depth) + "<Anchors>"];
        w.anchors.forEach(function (a) {
            var attrs = 'point="' + esc(a.point) + '"';
            if (a.relativeTo) { attrs += ' relativeTo="' + esc(a.relativeTo) + '"'; }
            if (a.relativePoint && a.relativePoint !== a.point) {
                attrs += ' relativePoint="' + esc(a.relativePoint) + '"';
            }
            if (a.x || a.y) {
                out.push(pad(depth + 1) + "<Anchor " + attrs + ">");
                out.push(pad(depth + 2) + "<Offset>");
                out.push(pad(depth + 3) + '<AbsDimension x="' + (a.x || 0) +
                    '" y="' + (a.y || 0) + '"/>');
                out.push(pad(depth + 2) + "</Offset>");
                out.push(pad(depth + 1) + "</Anchor>");
            } else {
                out.push(pad(depth + 1) + "<Anchor " + attrs + "/>");
            }
        });
        out.push(pad(depth) + "</Anchors>");
        return out;
    }

    function emitScripts(w, depth) {
        var keys = Object.keys(w.scripts || {}).filter(function (k) {
            return w.scripts[k];
        });
        if (!keys.length) { return []; }
        var out = [pad(depth) + "<Scripts>"];
        keys.forEach(function (k) {
            out.push(pad(depth + 1) + "<" + k + ">");
            out.push(pad(depth + 2) + esc(w.scripts[k]) + "();");
            out.push(pad(depth + 1) + "</" + k + ">");
        });
        out.push(pad(depth) + "</Scripts>");
        return out;
    }

    function emitLayerElement(w, depth) {
        var tag = w.type;
        var attrs = "";
        if (w.name) { attrs += ' name="' + esc(w.name) + '"'; }
        if (w.inherits) { attrs += ' inherits="' + esc(w.inherits) + '"'; }
        if (w.file) { attrs += ' file="' + esc(w.file) + '"'; }
        if (w.text) { attrs += ' text="' + esc(w.text) + '"'; }
        if (w.justifyH) { attrs += ' justifyH="' + esc(w.justifyH) + '"'; }
        if (w.setAllPoints) { attrs += ' setAllPoints="true"'; }
        if (w.hidden) { attrs += ' hidden="true"'; }

        var body = [].concat(emitSize(w, depth + 1), emitAnchors(w, depth + 1));
        if (!body.length) {
            return [pad(depth) + "<" + tag + attrs + "/>"];
        }
        return [pad(depth) + "<" + tag + attrs + ">"]
            .concat(body, [pad(depth) + "</" + tag + ">"]);
    }

    function emitFrame(w, depth) {
        var tag = w.type;
        var attrs = "";
        if (w.name) { attrs += ' name="' + esc(w.name) + '"'; }
        if (w.inherits) { attrs += ' inherits="' + esc(w.inherits) + '"'; }
        if (w.parentName) { attrs += ' parent="' + esc(w.parentName) + '"'; }
        if (w.toplevel) { attrs += ' toplevel="true"'; }
        if (w.enableMouse) { attrs += ' enableMouse="true"'; }
        if (w.setAllPoints) { attrs += ' setAllPoints="true"'; }
        if (w.hidden) { attrs += ' hidden="true"'; }

        var out = [pad(depth) + "<" + tag + attrs + ">"];
        out = out.concat(emitSize(w, depth + 1), emitAnchors(w, depth + 1));

        /* Textures and FontStrings go in <Layers>, grouped by draw layer, in
         * paint order. Layer order is load-bearing in 1.12 -- getting it wrong
         * silently hides things. */
        var layerKids = w.children.filter(function (c) {
            return M.isLayerElement(c.type);
        });
        if (layerKids.length) {
            out.push(pad(depth + 1) + "<Layers>");
            M.DRAW_LAYERS.forEach(function (layerName) {
                var inLayer = layerKids.filter(function (c) {
                    return (c.layer || "ARTWORK") === layerName;
                });
                if (!inLayer.length) { return; }
                out.push(pad(depth + 2) + '<Layer level="' + layerName + '">');
                inLayer.forEach(function (c) {
                    out = out.concat(emitLayerElement(c, depth + 3));
                });
                out.push(pad(depth + 2) + "</Layer>");
            });
            out.push(pad(depth + 1) + "</Layers>");
        }

        /* Button face textures */
        if (w.buttonText) {
            out.push(pad(depth + 1) + '<ButtonText name="$parentText"/>');
        }

        var frameKids = w.children.filter(function (c) {
            return !M.isLayerElement(c.type);
        });
        if (frameKids.length) {
            out.push(pad(depth + 1) + "<Frames>");
            frameKids.forEach(function (c) {
                out = out.concat(emitFrame(c, depth + 2));
            });
            out.push(pad(depth + 1) + "</Frames>");
        }

        out = out.concat(emitScripts(w, depth + 1));
        out.push(pad(depth) + "</" + tag + ">");
        return out;
    }

    function generateXml(p) {
        var out = [];
        out.push('<Ui xmlns="http://www.blizzard.com/wow/ui/"');
        out.push('    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"');
        out.push('    xsi:schemaLocation="http://www.blizzard.com/wow/ui/ ' +
            '..\\FrameXML\\UI.xsd">');
        out.push('\t<Script file="' + p.addonName + '.lua"/>');
        out.push("");
        p.widgets.forEach(function (w) {
            out = out.concat(emitFrame(w, 1));
        });
        out.push("</Ui>");
        out.push("");
        return out.join("\n");
    }

    /* ---------------------------------------------------------------- */
    /* Lua                                                              */
    /* ---------------------------------------------------------------- */

    function generateLua(p, snippetLibrary) {
        var ns = p.addonName;
        var out = [];

        out.push("-- " + ns + " -- generated by the MangosSuperUI Addon Builder");
        out.push("-- Layout lives in " + ns + ".xml. Behaviour lives here.");
        out.push("--");
        out.push("-- Vanilla 1.12 / Lua 5.0 rules apply throughout:");
        out.push("--   SetPoint takes 5 arguments, always");
        out.push("--   handlers use this / arg1 / event globals, not self");
        out.push("--   no string.match, no string.gmatch, no # operator, no table.getn");
        out.push("");
        out.push(ns + " = " + ns + " or {}");
        (p.savedVariables || []).forEach(function (v) {
            out.push(v + " = " + v + " or {}");
        });
        (p.savedVariablesPerCharacter || []).forEach(function (v) {
            out.push(v + " = " + v + " or {}");
        });
        out.push("");
        out.push("local A = " + ns + ";");
        out.push("");

        out.push("function A.Print(msg)");
        out.push("\tDEFAULT_CHAT_FRAME:AddMessage(\"|cff00ccff[" + ns +
            "]|r \" .. tostring(msg));");
        out.push("end");
        out.push("");

        /* Snippet library */
        (p.snippets || []).forEach(function (id) {
            var snip = snippetLibrary && snippetLibrary[id];
            if (!snip) { return; }
            out.push("-- ============================================================");
            out.push("-- " + snip.title);
            if (snip.note) { out.push("-- " + snip.note); }
            out.push("-- ============================================================");
            out.push(snip.code.replace(/\{\{NS\}\}/g, ns));
            out.push("");
        });

        /* Handler stubs for anything wired in the XML */
        var handlers = {};
        M.walk(p.widgets, function (w) {
            Object.keys(w.scripts || {}).forEach(function (k) {
                if (w.scripts[k]) {
                    handlers[w.scripts[k]] = { widget: w.name || w.type, script: k };
                }
            });
        });
        var hnames = Object.keys(handlers);
        if (hnames.length) {
            out.push("-- ============================================================");
            out.push("-- Handlers wired from " + ns + ".xml");
            out.push("-- ============================================================");
            out.push("");
            hnames.forEach(function (h) {
                out.push("function " + h + "()");
                out.push("\t-- " + handlers[h].script + " on " + handlers[h].widget);
                out.push("\t-- 'this' is the frame; 'arg1' carries the event argument");
                out.push("end");
                out.push("");
            });
        }

        /* Attach gate for load-on-demand Blizzard UI */
        if (p.attachTo) {
            out.push("-- ============================================================");
            out.push("-- Attach to " + p.attachTo);
            out.push("--");
            out.push("-- " + p.attachTo + " is LOAD ON DEMAND. The host frame does not");
            out.push("-- exist at login, and hooking its toggle global does not work --");
            out.push("-- the addon redefines that global when it loads and eats the hook.");
            out.push("-- Gate on ADDON_LOADED instead, and handle the already-loaded case.");
            out.push("-- ============================================================");
            out.push("");
            out.push("function A.Attach()");
            out.push("\tif ( A.attached ) then return; end");
            if (p.hostFrame) {
                out.push("\tif ( not " + p.hostFrame + " ) then return; end");
            }
            out.push("\tA.attached = 1;");
            out.push("\t-- reparent your top-level frame here, e.g.");
            if (p.hostFrame && p.widgets.length && p.widgets[0].name) {
                out.push("\t-- " + p.widgets[0].name + ":SetParent(" + p.hostFrame + ");");
                out.push("\t-- " + p.widgets[0].name + ':SetPoint("TOPLEFT", ' +
                    p.hostFrame + ', "TOPRIGHT", 2, 0);');
            }
            out.push("end");
            out.push("");
            out.push("local loader = CreateFrame(\"Frame\");");
            out.push("loader:RegisterEvent(\"ADDON_LOADED\");");
            out.push("loader:RegisterEvent(\"PLAYER_LOGIN\");");
            out.push("loader:SetScript(\"OnEvent\", function()");
            out.push("\tif ( event == \"ADDON_LOADED\" ) then");
            out.push("\t\tif ( arg1 == \"" + p.attachTo + "\" ) then");
            out.push("\t\t\tA.Attach();");
            out.push("\t\tend");
            out.push("\telseif ( event == \"PLAYER_LOGIN\" ) then");
            out.push("\t\tif ( IsAddOnLoaded(\"" + p.attachTo + "\") ) then");
            out.push("\t\t\tA.Attach();");
            out.push("\t\tend");
            out.push("\tend");
            out.push("end);");
            out.push("");
        }

        /* Show/hide reminders for hidden templates */
        var hiddenNamed = [];
        M.walk(p.widgets, function (w) {
            if (w.hidden && w.name) { hiddenNamed.push(w.name); }
        });
        if (hiddenNamed.length) {
            out.push("-- These frames are hidden=\"true\" in the XML. Call :Show() when ready:");
            hiddenNamed.forEach(function (n) {
                out.push("--   " + n + ":Show();");
            });
            out.push("");
        }

        return out.join("\n");
    }

    /* ---------------------------------------------------------------- */

    function generateAll(p, snippetLibrary) {
        return {
            folder: p.addonName,
            files: [
                { name: p.addonName + ".toc", content: generateToc(p) },
                { name: p.addonName + ".xml", content: generateXml(p) },
                { name: p.addonName + ".lua", content: generateLua(p, snippetLibrary) }
            ]
        };
    }

    root.AddonCodegen = {
        generateToc: generateToc,
        generateXml: generateXml,
        generateLua: generateLua,
        generateAll: generateAll
    };

}(typeof window !== "undefined" ? window : global));
