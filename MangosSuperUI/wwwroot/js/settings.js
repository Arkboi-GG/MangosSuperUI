// MangosSuperUI — Settings Page JS

$(function () {

    // ===================== FORM STATE =====================
    //
    // Two rules keep this form honest:
    //
    //   1. A field the operator has edited is never overwritten by a background
    //      load. Anything else silently eats typing.
    //   2. After a save, the form binds to the file the server just wrote and
    //      handed back - NOT to /Settings/Current.
    //
    // Rule 2 is the fix for the revert bug: server-config.json is registered with
    // reloadOnChange, but that reload is a file-watcher event that lands after the
    // save response. Re-reading /Settings/Current here painted pre-save values back
    // over the edits, and a second Save then wrote those stale values to disk,
    // undoing the first save.

    var fileStamp = null;        // content hash of server-config.json as last seen
    var suppressDirty = false;   // true while binding programmatically
    var loadedReconnectDelayMs = 3000;   // no form field; preserved across saves

    function $cfgFields() { return $('.cfg-input'); }

    // Dirty means "the operator touched this and it no longer matches what we last
    // bound". Touch is tracked explicitly rather than inferred: on first paint there
    // is no baseline yet, and a value typed into a field while the initial request is
    // still in flight must not be treated as bindable-over.
    function isDirty($el) {
        if (!$el.data('cfgTouched')) return false;
        var base = $el.data('cfgBaseline');
        return base === undefined
            ? String($el.val()) !== ''
            : String($el.val()) !== String(base);
    }

    function baseline($el) {
        $el.data('cfgBaseline', String($el.val()));
        $el.data('cfgTouched', false);
        $el.removeClass('cfg-dirty');
    }

    function nodesJson() { return JSON.stringify(getComfyNodesFromUI()); }

    function nodesDirty() {
        var base = $('#comfyNodesContainer').data('cfgBaseline');
        return base !== undefined && nodesJson() !== base;
    }

    function dirtyCount() {
        var n = $cfgFields().filter(function () { return isDirty($(this)); }).length;
        if (nodesDirty()) n++;
        return n;
    }

    function refreshDirtyUi() {
        $cfgFields().each(function () { $(this).toggleClass('cfg-dirty', isDirty($(this))); });
        $('#comfyNodesContainer').toggleClass('cfg-dirty', nodesDirty());

        var n = dirtyCount();
        $('#btnSaveConfig').html(n > 0
            ? '<i class="fa-solid fa-floppy-disk"></i> Save Settings <span class="dirty-badge">' + n + '</span>'
            : '<i class="fa-solid fa-floppy-disk"></i> Save Settings');
        $('#btnRevertConfig').toggle(n > 0);

        // Browsers ignore the string, but returning one triggers the prompt.
        window.onbeforeunload = n > 0
            ? function () { return 'You have unsaved settings changes.'; }
            : null;
    }

    $(document).on('input change', '.cfg-input, #comfyNodesContainer input', function () {
        if (suppressDirty) return;
        $(this).data('cfgTouched', true);
        refreshDirtyUi();
    });

    // Write one field unless the operator has edited it since the last bind.
    // Returns 1 if the incoming value was withheld to protect an edit.
    function setField(sel, value, force) {
        var $el = $(sel);
        if (!$el.length) return 0;
        if (!force && isDirty($el)) return 1;
        $el.val(value == null ? '' : value);
        baseline($el);
        return 0;
    }

    // ===================== BIND CONFIG =====================

    // force=true discards local edits (used after a save, when the file IS the truth).
    // Returns how many edited fields were left alone.
    function bindConfig(s, force) {
        if (!s) return 0;
        var kept = 0;
        suppressDirty = true;

        var cs = s.connectionStrings || {};
        kept += setField('#cfgMangos', cs.mangos, force);
        kept += setField('#cfgCharacters', cs.characters, force);
        kept += setField('#cfgRealmd', cs.realmd, force);
        kept += setField('#cfgLogs', cs.logs, force);
        kept += setField('#cfgAdmin', cs.admin, force);

        var ra = s.remoteAccess || {};
        // No form field for this one - round-trip it so a save can't reset it to the
        // JS default and quietly overwrite a value set in the file.
        if (ra.reconnectDelayMs != null) loadedReconnectDelayMs = ra.reconnectDelayMs;
        kept += setField('#cfgRaHost', ra.host, force);
        kept += setField('#cfgRaPort', ra.port, force);
        kept += setField('#cfgRaUser', ra.username, force);
        kept += setField('#cfgRaPass', ra.password, force);
        kept += setField('#cfgRaTimeout', ra.commandTimeoutMs, force);

        var vm = s.vmangos || {};
        kept += setField('#cfgBinDir', vm.binDirectory, force);
        kept += setField('#cfgRunDir', vm.runDirectory, force);
        kept += setField('#cfgLogDir', vm.logDirectory, force);
        kept += setField('#cfgConfDir', vm.configDirectory, force);
        kept += setField('#cfgMangosdProcess', vm.mangosdProcess, force);
        kept += setField('#cfgRealmdProcess', vm.realmdProcess, force);
        kept += setField('#cfgMangosdConfPath', vm.mangosdConfPath, force);
        kept += setField('#cfgLogsDir', vm.logsDir, force);
        kept += setField('#cfgDbcPath', vm.dbcPath, force);
        kept += setField('#cfgMapsDataPath', vm.mapsDataPath, force);
        kept += setField('#cfgBackupDir', vm.backupDirectory, force);
        kept += setField('#cfgSourcePath', vm.vmangosSourcePath, force);
        kept += setField('#cfgSqlPath', vm.vmangosSqlPath, force);
        kept += setField('#cfgExtractorsPath', vm.extractorsPath, force);
        kept += setField('#cfgServerDataPath', vm.serverDataPath, force);
        kept += setField('#cfgVmangosClientDataPath', vm.clientDataPath, force);
        kept += setField('#cfgVmapsDataPath', vm.vmapsDataPath, force);

        var sc = s.spellCreator || {};
        kept += setField('#cfgClientM2Path', sc.clientM2Path, force);
        kept += setField('#cfgClientDataPath', sc.clientDataPath, force);
        kept += setField('#cfgPatchOutputPath', sc.patchOutputPath, force);
        kept += setField('#cfgRawBlpPath', sc.rawBlpPath, force);
        kept += setField('#cfgSpellDataPath', sc.dataPath, force);
        kept += setField('#cfgClipModel2', sc.comfyUI ? sc.comfyUI.clipModel2 : '', force);

        var ol = sc.ollama || {};
        kept += setField('#cfgOllamaUrl', ol.baseUrl, force);
        kept += setField('#cfgOllamaModel', ol.model, force);
        kept += setField('#cfgOllamaVisionModel', ol.visionModel, force);

        var wf = s.weaponForge || {};
        kept += setField('#cfgTbcDataPath', wf.tbcDataPath, force);
        kept += setField('#cfgWotlkDataPath', wf.wotlkDataPath, force);

        kept += setField('#cfgWikiRoot', s.wiki ? s.wiki.root : '', force);
        kept += setField('#cfgKestrelUrl', s.kestrel ? s.kestrel.url : '', force);

        // Node rows get rebuilt wholesale, so only touch them when they are clean.
        if (force || !nodesDirty()) {
            renderComfyNodes(sc.comfyUI ? sc.comfyUI.nodes : []);
            $('#comfyNodesContainer').data('cfgBaseline', nodesJson()).removeClass('cfg-dirty');
        } else {
            kept++;
        }

        suppressDirty = false;
        refreshDirtyUi();
        return kept;
    }

    // ===================== LOAD CURRENT CONFIG =====================

    // Reads the RUNNING config (appsettings + override, merged). Correct on page load;
    // after a save, bind to the save response instead - see the note at the top.
    function loadConfig(force) {
        $.getJSON('/Settings/Current', function (data) {
            fileStamp = data.fileStamp || null;
            var kept = bindConfig(data.settings, force === true);

            if (data.overrideExists) {
                $('#configStatusTitle').text('Using server-config.json overrides');
                $('#configStatusDetail').text('Config file: ' + data.configFilePath);
                $('#configStatusCard').css('border-left', '3px solid var(--status-online)');
            } else {
                $('#configStatusTitle').text('Using appsettings.json defaults (no override file)');
                $('#configStatusDetail').text('Save settings to create a server-config.json override file.');
                $('#configStatusCard').css('border-left', '3px solid var(--accent)');
            }

            if (kept > 0) {
                $('#configStatusDetail').append(
                    ' — ' + kept + ' field(s) you edited were left as typed.');
            }
        });

        loadStatuses();
    }

    // Status panels only - never touches form fields.
    function loadStatuses() {
        loadDbcStatus();
        loadComfyStatus();
        loadBackupStatus();
        loadWikiStatus();
    }

    // ===================== COMFYUI NODE MANAGEMENT =====================

    function renderComfyNodes(nodes) {
        var $container = $('#comfyNodesContainer');
        $container.empty();

        if (!nodes || nodes.length === 0) {
            nodes = [{ name: '', baseUrl: '' }];
        }

        nodes.forEach(function (node, idx) {
            $container.append(buildNodeRow(node.name, node.baseUrl, idx));
        });
    }

    function buildNodeRow(name, url, idx) {
        return '<div class="comfy-node-row" data-node-idx="' + idx + '">' +
            '<input type="text" class="form-input node-name-input" placeholder="Name" value="' + escapeAttr(name) + '" />' +
            '<input type="text" class="form-input node-url-input" placeholder="http://192.168.0.244:8188" value="' + escapeAttr(url) + '" />' +
            '<span class="node-status-dot" title="Unknown" style="background: var(--text-muted);"></span>' +
            '<button class="btn-remove-node" title="Remove node"><i class="fa-solid fa-xmark"></i></button>' +
            '</div>';
    }

    // Add node button
    $('#btnAddComfyNode').on('click', function () {
        var idx = $('#comfyNodesContainer .comfy-node-row').length;
        $('#comfyNodesContainer').append(buildNodeRow('', '', idx));
    });

    // Remove node button (delegated)
    $('#comfyNodesContainer').on('click', '.btn-remove-node', function () {
        var $rows = $('#comfyNodesContainer .comfy-node-row');
        if ($rows.length <= 1) {
            showMessage('error', 'At least one ComfyUI node is required.');
            return;
        }
        $(this).closest('.comfy-node-row').remove();
    });

    // Collect node data from UI
    function getComfyNodesFromUI() {
        var nodes = [];
        $('#comfyNodesContainer .comfy-node-row').each(function () {
            var name = $(this).find('.node-name-input').val().trim();
            var url = $(this).find('.node-url-input').val().trim();
            if (url) {
                nodes.push({ name: name || ('node' + (nodes.length + 1)), baseUrl: url });
            }
        });
        return nodes;
    }

    // ===================== COMFYUI POOL STATUS =====================

    function loadComfyStatus() {
        $.getJSON('/Settings/ComfyPoolStatus', function (data) {
            var $panel = $('#comfyStatusPanel');
            var $row = $('#comfyStatusRow');

            if (data && data.length > 0) {
                var chips = '';
                data.forEach(function (node) {
                    var color = node.online
                        ? (node.busy ? 'var(--status-warning)' : 'var(--status-online)')
                        : 'var(--status-error)';
                    var label = node.online
                        ? (node.busy ? 'Busy (' + node.running + ' running, ' + node.pending + ' queued)' : 'Idle')
                        : (node.error ? 'Offline: ' + node.error : 'Offline');

                    chips += '<span class="dbc-count-chip">' +
                        '<span class="node-status-dot" style="background: ' + color + ';"></span> ' +
                        escapeHtml(node.name) + ': <span class="count-val">' + escapeHtml(label) + '</span></span> ';
                });

                $row.html(
                    '<i class="fa-solid fa-circle-check" style="font-size: 13px; color: var(--status-online);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">ComfyUI node pool</span>' +
                    '<div class="d-flex flex-wrap gap-2 mt-2">' + chips + '</div>'
                );

                var allOnline = data.every(function (n) { return n.online; });
                $panel.css('border-left', '3px solid ' + (allOnline ? 'var(--status-online)' : 'var(--status-warning)'));

                // Also update the dots next to each node row
                data.forEach(function (node) {
                    $('#comfyNodesContainer .comfy-node-row').each(function () {
                        var rowUrl = $(this).find('.node-url-input').val().trim().replace(/\/+$/, '');
                        var nodeUrl = (node.baseUrl || '').replace(/\/+$/, '');
                        if (rowUrl && nodeUrl && rowUrl === nodeUrl) {
                            var dotColor = node.online
                                ? (node.busy ? 'var(--status-warning)' : 'var(--status-online)')
                                : 'var(--status-error)';
                            var dotTitle = node.online
                                ? (node.busy ? 'Busy' : 'Idle')
                                : 'Offline';
                            $(this).find('.node-status-dot')
                                .css('background', dotColor)
                                .attr('title', dotTitle);
                        }
                    });
                });
            } else {
                $row.html(
                    '<i class="fa-solid fa-circle-xmark" style="font-size: 13px; color: var(--text-muted);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">No ComfyUI nodes configured</span>'
                );
                $panel.css('border-left', '3px solid var(--text-muted)');
            }
        }).fail(function () {
            $('#comfyStatusRow').html(
                '<i class="fa-solid fa-circle-xmark" style="font-size: 13px; color: var(--status-error);"></i>' +
                '<span style="font-size: 12.5px; color: var(--text-secondary);">Could not reach ComfyUI status endpoint</span>'
            );
            $('#comfyStatusPanel').css('border-left', '3px solid var(--status-error)');
        });
    }

    // ===================== DBC STATUS =====================

    function loadDbcStatus() {
        $.getJSON('/Dbc/Status', function (data) {
            var $panel = $('#dbcStatusPanel');
            var $row = $('#dbcStatusRow');

            if (data.isLoaded) {
                var chips = '';
                for (var dbcName in data.counts) {
                    chips += '<span class="dbc-count-chip">' + escapeHtml(dbcName) +
                        ': <span class="count-val">' + data.counts[dbcName] + '</span></span> ';
                }
                $row.html(
                    '<i class="fa-solid fa-circle-check" style="font-size: 13px; color: var(--status-online);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">DBC loaded from <code>' +
                    escapeHtml(data.dbcPath) + '</code></span>' +
                    '<div class="d-flex flex-wrap gap-2 mt-2">' + chips + '</div>'
                );
                $panel.css('border-left', '3px solid var(--status-online)');
            } else {
                var errMsg = data.error || 'DBC files not loaded';
                $row.html(
                    '<i class="fa-solid fa-triangle-exclamation" style="font-size: 13px; color: var(--status-warning);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">' + escapeHtml(errMsg) + '</span>' +
                    '<div style="font-size: 11.5px; color: var(--text-muted); margin-top: 4px;">' +
                    'Spell/Item browsers will not show icons until DBC files are available at the configured path.</div>'
                );
                $panel.css('border-left', '3px solid var(--status-warning)');
            }
        }).fail(function () {
            $('#dbcStatusRow').html(
                '<i class="fa-solid fa-circle-xmark" style="font-size: 13px; color: var(--status-error);"></i>' +
                '<span style="font-size: 12.5px; color: var(--text-secondary);">Could not reach DBC status endpoint</span>'
            );
            $('#dbcStatusPanel').css('border-left', '3px solid var(--status-error)');
        });
    }

    // ===================== VANILLA BACKUP STATUS =====================

    function loadBackupStatus() {
        $.getJSON('/WorldEditor/BackupStatus', function (data) {
            var $panel = $('#backupStatusPanel');
            var $row = $('#backupStatusRow');

            if (!data || data.error) {
                $row.html(
                    '<i class="fa-solid fa-circle-xmark" style="font-size: 13px; color: var(--status-error);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">' +
                    escapeHtml(data ? data.error : 'Could not check backup status') + '</span>'
                );
                $panel.css('border-left', '3px solid var(--status-error)');
                return;
            }

            if (data.totalBackups === 0) {
                $row.html(
                    '<i class="fa-solid fa-circle-info" style="font-size: 13px; color: var(--text-muted);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">' +
                    'No vanilla backups yet &mdash; backups are created automatically when server data is first regenerated after a WMO placement commit.</span>'
                );
                $panel.css('border-left', '3px solid var(--text-muted)');
            } else {
                var chips = '';
                if (data.dirBinBackup)
                    chips += '<span class="dbc-count-chip"><i class="fa-solid fa-check" style="color:var(--status-online);font-size:10px;"></i> dir_bin.vanilla</span> ';
                if (data.vmapFiles > 0)
                    chips += '<span class="dbc-count-chip"><i class="fa-solid fa-check" style="color:var(--status-online);font-size:10px;"></i> vmaps: <span class="count-val">' + data.vmapFiles + ' file(s)</span></span> ';
                if (data.mmapFiles > 0)
                    chips += '<span class="dbc-count-chip"><i class="fa-solid fa-check" style="color:var(--status-online);font-size:10px;"></i> mmaps: <span class="count-val">' + data.mmapFiles + ' file(s)</span></span> ';
                if (data.clientVmapFiles > 0)
                    chips += '<span class="dbc-count-chip"><i class="fa-solid fa-check" style="color:var(--status-online);font-size:10px;"></i> client vmaps: <span class="count-val">' + data.clientVmapFiles + ' file(s)</span></span> ';
                if (data.clientMmapFiles > 0)
                    chips += '<span class="dbc-count-chip"><i class="fa-solid fa-check" style="color:var(--status-online);font-size:10px;"></i> client mmaps: <span class="count-val">' + data.clientMmapFiles + ' file(s)</span></span> ';

                $row.html(
                    '<i class="fa-solid fa-shield-halved" style="font-size: 13px; color: var(--status-online);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">Vanilla backups available &mdash; Restore Defaults in the ' +
                    '<a href="/WorldEditor" style="color: var(--accent);">World Editor</a> placement panel will use these.</span>' +
                    '<div class="d-flex flex-wrap gap-2 mt-2">' + chips + '</div>'
                );
                $panel.css('border-left', '3px solid var(--status-online)');
            }
        }).fail(function () {
            var $panel = $('#backupStatusPanel');
            var $row = $('#backupStatusRow');
            $row.html(
                '<i class="fa-solid fa-circle-info" style="font-size: 13px; color: var(--text-muted);"></i>' +
                '<span style="font-size: 12.5px; color: var(--text-secondary);">Backup status unavailable (World Editor paths may not be configured)</span>'
            );
            $panel.css('border-left', '3px solid var(--text-muted)');
        });
    }

    // ===================== RELOAD DBC =====================
    $('#btnReloadDbc').on('click', function () {
        var $btn = $(this);
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Reloading...');

        $('#dbcStatusRow').html(
            '<i class="fa-solid fa-spinner fa-spin" style="font-size: 13px; color: var(--text-muted);"></i>' +
            '<span style="font-size: 12.5px; color: var(--text-secondary);">Reloading DBC files...</span>'
        );

        $.ajax({
            url: '/Dbc/Reload',
            type: 'POST',
            success: function (data) {
                if (data.success) {
                    showMessage('success', 'DBC files reloaded successfully');
                } else {
                    showMessage('error', 'DBC reload failed: ' + (data.error || 'Unknown error'));
                }
            },
            error: function (xhr) {
                showMessage('error', 'DBC reload request failed: ' + xhr.statusText);
            },
            complete: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-arrows-rotate"></i> Reload DBC');
                loadDbcStatus();
            }
        });
    });

    // ===================== WIKI STATUS =====================

    function loadWikiStatus() {
        $.getJSON('/Wiki/Stats', function (stats) {
            var $panel = $('#wikiStatusPanel');
            var $row = $('#wikiStatusRow');

            if (!stats || !stats.ready) {
                $row.html(
                    '<i class="fa-solid fa-triangle-exclamation" style="font-size: 13px; color: var(--status-warning);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">No docs found at the configured root &mdash; the Wiki page will be empty until the corpus is in place.</span>'
                );
                $panel.css('border-left', '3px solid var(--status-warning)');
                return;
            }

            // Corpus is present — layer the search-index state on top.
            $.getJSON('/Wiki/IndexStatus', function (idx) {
                var chips =
                    '<span class="dbc-count-chip">pages: <span class="count-val">' + (stats.pageCount || 0) + '</span></span> ' +
                    '<span class="dbc-count-chip">folders: <span class="count-val">' + (stats.folderCount || 0) + '</span></span> ';

                var idxLabel, idxColor;
                if (idx && idx.building) {
                    idxLabel = 'building ' + (idx.done || 0) + ' / ' + (idx.total || 0);
                    idxColor = 'var(--status-warning)';
                    setTimeout(loadWikiStatus, 2000);   // live progress while it builds
                } else if (idx && idx.lastError) {
                    idxLabel = 'error: ' + idx.lastError;
                    idxColor = 'var(--status-error)';
                } else if (idx && idx.lastCompletedUtc) {
                    idxLabel = 'ready';
                    idxColor = 'var(--status-online)';
                } else {
                    idxLabel = 'idle (builds on first search)';
                    idxColor = 'var(--text-muted)';
                }
                chips += '<span class="dbc-count-chip"><span class="node-status-dot" style="background: ' + idxColor + ';"></span> search index: <span class="count-val">' + escapeHtml(idxLabel) + '</span></span>';

                $('#wikiStatusRow').html(
                    '<i class="fa-solid fa-circle-check" style="font-size: 13px; color: var(--status-online);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">Corpus loaded from <code>' + escapeHtml(stats.root || '') + '</code></span>' +
                    '<div class="d-flex flex-wrap gap-2 mt-2">' + chips + '</div>'
                );
                $('#wikiStatusPanel').css('border-left', '3px solid var(--status-online)');
            }).fail(function () {
                // Stats worked, index endpoint didn't — show the corpus part alone.
                $('#wikiStatusRow').html(
                    '<i class="fa-solid fa-circle-check" style="font-size: 13px; color: var(--status-online);"></i>' +
                    '<span style="font-size: 12.5px; color: var(--text-secondary);">Corpus loaded (' + (stats.pageCount || 0) + ' pages) &mdash; index status unavailable</span>'
                );
                $('#wikiStatusPanel').css('border-left', '3px solid var(--status-online)');
            });
        }).fail(function () {
            $('#wikiStatusRow').html(
                '<i class="fa-solid fa-circle-xmark" style="font-size: 13px; color: var(--status-error);"></i>' +
                '<span style="font-size: 12.5px; color: var(--text-secondary);">Could not reach the wiki status endpoint</span>'
            );
            $('#wikiStatusPanel').css('border-left', '3px solid var(--status-error)');
        });
    }

    $('#btnWikiReindex').on('click', function () {
        var $btn = $(this);
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Rebuilding...');

        $.ajax({
            url: '/Wiki/Reindex',
            type: 'POST',
            success: function (data) {
                if (data && data.started) {
                    showMessage('success', 'Search index rebuild started \u2014 progress shows in the Wiki status below.');
                } else {
                    showMessage('error', 'Rebuild not started \u2014 a build is already running, or the docs root / Admin database is unavailable.');
                }
            },
            error: function (xhr) {
                showMessage('error', 'Reindex request failed: ' + xhr.statusText);
            },
            complete: function () {
                $btn.prop('disabled', false).html('<i class="fa-solid fa-arrows-rotate"></i> Rebuild Search Index');
                loadWikiStatus();
            }
        });
    });

    // ===================== SAVE =====================
    $('#btnSaveConfig').on('click', function () {
        var config = {
            connectionStrings: {
                mangos: $('#cfgMangos').val(),
                characters: $('#cfgCharacters').val(),
                realmd: $('#cfgRealmd').val(),
                logs: $('#cfgLogs').val(),
                admin: $('#cfgAdmin').val()
            },
            remoteAccess: {
                host: $('#cfgRaHost').val(),
                port: parseInt($('#cfgRaPort').val()) || 3443,
                username: $('#cfgRaUser').val(),
                password: $('#cfgRaPass').val(),
                reconnectDelayMs: loadedReconnectDelayMs,
                commandTimeoutMs: parseInt($('#cfgRaTimeout').val()) || 5000
            },
            vmangos: {
                binDirectory: $('#cfgBinDir').val(),
                runDirectory: $('#cfgRunDir').val() || '',
                logDirectory: $('#cfgLogDir').val(),
                configDirectory: $('#cfgConfDir').val(),
                mangosdProcess: $('#cfgMangosdProcess').val() || 'mangosd',
                realmdProcess: $('#cfgRealmdProcess').val() || 'realmd',
                mangosdConfPath: $('#cfgMangosdConfPath').val() || '',
                logsDir: $('#cfgLogsDir').val() || '',
                dbcPath: $('#cfgDbcPath').val() || '',
                mapsDataPath: $('#cfgMapsDataPath').val() || '',
                backupDirectory: $('#cfgBackupDir').val() || '',
                vmangosSourcePath: $('#cfgSourcePath').val() || '',
                vmangosSqlPath: $('#cfgSqlPath').val() || '',
                extractorsPath: $('#cfgExtractorsPath').val() || '',
                serverDataPath: $('#cfgServerDataPath').val() || '',
                clientDataPath: $('#cfgVmangosClientDataPath').val() || '',
                vmapsDataPath: $('#cfgVmapsDataPath').val() || ''
            },
            spellCreator: {
                comfyUI: {
                    nodes: getComfyNodesFromUI(),
                    clipModel2: $('#cfgClipModel2').val() || ''
                },
                ollama: {
                    baseUrl: $('#cfgOllamaUrl').val() || '',
                    model: $('#cfgOllamaModel').val() || '',
                    visionModel: $('#cfgOllamaVisionModel').val() || ''
                },
                rawBlpPath: $('#cfgRawBlpPath').val() || '',
                dataPath: $('#cfgSpellDataPath').val() || '',
                clientM2Path: $('#cfgClientM2Path').val() || '',
                clientDataPath: $('#cfgClientDataPath').val() || '',
                patchOutputPath: $('#cfgPatchOutputPath').val() || ''
            },
            weaponForge: {
                tbcDataPath: $('#cfgTbcDataPath').val() || '',
                wotlkDataPath: $('#cfgWotlkDataPath').val() || ''
            },
            wiki: {
                root: $('#cfgWikiRoot').val() || ''
            },
            kestrel: {
                url: $('#cfgKestrelUrl').val()
            }
        };

        postSave(config, false);
    });

    // expectedStamp lets the server refuse the write if server-config.json changed
    // underneath us (hand edit, setup script, a second tab). force=true says
    // "I saw the conflict, overwrite anyway".
    function postSave(config, force) {
        var $btn = $('#btnSaveConfig');
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Saving...');

        $.ajax({
            url: '/Settings/Save',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ settings: config, expectedStamp: fileStamp, force: !!force }),
            success: function (data) {
                if (data.success) {
                    // Bind to what is actually on disk now, not to IConfiguration:
                    // the reloadOnChange watcher has not fired yet at this point.
                    fileStamp = data.fileStamp || null;
                    bindConfig(data.settings, true);
                    showSaveResult(data);
                    loadStatuses();
                } else if (data.conflict) {
                    fileStamp = data.fileStamp || fileStamp;
                    pendingConfig = config;
                    showConflict(data);
                } else {
                    showMessage('error', 'Save failed: ' + data.error);
                }
            },
            error: function (xhr) {
                showMessage('error', 'Request failed: ' + xhr.statusText);
            },
            complete: function () {
                $btn.prop('disabled', false);
                refreshDirtyUi();
            }
        });
    }

    // The form the operator tried to save, held across a conflict prompt.
    var pendingConfig = null;

    function showSaveResult(data) {
        var keys = data.changedKeys || [];
        if (keys.length === 0) {
            showMessage('success', data.message);
            return;
        }

        var list = keys.slice(0, 12).map(function (k) {
            return '<code class="chg-key">' + escapeHtml(k) + '</code>';
        }).join(' ');
        if (keys.length > 12) list += ' <span class="chg-more">+' + (keys.length - 12) + ' more</span>';

        $('#saveMessageBody').html(
            '<i class="fa-solid fa-circle-check" style="color: var(--status-online); font-size: 18px;"></i>' +
            '<div style="font-size: 13.5px;">' +
            '<div style="font-weight:600;">' + escapeHtml(data.message) + '</div>' +
            '<div class="chg-list">' + list + '</div>' +
            (data.restartRequired
                ? '<div class="restart-note">Most values are read once at startup, so restart to be sure they take effect:' +
                  '<div class="restart-cmd"><code>' + escapeHtml(data.restartCommand || '') + '</code>' +
                  '<button class="btn-xs btn-copy-cmd" data-cmd="' + escapeAttr(data.restartCommand || '') + '">' +
                  '<i class="fa-solid fa-copy"></i> Copy</button></div></div>'
                : '') +
            '</div>');
        $('#saveMessage').show();
    }

    function showConflict(data) {
        $('#saveMessageBody').html(
            '<i class="fa-solid fa-triangle-exclamation" style="color: var(--status-warning); font-size: 18px;"></i>' +
            '<div style="font-size: 13.5px;">' +
            '<div style="font-weight:600;">Nothing was written — ' + escapeHtml(data.error) + '</div>' +
            '<div style="color: var(--text-secondary); margin-top:4px;">' +
            'Someone or something else rewrote the file since this page loaded. Overwriting would ' +
            'discard those changes.</div>' +
            '<div style="margin-top:8px; display:flex; gap:8px;">' +
            '<button class="btn-xs" id="btnConflictReload"><i class="fa-solid fa-rotate"></i> Discard mine, load the file</button>' +
            '<button class="btn-xs" id="btnConflictForce"><i class="fa-solid fa-triangle-exclamation"></i> Overwrite the file with my values</button>' +
            '</div></div>');
        $('#saveMessage').show();
    }

    $(document).on('click', '#btnConflictReload', function () {
        pendingConfig = null;
        $('#saveMessage').hide();
        loadConfig(true);
    });

    $(document).on('click', '#btnConflictForce', function () {
        if (!pendingConfig) return;
        $('#saveMessage').hide();
        postSave(pendingConfig, true);
    });

    $(document).on('click', '.btn-copy-cmd', function () {
        var cmd = $(this).data('cmd');
        var $b = $(this);
        if (navigator.clipboard) {
            navigator.clipboard.writeText(cmd).then(function () {
                $b.html('<i class="fa-solid fa-check"></i> Copied');
                setTimeout(function () { $b.html('<i class="fa-solid fa-copy"></i> Copy'); }, 1500);
            });
        }
    });

    // ===================== REVERT =====================
    $('#btnRevertConfig').on('click', function () {
        if (dirtyCount() === 0) return;
        if (!confirm('Discard your unsaved changes and reload the running configuration?')) return;
        $('#saveMessage').hide();
        loadConfig(true);
    });

    // ===================== RESET =====================
    $('#btnResetConfig').on('click', function () {
        if (!confirm('This deletes server-config.json ENTIRELY, including sections this page does not manage (e.g. BotChat inference profiles), and reverts to appsettings.json defaults on next restart. Continue?')) {
            return;
        }

        var $btn = $(this);
        $btn.prop('disabled', true);

        $.ajax({
            url: '/Settings/Reset',
            type: 'POST',
            success: function (data) {
                if (data.success) {
                    showMessage('success', data.message);
                } else {
                    showMessage('error', 'Reset failed: ' + data.error);
                }
            },
            error: function (xhr) {
                showMessage('error', 'Request failed: ' + xhr.statusText);
            },
            complete: function () {
                $btn.prop('disabled', false);
                fileStamp = null;
                loadConfig(true);   // the override is gone; defaults are the truth now
            }
        });
    });

    // ===================== FEEDBACK =====================
    function showMessage(type, text) {
        var icon = type === 'success'
            ? '<i class="fa-solid fa-circle-check" style="color: var(--status-online); font-size: 18px;"></i>'
            : '<i class="fa-solid fa-circle-exclamation" style="color: var(--status-error); font-size: 18px;"></i>';

        $('#saveMessageBody').html(icon + '<div style="font-size: 13.5px;">' + escapeHtml(text) + '</div>');
        $('#saveMessage').show();

        // Only the plain one-liners auto-hide. The save result and the conflict
        // prompt carry a command to run / buttons to click, so they stay put.
        setTimeout(function () { $('#saveMessage').fadeOut(300); }, 6000);
    }

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function escapeAttr(text) {
        return (text || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // ===================== INIT =====================
    loadConfig();

});