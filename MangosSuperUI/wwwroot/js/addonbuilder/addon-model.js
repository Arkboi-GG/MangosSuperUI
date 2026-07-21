/* MangosSuperUI :: Addon Builder -- addon-model.js
 *
 * The project model. One plain object describes a whole addon; the canvas
 * mutates it, the code generator reads it. Nothing in here touches the DOM,
 * so it is testable outside the browser.
 *
 * Deliberate design calls:
 *   - Widgets are anchor-based, never absolute. The canvas may let you drag,
 *     but a drag resolves to an anchor + offset before it is stored. Absolute
 *     coordinates produce UI that breaks at other resolutions.
 *   - Widgets are a tree. XML nesting is how parenting works in FrameXML.
 *   - Generation targets XML for layout and Lua only for behaviour. The XML
 *     dialect validates against Interface\FrameXML\UI.xsd.
 */
(function (root) {
    "use strict";

    var MODEL_VERSION = 1;

    /* Vanilla design surface. UIParent is 768 tall in UI units at default
     * scale; 1024x768 is the 4:3 reference the 1.12 UI was authored against. */
    var DESIGN_W = 1024;
    var DESIGN_H = 768;

    var ANCHOR_POINTS = [
        "TOPLEFT", "TOP", "TOPRIGHT",
        "LEFT", "CENTER", "RIGHT",
        "BOTTOMLEFT", "BOTTOM", "BOTTOMRIGHT"
    ];

    /* Widget types that may own children. Textures and FontStrings are leaves
     * and live in <Layers>, not <Frames>. */
    var CONTAINER_TYPES = [
        "Frame", "Button", "CheckButton", "EditBox", "Slider", "StatusBar",
        "ScrollFrame", "ScrollingMessageFrame", "SimpleHTML", "MessageFrame",
        "GameTooltip", "Model", "PlayerModel", "ColorSelect", "Cooldown"
    ];

    var LAYER_TYPES = ["Texture", "FontString"];

    var DRAW_LAYERS = ["BACKGROUND", "BORDER", "ARTWORK", "OVERLAY", "HIGHLIGHT"];

    var uidCounter = 0;
    function uid(prefix) {
        uidCounter += 1;
        return (prefix || "w") + "_" + uidCounter;
    }

    function isContainer(type) {
        return CONTAINER_TYPES.indexOf(type) !== -1;
    }
    function isLayerElement(type) {
        return LAYER_TYPES.indexOf(type) !== -1;
    }

    /* ---------------------------------------------------------------- */
    /* Project                                                          */
    /* ---------------------------------------------------------------- */

    function newProject(name) {
        name = name || "MyAddon";
        return {
            modelVersion: MODEL_VERSION,
            addonName: name,
            title: name,
            notes: "Created with the MangosSuperUI Addon Builder.",
            author: "",
            version: "1.0",
            interfaceVersion: "11200",
            savedVariables: [],
            savedVariablesPerCharacter: [],
            /* Blizzard LoD addons this addon attaches to. Drives the
             * ADDON_LOADED gate in the generated Lua. */
            attachTo: null,          /* e.g. "Blizzard_TalentUI" */
            hostFrame: null,         /* e.g. "TalentFrame" */
            snippets: [],            /* ids from the snippet library */
            widgets: []              /* tree of widget nodes */
        };
    }

    function newWidget(type, opts) {
        opts = opts || {};
        var w = {
            id: uid(),
            type: type,
            name: opts.name || "",
            inherits: opts.inherits || null,
            parentId: opts.parentId || null,

            width: opts.width == null ? null : opts.width,
            height: opts.height == null ? null : opts.height,

            anchors: opts.anchors || [],

            hidden: !!opts.hidden,
            enableMouse: !!opts.enableMouse,
            toplevel: !!opts.toplevel,
            setAllPoints: !!opts.setAllPoints,

            /* Texture / FontString only */
            layer: opts.layer || (isLayerElement(type) ? "ARTWORK" : null),
            file: opts.file || null,
            text: opts.text || null,
            justifyH: opts.justifyH || null,

            /* Button only */
            buttonText: opts.buttonText || null,

            scripts: opts.scripts || {},   /* { OnClick: "MyAddon_OnClick" } */
            children: []
        };
        return w;
    }

    function newAnchor(point, relativeTo, relativePoint, x, y) {
        return {
            point: point || "TOPLEFT",
            relativeTo: relativeTo || null,      /* null = parent */
            relativePoint: relativePoint || point || "TOPLEFT",
            x: x || 0,
            y: y || 0
        };
    }

    /* ---------------------------------------------------------------- */
    /* Tree helpers                                                     */
    /* ---------------------------------------------------------------- */

    function walk(widgets, fn, parent) {
        var i;
        for (i = 0; i < widgets.length; i += 1) {
            fn(widgets[i], parent || null);
            walk(widgets[i].children, fn, widgets[i]);
        }
    }

    function findById(project, id) {
        var hit = null;
        walk(project.widgets, function (w) {
            if (w.id === id) { hit = w; }
        });
        return hit;
    }

    function findParent(project, id) {
        var hit = null;
        walk(project.widgets, function (w, parent) {
            if (w.id === id) { hit = parent; }
        });
        return hit;
    }

    function addWidget(project, widget, parentId) {
        if (!parentId) {
            project.widgets.push(widget);
            return widget;
        }
        var parent = findById(project, parentId);
        if (!parent) {
            project.widgets.push(widget);
            return widget;
        }
        widget.parentId = parentId;
        parent.children.push(widget);
        return widget;
    }

    function removeWidget(project, id) {
        function prune(list) {
            var i;
            for (i = list.length - 1; i >= 0; i -= 1) {
                if (list[i].id === id) {
                    list.splice(i, 1);
                } else {
                    prune(list[i].children);
                }
            }
        }
        prune(project.widgets);
    }

    /* Every named widget in the project, for anchor target dropdowns. */
    function namedWidgets(project) {
        var out = [];
        walk(project.widgets, function (w) {
            if (w.name) { out.push(w.name); }
        });
        return out;
    }

    /* ---------------------------------------------------------------- */
    /* Applying a catalog template                                      */
    /* ---------------------------------------------------------------- */

    /* Drop a template from framexml_index.json onto the canvas. The catalog
     * record carries the real type and size, so an inherited widget arrives
     * looking like the thing Blizzard shipped. */
    function widgetFromTemplate(tpl, opts) {
        opts = opts || {};
        /* Resolved through the inheritance chain -- see the note in validate().
         * ActionBarButtonTemplate declares no size of its own but is 36x36 via
         * ActionButtonTemplate. */
        var eff = tpl.effective || null;
        var size = eff ? eff.size : tpl.size;

        var w = newWidget(tpl.type, {
            name: opts.name || "",
            inherits: tpl.name,
            width: size ? size.w : null,
            height: size ? size.h : null,
            hidden: eff ? !!eff.hidden : !!tpl.hidden,
            enableMouse: !!tpl.enableMouse
        });
        w._templateTextures = (eff ? eff.textures : tpl.textures) || [];
        w._templateScripts = (eff ? eff.scripts : tpl.scripts) || [];
        w._needsSize = !size;
        w._sizeFrom = eff ? eff.sizeFrom : (tpl.size ? tpl.name : null);
        return w;
    }

    /* ---------------------------------------------------------------- */
    /* Validation -- catches the traps this project learned the hard way */
    /* ---------------------------------------------------------------- */

    function validate(project, catalog) {
        var issues = [];
        var seenNames = {};
        var templateIndex = {};

        if (catalog && catalog.templates) {
            catalog.templates.forEach(function (t) { templateIndex[t.name] = t; });
        }

        if (!/^[A-Za-z0-9_]+$/.test(project.addonName)) {
            issues.push({
                level: "error",
                msg: "Addon name must be letters, numbers and underscores only " +
                    "-- the folder name must match the .toc filename."
            });
        }

        walk(project.widgets, function (w, parent) {
            var label = w.name || ("<unnamed " + w.type + ">");

            if (w.name) {
                if (seenNames[w.name]) {
                    issues.push({
                        level: "error", widgetId: w.id,
                        msg: "Duplicate frame name '" + w.name + "'. Global names must be unique."
                    });
                }
                seenNames[w.name] = true;
                if (!/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(w.name)) {
                    issues.push({
                        level: "error", widgetId: w.id,
                        msg: label + ": name is not a valid global identifier."
                    });
                }
            }

            /* The UIPanelButtonTemplate trap: virtual templates with no size
             * produce an invisible widget unless you set one.
             *
             * Size MUST be read through the RESOLVED inheritance chain, not off
             * the record: 86 templates inherit another template and 42 of them
             * take their size from an ancestor, so reading tpl.size directly
             * emits 42 false errors. `effective` is precomputed by
             * framexml_split.py; the catalog fallback covers an unsplit doc. */
            if (w.inherits && templateIndex[w.inherits]) {
                var tpl = templateIndex[w.inherits];
                var eff = tpl.effective || null;
                if (!eff && catalog && catalog.effective) {
                    eff = catalog.effective(w.inherits);
                }
                var effSize = eff ? eff.size : tpl.size;
                var effHidden = eff ? eff.hidden : tpl.hidden;
                var effScripts = (eff ? eff.scripts : tpl.scripts) || [];
                var effCalls = (eff ? eff.calls : tpl.calls) || [];

                var sizeless = (w.type !== "Font" && w.type !== "FontString");
                if (sizeless && !effSize && (w.width == null || w.height == null) &&
                    !w.setAllPoints) {
                    issues.push({
                        level: "error", widgetId: w.id,
                        msg: label + ": template '" + w.inherits + "' declares no size. " +
                            "Set width and height or the widget renders invisible."
                    });
                }
                if (effHidden && !w.hidden) {
                    issues.push({
                        level: "warn", widgetId: w.id,
                        msg: label + ": template '" + w.inherits + "' is hidden=\"true\". " +
                            "You must call :Show() at runtime."
                    });
                }
                /* Templates that hardcode a handler call in their XML OnClick */
                if (effCalls.length && effScripts.indexOf("OnClick") !== -1 &&
                    !w.scripts.OnClick) {
                    issues.push({
                        level: "warn", widgetId: w.id,
                        msg: label + ": '" + w.inherits + "' hardcodes an OnClick in its XML. " +
                            "Override it with SetScript or you will call Blizzard's handler."
                    });
                }
            } else if (w.inherits && catalog) {
                issues.push({
                    level: "error", widgetId: w.id,
                    msg: label + ": template '" + w.inherits + "' is not in the FrameXML catalog."
                });
            }

            if (!w.anchors.length && !w.setAllPoints) {
                issues.push({
                    level: "warn", widgetId: w.id,
                    msg: label + ": no anchor set. It will land at its parent's centre."
                });
            }

            w.anchors.forEach(function (a) {
                if (ANCHOR_POINTS.indexOf(a.point) === -1) {
                    issues.push({
                        level: "error", widgetId: w.id,
                        msg: label + ": invalid anchor point '" + a.point + "'."
                    });
                }
                if (a.relativeTo && !seenNames[a.relativeTo] &&
                    namedWidgets(project).indexOf(a.relativeTo) === -1) {
                    issues.push({
                        level: "warn", widgetId: w.id,
                        msg: label + ": anchors to '" + a.relativeTo +
                            "', which is not a frame in this project. " +
                            "Fine if it is a Blizzard frame, otherwise a typo."
                    });
                }
            });

            if (isLayerElement(w.type) && parent && !isContainer(parent.type)) {
                issues.push({
                    level: "error", widgetId: w.id,
                    msg: label + ": a " + w.type + " must live inside a frame."
                });
            }

            if (w.type === "Texture" && w.layer && DRAW_LAYERS.indexOf(w.layer) === -1) {
                issues.push({
                    level: "error", widgetId: w.id,
                    msg: label + ": unknown draw layer '" + w.layer + "'."
                });
            }
        });

        if (project.attachTo && !project.hostFrame) {
            issues.push({
                level: "warn",
                msg: "attachTo is set but no host frame chosen; the generated " +
                    "ADDON_LOADED gate will have nothing to parent to."
            });
        }

        return issues;
    }

    root.AddonModel = {
        MODEL_VERSION: MODEL_VERSION,
        DESIGN_W: DESIGN_W,
        DESIGN_H: DESIGN_H,
        ANCHOR_POINTS: ANCHOR_POINTS,
        CONTAINER_TYPES: CONTAINER_TYPES,
        LAYER_TYPES: LAYER_TYPES,
        DRAW_LAYERS: DRAW_LAYERS,
        newProject: newProject,
        newWidget: newWidget,
        newAnchor: newAnchor,
        widgetFromTemplate: widgetFromTemplate,
        addWidget: addWidget,
        removeWidget: removeWidget,
        findById: findById,
        findParent: findParent,
        namedWidgets: namedWidgets,
        walk: walk,
        isContainer: isContainer,
        isLayerElement: isLayerElement,
        validate: validate
    };

}(typeof window !== "undefined" ? window : global));
