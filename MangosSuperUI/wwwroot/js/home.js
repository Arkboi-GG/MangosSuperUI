// MangosSuperUI — Home/Dashboard Page JS

$(function () {

    // ===================== QUICK COMMAND =====================
    function sendQuickCommand() {
        var cmd = $('#quickCommand').val().trim();
        if (!cmd) return;

        var $output = $('#quickOutput');
        var $btn = $('#btnSendQuick');

        $btn.prop('disabled', true);
        $output.append('<div style="color: #7aa2f7;">&gt; ' + escapeHtml(cmd) + '</div>');

        $.ajax({
            url: '/Home/SendCommand',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ command: cmd }),
            success: function (data) {
                if (data.success) {
                    $output.append('<div>' + escapeHtml(data.response || '(no response)') + '</div>');
                } else {
                    $output.append('<div style="color: #f7768e;">Error: ' + escapeHtml(data.error) + '</div>');
                }
            },
            error: function (xhr) {
                $output.append('<div style="color: #f7768e;">Request failed: ' + xhr.statusText + '</div>');
            },
            complete: function () {
                $btn.prop('disabled', false);
                $output.scrollTop($output[0].scrollHeight);
                $('#quickCommand').val('').focus();
            }
        });
    }

    $('#btnSendQuick').on('click', sendQuickCommand);

    $('#quickCommand').on('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            sendQuickCommand();
        }
    });

    // ===================== QUICK ACTIONS (systemd) =====================
    function processAction(service, action, $btn) {
        var originalHtml = $btn.html();
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        $.ajax({
            url: '/Home/ProcessAction',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ service: service, action: action }),
            success: function (data) {
                if (data.success) {
                    $('#quickOutput').append('<div style="color: #9ece6a;">' + escapeHtml(data.message) + '</div>');
                } else {
                    $('#quickOutput').append('<div style="color: #f7768e;">Error: ' + escapeHtml(data.error) + '</div>');
                }
                $('#quickOutput').scrollTop($('#quickOutput')[0].scrollHeight);
            },
            error: function (xhr) {
                $('#quickOutput').append('<div style="color: #f7768e;">Request failed: ' + xhr.statusText + '</div>');
            },
            complete: function () {
                $btn.prop('disabled', false).html(originalHtml);
                setTimeout(pollStatus, 2000);
            }
        });
    }

    function sendRaQuick(cmd, $btn) {
        var originalHtml = $btn.html();
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        $.ajax({
            url: '/Home/SendCommand',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ command: cmd }),
            success: function (data) {
                if (data.success) {
                    $('#quickOutput').append('<div style="color: #9ece6a;">' + escapeHtml(data.response || 'OK') + '</div>');
                } else {
                    $('#quickOutput').append('<div style="color: #f7768e;">Error: ' + escapeHtml(data.error) + '</div>');
                }
                $('#quickOutput').scrollTop($('#quickOutput')[0].scrollHeight);
            },
            complete: function () {
                $btn.prop('disabled', false).html(originalHtml);
            }
        });
    }

    $('#btnStartWorld').on('click', function () { processAction('mangosd', 'start', $(this)); });
    $('#btnStopWorld').on('click', function () { processAction('mangosd', 'stop', $(this)); });
    $('#btnStartAuth').on('click', function () { processAction('realmd', 'start', $(this)); });
    $('#btnStopAuth').on('click', function () { processAction('realmd', 'stop', $(this)); });
    $('#btnSaveAll').on('click', function () { sendRaQuick('.saveall', $(this)); });

    $('#btnRestartBoth').on('click', function () {
        var $btn = $(this);
        var originalHtml = $btn.html();
        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i>');

        $.ajax({
            url: '/Home/ProcessAction',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ service: 'realmd', action: 'restart' }),
            complete: function () {
                $.ajax({
                    url: '/Home/ProcessAction',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({ service: 'mangosd', action: 'restart' }),
                    complete: function () {
                        $btn.prop('disabled', false).html(originalHtml);
                        $('#quickOutput').append('<div style="color: #9ece6a;">Restart both requested</div>');
                        $('#quickOutput').scrollTop($('#quickOutput')[0].scrollHeight);
                        setTimeout(pollStatus, 3000);
                    }
                });
            }
        });
    });

    // ===================== DESCRIPTOR LIMIT =====================
    // Checked once per page load, not on the 10 s poll: the limit only changes
    // when a unit restarts, and this reads /proc.
    function checkNoFileLimit() {
        $.getJSON('/Home/UnitLimits?unit=mangosd', function (d) {
            if (!d || !d.ok || !d.limitTooLow) { $('#nofileWarnRow').addClass('d-none'); return; }

            var soft = d.report && d.report.runningSoft;
            $('#nofileWarnTitle').text(
                'World server file-descriptor limit is ' + soft +
                ' — caps the bot fleet near ' + d.approximateBotCeiling + ' bots');
            $('#nofileWarnDetail').text(
                'Every bot holds one bridge socket in the world process, so this limit is a hard '
                + 'ceiling regardless of CPU or RAM. Recommended: ' + d.recommended + '. '
                + (d.remediation || ''));

            // Only offer the button when the privileged helper is actually
            // installed; otherwise the fix is re-running setup, and the text says so.
            $('#nofileFixBtn').toggleClass('d-none', !d.canFixInApp);
            $('#nofileWarnRow').removeClass('d-none');
        });
    }

    $(document).on('click', '#nofileFixBtn', function () {
        var $b = $(this).prop('disabled', true).text('Applying…');
        $.post('/Home/SetUnitNoFile', { unit: 'mangosd' }, function (r) {
            if (r && r.ok) {
                $b.addClass('d-none');
                $('#nofileWarnDetail').text(r.note + ' Restart the world server to apply it.');
            } else {
                $b.prop('disabled', false).text('Raise limit');
                $('#nofileWarnDetail').text('Could not apply: ' + ((r && r.error) || 'unknown error'));
            }
        }).fail(function () {
            $b.prop('disabled', false).text('Raise limit');
        });
    });

    checkNoFileLimit();

    // ===================== STATUS POLLING =====================
    var firstPollDone = false;

    // The CPU cards answer "how many cores is this using, out of how many";
    // the per-core strip below answers "which ones, and how hard". An aggregate
    // alone is actively misleading here: 0.44 spread over 32 cores looks idle
    // whether it is 32 cores at 1.4% or one core at 44%, and only the second
    // shape tells you anything about a serial world loop.
    //
    // A rate needs two readings, so the first poll after a page load genuinely
    // has no value. Show a placeholder, never 0 — 0 would read as "idle".
    function fmtCpuCard(breakdown, sample) {
        var total = (breakdown && breakdown.processorCount)
            || (sample && sample.processorCount) || null;
        if (!total) return '—';

        if (breakdown && breakdown.supported) {
            if (!breakdown.isRunning) return '— of ' + total + ' in use';
            if (!breakdown.cores || !breakdown.cores.length) return '… of ' + total + ' in use';
            return breakdown.coresInUse + ' of ' + total + ' in use';
        }

        // Non-Linux: /proc is unavailable, so only the aggregate exists.
        if (!sample || !sample.isRunning) return '— / ' + total + ' cores';
        if (sample.cpuPercent == null) return '… / ' + total + ' cores';
        return (sample.cpuPercent / 100).toFixed(2) + ' / ' + total + ' cores';
    }

    function cpuTitle(breakdown, sample) {
        var parts = [];
        if (breakdown && breakdown.supported && breakdown.isRunning) {
            parts.push(breakdown.totalCores.toFixed(2) + ' cores total across '
                + breakdown.threadCount + ' threads');
            if (breakdown.cores && breakdown.cores.length) {
                var b = breakdown.cores[0];
                parts.push('busiest core ' + b.core + ' at ' + b.percent.toFixed(1) + '%');
            }
        }
        if (sample && sample.cpuPercentOfHost != null) {
            parts.push(sample.cpuPercentOfHost.toFixed(2) + '% of total CPU capacity');
        }
        return parts.join(' · ');
    }

    // One cell per core, filled by how much of THAT core the process used.
    // Every core is drawn, present or idle, so the machine's width is visible
    // at a glance and a single hot core stands out against its idle neighbours.
    function coreStrip(breakdown, colour) {
        if (!breakdown || !breakdown.supported) {
            return '<span class="dash-label">per-core breakdown requires Linux</span>';
        }
        if (!breakdown.isRunning) return '<span class="dash-label">offline</span>';

        var byCore = {};
        (breakdown.cores || []).forEach(function (c) { byCore[c.core] = c; });

        var html = '<div style="display:flex; gap:2px; flex-wrap:wrap;">';
        for (var i = 0; i < breakdown.processorCount; i++) {
            var c = byCore[i];
            var pct = c ? c.percent : 0;
            var fill = Math.max(0, Math.min(100, pct));
            var tip = 'Core ' + i + ': ' + pct.toFixed(1) + '%'
                + (c && c.threads ? ' (' + c.threads + ' thread' + (c.threads > 1 ? 's' : '') + ')' : '');
            // Themed background + a real border: on the light theme a translucent white
            // fill is invisible against the white card, so idle cores vanished entirely.
            html += '<div title="' + tip + '" style="position:relative; width:22px; height:36px;'
                + ' border-radius:3px; background:var(--bg-card-alt); border:1px solid var(--border-medium);'
                + ' box-sizing:border-box; overflow:hidden;">'
                + '<div style="position:absolute; bottom:0; left:0; right:0; height:' + fill.toFixed(1)
                + '%; background:' + colour + ';"></div>'
                + '</div>';
        }
        return html + '</div>';
    }

    function coreDetailLine(breakdown) {
        if (!breakdown || !breakdown.supported || !breakdown.isRunning) return '';
        var hot = (breakdown.cores || []).filter(function (c) { return c.percent >= 1; });
        if (!hot.length) return 'no core above 1%';
        return hot.slice(0, 8).map(function (c) {
            return 'C' + c.core + ' ' + c.percent.toFixed(1) + '%';
        }).join(' · ') + (hot.length > 8 ? ' · +' + (hot.length - 8) + ' more' : '');
    }

    function renderCoreBreakdown(cores) {
        var $out = $('#coreBreakdown');
        if (!cores) { $out.empty(); return; }

        var rows = [
            { label: 'World Server', bd: cores.mangosd, colour: 'var(--accent, #4f8ef7)' },
            { label: 'SuperUI', bd: cores.superui, colour: 'var(--text-secondary)' }
        ];

        var html = '';
        rows.forEach(function (r) {
            var bd = r.bd;
            var summary = (bd && bd.supported && bd.isRunning)
                ? bd.coresInUse + ' of ' + bd.processorCount + ' cores in use · '
                  + bd.totalCores.toFixed(2) + ' cores total'
                : '';
            html += '<div class="mb-3">'
                + '<div class="d-flex justify-content-between flex-wrap gap-2 mb-1">'
                + '<div class="dash-value" style="font-size:15px;">' + r.label + '</div>'
                + '<div class="dash-label">' + summary + '</div>'
                + '</div>'
                + coreStrip(bd, r.colour)
                + '<div class="dash-label mt-1">' + coreDetailLine(bd) + '</div>'
                + '</div>';
        });
        $out.html(html);

        var host = cores.host;
        if (host && host.supported) {
            $('#coreHeading').text('CPU CORES · ' + host.processorCount + ' TOTAL');
            $('#hostCoreSummary').text('whole machine: ' + host.coresInUse + ' of '
                + host.processorCount + ' cores active · '
                + (host.totalPercent / 100).toFixed(2) + ' cores busy');
        } else if (host) {
            $('#coreHeading').text('CPU CORES · ' + host.processorCount + ' TOTAL');
            $('#hostCoreSummary').text('');
        }
    }

    function fmtMem(sample) {
        if (!sample || !sample.isRunning || sample.memoryBytes == null) return '—';
        var bytes = sample.memoryBytes;
        var text = bytes >= 1073741824
            ? (bytes / 1073741824).toFixed(2) + ' GiB'
            : (bytes / 1048576).toFixed(0) + ' MiB';
        if (sample.memoryPercentOfHost != null) {
            text += ' (' + sample.memoryPercentOfHost.toFixed(0) + '%)';
        }
        return text;
    }

    function pollStatus() {
        $.getJSON('/Home/Status', function (data) {
            // mangosd process
            var mRunning = data.mangosd && data.mangosd.isRunning;
            $('#mangosdStatus').removeClass('online offline').addClass(mRunning ? 'online' : 'offline');
            var mText = 'Offline';
            if (mRunning) {
                mText = 'Running (PID ' + data.mangosd.pid + ')';
                // Show resolved name if different from what you'd expect
                if (data.mangosd.processName) {
                    mText += ' · ' + data.mangosd.processName;
                }
            }
            $('#mangosdText').text(mText);

            // realmd process
            var rRunning = data.realmd && data.realmd.isRunning;
            $('#realmdStatus').removeClass('online offline').addClass(rRunning ? 'online' : 'offline');
            var rText = 'Offline';
            if (rRunning) {
                rText = 'Running (PID ' + data.realmd.pid + ')';
                if (data.realmd.processName) {
                    rText += ' · ' + data.realmd.processName;
                }
            }
            $('#realmdText').text(rText);

            // Process CPU / RAM
            var res = data.resources || {};
            var cores = data.cores || {};
            $('#mangosdCpu').text(fmtCpuCard(cores.mangosd, res.mangosd))
                            .attr('title', cpuTitle(cores.mangosd, res.mangosd));
            $('#mangosdMem').text(fmtMem(res.mangosd));
            $('#superuiCpu').text(fmtCpuCard(cores.superui, res.superui))
                            .attr('title', cpuTitle(cores.superui, res.superui));
            $('#superuiMem').text(fmtMem(res.superui));
            renderCoreBreakdown(cores);

            // RA
            $('#raStatus').removeClass('online offline error').addClass(data.raConnected ? 'online' : 'offline');
            $('#raStatusText').text(data.raConnected ? 'Connected' : 'Not connected');

            // Server info (from RA .server info parse)
            $('#playersOnline').text(data.playersOnline != null ? data.playersOnline : '—');
            $('#maxOnline').text(data.maxOnline != null ? data.maxOnline : '—');
            $('#serverUptime').text(data.uptime || '—');
            $('#coreRevision').text(data.coreRevision || '—');

            // DB stats
            $('#totalAccounts').text(data.totalAccounts != null ? data.totalAccounts : '—');
            $('#totalCharacters').text(data.totalCharacters != null ? data.totalCharacters : '—');
            $('#gmAccounts').text(data.gmAccounts != null ? data.gmAccounts : '—');
            $('#bannedAccounts').text(data.bannedAccounts != null ? data.bannedAccounts : '—');

            // On first poll, check if things look broken → auto-run diagnose
            if (!firstPollDone) {
                firstPollDone = true;
                var allDown = !mRunning && !rRunning && !data.raConnected;
                if (allDown) {
                    // Probably first run or misconfigured — auto-diagnose
                    runDiagnose(true);
                }
            }
        });
    }

    pollStatus();
    setInterval(pollStatus, 20000);

    // ===================== DATABASE HEALTH (once on load) =====================
    function loadDbHealth() {
        var $panel = $('#dbHealthPanel');
        var $body = $('#dbHealthBody');

        $.getJSON('/Home/DbHealth', function (data) {
            var html = '';

            // Per-database connectivity chips
            var dbOrder = [
                { key: 'mangos', label: 'mangos' },
                { key: 'characters', label: 'characters' },
                { key: 'realmd', label: 'realmd' },
                { key: 'logs', label: 'logs' },
                { key: 'vmangos_admin', label: 'vmangos_admin' }
            ];

            html += '<div class="db-health-chips">';
            var allOk = true;
            for (var i = 0; i < dbOrder.length; i++) {
                var db = dbOrder[i];
                var info = data.databases[db.key];
                var ok = info && info.reachable;
                if (!ok) allOk = false;

                var dotClass = ok ? 'online' : 'offline';
                var tooltip = ok ? 'Connected' : (info && info.error ? info.error : 'Unreachable');

                html += '<span class="db-health-chip" title="' + escapeHtml(tooltip) + '">';
                html += '<span class="status-dot ' + dotClass + '" style="width: 8px; height: 8px;"></span>';
                html += '<span class="db-health-chip-label">' + escapeHtml(db.label) + '</span>';
                html += '</span>';
            }
            html += '</div>';

            // Admin DB init status
            if (data.adminInitialized) {
                var detail = '';
                if (data.tablesCreated > 0) {
                    detail = data.tablesCreated + ' table(s) created on this boot';
                } else {
                    detail = 'All tables already existed';
                }

                html += '<div class="db-health-init-status">';
                html += '<i class="fa-solid fa-circle-check" style="color: var(--status-online); font-size: 12px;"></i> ';
                html += '<span>' + escapeHtml(detail) + '</span>';
                html += '</div>';

                $panel.css('border-left-color', allOk ? 'var(--status-online)' : 'var(--status-warning)');
            } else {
                var errMsg = data.adminInitError || 'vmangos_admin bootstrap failed';
                html += '<div class="db-health-init-status db-health-init-error">';
                html += '<i class="fa-solid fa-triangle-exclamation" style="color: var(--status-error); font-size: 12px;"></i> ';
                html += '<span>' + escapeHtml(errMsg) + '</span>';
                html += '</div>';

                $panel.css('border-left-color', 'var(--status-error)');
            }

            $body.html(html);
        }).fail(function () {
            $body.html(
                '<span style="color: var(--text-muted); font-size: 12.5px;">' +
                '<i class="fa-solid fa-circle-xmark" style="color: var(--status-error);"></i> ' +
                'Could not reach health endpoint</span>'
            );
            $panel.css('border-left-color', 'var(--status-error)');
        });
    }

    loadDbHealth();

    // ===================== DIAGNOSE =====================

    $('#btnDiagnose').on('click', function () { runDiagnose(false); });

    function runDiagnose(isAutoRun) {
        var $btn = $('#btnDiagnose');
        var $panel = $('#diagPanel');
        var $body = $('#diagBody');

        $btn.prop('disabled', true).html('<i class="fa-solid fa-spinner fa-spin"></i> Diagnosing...');
        $panel.slideDown(200);
        $body.html('<div style="color: var(--text-muted); font-size: 12.5px;"><i class="fa-solid fa-spinner fa-spin"></i> Running diagnostics...</div>');

        $.getJSON('/Home/Diagnose', function (data) {
            var html = '';

            // Show first-run banner if detected
            if (data.summary.isFirstRun) {
                $('#setupBanner').slideDown(200);
            }

            // Group checks by category
            var categoryOrder = ['config', 'process', 'ra', 'database', 'paths', 'assets'];
            var categoryLabels = {
                config: 'Configuration',
                process: 'Processes',
                ra: 'Remote Access',
                database: 'Databases',
                paths: 'Server Paths',
                assets: 'Static Assets'
            };

            for (var ci = 0; ci < categoryOrder.length; ci++) {
                var cat = categoryOrder[ci];
                var catChecks = data.checks.filter(function (c) { return c.category === cat; });
                if (catChecks.length === 0) continue;

                html += '<div style="font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-muted); margin-top: 12px; margin-bottom: 4px;">';
                html += escapeHtml(categoryLabels[cat] || cat);
                html += '</div>';

                for (var i = 0; i < catChecks.length; i++) {
                    html += renderCheck(catChecks[i]);
                }
            }

            $body.html(html);

            // Summary badge
            var s = data.summary;
            var summaryText = s.ok + ' ok';
            if (s.warnings > 0) summaryText += ', ' + s.warnings + ' warning' + (s.warnings > 1 ? 's' : '');
            if (s.errors > 0) summaryText += ', ' + s.errors + ' error' + (s.errors > 1 ? 's' : '');
            $('#diagSummary').text('(' + summaryText + ')');

            // Border color
            var borderColor = s.errors > 0 ? 'var(--status-error)'
                : s.warnings > 0 ? 'var(--status-warning)'
                    : 'var(--status-online)';
            $panel.css('border-left-color', borderColor);

        }).fail(function () {
            $body.html('<div style="color: var(--status-error); font-size: 12.5px;"><i class="fa-solid fa-circle-xmark"></i> Diagnostics endpoint unreachable</div>');
        }).always(function () {
            $btn.prop('disabled', false).html('<i class="fa-solid fa-stethoscope"></i> Diagnose');
        });
    }

    function renderCheck(check) {
        var iconMap = {
            ok: '<i class="fa-solid fa-circle-check" style="color: var(--status-online);"></i>',
            warning: '<i class="fa-solid fa-triangle-exclamation" style="color: var(--status-warning);"></i>',
            error: '<i class="fa-solid fa-circle-xmark" style="color: var(--status-error);"></i>',
            info: '<i class="fa-solid fa-circle-info" style="color: var(--text-muted);"></i>'
        };

        var html = '<div class="diag-check">';
        html += '<div class="diag-icon">' + (iconMap[check.status] || iconMap.info) + '</div>';
        html += '<div class="diag-content">';
        html += '<div class="diag-name">' + escapeHtml(check.name) + '</div>';
        html += '<div class="diag-detail">' + escapeHtml(check.detail) + '</div>';
        if (check.fix) {
            html += '<div class="diag-fix">' + escapeHtml(check.fix) + '</div>';
        }
        html += '</div></div>';
        return html;
    }

    // ===================== UTILITY =====================
    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

});