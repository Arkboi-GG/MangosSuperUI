// MangosSuperUI — Bot Monitor JS (BotBridge + BotBrain SignalR client)
// Session 25: Stale bot cleanup — AllBots purges old entries, BotDisconnected auto-removes after 30s

$(function () {

    // ===================== CONSTANTS =====================
    var CLASS_NAMES = {
        1: 'Warrior', 2: 'Paladin', 3: 'Hunter', 4: 'Rogue',
        5: 'Priest', 7: 'Shaman', 8: 'Mage', 9: 'Warlock', 11: 'Druid'
    };
    var CLASS_CSS = {
        1: 'class-warrior', 2: 'class-paladin', 3: 'class-hunter', 4: 'class-rogue',
        5: 'class-priest', 7: 'class-shaman', 8: 'class-mage', 9: 'class-warlock', 11: 'class-druid'
    };
    var RACE_NAMES = {
        1: 'Human', 2: 'Orc', 3: 'Dwarf', 4: 'Night Elf',
        5: 'Undead', 6: 'Tauren', 7: 'Gnome', 8: 'Troll'
    };
    var TRAIT_META = {
        patience: { icon: 'fa-hourglass-half', color: '#9ece6a' },
        greed: { icon: 'fa-coins', color: '#e0af68' },
        curiosity: { icon: 'fa-compass', color: '#7aa2f7' },
        sociability: { icon: 'fa-comments', color: '#bb9af7' },
        aggression: { icon: 'fa-crosshairs', color: '#f7768e' },
        efficiency: { icon: 'fa-bolt', color: '#ff9e64' },
        cautiousness: { icon: 'fa-shield-halved', color: '#73daca' },
        indecisiveness: { icon: 'fa-shuffle', color: '#c0caf5' },
        spontaneity: { icon: 'fa-dice', color: '#2ac3de' }
    };
    var QUALITY_COLORS = {
        0: '#9d9d9d', // Poor (grey)
        1: '#ffffff', // Common (white)
        2: '#1eff00', // Uncommon (green)
        3: '#0070dd', // Rare (blue)
        4: '#a335ee', // Epic (purple)
        5: '#ff8000', // Legendary (orange)
        6: '#e6cc80'  // Artifact (light gold)
    };
    var QUALITY_NAMES = {
        0: 'Poor', 1: 'Common', 2: 'Uncommon', 3: 'Rare',
        4: 'Epic', 5: 'Legendary', 6: 'Artifact'
    };
    var EQUIP_SLOT_NAMES = {
        0: 'Non-equip', 1: 'Head', 2: 'Neck', 3: 'Shoulder', 4: 'Shirt',
        5: 'Chest', 6: 'Waist', 7: 'Legs', 8: 'Feet', 9: 'Wrists',
        10: 'Hands', 11: 'Finger', 12: 'Trinket', 13: 'One-Hand',
        14: 'Shield', 15: 'Ranged', 16: 'Back', 17: 'Two-Hand',
        18: 'Bag', 20: 'Robe', 21: 'Main Hand', 22: 'Off Hand',
        23: 'Holdable', 24: 'Ammo', 25: 'Thrown', 26: 'Ranged'
    };
    var ITEM_CLASS_NAMES = {
        0: 'Consumable', 1: 'Container', 2: 'Weapon', 4: 'Armor',
        5: 'Reagent', 6: 'Projectile', 7: 'Trade Goods', 9: 'Recipe',
        11: 'Quiver', 12: 'Quest', 13: 'Key', 15: 'Miscellaneous'
    };

    // ===================== STATE =====================
    var connection = null;
    var connected = false;
    var botStates = {};       // guid → BotState (from bridge)
    var botBrains = {};       // guid → brain data (personality, decisions)
    var selectedGuid = null;
    var decisionLog = {};     // guid → array of decision entries
    var decisionCount = 0;
    var dpmStartTime = Date.now();
    var engineEnabled = false;
    var maxTimelineEntries = 100;
    var inventoryCache = {};  // guid → inventory data from /Bots/Inventory

    // Live tab (real-time BotContext feed)
    var detailTab = 'overview';   // 'overview' | 'live'
    var livePollTimer = null;     // 5s server poll
    var liveTickTimer = null;     // 1s client-side age ticker
    var liveData = null;          // last /Bots/LiveState payload
    var liveGuid = null;          // guid liveData belongs to
    var liveFetchedAt = 0;        // Date.now() of last fetch (for age offset)
    var liveScaffoldGuid = null;  // guid the live DOM scaffold was built for
    var liveLogTimer = null;      // 2s per-bot log poll
    var liveLogSeq = 0;           // last-seen log sequence cursor
    var liveQuestTimer = null;    // 4s per-bot server quest-status poll
    var liveQuestMap = {};        // questId → server queststatus row (authoritative kill credit)
    var liveQuestGuid = null;     // guid liveQuestMap belongs to
    var liveLastFleetAt = 0;      // wall-clock of last fleet heartbeat shown in this bot's log
    var FLEET_MIN_MS = 90000;     // show the fleet census at most once per ~90s per bot

    // ===================== SIGNALR =====================
    function initConnection() {
        connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/botbridge')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();

        // --- Bridge events ---

        // Session 25: AllBots now purges stale entries from previous sessions
        connection.on('AllBots', function (bots) {
            var newStates = {};
            for (var i = 0; i < bots.length; i++) newStates[bots[i].guid] = bots[i];

            // Remove brain/decision/inventory/DOM for bots no longer present
            var oldGuids = Object.keys(botStates);
            for (var i = 0; i < oldGuids.length; i++) {
                var g = oldGuids[i];
                if (!newStates[g]) {
                    delete botBrains[g];
                    delete decisionLog[g];
                    delete inventoryCache[g];
                    $('#roster-' + g).remove();
                }
            }

            botStates = newStates;
            renderRoster();
            updateStats();
            updateBotDropdown();

            // If selected bot no longer exists, deselect
            if (selectedGuid && !botStates[selectedGuid]) {
                selectedGuid = null;
                $('#detailEmpty').show();
                $('#detailPanel').empty();
                stopBrainPoll();
                stopLivePoll();
            }
        });

        connection.on('BotConnected', function (state) {
            botStates[state.guid] = state;
            renderRoster();
            updateStats();
            updateBotDropdown();
            tlAppend(state.guid, 'Connected: ' + state.name + ' L' + state.level + ' ' + (CLASS_NAMES[state.classId] || ''), 'bt-tl-event');
        });

        // Session 25: BotDisconnected auto-removes after 30s if bot doesn't reconnect
        connection.on('BotDisconnected', function (guid) {
            if (botStates[guid]) {
                botStates[guid].taskState = 'DISCONNECTED';
                renderRosterCard(guid);
                updateStats();
                tlAppend(guid, 'Disconnected', 'bt-tl-error');

                // Auto-remove after 30s if still disconnected (nuke/shutdown)
                setTimeout(function () {
                    if (botStates[guid] && botStates[guid].taskState === 'DISCONNECTED') {
                        delete botStates[guid];
                        delete botBrains[guid];
                        delete decisionLog[guid];
                        delete inventoryCache[guid];
                        $('#roster-' + guid).remove();
                        updateStats();
                        updateBotDropdown();

                        if (selectedGuid === parseInt(guid)) {
                            selectedGuid = null;
                            $('#detailEmpty').show();
                            $('#detailPanel').empty();
                            stopBrainPoll();
                            stopLivePoll();
                        }
                    }
                }, 30000);
            }
        });

        connection.on('BotStateUpdate', function (state) {
            botStates[state.guid] = state;
            renderRosterCard(state.guid);
            // Live-update economy strip if this bot is selected
            if (selectedGuid === state.guid) updateEconomyStrip(state);
            updateStats();
        });

        connection.on('BotEvent', function (evt) {
            var cls = 'bt-tl-event';
            var text = evt.eventType;
            if (evt.eventType === 'KILL') text += ' creature=' + evt.creatureEntry;
            else if (evt.eventType === 'LEVEL_UP') { text += ' → L' + evt.newLevel; cls = 'bt-tl-switch'; }
            else if (evt.eventType === 'QUEST_UPDATE') text += ' quest=' + evt.questId + ' ' + evt.status;
            else if (evt.eventType === 'SELL_ACK') { text += ' ' + (evt.data || ''); cls = 'bt-tl-switch'; }
            else if (evt.eventType === 'EQUIP') { text += ' ' + (evt.data || ''); }
            else text += ' ' + (evt.data || '');

            // Invalidate inventory cache on loot/sell/equip events
            if (['LOOT', 'SELL_ACK', 'EQUIP', 'BAG_EQUIP'].indexOf(evt.eventType) >= 0) {
                delete inventoryCache[evt.guid];
            }
            tlAppend(evt.guid, text, cls);
        });

        connection.on('BotChatReceived', function (chat) {
            tlAppend(chat.guid, 'WHISPER from ' + chat.senderName + ': ' + chat.message, 'bt-tl-event');
        });

        // --- Brain events ---
        connection.on('BotBrainInit', function (data) {
            botBrains[data.guid] = data;
            renderRosterCard(data.guid);
            if (selectedGuid === data.guid) renderDetail();
            tlAppend(data.guid, 'Brain initialized — ' +
                data.personality.chatStyle + '/' + data.personality.temperament +
                (data.personality.quirks.length ? ' [' + data.personality.quirks.map(function (q) { return q.name; }).join(', ') + ']' : ''),
                'bt-tl-switch');
        });

        connection.on('BotDecision', function (data) {
            botBrains[data.guid] = botBrains[data.guid] || {};
            botBrains[data.guid].lastDecision = data;
            decisionCount++;

            if (!decisionLog[data.guid]) decisionLog[data.guid] = [];
            decisionLog[data.guid].push(data);
            if (decisionLog[data.guid].length > maxTimelineEntries)
                decisionLog[data.guid].shift();

            renderRosterCard(data.guid);
            if (selectedGuid === data.guid) {
                renderWeights(data.weights);
                renderTimeline(data.guid);
            }

            var cls = data.activityChanged ? 'bt-tl-switch' : 'bt-tl-stay';
            tlAppend(data.guid, data.decision, cls);
        });

        connection.on('CommandAck', function () { /* silent */ });

        // --- Lifecycle ---
        connection.onreconnecting(function () { setStatus('offline'); });
        connection.onreconnected(function () {
            setStatus('online');
            connection.invoke('GetAllBots').catch(function () { });
        });
        connection.onclose(function () { setStatus('offline'); });

        connection.start().then(function () {
            setStatus('online');
            connection.invoke('GetAllBots').catch(function () { });

            // Load brain state + grouping mode from server (survives page refresh)
            $.getJSON('/Bots/BrainStatus', function (data) {
                // Sync engine toggle to actual server state
                engineEnabled = data.enabled;
                $('#engineToggle').toggleClass('active', engineEnabled);
                $('#engineToggle').find('.bt-engine-label').text(engineEnabled ? 'Engine On' : 'Engine Off');

                // Sync grouping mode dropdown
                if (typeof data.groupingMode !== 'undefined') {
                    $('#groupingMode').val(data.groupingMode);
                    updateGroupingUI(data);
                }

                if (data.bots) {
                    data.bots.forEach(function (b) {
                        $.getJSON('/Bots/BrainState/' + b.guid, function (bs) {
                            if (bs && bs.personality) {
                                botBrains[bs.guid] = bs;
                                renderRosterCard(bs.guid);
                                if (selectedGuid === bs.guid) renderDetail();
                            }
                        });
                    });
                }
            });
        }).catch(function (err) {
            setStatus('error');
        });
    }

    function setStatus(state) {
        connected = (state === 'online');
        $('#bridgeStatus').removeClass('online offline error').addClass(state);
        var labels = { online: 'Bridge: Connected', offline: 'Bridge: Disconnected', error: 'Bridge: Error' };
        $('#bridgeStatusText').text(labels[state] || state);
    }

    // ===================== ROSTER =====================

    function renderRoster() {
        var guids = Object.keys(botStates).sort(function (a, b) {
            return (botStates[a].name || '').localeCompare(botStates[b].name || '');
        });

        if (guids.length === 0) {
            $('#rosterEmpty').show();
            $('#botRoster .bt-roster-card').remove();
            return;
        }
        $('#rosterEmpty').hide();

        // Session 25: Remove DOM cards for bots no longer in botStates
        $('#botRoster .bt-roster-card').each(function () {
            var cardGuid = $(this).data('guid');
            if (!botStates[cardGuid]) $(this).remove();
        });

        var existing = {};
        $('#botRoster .bt-roster-card').each(function () { existing[$(this).data('guid')] = true; });

        for (var i = 0; i < guids.length; i++) {
            var guid = parseInt(guids[i]);
            if (!existing[guid]) {
                $('#botRoster').append('<div class="bt-roster-card" data-guid="' + guid + '" id="roster-' + guid + '"></div>');
            }
            renderRosterCard(guid);
        }
        updateBotDropdown();
    }

    function renderRosterCard(guid) {
        var s = botStates[guid];
        if (!s) return;
        var $card = $('#roster-' + guid);
        if ($card.length === 0) return;

        var isDisc = s.taskState === 'DISCONNECTED';
        var isDead = s.isDead;
        var dotCls = isDisc ? 'offline' : (isDead ? 'dead' : 'alive');

        var brain = botBrains[guid];
        var actText = 'IDLE';
        var actCls = 'bt-act-idle';
        if (brain && brain.lastDecision) {
            actText = brain.lastDecision.newActivity;
            actCls = 'bt-act-' + actText.toLowerCase();
        } else if (s.inCombat) {
            actText = 'COMBAT';
            actCls = 'bt-act-grinding';
        } else if (s.taskState && s.taskState !== 'IDLE') {
            actText = s.taskState;
        }

        var className = CLASS_NAMES[s.classId] || '?';
        var raceName = RACE_NAMES[s.race] || '?';

        // Gold in roster card (from enriched STATE copper field)
        var copper = s.copper || 0;
        var goldStr = formatGold(copper);

        $card.html(
            '<span class="bt-roster-dot ' + dotCls + '"></span>' +
            '<div class="bt-roster-info">' +
            '<div class="bt-roster-name">' + esc(s.name) + '</div>' +
            '<div class="bt-roster-meta">L' + (s.level || 0) + ' ' + raceName + ' <span class="bt-class-badge ' + (CLASS_CSS[s.classId] || '') + '">' + className + '</span>' +
            ' <span style="color:#e0af68;margin-left:4px;">' + goldStr + '</span></div>' +
            '</div>' +
            '<span class="bt-roster-activity ' + actCls + '">' + actText + '</span>'
        );

        $card.toggleClass('disconnected', isDisc);
        $card.toggleClass('selected', selectedGuid === guid);
    }

    // ===================== DETAIL PANEL =====================

    function renderDetail() {
        if (!selectedGuid) {
            $('#detailEmpty').show();
            stopLivePoll();
            return;
        }
        $('#detailEmpty').hide();

        var s = botStates[selectedGuid];
        if (!s) return;

        // Tab scaffold — Overview keeps everything that was here; Live is the real-time feed.
        var scaffold =
            '<div class="bt-detail-tabs">' +
            '<div class="bt-detail-tab' + (detailTab === 'overview' ? ' active' : '') + '" data-dtab="overview">' +
            '<i class="fa-solid fa-id-card" style="margin-right:5px;"></i>Overview</div>' +
            '<div class="bt-detail-tab' + (detailTab === 'live' ? ' active' : '') + '" data-dtab="live">' +
            '<i class="fa-solid fa-satellite-dish" style="margin-right:5px;"></i>Live' +
            '<span class="bt-live-dot"></span></div>' +
            '</div>' +
            '<div id="detailTabBody"></div>';
        $('#detailPanel').html(scaffold);

        renderActiveDetailTab();
        ensureLivePoll();
    }

    function renderActiveDetailTab() {
        if (!selectedGuid) return;
        var s = botStates[selectedGuid];
        if (!s) return;

        if (detailTab === 'live') {
            renderLiveTab(s);
            return;
        }
        var brain = botBrains[selectedGuid];
        try { renderDetailInner(s, brain); }
        catch (ex) {
            console.error('renderDetail crashed for guid ' + selectedGuid + ':', ex);
            $('#detailTabBody').html('<div style="color:#f7768e;padding:16px;">Detail render error: ' + esc(ex.message) + '</div>');
        }
    }

    // ===================== LIVE TAB (real-time BotContext feed) =====================

    var GOAL_COLOR = {
        Idle: '#5f6b7a', Questing: '#7aa2f7', Grinding: '#f7768e', Vendoring: '#e0af68',
        Training: '#bb9af7', Maintenance: '#ff9e64', Following: '#73daca',
        Exploring: '#2ac3de', Socializing: '#9ece6a'
    };

    function startLivePoll() {
        // New bot? drop stale payload so we show "connecting" not the last bot's data.
        if (liveGuid !== selectedGuid) {
            liveData = null; liveGuid = selectedGuid;
            liveLogSeq = 0; liveScaffoldGuid = null;
            liveQuestMap = {}; liveQuestGuid = null;
            liveLastFleetAt = 0;
        }
        stopLivePoll();
        fetchLive();                                  // immediate
        fetchLiveLog();                               // immediate log pull
        fetchLiveQuestStatus();                       // immediate server quest credit pull
        livePollTimer = setInterval(fetchLive, 5000); // server refresh
        liveLogTimer = setInterval(fetchLiveLog, 2000); // log feed
        liveQuestTimer = setInterval(fetchLiveQuestStatus, 4000); // server kill-credit truth
        liveTickTimer = setInterval(function () {     // client-side age ticking between polls
            if (detailTab === 'live' && selectedGuid && liveData) renderLiveTab(botStates[selectedGuid]);
        }, 1000);
    }

    // (Re)start only when needed — avoids an extra fetch on every incidental re-render.
    function ensureLivePoll() {
        // Poll the realtime spine for ANY selected bot — the Overview "Current quest"
        // section reads the same liveData/liveQuestMap the Live tab does. The heavy
        // live-LOG poll still self-gates to the Live tab (see fetchLiveLog).
        if (!selectedGuid) { stopLivePoll(); return; }
        if (livePollTimer && liveGuid === selectedGuid) return;
        startLivePoll();
    }

    function stopLivePoll() {
        if (livePollTimer) { clearInterval(livePollTimer); livePollTimer = null; }
        if (liveTickTimer) { clearInterval(liveTickTimer); liveTickTimer = null; }
        if (liveLogTimer) { clearInterval(liveLogTimer); liveLogTimer = null; }
        if (liveQuestTimer) { clearInterval(liveQuestTimer); liveQuestTimer = null; }
    }

    function fetchLive() {
        if (!selectedGuid || !connected) return;
        var g = selectedGuid;
        $.getJSON('/Bots/LiveState/' + g, function (data) {
            if (selectedGuid !== g) return;           // selection moved on while in flight
            if (!data || data.error) {
                liveData = null;
                if (detailTab === 'live') renderLiveTab(botStates[g]);
                else updateDetailQuest();
                return;
            }
            liveData = data;
            liveGuid = g;
            liveFetchedAt = Date.now();
            if (detailTab === 'live') renderLiveTab(botStates[g]);
            else updateDetailQuest();
        });
    }

    function fetchLiveLog() {
        if (!selectedGuid || !connected || detailTab !== 'live') return;
        var g = selectedGuid;
        var st = botStates[g];
        if (!st || !st.name) return;
        $.getJSON('/Bots/LiveLog', { name: st.name, after: liveLogSeq }, function (data) {
            if (selectedGuid !== g || detailTab !== 'live') return;
            if (!data) return;
            if (typeof data.lastSeq === 'number') liveLogSeq = data.lastSeq;
            var $box = $('#liveLogBox');
            if ($box.length === 0) return;
            if (data.lines && data.lines.length) {
                var el = $box[0];
                var nearBottom = (el.scrollHeight - el.scrollTop - el.clientHeight) < 48;
                var add = '';
                for (var i = 0; i < data.lines.length; i++) {
                    var ln = data.lines[i];
                    // The FleetReport heartbeat names every bot, so the server's name filter
                    // hands it to this single-bot feed every tick. Keep it (it's a useful
                    // fleet pulse) but throttle to ~once/90s so it can't bury this bot's lines.
                    if (isFleetLine(ln.msg)) {
                        var now = Date.now();
                        if (now - liveLastFleetAt < FLEET_MIN_MS) continue;
                        liveLastFleetAt = now;
                        add += logLineHtml(ln, true);
                    } else {
                        add += logLineHtml(ln);
                    }
                }
                if (add) $box.append(add);
                var kids = $box.children();
                if (kids.length > 400) kids.slice(0, kids.length - 400).remove();
                if (nearBottom) el.scrollTop = el.scrollHeight;
                $('#liveLogMeta').text('(' + $box.children().length + ')');
            }
        });
    }

    // Pull the authoritative kill credit (character_queststatus.mob_count*) for the
    // watched bot. The live BotContext objective `have` has NO per-kill feed — C++ only
    // emits QUEST_UPDATE on accept/reward/abandon (AIBOTAI_REFERENCE §SendQuestUpdateEvent)
    // — so the spine's projected count sits at its seed (0) until turn-in. The DB count is
    // ground truth, and surfacing it makes "0/10 but it's killing" self-diagnosing: if the
    // server count is also 0 while kills land, that's the tag-credit bug, not a display gap.
    function fetchLiveQuestStatus() {
        if (!selectedGuid || !connected) return;
        var g = selectedGuid;
        $.getJSON('/Bots/QuestStatus', { guid: g }, function (data) {
            if (selectedGuid !== g) return;
            if (!data || data.error || !data.quests) return;
            var map = {};
            for (var i = 0; i < data.quests.length; i++) map[data.quests[i].questId] = data.quests[i];
            liveQuestMap = map;
            liveQuestGuid = g;
            if (detailTab === 'live') { if (liveData) renderLiveTab(botStates[g]); }
            else updateDetailQuest();
        });
    }

    // index of the n-th (0-based) non-zero entry in a required-count array, or -1
    function nthNonzero(arr, n) {
        if (!arr) return -1;
        var seen = 0;
        for (var i = 0; i < arr.length; i++) {
            if (arr[i] > 0) { if (seen === n) return i; seen++; }
        }
        return -1;
    }

    // Map one live objective to its authoritative server slot. killIdx/itemIdx are the
    // running per-kind ordinals so the k-th kill objective pairs with the k-th non-zero
    // mob slot — exact for single-objective quests, best-effort (positional) otherwise.
    function serverObjFor(questId, o, killIdx, itemIdx) {
        var row = liveQuestMap[questId];
        if (!row) return null;
        if (o.kind === 'kill') {
            var slot = nthNonzero(row.mobRequired, killIdx);
            if (slot < 0) return null;
            return { have: row.mobCounts[slot], need: row.mobRequired[slot], status: row.status };
        }
        if (o.kind === 'gather') {
            var s2 = nthNonzero(row.itemRequired, itemIdx);
            if (s2 < 0) return null;
            return { have: row.itemCounts[s2], need: row.itemRequired[s2], status: row.status };
        }
        return null;
    }

    function reEscape(s) { return String(s).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
    function wholeWord(hay, word) {
        if (!word) return false;
        return new RegExp('\\b' + reEscape(word) + '\\b').test(hay);
    }
    // A fleet heartbeat names many bots in one line. Detect it from the live roster:
    // ≥3 bot names is unambiguous; ≥2 names plus a census token catches small fleets
    // without flagging an ordinary grouping/assist line that merely mentions one ally.
    function isFleetLine(m) {
        if (!m) return false;
        var seen = 0;
        for (var g in botStates) {
            var nm = botStates[g] && botStates[g].name;
            if (nm && wholeWord(m, nm)) { seen++; if (seen >= 3) return true; }
        }
        return seen >= 2 && /pick=|av=|why=/.test(m);
    }

    function logLineHtml(l, isFleet) {
        var t = '';
        try { t = new Date(l.t).toLocaleTimeString(); } catch (e) { t = ''; }
        var m = l.msg || '';
        var c = 'var(--text-secondary)';
        if (/STALL|no_path|MOVE_FAILED|PATH_UNSAFE|negated|deferring|shelving|_FAIL|cross_map/i.test(m)) c = '#f7768e';
        else if (/LEVEL_UP|completed|GRIND finished|rewarded|grey-drop/i.test(m)) c = '#9ece6a';
        else if (/RESURRECT|RESPAWN|DEATH|relocate|heal|graveyard|REPAIR|SELL/i.test(m)) c = '#ff9e64';
        else if (/\[QUEST\]|batch|seeding|QUEST_/i.test(m)) c = '#7aa2f7';
        else if (/KILL/i.test(m)) c = 'var(--text-muted)';
        var tag = isFleet ? '<span class="bt-live-fleet-tag" title="fleet heartbeat (throttled to ~90s)">fleet</span> ' : '';
        return '<div class="bt-live-logline">' + tag + '<span class="bt-live-logt">' + t + '</span> ' +
            '<span style="color:' + c + ';">' + esc(m) + '</span></div>';
    }

    // seconds offset since last fetch — lets ages tick up / countdowns tick down live
    function liveOff() { return Math.max(0, Math.floor((Date.now() - liveFetchedAt) / 1000)); }
    function ageUp(n) { return (n == null) ? null : (n + liveOff()); }
    function ageDn(n) { return (n == null) ? null : (n - liveOff()); }
    function secs(n) { return (n == null) ? '—' : (n + 's'); }
    function clk(sec, goodUnder, warnUnder) {
        // color a "time since" by health: low good, high bad
        var c = sec == null ? 'var(--text-muted)' : (sec < goodUnder ? '#9ece6a' : (sec < warnUnder ? '#e0af68' : '#f7768e'));
        return '<span style="color:' + c + ';">' + secs(sec) + '</span>';
    }
    function chip(text, color, bg) {
        return '<span style="display:inline-block;padding:1px 7px;border-radius:10px;font-size:10px;font-weight:700;' +
            'color:' + color + ';background:' + (bg || 'rgba(122,162,247,0.12)') + ';margin-right:4px;">' + esc(text) + '</span>';
    }
    function statBox(label, val, color) {
        return '<div class="bt-live-stat"><div class="bt-live-stat-val" style="color:' + (color || 'var(--text-primary)') + ';">' +
            val + '</div><div class="bt-live-stat-lbl">' + esc(label) + '</div></div>';
    }

    // 2D distance from the bot to a world point, or null if a different map (cross-map dist is meaningless).
    function objDist(d, o) {
        if (!d || !o || o.map !== d.mapId) return null;
        var dx = d.pos.x - o.x, dy = d.pos.y - o.y;
        return Math.round(Math.sqrt(dx * dx + dy * dy));
    }
    function distTag(d, o) {
        var dist = objDist(d, o);
        if (dist != null) return '<span class="bt-live-obj-dist">' + dist + 'y</span>';
        if (o && o.map !== d.mapId) return '<span class="bt-live-obj-dist">map ' + o.map + '</span>';
        return '';
    }
    function questNpcRow(label, icon, npc, d, color) {
        return '<div class="bt-live-obj"><i class="fa-solid ' + icon + '" style="color:' + color + ';width:14px;"></i> ' +
            '<span style="color:var(--text-muted);">' + label + ':</span> ' +
            '<span class="bt-live-obj-name">' + esc(npc.name || '?') + '</span>' + distTag(d, npc) + '</div>';
    }

    // ===================== "RIGHT NOW" NARRATIVE =====================
    // The payload has goal/step/pending/target/scratch/timers but makes you fuse them in
    // your head. These helpers synthesize a plain-English story: what the bot is doing,
    // where it's going (the MOVE_TO target, named by matching the driven-to coords to the
    // scratch's known waypoints), why, and what to watch for next.

    // Objectives augmented with the server-authoritative kill credit (same override the
    // objective panel uses), so the story's progress matches what's shown below it.
    function objsWithServerHave(aq) {
        var out = [], killIdx = 0, itemIdx = 0;
        if (!aq || !aq.objectives) return out;
        for (var i = 0; i < aq.objectives.length; i++) {
            var o = aq.objectives[i];
            var srv = serverObjFor(aq.id, o, killIdx, itemIdx);
            if (o.kind === 'kill') killIdx++; else if (o.kind === 'gather') itemIdx++;
            out.push({
                name: o.name, kind: o.kind, from: o.from, x: o.x, y: o.y, map: o.map, active: o.active,
                have: (srv != null) ? srv.have : o.have,
                need: (srv != null && srv.need > 0) ? srv.need : o.need
            });
        }
        return out;
    }

    // Named, located waypoints the spine knows from the active scratch.
    function liveWaypoints(d) {
        var pts = [], sc = d.scratch;
        if (!sc) return pts;
        if (sc.kind === 'quest' && sc.active) {
            var aq = sc.active;
            if (aq.objectives) for (var i = 0; i < aq.objectives.length; i++) {
                var o = aq.objectives[i];
                // For a gather-from-creature objective the bot drives to the SOURCE creature,
                // so name that (with the item as context) rather than the item itself.
                var lbl = (o.kind === 'gather' && o.from) ? (o.from + ' (for ' + o.name + ')') : o.name;
                pts.push({ label: lbl, kind: 'objective', active: !!o.active, x: o.x, y: o.y, map: o.map });
            }
            if (aq.giver) pts.push({ label: aq.giver.name, kind: 'giver', x: aq.giver.x, y: aq.giver.y, map: aq.giver.map });
            if (aq.turnIn) pts.push({ label: aq.turnIn.name, kind: 'turnin', x: aq.turnIn.x, y: aq.turnIn.y, map: aq.turnIn.map });
        } else if (sc.kind === 'vendor' && sc.target) {
            pts.push({ label: sc.canRepair ? 'repair vendor' : 'vendor', kind: 'vendor', x: sc.target.x, y: sc.target.y, map: sc.target.map });
        } else if (sc.kind === 'grind' && sc.center) {
            pts.push({ label: 'grind area', kind: 'grind', x: sc.center.x, y: sc.center.y, map: sc.center.map });
        }
        return pts;
    }

    // Name the current MOVE_TO destination by matching driven-to coords to a waypoint;
    // nearest wins (target IS the dest coord, so the match is near-exact). Falls back to
    // objective-grind/step hints, else null (caller shows coords).
    function liveDest(d) {
        var t = d.target, pts = liveWaypoints(d);
        if (t) {
            var best = null, bestD = Infinity;
            for (var i = 0; i < pts.length; i++) {
                var p = pts[i];
                if (p.map !== t.map) continue;
                var dx = p.x - t.x, dy = p.y - t.y, dist = Math.sqrt(dx * dx + dy * dy);
                if (dist < bestD) { bestD = dist; best = p; }
            }
            if (best && bestD <= 25) return { label: best.label, kind: best.kind };
        }
        if (d.pending && d.pending.isObjectiveGrind) {
            for (var j = 0; j < pts.length; j++) if (pts[j].kind === 'objective' && pts[j].active) return { label: pts[j].label, kind: 'objective' };
            for (var k = 0; k < pts.length; k++) if (pts[k].kind === 'objective') return { label: pts[k].label, kind: 'objective' };
        }
        var st = (d.step || '').toLowerCase();
        if (/accept|giver/.test(st)) for (var a = 0; a < pts.length; a++) if (pts[a].kind === 'giver') return pts[a];
        if (/turnin/.test(st)) for (var b = 0; b < pts.length; b++) if (pts[b].kind === 'turnin') return pts[b];
        return null;
    }

    function humanWhy(w) {
        if (!w) return '';
        if (/^in-quest/.test(w)) return 'already mid-quest';
        if (/graph-loading/.test(w)) return 'quest graph still loading';
        if (/no-identity/.test(w)) return 'no identity assigned yet';
        var m = w.match(/q av=(\d+) pick=(\d+)/);
        if (m) return 'picked from ' + m[1] + ' available quest' + (m[1] === '1' ? '' : 's');
        return w;
    }

    function composeStory(d) {
        var sc = d.scratch || {};
        var dest = liveDest(d);

        // ---- DOING ----
        var doing;
        if (d.dead || sc.kind === 'maintenance') {
            var ph = sc.phase || '';
            doing = ph === 'resurrecting' ? 'Resurrecting'
                : ph === 'relocate' ? 'Walking back from the graveyard'
                    : ph === 'heal' ? 'Healing up after a death'
                        : ph === 'rez-wait' ? 'Dead — waiting to resurrect'
                            : 'Recovering from a death';
        } else if (sc.kind === 'vendor') {
            doing = sc.canRepair ? 'On a repair/vendor run' : 'On a vendor run';
        } else if (d.goal === 'Questing') {
            var st = (d.step || '').toLowerCase();
            doing = /objective/.test(st) ? 'Working a quest objective'
                : /accept|giver/.test(st) ? 'Going to pick up a quest'
                    : /turnin/.test(st) ? 'Going to turn in a quest'
                        : 'Questing';
        } else if (d.goal === 'Grinding') doing = 'Grinding mobs';
        else if (d.goal === 'Training') doing = 'Training new skills';
        else if (d.goal === 'Following') doing = 'Following the group';
        else if (d.goal === 'Idle') doing = (d.why && d.why !== 'idle') ? 'Idle — about to pick its next move' : 'Idle';
        else doing = d.goal;
        if (d.inCombat) doing += ' (fighting right now)';

        // ---- GOING TO ----
        var going;
        if (d.pending) {
            var p = d.pending;
            if (p.cmd === 'MOVE_TO') {
                var where = dest ? dest.label : 'a waypoint';
                var dist = (d.distToTarget != null) ? (', ' + d.distToTarget + 'y out') : '';
                going = 'heading to <b>' + esc(where) + '</b>' + dist + (p.isObjectiveGrind ? ' to grind it down' : '');
            } else if (p.cmd === 'QUEST_INTERACT') {
                going = 'talking to <b>' + esc(dest ? dest.label : 'the questgiver') + '</b>';
            } else if (p.cmd === 'SET_TASK') going = 'grinding in place';
            else if (p.cmd === 'TAKE_FLIGHT') going = 'taking a flight path';
            else going = esc(p.cmd) + ', waiting on ' + esc(p.expect);
        } else {
            going = 'between commands — deciding what to do next';
        }

        // ---- WHY ----
        var why = null;
        if (sc.kind === 'quest' && sc.active) {
            var aq = sc.active;
            var objs = objsWithServerHave(aq);
            var ao = null;
            for (var i = 0; i < objs.length; i++) if (objs[i].active) { ao = objs[i]; break; }
            if (!ao && objs.length) ao = objs[0];
            var prog = '';
            if (ao && ao.have != null && ao.need) prog = ' — ' + ao.have + '/' + ao.need + ' ' + esc(ao.name);
            else if (ao) prog = ' — needs ' + esc(ao.name);
            why = 'For <b>' + esc(aq.title || ('#' + aq.id)) + '</b> (#' + aq.id + ')' + prog;
            if (sc.count > 1) {
                var done = 0, deferred = 0, failed = 0;
                for (var b = 0; b < sc.batch.length; b++) {
                    if (sc.batch[b].turnedIn) done++; else if (sc.batch[b].deferred) deferred++; else if (sc.batch[b].failed) failed++;
                }
                var ex = [];
                if (done) ex.push(done + ' done');
                if (deferred) ex.push(deferred + ' deferred');
                if (failed) ex.push(failed + ' failed');
                why += '. Quest ' + (sc.activeSlot + 1) + ' of ' + sc.count + ' in the batch' + (ex.length ? ' (' + ex.join(', ') + ')' : '');
            } else {
                why += '. Only quest in the batch right now';
            }
        } else if (sc.kind === 'grind') {
            why = 'Grinding ' + (sc.creatureEntry ? 'creature #' + sc.creatureEntry : 'nearby mobs') +
                (sc.killGoal > 0 ? ' — ' + sc.killCount + '/' + sc.killGoal + ' killed' : ' to level up / earn gold');
        } else if (sc.kind === 'vendor') {
            why = sc.canRepair ? 'Gear durability or bags need attention' : 'Bags need clearing';
        } else if (sc.kind === 'maintenance') {
            why = 'It died' + (sc.deathLoop ? ' repeatedly (death loop)' : '') + ' and must recover before doing anything else';
        } else if (d.why) {
            why = 'Reason: ' + esc(humanWhy(d.why));
        }

        // ---- WATCH NEXT ----
        var next = [];
        if (sc.kind === 'maintenance') {
            if (sc.rezInSec != null && sc.rezInSec > 0) next.push('resurrects in ~' + ageDn(sc.rezInSec) + 's');
            if (sc.deathLoop) next.push('death loop — will escalate to a graveyard port');
            else if (sc.phase === 'relocate') next.push('then walks back to where it died');
        }
        if (sc.kind === 'quest' && sc.active) {
            var oo = objsWithServerHave(sc.active), allDone = true, anyKill = false;
            for (var i2 = 0; i2 < oo.length; i2++) {
                var o2 = oo[i2];
                if (o2.have != null && o2.need) {
                    anyKill = true;
                    if (o2.have < o2.need) { allDone = false; var rem = o2.need - o2.have; if (rem <= 3) next.push(rem + ' more ' + esc(o2.name) + ' to go'); }
                }
            }
            if (anyKill && allDone) next.push('objective done — heading to turn in' + (sc.active.turnIn ? ' to ' + esc(sc.active.turnIn.name) : ''));
        }
        var np = ageUp(d.noProgressSec);
        if (np != null && np >= 30 && !d.dead) next.push('no progress for ' + np + 's — reselects if it passes ~120s');
        if (d.failure && ageUp(d.failure.ageSec) < 60) next.push('last move failed (' + esc(d.failure.reason) + ') — recovering');
        if (d.freeSlots <= 2) next.push('bags almost full — vendor run likely soon');
        if (d.durability < 30) next.push('durability low — repair trip likely soon');
        var dl2 = d.pending ? ageDn(d.pending.secsToDeadline) : null;
        if (dl2 != null && dl2 <= 30 && dl2 > -3600) next.push('command deadline in ' + dl2 + 's');

        return { doing: doing, going: going, why: why, next: next };
    }

    function renderStoryCard(d) {
        var s = composeStory(d);
        var html = '<div class="bt-live-card bt-story">';
        html += '<div class="bt-live-card-h"><i class="fa-solid fa-book-open"></i> Right now</div>';
        html += '<div class="bt-story-lead">' + s.doing + ' · ' + s.going + '.</div>';
        if (s.why) html += '<div class="bt-story-why">' + s.why + '.</div>';
        if (s.next && s.next.length)
            html += '<div class="bt-story-next"><span class="bt-story-next-h">Next</span> ' + s.next.slice(0, 3).join(' &nbsp;·&nbsp; ') + '</div>';
        html += '</div>';
        return html;
    }

    function renderLiveTab(s) {
        var $body = $('#detailTabBody');
        if ($body.length === 0) return;

        var d = liveData;
        if (!d || liveGuid !== selectedGuid) {
            liveScaffoldGuid = null;
            $body.html('<div style="padding:24px;text-align:center;color:var(--text-muted);">' +
                '<i class="fa-solid fa-satellite-dish fa-fade" style="font-size:20px;margin-bottom:8px;display:block;"></i>' +
                'Connecting to live feed…<div style="font-size:11px;margin-top:4px;">(brain engine must be ON)</div></div>');
            return;
        }

        var goalColor = GOAL_COLOR[d.goal] || 'var(--accent)';
        var html = '';

        // --- Goal / Step banner ---
        html += '<div class="bt-live-banner" style="border-left:3px solid ' + goalColor + ';">';
        html += '<div class="bt-live-goal" style="color:' + goalColor + ';">' + esc(d.goal) +
            '<span class="bt-live-step"> / ' + esc(d.step) + '</span></div>';
        if (d.why) html += '<div class="bt-live-why">why = ' + esc(d.why) + '</div>';
        html += '<div class="bt-live-badges">';
        if (d.dead) html += chip('DEAD', '#fff', 'rgba(247,118,142,0.8)');
        if (d.inCombat) html += chip('IN COMBAT', '#f7768e', 'rgba(247,118,142,0.15)');
        if (d.goal === 'Idle' && d.why && d.why !== 'idle')
            html += chip('↻ reselecting', '#ff9e64', 'rgba(255,158,100,0.15)');
        html += chip('L' + d.level, 'var(--text-secondary)', 'rgba(255,255,255,0.06)');
        html += chip('zone ' + d.zoneId + ' · map ' + d.mapId, 'var(--text-muted)', 'rgba(255,255,255,0.04)');
        html += '</div></div>';

        // --- "Right now" narrative (synthesizes the state below into plain English) ---
        html += renderStoryCard(d);

        // --- Vitals strip ---
        var durColor = d.durability < 30 ? '#f7768e' : (d.durability < 60 ? '#e0af68' : '#9ece6a');
        var bagColor = d.freeSlots <= 0 ? '#f7768e' : (d.freeSlots <= 2 ? '#e0af68' : '#9ece6a');
        html += '<div class="bt-live-stats">';
        html += statBox('HP', d.hpPct + '%', d.hpPct < 35 ? '#f7768e' : '#9ece6a');
        if (d.manaPct > 0 && d.manaPct < 100) html += statBox('MP', d.manaPct + '%', '#7aa2f7');
        html += statBox('Durability', d.durability + '%', durColor);
        html += statBox('Free bags', d.freeSlots, bagColor);
        html += statBox('Gold', formatGold(d.copper), '#e0af68');
        html += '</div>';

        // --- THE WAIT (hero signal) ---
        html += '<div class="bt-live-card">';
        html += '<div class="bt-live-card-h"><i class="fa-solid fa-hourglass-half"></i> Outstanding command</div>';
        if (d.pending) {
            var age = ageUp(d.pending.ageSec);
            var dl = ageDn(d.pending.secsToDeadline);
            var dlColor = dl == null ? 'var(--text-muted)' : (dl > 60 ? '#9ece6a' : (dl > 15 ? '#e0af68' : '#f7768e'));
            html += '<div class="bt-live-wait">';
            html += '<span class="bt-live-wait-cmd">' + esc(d.pending.cmd) + '</span>';
            var pdest = liveDest(d);
            if (pdest) html += ' <span style="color:var(--text-muted);">→</span> <span class="bt-live-wait-dest">' + esc(pdest.label) + '</span>';
            html += ' <span style="color:var(--text-muted);">· waiting</span> ';
            html += '<span class="bt-live-wait-evt">' + esc(d.pending.expect) + '</span>';
            html += ' <span style="color:var(--text-muted);">(' + secs(age) + ')</span>';
            html += '<span style="float:right;color:' + dlColor + ';font-weight:700;">deadline ' + secs(dl) + '</span>';
            html += '</div><div class="bt-live-badges" style="margin-top:6px;">';
            if (d.pending.isObjectiveGrind) html += chip('objective grind', '#f7768e', 'rgba(247,118,142,0.15)');
            if (d.pending.interruptible) html += chip('interruptible trek', '#73daca', 'rgba(115,218,202,0.15)');
            if (d.distToTarget != null) html += chip('tgt ' + d.distToTarget + 'y', '#7aa2f7', 'rgba(122,162,247,0.12)');
            html += '</div>';
        } else {
            html += '<div style="color:var(--text-muted);font-size:12px;">— none (between commands)</div>';
        }
        html += '</div>';

        // --- Failure (only if present) ---
        if (d.failure) {
            html += '<div class="bt-live-card" style="border-color:rgba(247,118,142,0.4);background:rgba(247,118,142,0.06);">';
            html += '<div class="bt-live-card-h" style="color:#f7768e;"><i class="fa-solid fa-triangle-exclamation"></i> Last failure</div>';
            html += '<div style="font-size:12px;">' + esc(d.failure.cmd) + ' ← <span style="color:#f7768e;font-weight:700;">' +
                esc(d.failure.reason) + '</span> <span style="color:var(--text-muted);">(' + secs(ageUp(d.failure.ageSec)) + ')</span>';
            if (d.failure.danger > 0) html += ' ' + chip('danger ' + d.failure.danger, '#ff9e64', 'rgba(255,158,100,0.15)');
            if (d.failure.questId) html += ' ' + chip('quest #' + d.failure.questId, '#e0af68', 'rgba(224,175,104,0.12)');
            html += '</div></div>';
        }

        // --- Stall (only if present) ---
        if (d.stall) {
            html += '<div class="bt-live-card" style="border-color:rgba(247,118,142,0.5);background:rgba(247,118,142,0.1);">';
            html += '<div style="color:#f7768e;font-weight:700;font-size:12px;"><i class="fa-solid fa-ban" style="margin-right:5px;"></i>STALLED — ' +
                esc(d.stall.reason) + ' (' + secs(ageUp(d.stall.sinceSec)) + ')</div></div>';
        }

        // --- Progress clocks ---
        html += '<div class="bt-live-card">';
        html += '<div class="bt-live-card-h"><i class="fa-solid fa-gauge-high"></i> Progress</div>';
        html += '<div class="bt-live-rows">';
        html += liveRow('In goal', secs(ageUp(d.timeInGoalSec)));
        html += liveRow('In step', secs(ageUp(d.timeInStepSec)));
        html += liveRow('No progress', clk(ageUp(d.noProgressSec), 30, 120));
        html += liveRow('Last kill', clk(ageUp(d.lastKillSec), 30, 120));
        html += liveRow('Last quest advance', clk(ageUp(d.lastQuestSec), 120, 600));
        html += liveRow('Last level', secs(ageUp(d.lastLevelSec)));
        html += '</div></div>';

        // --- Active scratch ---
        html += renderLiveScratch(d.scratch, d);

        // --- Position footer ---
        html += '<div class="bt-live-foot">pos ' + Math.round(d.pos.x) + ', ' + Math.round(d.pos.y) + ', ' + Math.round(d.pos.z) +
            ' @ map ' + d.mapId + ' &nbsp;·&nbsp; refreshed ' + liveOff() + 's ago</div>';

        // Lay the scaffold once per bot so the log panel (appended incrementally by the
        // 2s poll) survives the 1s state re-render. Then update only the state region.
        // Rebuild also when the scaffold node is gone — switching to Overview overwrites
        // #detailTabBody, so returning to Live for the SAME bot finds no #liveState to fill.
        if (liveScaffoldGuid !== selectedGuid || $('#liveState').length === 0) {
            $body.html(
                '<div id="liveState"></div>' +
                '<div class="bt-live-card" id="liveLogCard">' +
                '<div class="bt-live-card-h"><i class="fa-solid fa-terminal"></i> Live log ' +
                '<span id="liveLogMeta" style="font-weight:400;color:var(--text-muted);"></span>' +
                '<button id="btnBotReport" class="bt-report-btn" title="Quantized report from this bot\'s buffered log">' +
                '<i class="fa-solid fa-bolt"></i> Report</button></div>' +
                '<div id="liveLogBox" class="bt-live-log"></div>' +
                '</div>');
            liveScaffoldGuid = selectedGuid;
        }
        $('#liveState').html(html);
    }

    function liveRow(label, valHtml) {
        return '<div class="bt-live-row"><span class="bt-live-row-lbl">' + esc(label) + '</span>' +
            '<span class="bt-live-row-val">' + valHtml + '</span></div>';
    }

    function renderLiveScratch(sc, d) {
        if (!sc || sc.kind === 'none') {
            return '<div class="bt-live-card"><div class="bt-live-card-h"><i class="fa-solid fa-list-check"></i> Task</div>' +
                '<div style="color:var(--text-muted);font-size:12px;">No active task scratch.</div></div>';
        }
        var html = '<div class="bt-live-card">';

        if (sc.kind === 'quest') {
            html += '<div class="bt-live-card-h"><i class="fa-solid fa-scroll"></i> Quest — ' + sc.count + ' in batch ' +
                chip(d.step, 'var(--text-secondary)', 'rgba(255,255,255,0.06)') + '</div>';

            // Active quest detail — title, objectives (active highlighted), where to accept / hand in.
            var aq = sc.active;
            if (aq) {
                html += '<div class="bt-live-active">';
                html += '<div class="bt-live-active-t">★ ' + esc(aq.title || ('#' + aq.id)) +
                    ' <span style="color:var(--text-muted);font-weight:400;">#' + aq.id + (aq.level ? ' · L' + aq.level : '') + '</span></div>';

                if (aq.objectives && aq.objectives.length) {
                    var killIdx = 0, itemIdx = 0;
                    for (var oi = 0; oi < aq.objectives.length; oi++) {
                        var o = aq.objectives[oi];
                        var icon = o.kind === 'kill' ? 'fa-khanda' : (o.kind === 'gather' ? 'fa-hand-holding' : 'fa-hand-pointer');

                        // Authoritative server kill credit overrides the spine's un-fed `have`
                        // (no per-kill QUEST_UPDATE → projected count sticks at its seed).
                        var srv = serverObjFor(aq.id, o, killIdx, itemIdx);
                        if (o.kind === 'kill') killIdx++; else if (o.kind === 'gather') itemIdx++;
                        var have = (srv != null) ? srv.have : o.have;
                        var need = (srv != null && srv.need > 0) ? srv.need : o.need;

                        var done = (have != null && need > 0 && have >= need);
                        var cnt = (have != null) ? (have + '/' + need) : ('×' + need);
                        var cntColor = done ? '#9ece6a' : (o.active ? '#e0af68' : 'var(--text-secondary)');
                        html += '<div class="bt-live-obj' + (o.active ? ' active' : '') + '">';
                        html += '<i class="fa-solid ' + icon + '" style="width:14px;"></i> ';
                        html += '<span class="bt-live-obj-cnt" style="color:' + cntColor + ';">' + cnt + '</span> ';
                        if (srv != null) html += '<span class="bt-live-obj-srv" title="live server kill credit (character_queststatus.mob_count)">srv</span> ';
                        html += '<span class="bt-live-obj-name">' + esc(o.name) + '</span>';
                        if (o.kind === 'gather' && o.from) html += ' <span style="color:var(--text-muted);">from ' + esc(o.from) + '</span>';
                        // No-credit flag: actively grinding this kill, killed in the last 90s,
                        // server credit still 0 → real tag-credit contention, not a display gap.
                        if (o.kind === 'kill' && o.active && srv != null && have === 0 && need > 0 &&
                            d.lastKillSec != null && ageUp(d.lastKillSec) < 90) {
                            html += ' ' + chip('no quest credit', '#ff9e64', 'rgba(255,158,100,0.15)');
                        }
                        html += distTag(d, o);
                        html += '</div>';
                    }
                }

                if (aq.giver) html += questNpcRow('Accept', 'fa-circle-question', aq.giver, d, '#7aa2f7');
                if (aq.turnIn) html += questNpcRow('Turn in', 'fa-flag-checkered', aq.turnIn, d, '#9ece6a');
                html += '</div>';
            }

            // Full batch — id + title, active row highlighted.
            html += '<div class="bt-live-qlist">';
            for (var i = 0; i < sc.batch.length; i++) {
                var q = sc.batch[i];
                var col = '#7aa2f7', tag = 'fa-circle-dot';
                if (q.turnedIn) { col = '#9ece6a'; tag = 'fa-circle-check'; }
                else if (q.failed) { col = '#f7768e'; tag = 'fa-circle-xmark'; }
                else if (q.deferred) { col = '#e0af68'; tag = 'fa-circle-pause'; }
                else if (q.force) { col = '#bb9af7'; tag = 'fa-bolt'; }
                else if (q.accepted) { col = '#73daca'; tag = 'fa-circle-dot'; }
                var isActive = (sc.activeId && q.id === sc.activeId);
                html += '<div class="bt-live-qrow' + (isActive ? ' active' : '') + '">' +
                    '<i class="fa-solid ' + tag + '" style="color:' + col + ';width:14px;"></i> ' +
                    '<span class="bt-live-qid">#' + q.id + '</span> ' +
                    '<span class="bt-live-qtitle">' + esc(q.title || '(unknown)') + '</span></div>';
            }
            html += '</div>';
        } else if (sc.kind === 'grind') {
            html += '<div class="bt-live-card-h"><i class="fa-solid fa-khanda"></i> Grind</div>';
            html += '<div class="bt-live-rows">';
            html += liveRow('Creature entry', sc.creatureEntry || 'any');
            html += liveRow('Kills', sc.killCount + (sc.killGoal > 0 ? ' / ' + sc.killGoal : ' (indefinite)'));
            html += liveRow('Radius', Math.round(sc.radius) + 'y');
            html += '</div>';
        } else if (sc.kind === 'maintenance') {
            var phaseColor = { 'rez-wait': '#ff9e64', 'resurrecting': '#ff9e64', 'relocate': '#e0af68', 'heal': '#9ece6a', 'done': '#73daca', 'post-rez': '#7aa2f7' };
            html += '<div class="bt-live-card-h" style="color:#ff9e64;"><i class="fa-solid fa-kit-medical"></i> Recovery</div>';
            html += '<div class="bt-live-badges" style="margin-bottom:6px;">';
            html += chip(sc.phase.toUpperCase(), '#1a1b26', (phaseColor[sc.phase] || '#ff9e64'));
            if (sc.deathLoop) html += chip('death loop', '#f7768e', 'rgba(247,118,142,0.15)');
            if (sc.escalated) html += chip('graveyard port', '#bb9af7', 'rgba(187,154,247,0.15)');
            html += '</div><div class="bt-live-rows">';
            if (sc.deadForSec != null) html += liveRow('Dead for', secs(ageUp(sc.deadForSec)));
            if (sc.rezInSec != null && sc.rezInSec > -3600) html += liveRow('Rez in', secs(ageDn(sc.rezInSec)));
            html += liveRow('Relocate', sc.relocateDone ? 'done' : (sc.relocateSent ? 'in progress' : 'pending'));
            html += liveRow('Heal', sc.healDone ? 'done' : 'pending');
            html += '</div>';
        } else if (sc.kind === 'vendor') {
            html += '<div class="bt-live-card-h" style="color:#e0af68;"><i class="fa-solid fa-store"></i> Vendor errand</div>';
            html += '<div class="bt-live-badges" style="margin-bottom:6px;">';
            html += chip(sc.phase.toUpperCase(), '#1a1b26', '#e0af68');
            if (sc.canRepair) html += chip('repairs', '#9ece6a', 'rgba(158,206,106,0.15)');
            html += '</div><div class="bt-live-rows">';
            html += liveRow('Vendor NPC', '#' + sc.npcEntry);
            if (sc.startedSec != null) html += liveRow('Trip time', secs(ageUp(sc.startedSec)));
            html += '</div>';
        }

        html += '</div>';
        return html;
    }

    function renderDetailInner(s, brain) {

        var html = '';

        // --- Bot Header ---
        var className = CLASS_NAMES[s.classId] || '?';
        var raceName = RACE_NAMES[s.race] || '?';
        var hpPct = (s.maxHealth || 0) > 0 ? Math.round((s.health || 0) / s.maxHealth * 100) : 0;
        var mpPct = (s.maxMana || 0) > 0 ? Math.round((s.mana || 0) / s.maxMana * 100) : 0;
        var posX = (s.x != null ? s.x : 0).toFixed(0);
        var posY = (s.y != null ? s.y : 0).toFixed(0);

        html += '<div class="bt-section"><div class="bt-section-body">' +
            '<div class="d-flex align-items-center justify-content-between mb-2">' +
            '<div><span style="font-size:16px;font-weight:700;">' + esc(s.name) + '</span> ' +
            '<span class="bt-class-badge ' + (CLASS_CSS[s.classId] || '') + '">' + className + '</span>' +
            ' <button class="btn-sm btnOpenModal" data-guid="' + s.guid + '" style="font-size:10px;padding:2px 10px;cursor:pointer;background:var(--bg-card-alt,#24283b);border:1px solid var(--border-light,#414868);border-radius:3px;color:var(--accent,#7aa2f7);margin-left:8px;">' +
            '<i class="fa-solid fa-up-right-from-square" style="margin-right:3px;"></i>Details</button></div>' +
            '<div style="font-size:12px;color:var(--text-muted);">L' + (s.level || 0) + ' ' + raceName + ' — Map ' + (s.mapId || 0) + ' (' + posX + ', ' + posY + ')</div>' +
            '</div>' +
            '<div class="d-flex gap-3" style="font-size:12px;">' +
            '<div><span style="color:#9ece6a;">HP ' + hpPct + '%</span></div>' +
            '<div><span style="color:#7aa2f7;">MP ' + mpPct + '%</span></div>' +
            (s.inCombat ? '<div><span style="color:#f7768e;font-weight:600;">IN COMBAT</span></div>' : '') +
            (s.isDead ? '<div><span style="color:#f7768e;font-weight:600;">DEAD</span></div>' : '') +
            '</div>';

        // Sub-phase + quest info from brain
        if (brain && brain.subPhase) {
            html += '<div style="margin-top:6px;font-size:11px;color:var(--text-muted);">' +
                '<i class="fa-solid fa-route" style="margin-right:4px;"></i>' +
                '<span style="color:var(--text-secondary);">' + esc(brain.activity || '') + '</span>' +
                ' → <span style="color:#7aa2f7;">' + esc(brain.subPhase) + '</span>';
            // (Quest # moved to the realtime "Current quest" section below — the brain
            //  summary's activeQuestId is stale; the spine scratch is the live truth.)
            if (brain.contextTag) html += ' <span style="color:var(--text-muted);">' + esc(brain.contextTag) + '</span>';
            html += '</div>';
        }

        // Pending action indicator
        if (brain && brain.pendingAction) {
            html += '<div style="margin-top:4px;font-size:11px;"><span style="color:#ff9e64;">' +
                '<i class="fa-solid fa-rotate-left" style="margin-right:4px;"></i>Pending: return to ' +
                esc(brain.pendingAction.returnTo) + ' (' + esc(brain.pendingAction.subPhase || '') + ')' +
                (brain.pendingAction.questId ? ' quest #' + brain.pendingAction.questId : '') +
                '</span></div>';
        }

        html += '</div></div>';

        // --- Current quest (realtime spine + server kill-credit; filled by the live poll) ---
        html += '<div class="bt-section"><div class="bt-section-header"><span>' +
            '<i class="fa-solid fa-scroll" style="color:#e0af68;margin-right:6px;"></i>Current quest</span></div>' +
            '<div class="bt-section-body"><div id="detailQuest"></div></div></div>';

        // --- Personality Section ---
        if (brain && brain.personality) {
            var p = brain.personality;
            html += '<div class="bt-section"><div class="bt-section-header"><span><i class="fa-solid fa-fingerprint" style="color:var(--accent);margin-right:6px;"></i>Personality</span>' +
                '<span style="font-weight:400;text-transform:none;letter-spacing:0;font-size:11px;color:var(--text-muted);">' +
                p.chatStyle + ' / ' + p.temperament + ' — tick base ' + p.tickBase.toFixed(1) + 's</span></div>';
            html += '<div class="bt-section-body">';

            var traits = ['patience', 'greed', 'curiosity', 'sociability', 'aggression', 'efficiency', 'cautiousness', 'indecisiveness', 'spontaneity'];
            for (var i = 0; i < traits.length; i++) {
                var t = traits[i];
                var val = p[t];
                var meta = TRAIT_META[t] || { icon: 'fa-circle', color: '#888' };
                var pct = Math.round(val * 100);
                html += '<div class="bt-trait">' +
                    '<span class="bt-trait-icon"><i class="fa-solid ' + meta.icon + '" style="color:' + meta.color + ';"></i></span>' +
                    '<span class="bt-trait-label">' + capitalize(t) + '</span>' +
                    '<div class="bt-trait-bar-track"><div class="bt-trait-bar-fill" style="width:' + pct + '%;background:' + meta.color + ';"></div></div>' +
                    '<span class="bt-trait-val">' + pct + '</span>' +
                    '</div>';
            }

            if (p.quirks && p.quirks.length > 0) {
                html += '<div style="margin-top:10px;">';
                for (var qi = 0; qi < p.quirks.length; qi++) {
                    var q = p.quirks[qi];
                    html += '<span class="bt-quirk" title="' + esc(q.description || '') + '"><i class="fa-solid fa-star" style="font-size:9px;"></i> ' + esc(q.name) + '</span>';
                }
                html += '</div>';
            } else {
                html += '<div style="margin-top:8px;font-size:11px;color:var(--text-muted);">No quirks</div>';
            }

            html += '</div></div>';
        }

        // --- Last Decision / Weights ---
        html += '<div class="bt-section"><div class="bt-section-header"><span><i class="fa-solid fa-scale-balanced" style="color:var(--accent);margin-right:6px;"></i>Decision Weights</span></div>';
        html += '<div class="bt-section-body"><div class="bt-weights" id="weightsGrid">';
        if (brain && brain.lastDecision && brain.lastDecision.weights) {
            html += renderWeightsHtml(brain.lastDecision.weights);
        } else {
            html += '<div style="font-size:12px;color:var(--text-muted);grid-column:1/-1;">Waiting for first decision tick...</div>';
        }
        html += '</div></div></div>';

        // --- Economy (real data from enriched STATE) ---
        var copper = s.copper || 0;
        var freeSlots = s.freeSlots != null ? s.freeSlots : 16;
        var totalSlots = s.totalSlots != null ? s.totalSlots : 16;
        var usedSlots = totalSlots - freeSlots;
        var bagPct = totalSlots > 0 ? Math.round(usedSlots / totalSlots * 100) : 0;
        var bagColor = bagPct >= 90 ? '#f7768e' : (bagPct >= 70 ? '#e0af68' : '#9ece6a');

        html += '<div class="bt-section"><div class="bt-section-header">' +
            '<span><i class="fa-solid fa-coins" style="color:#e0af68;margin-right:6px;"></i>Economy</span>' +
            '<button class="btn-sm" id="btnLoadInventory" style="font-size:10px;padding:2px 8px;cursor:pointer;background:var(--bg-card-alt);border:1px solid var(--border-light);border-radius:3px;color:var(--text-secondary);">' +
            '<i class="fa-solid fa-box-open" style="margin-right:3px;"></i>Inventory</button>' +
            '</div>';
        html += '<div class="bt-section-body">';
        html += '<div class="bt-econ-grid" id="econStrip">';
        html += renderEconStripHtml(s, brain);
        html += '</div>';
        // Inventory container (populated on click)
        html += '<div id="inventoryPanel" style="display:none;margin-top:12px;"></div>';
        html += '</div></div>';

        // --- Activity Timeline ---
        html += '<div class="bt-section"><div class="bt-section-header"><span><i class="fa-solid fa-clock-rotate-left" style="color:var(--accent);margin-right:6px;"></i>Activity Timeline</span></div>';
        html += '<div class="bt-section-body"><div class="bt-timeline" id="timeline"></div></div></div>';

        $('#detailTabBody').html(html);
        renderTimeline(selectedGuid);
        updateDetailQuest();   // fill the realtime "Current quest" section
    }

    // ---- Overview "Current quest": realtime quest progress — the same spine scratch +
    // server kill-credit the Live tab renders — written into the stable #detailQuest
    // container so the 4-5s polls refresh it in place without rebuilding the Overview panel.
    function updateDetailQuest() {
        var $box = $('#detailQuest');
        if ($box.length === 0) return;            // not on Overview / panel not built yet
        $box.html(renderDetailQuestHtml());
    }

    function renderDetailQuestHtml() {
        var d = liveData;
        if (!d || liveGuid !== selectedGuid) {
            return '<div style="font-size:11px;color:var(--text-muted);">' +
                '<i class="fa-solid fa-satellite-dish fa-fade" style="margin-right:5px;"></i>' +
                'connecting to live feed… (brain engine must be ON)</div>';
        }

        var goalColor = GOAL_COLOR[d.goal] || 'var(--accent)';
        var head = '<div style="font-size:12px;margin-bottom:6px;">' +
            '<span style="color:' + goalColor + ';font-weight:700;">' + esc(d.goal) + '</span>' +
            '<span style="color:var(--text-muted);"> / ' + esc(d.step) + '</span>' +
            (d.why ? '<span style="color:var(--text-muted);margin-left:8px;">why = ' + esc(d.why) + '</span>' : '') +
            '</div>';

        var sc = d.scratch;
        if (!sc || sc.kind !== 'quest' || !sc.active) {
            return head + '<div style="font-size:11px;color:var(--text-muted);">No active quest objective right now.</div>';
        }

        var aq = sc.active;
        var html = head + '<div class="bt-live-active">';
        html += '<div class="bt-live-active-t">★ ' + esc(aq.title || ('#' + aq.id)) +
            ' <span style="color:var(--text-muted);font-weight:400;">#' + aq.id + (aq.level ? ' · L' + aq.level : '') + '</span></div>';

        if (aq.objectives && aq.objectives.length) {
            var killIdx = 0, itemIdx = 0;
            for (var oi = 0; oi < aq.objectives.length; oi++) {
                var o = aq.objectives[oi];
                var icon = o.kind === 'kill' ? 'fa-khanda' : (o.kind === 'gather' ? 'fa-hand-holding' : 'fa-hand-pointer');
                // Authoritative server kill credit overrides the spine's un-fed `have`.
                var srv = serverObjFor(aq.id, o, killIdx, itemIdx);
                if (o.kind === 'kill') killIdx++; else if (o.kind === 'gather') itemIdx++;
                var have = (srv != null) ? srv.have : o.have;
                var need = (srv != null && srv.need > 0) ? srv.need : o.need;
                var done = (have != null && need > 0 && have >= need);
                var cnt = (have != null) ? (have + '/' + need) : ('×' + need);
                var cntColor = done ? '#9ece6a' : (o.active ? '#e0af68' : 'var(--text-secondary)');
                html += '<div class="bt-live-obj' + (o.active ? ' active' : '') + '">';
                html += '<i class="fa-solid ' + icon + '" style="width:14px;"></i> ';
                html += '<span class="bt-live-obj-cnt" style="color:' + cntColor + ';">' + cnt + '</span> ';
                if (srv != null) html += '<span class="bt-live-obj-srv" title="live server kill credit (character_queststatus.mob_count)">srv</span> ';
                html += '<span class="bt-live-obj-name">' + esc(o.name) + '</span>';
                if (o.kind === 'gather' && o.from) html += ' <span style="color:var(--text-muted);">from ' + esc(o.from) + '</span>';
                html += distTag(d, o);
                html += '</div>';
            }
        }
        if (aq.giver) html += questNpcRow('Accept', 'fa-circle-question', aq.giver, d, '#7aa2f7');
        if (aq.turnIn) html += questNpcRow('Turn in', 'fa-flag-checkered', aq.turnIn, d, '#9ece6a');
        html += '</div>';

        if (sc.batch && sc.batch.length) {
            var doneN = 0;
            for (var bi = 0; bi < sc.batch.length; bi++) if (sc.batch[bi].turnedIn) doneN++;
            html += '<div style="font-size:11px;color:var(--text-muted);margin-top:6px;">batch: ' +
                sc.batch.length + ' quest' + (sc.batch.length === 1 ? '' : 's') +
                (doneN ? ' · ' + doneN + ' turned in' : '') + '</div>';
        }
        return html;
    }

    // --- Economy strip (updates live from STATE) ---
    function renderEconStripHtml(s, brain) {
        var copper = s.copper || 0;
        var freeSlots = s.freeSlots != null ? s.freeSlots : 16;
        var totalSlots = s.totalSlots != null ? s.totalSlots : 16;
        var usedSlots = totalSlots - freeSlots;
        var bagPct = totalSlots > 0 ? Math.round(usedSlots / totalSlots * 100) : 0;
        var bagColor = bagPct >= 90 ? '#f7768e' : (bagPct >= 70 ? '#e0af68' : '#9ece6a');

        var html = '';
        html += '<div class="bt-econ-item"><div class="bt-econ-val" style="color:#e0af68;">' + formatGold(copper) + '</div><div class="bt-econ-label">Gold</div></div>';
        html += '<div class="bt-econ-item"><div class="bt-econ-val" style="color:' + bagColor + ';">' + usedSlots + '/' + totalSlots + '</div><div class="bt-econ-label">Bag Slots</div></div>';
        html += '<div class="bt-econ-item"><div class="bt-econ-val">' + (brain && brain.hasUnlearnedSpells ? '<span style="color:#f7768e;">Yes</span>' : '<span style="color:#9ece6a;">No</span>') + '</div><div class="bt-econ-label">Needs Training</div></div>';
        return html;
    }

    function updateEconomyStrip(s) {
        var $strip = $('#econStrip');
        if ($strip.length === 0) return;
        var brain = botBrains[s.guid];
        $strip.html(renderEconStripHtml(s, brain));
    }

    // --- Inventory panel (lazy-loaded from /Bots/Inventory) ---
    $(document).on('click', '#btnLoadInventory', function () {
        var $panel = $('#inventoryPanel');
        if ($panel.is(':visible')) {
            $panel.hide();
            return;
        }
        if (!selectedGuid) return;

        // Check cache
        if (inventoryCache[selectedGuid]) {
            renderInventoryPanel(inventoryCache[selectedGuid]);
            return;
        }

        $panel.html('<div style="text-align:center;padding:12px;color:var(--text-muted);"><i class="fa-solid fa-spinner fa-spin"></i> Loading inventory...</div>').show();

        $.getJSON('/Bots/Inventory', { guid: selectedGuid }, function (data) {
            if (data.error) {
                $panel.html('<div style="color:#f7768e;font-size:12px;">Error: ' + esc(data.error) + '</div>');
                return;
            }
            inventoryCache[selectedGuid] = data;
            renderInventoryPanel(data);
        }).fail(function () {
            $panel.html('<div style="color:#f7768e;font-size:12px;">Failed to load inventory</div>');
        });
    });

    function renderInventoryPanel(data) {
        var $panel = $('#inventoryPanel');
        var html = '';
        var icons = data.icons || {};

        // Equipped gear
        if (data.equipped && data.equipped.length > 0) {
            html += '<div class="bt-inv-section-title"><i class="fa-solid fa-shield-halved"></i> Equipped</div>';
            html += '<div class="bt-inv-grid">';
            for (var i = 0; i < data.equipped.length; i++) {
                html += renderInvItem(data.equipped[i], true, icons);
            }
            html += '</div>';
        }

        // Backpack
        if (data.backpack && data.backpack.length > 0) {
            html += '<div class="bt-inv-section-title" style="margin-top:10px;"><i class="fa-solid fa-suitcase"></i> Backpack (' + data.backpack.length + '/16)</div>';
            html += '<div class="bt-inv-grid">';
            for (var i = 0; i < data.backpack.length; i++) {
                html += renderInvItem(data.backpack[i], false, icons);
            }
            html += '</div>';
        }

        // Extra bags
        if (data.bags && data.bags.length > 0) {
            for (var b = 0; b < data.bags.length; b++) {
                var bag = data.bags[b];
                var bagName = bag.bag ? bag.bag.name : 'Bag';
                html += '<div class="bt-inv-section-title" style="margin-top:10px;"><i class="fa-solid fa-box"></i> ' + esc(bagName) + ' (' + bag.used + '/' + bag.capacity + ')</div>';
                if (bag.contents.length > 0) {
                    html += '<div class="bt-inv-grid">';
                    for (var c = 0; c < bag.contents.length; c++) {
                        html += renderInvItem(bag.contents[c], false, icons);
                    }
                    html += '</div>';
                } else {
                    html += '<div style="font-size:11px;color:var(--text-muted);padding:4px 0;">Empty</div>';
                }
            }
        }

        // Summary
        if (data.totalSellValue > 0) {
            html += '<div style="margin-top:8px;font-size:11px;color:var(--text-muted);">' +
                'Total sell value: <span style="color:#e0af68;">' + formatGold(data.totalSellValue) + '</span>' +
                '</div>';
        }

        $panel.html(html).show();
    }

    function renderInvItem(item, isEquipped, icons) {
        var qColor = QUALITY_COLORS[item.quality] || '#fff';
        var slotLabel = isEquipped ? (EQUIP_SLOT_NAMES[item.inventoryType] || 'Slot ' + item.slot) : '';
        var iconPath = (item.displayId && icons[item.displayId]) ? icons[item.displayId] : '/icons/inv_misc_questionmark.png';
        var count = item.stackCount || 1;

        return '<div class="bt-inv-item"' +
            ' data-tt-name="' + esc(item.name) + '"' +
            ' data-tt-quality="' + item.quality + '"' +
            ' data-tt-class="' + item.itemClass + '"' +
            ' data-tt-subclass="' + (item.subclass || 0) + '"' +
            ' data-tt-invtype="' + item.inventoryType + '"' +
            ' data-tt-ilvl="' + item.itemLevel + '"' +
            ' data-tt-armor="' + item.armor + '"' +
            ' data-tt-sell="' + item.sellPrice + '"' +
            ' data-tt-equipped="' + isEquipped + '"' +
            ' data-tt-count="' + count + '"' +
            '>' +
            '<div class="bt-inv-icon-wrap">' +
            '<img class="bt-inv-icon" src="' + esc(iconPath) + '" alt="" loading="lazy" />' +
            (count > 1 ? '<span class="bt-inv-count">' + count + '</span>' : '') +
            '</div>' +
            '<span class="bt-inv-name" style="color:' + qColor + ';">' + esc(item.name) + '</span>' +
            (isEquipped ? '<span class="bt-inv-slot">' + slotLabel + '</span>' : '') +
            (item.armor > 0 ? '<span class="bt-inv-stat">' + item.armor + ' armor</span>' : '') +
            '</div>';
    }

    // ===================== WEIGHTS =====================

    function renderWeightsHtml(weights) {
        var html = '';
        var maxW = 0;
        var keys = Object.keys(weights);
        for (var i = 0; i < keys.length; i++) if (weights[keys[i]] > maxW) maxW = weights[keys[i]];
        if (maxW === 0) maxW = 1;

        keys.sort(function (a, b) { return weights[b] - weights[a]; });
        for (var i = 0; i < keys.length; i++) {
            var k = keys[i];
            var v = weights[k];
            var pct = Math.round(v / maxW * 100);
            html += '<div class="bt-weight-row">' +
                '<span class="bt-weight-label">' + k + '</span>' +
                '<div class="bt-weight-bar-track"><div class="bt-weight-bar-fill" style="width:' + pct + '%;"></div></div>' +
                '<span class="bt-weight-val">' + v.toFixed(2) + '</span>' +
                '</div>';
        }
        return html;
    }

    function renderWeights(weights) {
        var $grid = $('#weightsGrid');
        if ($grid.length === 0) return;
        $grid.html(renderWeightsHtml(weights));
    }

    // ===================== TIMELINE =====================

    function renderTimeline(guid) {
        var $tl = $('#timeline');
        if ($tl.length === 0) return;

        var entries = decisionLog[guid];
        if (!entries || entries.length === 0) {
            $tl.html('<div style="color:#5f6b7a;">No decisions recorded yet.</div>');
            return;
        }

        var html = '';
        var start = Math.max(0, entries.length - 30);
        for (var i = start; i < entries.length; i++) {
            var e = entries[i];
            var cls = e.activityChanged ? 'bt-tl-switch' : 'bt-tl-stay';
            var ts = new Date(e.timestamp).toLocaleTimeString();
            html += '<div class="' + cls + '">[' + ts + '] ' + esc(e.decision) + '</div>';
        }
        $tl.html(html);
        $tl[0].scrollTop = $tl[0].scrollHeight;
    }

    function tlAppend(guid, text, cls) {
        if (selectedGuid !== guid) return;
        var $tl = $('#timeline');
        if ($tl.length === 0) return;
        var ts = new Date().toLocaleTimeString();
        $tl.append('<div class="' + (cls || 'bt-tl-event') + '">[' + ts + '] ' + esc(text) + '</div>');
        while ($tl.children().length > maxTimelineEntries) $tl.children(':first').remove();
        $tl[0].scrollTop = $tl[0].scrollHeight;
    }

    // ===================== STATS =====================

    function updateStats() {
        var guids = Object.keys(botStates);
        var tracked = 0;
        for (var i = 0; i < guids.length; i++) {
            if (botStates[guids[i]].taskState !== 'DISCONNECTED') tracked++;
        }
        $('#statTracked').text(tracked);
        $('#statBrains').text(Object.keys(botBrains).length);

        var elapsed = (Date.now() - dpmStartTime) / 60000;
        var dpm = elapsed > 0 ? Math.round(decisionCount / elapsed) : 0;
        $('#statDpm').text(dpm);
    }

    function updateBotDropdown() {
        var $sel = $('#cmdBotSelect');
        var current = $sel.val();
        $sel.find('option:not(:first)').remove();
        var guids = Object.keys(botStates).sort(function (a, b) {
            return (botStates[a].name || '').localeCompare(botStates[b].name || '');
        });
        for (var i = 0; i < guids.length; i++) {
            var s = botStates[guids[i]];
            if (s.taskState === 'DISCONNECTED') continue;
            $sel.append('<option value="' + s.guid + '">' + s.name + ' (L' + s.level + ')</option>');
        }
        if (current) $sel.val(current);
    }

    // ===================== PERIODIC BRAIN REFRESH =====================
    // Poll brain state every 5s for the selected bot so sub-phase/quest
    // info stays fresh between strategic evals (which can be 3-10 min apart)

    var brainPollTimer = null;
    var rosterPollTimer = null;

    function startBrainPoll() {
        stopBrainPoll();
        brainPollTimer = setInterval(function () {
            if (!selectedGuid || !connected) return;
            $.getJSON('/Bots/BrainState/' + selectedGuid, function (data) {
                if (data && data.guid) {
                    var existing = botBrains[data.guid] || {};
                    var prevDecision = existing.lastDecision;
                    botBrains[data.guid] = data;
                    if (prevDecision) botBrains[data.guid].lastDecision = prevDecision;
                    updateBrainHeader(data.guid);
                    renderRosterCard(data.guid);
                }
            });
        }, 5000);
    }

    function stopBrainPoll() {
        if (brainPollTimer) { clearInterval(brainPollTimer); brainPollTimer = null; }
    }

    // Refresh just the header/sub-phase section without re-rendering the whole detail panel
    function updateBrainHeader(guid) {
        if (selectedGuid !== guid) return;
        var s = botStates[guid];
        var brain = botBrains[guid];
        if (!s || !brain) return;

        // Update header info strip
        var $header = $('#botHeaderInfo');
        if ($header.length > 0) {
            var hpPct = (s.maxHealth || 0) > 0 ? Math.round((s.health || 0) / s.maxHealth * 100) : 0;
            var mpPct = (s.maxMana || 0) > 0 ? Math.round((s.mana || 0) / s.maxMana * 100) : 0;
            var headerHtml = '<span style="color:#9ece6a;">HP ' + hpPct + '%</span>';
            headerHtml += ' &nbsp; <span style="color:#7aa2f7;">MP ' + mpPct + '%</span>';
            if (s.inCombat) headerHtml += ' &nbsp; <span style="color:#f7768e;font-weight:600;">IN COMBAT</span>';
            if (s.isDead) headerHtml += ' &nbsp; <span style="color:#f7768e;font-weight:600;">DEAD</span>';
            $header.html(headerHtml);
        }

        // Update sub-phase strip
        var $subphase = $('#botSubPhase');
        if ($subphase.length > 0) {
            var spHtml = '<i class="fa-solid fa-route" style="margin-right:4px;"></i>';
            spHtml += '<span style="color:var(--text-secondary);">' + esc(brain.activity || '') + '</span>';
            spHtml += ' &rarr; <span style="color:#7aa2f7;">' + esc(brain.subPhase || '') + '</span>';
            if (brain.activeQuestId) spHtml += ' <span style="color:#e0af68;">(Quest #' + brain.activeQuestId + ')</span>';
            if (brain.contextTag) spHtml += ' <span style="color:var(--text-muted);">' + esc(brain.contextTag) + '</span>';
            $subphase.html(spHtml);
        }

        // Update pending action
        var $pending = $('#botPending');
        if ($pending.length > 0) {
            if (brain.pendingAction) {
                $pending.html('<span style="color:#ff9e64;">' +
                    '<i class="fa-solid fa-rotate-left" style="margin-right:4px;"></i>Pending: return to ' +
                    esc(brain.pendingAction.returnTo) + ' (' + esc(brain.pendingAction.subPhase || '') + ')' +
                    (brain.pendingAction.questId ? ' quest #' + brain.pendingAction.questId : '') +
                    '</span>').show();
            } else {
                $pending.hide();
            }
        }

        // Update position
        var $pos = $('#botPosition');
        if ($pos.length > 0) {
            $pos.text('L' + (s.level || 0) + ' ' + (RACE_NAMES[s.race] || '?') + ' \u2014 Map ' + (s.mapId || 0) + ' (' + (s.x != null ? s.x : 0).toFixed(0) + ', ' + (s.y != null ? s.y : 0).toFixed(0) + ')');
        }
    }

    // Roster-level brain refresh: poll all bots every 10s for roster card accuracy
    function startRosterPoll() {
        if (rosterPollTimer) clearInterval(rosterPollTimer);
        rosterPollTimer = setInterval(function () {
            if (!connected) return;
            var guids = Object.keys(botStates);
            for (var i = 0; i < guids.length; i++) {
                (function (g) {
                    $.getJSON('/Bots/BrainState/' + g, function (data) {
                        if (data && data.guid) {
                            var existing = botBrains[data.guid] || {};
                            var prevDecision = existing.lastDecision;
                            botBrains[data.guid] = data;
                            if (prevDecision) botBrains[data.guid].lastDecision = prevDecision;
                            renderRosterCard(data.guid);
                        }
                    });
                })(guids[i]);
            }
        }, 10000);
    }

    // ===================== ENGINE TOGGLE =====================

    $('#engineToggle').on('click', function () {
        engineEnabled = !engineEnabled;
        $(this).toggleClass('active', engineEnabled);
        $(this).find('.bt-engine-label').text(engineEnabled ? 'Engine On' : 'Engine Off');
        $.post('/Bots/ToggleBrain', { enabled: engineEnabled });
    });

    // ===================== GROUPING MODE (Session 31) =====================

    $('#groupingMode').on('change', function () {
        var mode = parseInt($(this).val());
        $.ajax({
            url: '/Bots/SetGroupingMode',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ mode: mode }),
            success: function (data) {
                if (data.success) {
                    showToast('Grouping: ' + data.modeName);
                    refreshGroupingStatus();
                } else {
                    showToast('Error: ' + (data.error || 'unknown'), true);
                }
            }
        });
    });

    $('#autoFormGroups').on('click', function () {
        $.ajax({
            url: '/Bots/AutoFormGroups',
            type: 'POST',
            contentType: 'application/json',
            data: '{}',
            success: function (data) {
                if (data.success) {
                    showToast('Formed ' + data.groupsFormed + ' group(s)');
                    refreshGroupingStatus();
                }
            }
        });
    });

    function refreshGroupingStatus() {
        $.getJSON('/Bots/BrainStatus', function (data) {
            updateGroupingUI(data);
        });
    }

    function updateGroupingUI(data) {
        var groups = data.groups || [];
        var $list = $('#groupList');
        $('#groupCount').text(groups.length);
        if (groups.length === 0) {
            $list.html('<div style="color:var(--text-muted);font-size:12px;">No active groups</div>');
            return;
        }
        var html = '';
        groups.forEach(function (g) {
            var memberNames = g.memberGuids.map(function (guid) {
                var s = botStates[guid];
                var isLeader = guid === g.leaderGuid;
                var name = s ? s.name : ('Bot #' + guid);
                return '<span style="color:' + (isLeader ? '#e0af68' : '#c0caf5') + ';">'
                    + (isLeader ? '<i class="fa-solid fa-crown" style="font-size:10px;margin-right:2px;"></i>' : '')
                    + name + '</span>';
            }).join(', ');

            html += '<div class="bt-group-row" style="display:flex;align-items:center;gap:8px;padding:4px 0;border-bottom:1px solid var(--border);">'
                + '<span style="color:var(--text-muted);font-size:11px;min-width:24px;">#' + g.groupId + '</span>'
                + '<span style="flex:1;font-size:12px;">' + memberNames + '</span>'
                + '<button class="btn btn-sm" style="padding:1px 6px;font-size:10px;background:var(--danger);color:#fff;border:none;border-radius:3px;cursor:pointer;" '
                + 'onclick="disbandGroup(' + g.groupId + ')">Disband</button>'
                + '</div>';
        });
        $list.html(html);
    }

    // Global function for inline onclick
    window.disbandGroup = function (groupId) {
        $.ajax({
            url: '/Bots/DisbandGroup',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ groupId: groupId }),
            success: function (data) {
                if (data.success) {
                    showToast('Group #' + groupId + ' disbanded');
                    refreshGroupingStatus();
                }
            }
        });
    };

    function showToast(msg, isError) {
        var $t = $('<div class="bt-toast">' + msg + '</div>').css({
            position: 'fixed', bottom: '20px', right: '20px', padding: '8px 16px',
            background: isError ? '#f7768e' : '#9ece6a', color: '#1a1b26',
            borderRadius: '6px', fontWeight: 600, fontSize: '13px', zIndex: 9999,
            boxShadow: '0 2px 8px rgba(0,0,0,0.4)', opacity: 0
        });
        $('body').append($t);
        $t.animate({ opacity: 1 }, 200).delay(2500).animate({ opacity: 0 }, 300, function () { $(this).remove(); });
    }

    // ===================== ROSTER SELECTION =====================

    $(document).on('click', '.bt-roster-card', function () {
        var guid = parseInt($(this).data('guid'));
        selectedGuid = guid;
        $('.bt-roster-card').removeClass('selected');
        $(this).addClass('selected');

        // Start polling for this bot's brain state
        startBrainPoll();

        if (!botBrains[guid]) {
            $.getJSON('/Bots/BrainState/' + guid, function (data) {
                if (data && data.guid) {
                    botBrains[guid] = data;
                }
                renderDetail();
            }).fail(function () {
                renderDetail();
            });
        } else {
            renderDetail();
        }
    });

    // Detail-panel tab switching (Overview | Live)
    $(document).on('click', '.bt-detail-tab', function () {
        var t = $(this).data('dtab');
        if (t === detailTab) return;
        detailTab = t;
        $('.bt-detail-tab').removeClass('active');
        $(this).addClass('active');
        renderActiveDetailTab();
        ensureLivePoll();
    });

    // ===================== COMMAND BAR =====================

    $('#cmdType').on('change', function () {
        var type = $(this).val();
        $('#cmdParamsMoveTo, #cmdParamsSay, #cmdParamsQuest, #cmdParamsSpell, #cmdParamsTarget').hide();
        switch (type) {
            case 'move_to': $('#cmdParamsMoveTo').show(); break;
            case 'say': case 'yell': $('#cmdParamsSay').show(); break;
            case 'accept_quest': case 'complete_quest': case 'abandon_quest': $('#cmdParamsQuest').show(); break;
            case 'learn_spell': $('#cmdParamsSpell').show(); break;
            case 'attack_target': case 'interact_npc': $('#cmdParamsTarget').show(); break;
        }
    });

    $('#btnSendCmd').on('click', function () {
        if (!connected) return;
        var guid = parseInt($('#cmdBotSelect').val());
        var cmdType = $('#cmdType').val();

        switch (cmdType) {
            case 'move_to':
                var m = parseInt($('#cmdMapId').val()) || 0, x = parseFloat($('#cmdX').val()) || 0;
                var y = parseFloat($('#cmdY').val()) || 0, z = parseFloat($('#cmdZ').val()) || 0;
                if (guid === 0) connection.invoke('SendMoveToAll', m, x, y, z).catch(logErr);
                else connection.invoke('SendMoveTo', guid, m, x, y, z).catch(logErr);
                break;
            case 'say': case 'yell':
                var text = $('#cmdText').val().trim();
                if (!text) return;
                var chatType = (cmdType === 'yell') ? 6 : 0;
                if (guid === 0) {
                    Object.keys(botStates).forEach(function (g) {
                        if (botStates[g].taskState !== 'DISCONNECTED')
                            connection.invoke('SendSayText', parseInt(g), text, chatType).catch(logErr);
                    });
                } else connection.invoke('SendSayText', guid, text, chatType).catch(logErr);
                $('#cmdText').val('');
                break;
            case 'accept_quest':
                var qid = parseInt($('#cmdQuestId').val()) || 0;
                if (qid && guid) connection.invoke('SendAcceptQuest', guid, qid).catch(logErr);
                break;
            case 'complete_quest':
                var qid = parseInt($('#cmdQuestId').val()) || 0;
                if (qid && guid) connection.invoke('SendCompleteQuest', guid, qid).catch(logErr);
                break;
            case 'abandon_quest':
                var qid = parseInt($('#cmdQuestId').val()) || 0;
                if (qid && guid) connection.invoke('SendAbandonQuest', guid, qid).catch(logErr);
                break;
            case 'learn_spell':
                var sid = parseInt($('#cmdSpellId').val()) || 0;
                if (sid && guid) connection.invoke('SendLearnSpell', guid, sid).catch(logErr);
                break;
            case 'attack_target':
                var tg = parseInt($('#cmdTargetGuid').val()) || 0;
                if (tg && guid) connection.invoke('SendAttackTarget', guid, tg).catch(logErr);
                break;
            case 'interact_npc':
                var ng = parseInt($('#cmdTargetGuid').val()) || 0;
                if (ng && guid) connection.invoke('SendInteractNpc', guid, ng).catch(logErr);
                break;
        }
        function logErr(err) { console.error('Cmd send failed:', err); }
    });

    $('#cmdText').on('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); $('#btnSendCmd').click(); }
    });

    // ===================== HELPERS =====================

    function formatGold(copper) {
        if (!copper || copper <= 0) return '0c';
        var g = Math.floor(copper / 10000);
        var s = Math.floor((copper % 10000) / 100);
        var c = copper % 100;
        var parts = [];
        if (g > 0) parts.push(g + 'g');
        if (s > 0) parts.push(s + 's');
        if (c > 0 || parts.length === 0) parts.push(c + 'c');
        return parts.join(' ');
    }

    function esc(s) {
        if (!s) return '';
        var d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

    function capitalize(s) {
        return s.charAt(0).toUpperCase() + s.slice(1);
    }

    // ===================== BOT DETAIL MODAL =====================

    var QUEST_STATUS_NAMES = { 0: 'None', 1: 'In Progress', 3: 'Complete', 6: 'Failed' };
    var QUEST_STATUS_ICONS = { 0: 'fa-circle-xmark', 1: 'fa-spinner', 3: 'fa-circle-check', 6: 'fa-skull' };
    var QUEST_STATUS_COLORS = { 0: '#5f6b7a', 1: '#7aa2f7', 3: '#e0af68', 6: '#f7768e' };
    var ZONE_NAMES = {
        9: 'Northshire Valley', 12: 'Elwynn Forest', 1: 'Dun Morogh', 14: 'Durotar',
        85: 'Tirisfal Glades', 141: 'Teldrassil', 215: 'Mulgore',
        '-81': 'Warrior', '-141': 'Paladin', '-261': 'Mage'
    };
    var questStatusCache = {};

    // Inject modal styles
    $('<style>').text(
        '.bm-overlay { position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.6);z-index:5000;display:none;align-items:center;justify-content:center; }' +
        '.bm-overlay.active { display:flex; }' +
        '.bm-modal { background:var(--bg-card, #1a1b26);border:1px solid var(--border-light, #414868);border-radius:10px;width:90vw;max-width:1100px;height:88vh;display:flex;flex-direction:column;box-shadow:0 16px 48px rgba(0,0,0,0.5);overflow:hidden; }' +
        '.bm-header { display:flex;align-items:center;justify-content:space-between;padding:14px 20px;border-bottom:1px solid var(--border-light, #414868);flex-shrink:0; }' +
        '.bm-header-title { font-size:15px;font-weight:700;color:var(--text-primary, #c0caf5); }' +
        '.bm-close { background:none;border:none;color:var(--text-muted, #5f6b7a);font-size:18px;cursor:pointer;padding:4px 8px; }' +
        '.bm-close:hover { color:var(--text-primary, #c0caf5); }' +
        '.bm-tabs { display:flex;gap:0;border-bottom:1px solid var(--border-light, #414868);flex-shrink:0;padding:0 20px; }' +
        '.bm-tab { padding:8px 16px;font-size:12px;font-weight:600;color:var(--text-muted, #5f6b7a);cursor:pointer;border-bottom:2px solid transparent;text-transform:uppercase;letter-spacing:0.5px; }' +
        '.bm-tab:hover { color:var(--text-secondary, #a9b1d6); }' +
        '.bm-tab.active { color:var(--accent, #7aa2f7);border-bottom-color:var(--accent, #7aa2f7); }' +
        '.bm-body { flex:1;overflow-y:auto;padding:16px 20px; }' +

        '.bq-zone-group { margin-bottom:16px; }' +
        '.bq-zone-header { font-size:13px;font-weight:700;color:var(--text-secondary, #a9b1d6);margin-bottom:8px;display:flex;align-items:center;gap:8px; }' +
        '.bq-zone-badge { font-size:10px;background:var(--bg-card-alt, #24283b);padding:2px 8px;border-radius:10px;color:var(--text-muted); }' +
        '.bq-quest-row { display:flex;align-items:center;gap:10px;padding:6px 10px;border-radius:5px;cursor:pointer;font-size:12px;transition:background 0.15s; }' +
        '.bq-quest-row:hover { background:var(--bg-card-alt, #24283b); }' +
        '.bq-quest-row.expanded { background:var(--bg-card-alt, #24283b);border-radius:5px 5px 0 0; }' +
        '.bq-status-icon { width:16px;text-align:center;flex-shrink:0; }' +
        '.bq-quest-title { flex:1;font-weight:500; }' +
        '.bq-quest-level { color:var(--text-muted);font-size:11px;width:30px;text-align:right;flex-shrink:0; }' +
        '.bq-quest-progress { font-size:11px;width:80px;text-align:right;flex-shrink:0; }' +
        '.bq-rewarded { color:#9ece6a; }' +
        '.bq-detail { background:var(--bg-card-alt, #24283b);padding:10px 14px 10px 36px;margin-bottom:4px;border-radius:0 0 5px 5px;font-size:11px;line-height:1.7;display:none;color:var(--text-muted); }' +
        '.bq-detail.visible { display:block; }' +
        '.bq-detail-label { color:var(--text-secondary, #a9b1d6);font-weight:600;margin-right:4px; }' +
        '.bq-obj-row { display:flex;align-items:center;gap:6px;margin-top:2px; }' +
        '.bq-obj-bar { height:4px;background:var(--border-light, #414868);border-radius:2px;flex:1;max-width:100px;overflow:hidden; }' +
        '.bq-obj-fill { height:100%;border-radius:2px; }' +
        '.bq-chain-tag { font-size:10px;padding:1px 6px;border-radius:3px;border:1px solid var(--border-light);color:var(--text-muted); }' +
        '.bq-excl-tag { font-size:10px;padding:1px 6px;border-radius:3px;background:#f7768e22;border:1px solid #f7768e44;color:#f7768e; }' +
        '.bq-loading { text-align:center;padding:40px;color:var(--text-muted); }'
    ).appendTo('head');

    // Inject modal DOM
    $('body').append(
        '<div class="bm-overlay" id="botModal">' +
        '<div class="bm-modal">' +
        '<div class="bm-header">' +
        '<span class="bm-header-title" id="bmTitle">Bot Details</span>' +
        '<button class="bm-close" id="bmClose"><i class="fa-solid fa-xmark"></i></button>' +
        '</div>' +
        '<div class="bm-tabs">' +
        '<div class="bm-tab active" data-tab="quests"><i class="fa-solid fa-scroll" style="margin-right:5px;"></i>Quests</div>' +
        '<div class="bm-tab" data-tab="gear"><i class="fa-solid fa-shield-halved" style="margin-right:5px;"></i>Gear</div>' +
        '<div class="bm-tab" data-tab="brain"><i class="fa-solid fa-brain" style="margin-right:5px;"></i>Brain</div>' +
        '</div>' +
        '<div class="bm-body" id="bmBody"></div>' +
        '</div>' +
        '</div>'
    );

    // Modal close
    $('#bmClose').on('click', function () { $('#botModal').removeClass('active'); });
    $('#botModal').on('click', function (e) {
        if (e.target === this) $(this).removeClass('active');
    });
    $(document).on('keydown', function (e) {
        if (e.key === 'Escape') $('#botModal').removeClass('active');
    });

    // Tab switching
    $(document).on('click', '.bm-tab', function () {
        var tab = $(this).data('tab');
        $('.bm-tab').removeClass('active');
        $(this).addClass('active');
        loadModalTab(tab);
    });

    // ===================== BOT REPORT (quantized, on-the-spot) =====================
    // Pulls /Bots/BotReport for the watched bot and renders a bounded, counts-only
    // snapshot of what that one bot's buffered log shows — the cocktail, on demand.

    $('body').append(
        '<div class="br-overlay" id="botReportModal">' +
        '<div class="br-modal">' +
        '<div class="br-header"><span id="brTitle"><i class="fa-solid fa-bolt"></i> Bot report</span>' +
        '<button class="br-close" id="brClose"><i class="fa-solid fa-xmark"></i></button></div>' +
        '<div class="br-body" id="brBody"></div>' +
        '</div></div>'
    );
    $('#brClose').on('click', function () { $('#botReportModal').removeClass('active'); });
    $('#botReportModal').on('click', function (e) { if (e.target === this) $(this).removeClass('active'); });
    $(document).on('keydown', function (e) { if (e.key === 'Escape') $('#botReportModal').removeClass('active'); });

    // ===================== ADD BOTS MODAL (quick .bot addai spawner) =====================
    // Alliance classes only (no Shaman). Spawns via POST /Bots/AddBots → the server runs
    // `.bot addai <class>` once per entry over RA. Default preset = 2× each class, for fast reruns.
    var ADD_BOT_CLASSES = ['warrior', 'paladin', 'hunter', 'rogue', 'priest', 'mage', 'warlock', 'druid'];
    // Class color + iconography pulled from the subject's own world (vanilla WoW class colors).
    var ADD_BOT_META = {
        warrior: { color: '#C79C6E', icon: 'fa-shield-halved' },
        paladin: { color: '#F58CBA', icon: 'fa-hammer' },
        hunter: { color: '#ABD473', icon: 'fa-crosshairs' },
        rogue: { color: '#FFF569', icon: 'fa-user-ninja' },
        priest: { color: '#FFFFFF', icon: 'fa-hands-praying' },
        mage: { color: '#69CCF0', icon: 'fa-hat-wizard' },
        warlock: { color: '#9482C9', icon: 'fa-skull' },
        druid: { color: '#FF7D0A', icon: 'fa-paw' }
    };
    var addBotCounts = {};
    ADD_BOT_CLASSES.forEach(function (c) { addBotCounts[c] = 2; });

    $('head').append(
        '<style>' +
        '.ab-overlay{position:fixed;inset:0;background:rgba(10,11,20,0.62);backdrop-filter:blur(2px);display:none;align-items:center;justify-content:center;z-index:1000;}' +
        '.ab-overlay.active{display:flex;animation:abFade .14s ease;}' +
        '@keyframes abFade{from{opacity:0}to{opacity:1}}' +
        '@keyframes abPop{from{transform:translateY(8px) scale(.985);opacity:.5}to{transform:none;opacity:1}}' +
        '.ab-modal{background:var(--bg-card,#1a1b26);border:1px solid var(--border-light,#414868);border-radius:14px;width:92vw;max-width:540px;max-height:88vh;display:flex;flex-direction:column;box-shadow:0 24px 64px rgba(0,0,0,0.6);overflow:hidden;animation:abPop .16s cubic-bezier(.2,.8,.2,1);}' +
        '.ab-header{display:flex;align-items:center;justify-content:space-between;padding:16px 18px;border-bottom:1px solid var(--border-light,#414868);background:linear-gradient(135deg,rgba(122,162,247,0.14),rgba(187,154,247,0.05));}' +
        '.ab-hleft{display:flex;align-items:center;}' +
        '.ab-hicon{width:34px;height:34px;border-radius:9px;display:flex;align-items:center;justify-content:center;background:rgba(122,162,247,0.16);color:var(--accent,#7aa2f7);font-size:15px;margin-right:11px;}' +
        '.ab-htxt{display:flex;flex-direction:column;gap:2px;}' +
        '.ab-htxt b{font-size:15px;font-weight:700;letter-spacing:.2px;}' +
        '.ab-htxt span{font-size:11px;color:var(--text-muted,#787c99);}' +
        '.ab-close{background:none;border:none;color:var(--text-muted,#787c99);cursor:pointer;font-size:17px;line-height:1;padding:4px 6px;border-radius:6px;transition:all .12s;}' +
        '.ab-close:hover{color:var(--text-secondary,#c0caf5);background:rgba(255,255,255,0.06);}' +
        '.ab-body{padding:14px 18px;overflow-y:auto;}' +
        '.ab-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;}' +
        '.ab-card{position:relative;display:flex;align-items:center;justify-content:space-between;gap:8px;padding:10px 12px 10px 15px;border-radius:10px;border:1px solid var(--border-light,#414868);background:var(--bg-card-alt,#24283b);overflow:hidden;transition:opacity .14s,box-shadow .14s,border-color .14s;}' +
        '.ab-card::before{content:"";position:absolute;left:0;top:0;bottom:0;width:3px;background:var(--cc,#7aa2f7);opacity:.45;transition:opacity .14s;}' +
        '.ab-card.on{box-shadow:0 2px 14px rgba(0,0,0,0.28);}' +
        '.ab-card.on::before{opacity:1;}' +
        '.ab-card.off{opacity:.48;}' +
        '.ab-cname{display:flex;align-items:center;gap:9px;min-width:0;}' +
        '.ab-cname i{font-size:14px;width:18px;text-align:center;}' +
        '.ab-cname b{font-size:13px;font-weight:600;text-transform:capitalize;letter-spacing:.2px;}' +
        '.ab-step{display:inline-flex;align-items:center;gap:6px;flex:none;}' +
        '.ab-step button{width:24px;height:24px;border-radius:6px;border:1px solid var(--border-light,#414868);background:var(--bg-card,#1a1b26);color:var(--text-secondary,#c0caf5);cursor:pointer;font-size:14px;line-height:1;display:flex;align-items:center;justify-content:center;transition:all .12s;}' +
        '.ab-step button:hover{border-color:var(--cc,#7aa2f7);color:#fff;}' +
        '.ab-step .ab-cnt{min-width:18px;text-align:center;font-weight:700;font-size:13px;font-variant-numeric:tabular-nums;}' +
        '.ab-foot{display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-top:1px solid var(--border-light,#414868);gap:10px;background:rgba(0,0,0,0.12);}' +
        '.ab-presets{display:flex;gap:6px;}' +
        '.ab-preset{font-size:11px;padding:5px 10px;cursor:pointer;background:transparent;border:1px solid var(--border-light,#414868);border-radius:7px;color:var(--text-muted,#787c99);transition:all .12s;}' +
        '.ab-preset:hover{color:var(--text-secondary,#c0caf5);border-color:var(--accent,#7aa2f7);}' +
        '.ab-spawn{font-size:13px;font-weight:600;padding:9px 18px;border:none;border-radius:9px;cursor:pointer;color:#fff;display:inline-flex;align-items:center;gap:7px;background:linear-gradient(135deg,var(--accent,#7aa2f7),#bb9af7);box-shadow:0 4px 14px rgba(122,162,247,0.35);transition:transform .12s,box-shadow .12s,filter .12s;}' +
        '.ab-spawn:hover{transform:translateY(-1px);box-shadow:0 6px 20px rgba(122,162,247,0.45);}' +
        '.ab-spawn:active{transform:translateY(0);}' +
        '.ab-spawn:disabled{filter:grayscale(.4) brightness(.78);cursor:default;transform:none;box-shadow:none;}' +
        '.ab-tot{font-weight:700;font-variant-numeric:tabular-nums;}' +
        '</style>'
    );

    $('body').append(
        '<div class="ab-overlay" id="addBotsModal">' +
        '<div class="ab-modal">' +
        '<div class="ab-header">' +
        '<div class="ab-hleft"><div class="ab-hicon"><i class="fa-solid fa-user-plus"></i></div>' +
        '<div class="ab-htxt"><b>Add Bots</b><span>Quick-spawn alliance bots for a rerun</span></div></div>' +
        '<button class="ab-close" id="abClose"><i class="fa-solid fa-xmark"></i></button></div>' +
        '<div class="ab-body"><div class="ab-grid" id="abRows"></div></div>' +
        '<div class="ab-foot">' +
        '<div class="ab-presets">' +
        '<button class="ab-preset" data-ab-preset="0">Clear</button>' +
        '<button class="ab-preset" data-ab-preset="1">1× each</button>' +
        '<button class="ab-preset" data-ab-preset="2">2× each</button>' +
        '</div>' +
        '<button class="ab-spawn" id="abSpawn"><i class="fa-solid fa-bolt"></i> Spawn <span class="ab-tot" id="abTotal">0</span></button>' +
        '</div></div></div>'
    );

    function renderAddBotCards() {
        var html = '', total = 0;
        for (var i = 0; i < ADD_BOT_CLASSES.length; i++) {
            var c = ADD_BOT_CLASSES[i], n = addBotCounts[c] || 0, m = ADD_BOT_META[c];
            total += n;
            html += '<div class="ab-card ' + (n > 0 ? 'on' : 'off') + '" style="--cc:' + m.color + ';">' +
                '<span class="ab-cname"><i class="fa-solid ' + m.icon + '" style="color:' + m.color + ';"></i><b>' + c + '</b></span>' +
                '<span class="ab-step">' +
                '<button data-ab-dec="' + c + '">−</button>' +
                '<span class="ab-cnt">' + n + '</span>' +
                '<button data-ab-inc="' + c + '">+</button>' +
                '</span></div>';
        }
        $('#abRows').html(html);
        $('#abTotal').text(total);
        $('#abSpawn').prop('disabled', total === 0);
    }

    $('#btnAddBots').on('click', function () { renderAddBotCards(); $('#addBotsModal').addClass('active'); });
    $('#abClose').on('click', function () { $('#addBotsModal').removeClass('active'); });
    $('#addBotsModal').on('click', function (e) { if (e.target === this) $(this).removeClass('active'); });
    $(document).on('keydown', function (e) { if (e.key === 'Escape') $('#addBotsModal').removeClass('active'); });

    $(document).on('click', '[data-ab-inc]', function () {
        var c = $(this).attr('data-ab-inc'); addBotCounts[c] = Math.min(20, (addBotCounts[c] || 0) + 1); renderAddBotCards();
    });
    $(document).on('click', '[data-ab-dec]', function () {
        var c = $(this).attr('data-ab-dec'); addBotCounts[c] = Math.max(0, (addBotCounts[c] || 0) - 1); renderAddBotCards();
    });
    $(document).on('click', '[data-ab-preset]', function () {
        var v = parseInt($(this).attr('data-ab-preset'), 10) || 0;
        ADD_BOT_CLASSES.forEach(function (c) { addBotCounts[c] = v; });
        renderAddBotCards();
    });

    $('#abSpawn').on('click', function () {
        var classes = [];
        ADD_BOT_CLASSES.forEach(function (c) { for (var i = 0; i < (addBotCounts[c] || 0); i++) classes.push(c); });
        if (!classes.length) { showToast('Pick at least one bot', true); return; }
        var $btn = $(this).prop('disabled', true);
        $.ajax({ url: '/Bots/AddBots', method: 'POST', contentType: 'application/json', data: JSON.stringify({ classes: classes }) })
            .done(function (res) {
                if (res && res.success) showToast('Spawning ' + classes.length + ' bot(s) — ' + (res.sent != null ? res.sent : classes.length) + ' command(s) sent');
                else showToast('Add bots failed: ' + ((res && res.error) || 'unknown'), true);
                $('#addBotsModal').removeClass('active');
            })
            .fail(function (xhr) { showToast('Add Bots endpoint not wired yet (' + xhr.status + ')', true); })
            .always(function () { $btn.prop('disabled', false); });
    });

    $(document).on('click', '#btnBotReport', function (e) {
        e.stopPropagation();
        openBotReport();
    });

    function openBotReport() {
        var g = selectedGuid;
        var st = g ? botStates[g] : null;
        if (!st || !st.name) return;
        $('#brTitle').html('<i class="fa-solid fa-bolt"></i> Report — ' + esc(st.name));
        $('#brBody').html('<div class="br-loading"><i class="fa-solid fa-spinner fa-spin"></i> reading buffered log…</div>');
        $('#botReportModal').addClass('active');
        $.getJSON('/Bots/BotReport', { name: st.name }, function (data) {
            if (!data || data.error) { $('#brBody').html('<div class="br-loading">' + esc((data && data.error) || 'no data') + '</div>'); return; }
            $('#brBody').html(renderBotReport(data));
        }).fail(function () { $('#brBody').html('<div class="br-loading">request failed</div>'); });
    }

    function renderBotReport(d) {
        if (d.empty || !d.total) return '<div class="br-loading">log buffer empty for this bot</div>';
        var c = d.census || {};
        var h = d.health || {};

        var html = '<div class="br-meta">' + (d.botLines != null ? d.botLines : d.total) + ' bot lines' +
            (d.fleetLines ? ' · ' + d.fleetLines + ' fleet heartbeats folded out' : '') +
            ' · ' + (d.spanSec || 0) + 's window</div>';

        // Health banner — the two diagnostics proxies up top.
        var spiral = h.deathSpiral;
        html += '<div class="br-health' + (spiral ? ' bad' : '') + '">' +
            '<span><b>' + (h.killsVsCompletions || '—') + '</b> kills/credit</span>' +
            '<span>rez/kill <b>' + (h.rezPerKill != null ? h.rezPerKill : '—') + '</b></span>' +
            (spiral ? '<span class="br-flag">death spiral</span>' : '') +
            ((c.kills > 0 && c.completions === 0) ? '<span class="br-flag">kills, 0 credit</span>' : '') +
            '</div>';

        // Census grid — bounded, counts only.
        var cells = [
            ['kills', c.kills, '#9ece6a'], ['completions', c.completions, '#9ece6a'],
            ['rewarded', c.rewarded, '#9ece6a'], ['grind done', c.grindFinished, '#9ece6a'],
            ['level-ups', c.levelUps, '#bb9af7'], ['quest evts', c.questEvents, '#7aa2f7'],
            ['resurrects', c.resurrects, '#ff9e64'], ['deaths', c.deaths, '#f7768e'],
            ['relocates', c.relocates, '#ff9e64'], ['repairs', c.repairs, '#ff9e64'],
            ['sells', c.sells, '#ff9e64'], ['overflow', c.overflow, '#e0af68'],
            ['shelvings', c.shelvings, '#e0af68'], ['stalls', c.stalls, '#f7768e'],
            ['no-path', c.noPath, '#f7768e'], ['path-unsafe', c.pathUnsafe, '#f7768e']
        ];
        html += '<div class="br-grid">';
        for (var i = 0; i < cells.length; i++) {
            var n = cells[i][1] || 0;
            html += '<div class="br-cell"><div class="br-cell-n" style="color:' + (n > 0 ? cells[i][2] : 'var(--text-muted)') + ';">' +
                n + '</div><div class="br-cell-l">' + cells[i][0] + '</div></div>';
        }
        html += '</div>';

        // Top repeated signatures (≤12).
        if (d.top && d.top.length) {
            html += '<div class="br-sec-h">Top repeated lines</div><div class="br-top">';
            for (var t = 0; t < d.top.length; t++) {
                html += '<div class="br-top-row"><span class="br-top-n">' + d.top[t].n + '</span>' +
                    '<span class="br-top-sig">' + esc(d.top[t].sig) + '</span></div>';
            }
            html += '</div>';
        }
        return html;
    }

    // Open modal on double-click of roster card (single click keeps existing detail panel)
    $(document).on('dblclick', '.bt-roster-card', function () {
        var guid = parseInt($(this).data('guid'));
        openBotModal(guid);
    });

    // Details button in the bot header panel
    $(document).on('click', '.btnOpenModal', function (e) {
        e.stopPropagation();
        var guid = parseInt($(this).data('guid'));
        openBotModal(guid);
    });

    function openBotModal(guid) {
        var s = botStates[guid];
        if (!s) return;
        var brain = botBrains[guid];
        var className = CLASS_NAMES[s.classId] || '?';

        $('#bmTitle').html(
            '<span style="color:var(--text-primary);">' + esc(s.name) + '</span>' +
            ' <span class="bt-class-badge ' + (CLASS_CSS[s.classId] || '') + '" style="font-size:11px;">' + className + '</span>' +
            ' <span style="font-weight:400;font-size:12px;color:var(--text-muted);">L' + (s.level || 0) + '</span>'
        );
        $('#botModal').data('guid', guid).addClass('active');
        $('.bm-tab').first().click(); // load first tab
    }

    function loadModalTab(tab) {
        var guid = $('#botModal').data('guid');
        if (!guid) return;

        switch (tab) {
            case 'quests':
                loadQuestTab(guid);
                break;
            case 'gear':
                $('#bmBody').html('<div class="bq-loading"><i class="fa-solid fa-hammer" style="margin-right:6px;"></i>Coming soon</div>');
                break;
            case 'brain':
                $('#bmBody').html('<div class="bq-loading"><i class="fa-solid fa-hammer" style="margin-right:6px;"></i>Coming soon</div>');
                break;
        }
    }

    function loadQuestTab(guid) {
        var $body = $('#bmBody');
        $body.html('<div class="bq-loading"><i class="fa-solid fa-spinner fa-spin"></i> Loading quest status...</div>');

        // Always fetch fresh
        $.getJSON('/Bots/QuestStatus', { guid: guid }, function (data) {
            if (data.error) {
                $body.html('<div style="color:#f7768e;padding:16px;">' + esc(data.error) + '</div>');
                return;
            }
            questStatusCache[guid] = data;
            renderQuestTab(data);
        }).fail(function () {
            $body.html('<div style="color:#f7768e;padding:16px;">Failed to load quest status</div>');
        });
    }

    function renderQuestTab(data) {
        var $body = $('#bmBody');
        var quests = data.quests || [];

        if (quests.length === 0) {
            $body.html('<div style="color:var(--text-muted);padding:20px;text-align:center;">No quests in log</div>');
            return;
        }

        // Group by zone
        var zones = {};
        for (var i = 0; i < quests.length; i++) {
            var q = quests[i];
            var z = q.zone || 0;
            if (!zones[z]) zones[z] = [];
            zones[z].push(q);
        }

        // Sort zones: positive zones first (by ID), then negative (class quests)
        var zoneKeys = Object.keys(zones).sort(function (a, b) {
            var ai = parseInt(a), bi = parseInt(b);
            if (ai > 0 && bi > 0) return ai - bi;
            if (ai > 0) return -1;
            if (bi > 0) return 1;
            return ai - bi;
        });

        // Stats summary
        var rewarded = quests.filter(function (q) { return q.rewarded === 1; }).length;
        var active = quests.filter(function (q) { return q.status === 1 && q.rewarded === 0; }).length;
        var complete = quests.filter(function (q) { return q.status === 3 && q.rewarded === 0; }).length;

        var html = '<div style="display:flex;gap:16px;margin-bottom:14px;font-size:12px;">' +
            '<span style="color:#9ece6a;"><i class="fa-solid fa-circle-check" style="margin-right:4px;"></i>' + rewarded + ' rewarded</span>' +
            '<span style="color:#e0af68;"><i class="fa-solid fa-circle-check" style="margin-right:4px;"></i>' + complete + ' complete</span>' +
            '<span style="color:#7aa2f7;"><i class="fa-solid fa-spinner" style="margin-right:4px;"></i>' + active + ' active</span>' +
            '<span style="color:var(--text-muted);">' + quests.length + ' total</span>' +
            '</div>';

        for (var zi = 0; zi < zoneKeys.length; zi++) {
            var zoneId = zoneKeys[zi];
            var zoneQuests = zones[zoneId];
            var zoneName = ZONE_NAMES[zoneId] || ('Zone ' + zoneId);
            var zoneRewarded = zoneQuests.filter(function (q) { return q.rewarded === 1; }).length;

            html += '<div class="bq-zone-group">';
            html += '<div class="bq-zone-header"><i class="fa-solid fa-map-location-dot"></i> ' + esc(zoneName) +
                ' <span class="bq-zone-badge">' + zoneRewarded + '/' + zoneQuests.length + ' done</span></div>';

            for (var qi = 0; qi < zoneQuests.length; qi++) {
                var q = zoneQuests[qi];
                var statusColor = q.rewarded === 1 ? '#9ece6a' : (QUEST_STATUS_COLORS[q.status] || '#5f6b7a');
                var statusIcon = q.rewarded === 1 ? 'fa-circle-check' : (QUEST_STATUS_ICONS[q.status] || 'fa-circle-xmark');
                var statusText = q.rewarded === 1 ? 'Rewarded' : (QUEST_STATUS_NAMES[q.status] || 'Unknown');

                // Build progress string
                var progressHtml = '';
                if (q.rewarded === 1) {
                    progressHtml = '<span class="bq-rewarded"><i class="fa-solid fa-check"></i> Done</span>';
                } else if (q.status === 1 || q.status === 3) {
                    // Show mob/item progress
                    var parts = [];
                    for (var m = 0; m < 4; m++) {
                        if (q.mobRequired[m] > 0)
                            parts.push(q.mobCounts[m] + '/' + q.mobRequired[m] + ' kills');
                    }
                    for (var it = 0; it < 4; it++) {
                        if (q.itemRequired[it] > 0)
                            parts.push(q.itemCounts[it] + '/' + q.itemRequired[it] + ' items');
                    }
                    if (parts.length > 0) progressHtml = parts.join(', ');
                    else if (q.status === 3) progressHtml = '<span style="color:#e0af68;">Turn in</span>';
                    else progressHtml = '<span style="color:#7aa2f7;">Active</span>';
                }

                // Chain tags
                var tags = '';
                if (q.prevQuestId !== 0) tags += ' <span class="bq-chain-tag">req #' + Math.abs(q.prevQuestId) + '</span>';
                if (q.exclusiveGroup !== 0) tags += ' <span class="bq-excl-tag">excl ' + q.exclusiveGroup + '</span>';

                var rowId = 'bqr-' + data.guid + '-' + q.questId;
                var detId = 'bqd-' + data.guid + '-' + q.questId;

                html += '<div class="bq-quest-row" data-detail="' + detId + '" id="' + rowId + '">' +
                    '<div class="bq-status-icon"><i class="fa-solid ' + statusIcon + '" style="color:' + statusColor + ';"></i></div>' +
                    '<span class="bq-quest-title" style="color:' + (q.rewarded === 1 ? '#9ece6a' : 'var(--text-primary)') + ';">' +
                    '<span style="color:var(--text-muted);font-weight:400;font-size:11px;margin-right:4px;">[#' + q.questId + ']</span>' +
                    esc(q.title) + tags + '</span>' +
                    '<span class="bq-quest-level">L' + q.questLevel + '</span>' +
                    '<span class="bq-quest-progress">' + progressHtml + '</span>' +
                    '</div>';

                // Expandable detail
                html += '<div class="bq-detail" id="' + detId + '">';
                html += '<div><span class="bq-detail-label">Quest ID:</span> ' + q.questId + '</div>';
                if (q.giverName) html += '<div><span class="bq-detail-label">Given by:</span> ' + esc(q.giverName) + '</div>';
                if (q.turnInName) html += '<div><span class="bq-detail-label">Turn in:</span> ' + esc(q.turnInName) + '</div>';
                html += '<div><span class="bq-detail-label">Level:</span> ' + q.questLevel + ' (min ' + q.minLevel + ')</div>';
                html += '<div><span class="bq-detail-label">Status:</span> ' + statusText + ' (DB status=' + q.status + ', rewarded=' + q.rewarded + ')</div>';

                // Detailed objective progress bars
                for (var m = 0; m < 4; m++) {
                    if (q.mobRequired[m] > 0) {
                        var pct = Math.min(100, Math.round(q.mobCounts[m] / q.mobRequired[m] * 100));
                        var barColor = pct >= 100 ? '#9ece6a' : '#7aa2f7';
                        html += '<div class="bq-obj-row">' +
                            '<span>Kill slot ' + (m + 1) + ': ' + q.mobCounts[m] + '/' + q.mobRequired[m] + '</span>' +
                            '<div class="bq-obj-bar"><div class="bq-obj-fill" style="width:' + pct + '%;background:' + barColor + ';"></div></div>' +
                            '</div>';
                    }
                }
                for (var it = 0; it < 4; it++) {
                    if (q.itemRequired[it] > 0) {
                        var pct = Math.min(100, Math.round(q.itemCounts[it] / q.itemRequired[it] * 100));
                        var barColor = pct >= 100 ? '#9ece6a' : '#e0af68';
                        html += '<div class="bq-obj-row">' +
                            '<span>Item slot ' + (it + 1) + ': ' + q.itemCounts[it] + '/' + q.itemRequired[it] + '</span>' +
                            '<div class="bq-obj-bar"><div class="bq-obj-fill" style="width:' + pct + '%;background:' + barColor + ';"></div></div>' +
                            '</div>';
                    }
                }

                if (q.prevQuestId !== 0)
                    html += '<div><span class="bq-detail-label">Requires:</span> Quest #' + Math.abs(q.prevQuestId) + (q.prevQuestId > 0 ? ' (rewarded)' : ' (active)') + '</div>';
                if (q.exclusiveGroup !== 0)
                    html += '<div><span class="bq-detail-label">Exclusive Group:</span> ' + q.exclusiveGroup + ' — only one from this group can be active/completed</div>';

                html += '</div>';
            }
            html += '</div>';
        }

        $body.html(html);
    }

    // Toggle quest detail on click
    $(document).on('click', '.bq-quest-row', function () {
        var detId = $(this).data('detail');
        var $det = $('#' + detId);
        var wasVisible = $det.hasClass('visible');
        // Collapse all in this zone group, then toggle this one
        $(this).closest('.bq-zone-group').find('.bq-detail').removeClass('visible');
        $(this).closest('.bq-zone-group').find('.bq-quest-row').removeClass('expanded');
        if (!wasVisible) {
            $det.addClass('visible');
            $(this).addClass('expanded');
        }
    });

    // ===================== INIT =====================
    // Inject stack count badge styles
    $('<style>')
        .text(
            '.bt-inv-icon-wrap { position: relative; display: inline-block; }' +
            '.bt-inv-count { position: absolute; bottom: 0; right: 0; background: rgba(0,0,0,0.8);' +
            '  color: #fff; font-size: 10px; font-weight: 700; line-height: 1; padding: 1px 3px;' +
            '  border-radius: 3px; min-width: 14px; text-align: center; pointer-events: none; }'
        )
        .appendTo('head');

    // Live tab + detail tab styles
    $('<style>')
        .text(
            '.bt-detail-tabs { display:flex;gap:0;border-bottom:1px solid var(--border-light,#414868);position:sticky;top:0;background:var(--bg-card,#1a1b26);z-index:2; }' +
            '.bt-detail-tab { padding:9px 18px;font-size:12px;font-weight:600;color:var(--text-muted,#5f6b7a);cursor:pointer;border-bottom:2px solid transparent;text-transform:uppercase;letter-spacing:0.5px;user-select:none; }' +
            '.bt-detail-tab:hover { color:var(--text-secondary,#a9b1d6); }' +
            '.bt-detail-tab.active { color:var(--accent,#7aa2f7);border-bottom-color:var(--accent,#7aa2f7); }' +
            '.bt-live-dot { display:inline-block;width:6px;height:6px;border-radius:50%;background:#9ece6a;margin-left:6px;vertical-align:middle;animation:btLivePulse 1.6s ease-in-out infinite; }' +
            '@keyframes btLivePulse { 0%,100%{opacity:0.35;} 50%{opacity:1;} }' +
            '#detailTabBody { padding:14px 16px; }' +
            '.bt-live-banner { padding:10px 12px;background:var(--bg-card-alt,#24283b);border-radius:6px;margin-bottom:10px; }' +
            '.bt-live-goal { font-size:17px;font-weight:800;letter-spacing:0.3px; }' +
            '.bt-live-step { font-size:14px;font-weight:500;color:var(--text-secondary,#a9b1d6); }' +
            '.bt-live-why { font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11px;color:var(--text-muted,#5f6b7a);margin-top:3px; }' +
            '.bt-live-badges { margin-top:7px; }' +
            '.bt-live-stats { display:flex;gap:8px;margin-bottom:10px;flex-wrap:wrap; }' +
            '.bt-live-stat { flex:1;min-width:64px;background:var(--bg-card-alt,#24283b);border-radius:6px;padding:7px 4px;text-align:center; }' +
            '.bt-live-stat-val { font-size:16px;font-weight:800;line-height:1.1; }' +
            '.bt-live-stat-lbl { font-size:9px;text-transform:uppercase;letter-spacing:0.5px;color:var(--text-muted,#5f6b7a);margin-top:2px; }' +
            '.bt-live-card { background:var(--bg-card-alt,#24283b);border:1px solid var(--border-light,#414868);border-radius:6px;padding:9px 11px;margin-bottom:9px; }' +
            '.bt-live-card-h { font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;color:var(--text-secondary,#a9b1d6);margin-bottom:7px; }' +
            '.bt-live-card-h i { color:var(--accent,#7aa2f7);margin-right:5px; }' +
            '.bt-live-wait { font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12px;line-height:1.5; }' +
            '.bt-live-wait-cmd { color:var(--text-primary,#c0caf5);font-weight:700; }' +
            '.bt-live-wait-evt { color:#7aa2f7;font-weight:700; }' +
            '.bt-live-rows { display:flex;flex-direction:column;gap:3px; }' +
            '.bt-live-row { display:flex;justify-content:space-between;font-size:12px; }' +
            '.bt-live-row-lbl { color:var(--text-muted,#5f6b7a); }' +
            '.bt-live-row-val { font-family:ui-monospace,Menlo,Consolas,monospace;color:var(--text-primary,#c0caf5); }' +
            '.bt-live-chips, .bt-live-badges { display:flex;flex-wrap:wrap;gap:4px; }' +
            '.bt-live-qchip { font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11px;font-weight:700;padding:2px 8px;border-radius:10px; }' +
            '.bt-live-active { background:rgba(122,162,247,0.06);border:1px solid rgba(122,162,247,0.25);border-radius:5px;padding:8px 10px;margin-bottom:8px; }' +
            '.bt-live-active-t { font-size:13px;font-weight:700;color:var(--text-primary,#c0caf5);margin-bottom:6px; }' +
            '.bt-live-obj { font-size:12px;line-height:1.7;display:flex;align-items:center;gap:2px;flex-wrap:wrap; }' +
            '.bt-live-obj i { color:var(--text-muted,#5f6b7a); }' +
            '.bt-live-obj.active { background:rgba(224,175,104,0.10);border-radius:4px;padding:1px 5px;margin:1px -5px; }' +
            '.bt-live-obj.active i { color:#e0af68; }' +
            '.bt-live-obj-cnt { font-family:ui-monospace,Menlo,Consolas,monospace;font-weight:700; }' +
            '.bt-live-obj-name { color:var(--text-secondary,#a9b1d6); }' +
            '.bt-live-obj-dist { margin-left:auto;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11px;color:#7aa2f7;padding-left:8px; }' +
            '.bt-live-qlist { display:flex;flex-direction:column;gap:1px; }' +
            '.bt-live-qrow { display:flex;align-items:center;gap:6px;font-size:12px;padding:2px 4px;border-radius:3px; }' +
            '.bt-live-qrow.active { background:rgba(115,218,202,0.10); }' +
            '.bt-live-qid { font-family:ui-monospace,Menlo,Consolas,monospace;color:var(--text-muted,#5f6b7a);font-size:11px; }' +
            '.bt-live-qtitle { color:var(--text-secondary,#a9b1d6);white-space:nowrap;overflow:hidden;text-overflow:ellipsis; }' +
            '.bt-live-log { max-height:240px;overflow-y:auto;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11px;line-height:1.5;background:#000;border:1px solid var(--border-light,#414868);border-radius:4px;padding:6px 8px; }' +
            '.bt-live-logline { white-space:pre-wrap;word-break:break-word; }' +
            '.bt-live-logt { color:var(--text-muted,#5f6b7a);margin-right:6px; }' +
            '.bt-live-foot { font-size:10px;color:var(--text-muted,#5f6b7a);text-align:right;margin-top:2px; }' +
            '.bt-live-obj-srv { font-family:ui-monospace,Menlo,Consolas,monospace;font-size:9px;font-weight:700;color:#73daca;background:rgba(115,218,202,0.12);padding:0 4px;border-radius:3px;letter-spacing:0.5px; }' +
            '.bt-live-fleet-tag { font-size:9px;font-weight:700;color:#bb9af7;background:rgba(187,154,247,0.14);padding:0 4px;border-radius:3px;margin-right:4px;letter-spacing:0.5px; }' +
            '.bt-story { background:linear-gradient(180deg,rgba(122,162,247,0.08),rgba(122,162,247,0.02));border-color:rgba(122,162,247,0.30); }' +
            '.bt-story-lead { font-size:13px;line-height:1.55;color:var(--text-primary,#c0caf5); }' +
            '.bt-story-why { font-size:12px;line-height:1.55;color:var(--text-secondary,#a9b1d6);margin-top:5px; }' +
            '.bt-story-next { font-size:12px;line-height:1.5;color:var(--text-secondary,#a9b1d6);margin-top:7px; }' +
            '.bt-story-next-h { color:#bb9af7;font-weight:700;text-transform:uppercase;font-size:10px;letter-spacing:0.6px;margin-right:6px; }' +
            '.bt-story b { color:var(--text-primary,#c0caf5);font-weight:700; }' +
            '.bt-live-wait-dest { color:var(--text-primary,#c0caf5);font-weight:700; }' +
            '.bt-report-btn { float:right;font-size:10px;font-weight:700;cursor:pointer;background:rgba(187,154,247,0.12);border:1px solid rgba(187,154,247,0.35);border-radius:4px;color:#bb9af7;padding:1px 8px;text-transform:none;letter-spacing:0; }' +
            '.bt-report-btn:hover { background:rgba(187,154,247,0.22); }' +
            '.bt-report-btn i { color:#bb9af7;margin-right:3px; }' +
            '.br-overlay { position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.6);z-index:5500;display:none;align-items:center;justify-content:center; }' +
            '.br-overlay.active { display:flex; }' +
            '.br-modal { background:var(--bg-card,#1a1b26);border:1px solid var(--border-light,#414868);border-radius:10px;width:90vw;max-width:560px;max-height:86vh;display:flex;flex-direction:column;box-shadow:0 16px 48px rgba(0,0,0,0.55);overflow:hidden; }' +
            '.br-header { display:flex;align-items:center;justify-content:space-between;padding:12px 16px;border-bottom:1px solid var(--border-light,#414868);font-weight:700;font-size:14px;color:var(--text-primary,#c0caf5); }' +
            '.br-header i { color:#bb9af7;margin-right:6px; }' +
            '.br-close { background:none;border:none;color:var(--text-muted,#5f6b7a);font-size:16px;cursor:pointer; }' +
            '.br-close:hover { color:var(--text-primary,#c0caf5); }' +
            '.br-body { padding:14px 16px;overflow-y:auto; }' +
            '.br-loading { text-align:center;padding:34px;color:var(--text-muted,#5f6b7a);font-size:13px; }' +
            '.br-meta { font-size:11px;color:var(--text-muted,#5f6b7a);margin-bottom:10px;font-family:ui-monospace,Menlo,Consolas,monospace; }' +
            '.br-health { display:flex;gap:14px;align-items:center;flex-wrap:wrap;background:rgba(115,218,202,0.06);border:1px solid rgba(115,218,202,0.25);border-radius:6px;padding:9px 12px;margin-bottom:12px;font-size:12px;color:var(--text-secondary,#a9b1d6); }' +
            '.br-health.bad { background:rgba(247,118,142,0.07);border-color:rgba(247,118,142,0.35); }' +
            '.br-health b { color:var(--text-primary,#c0caf5); }' +
            '.br-flag { font-size:10px;font-weight:700;color:#ff9e64;background:rgba(255,158,100,0.15);padding:1px 8px;border-radius:10px; }' +
            '.br-grid { display:grid;grid-template-columns:repeat(4,1fr);gap:7px;margin-bottom:14px; }' +
            '.br-cell { background:var(--bg-card-alt,#24283b);border:1px solid var(--border-light,#414868);border-radius:6px;padding:8px 4px;text-align:center; }' +
            '.br-cell-n { font-size:18px;font-weight:800;line-height:1.1;font-family:ui-monospace,Menlo,Consolas,monospace; }' +
            '.br-cell-l { font-size:9px;text-transform:uppercase;letter-spacing:0.4px;color:var(--text-muted,#5f6b7a);margin-top:3px; }' +
            '.br-sec-h { font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;color:var(--text-secondary,#a9b1d6);margin:4px 0 7px; }' +
            '.br-top { display:flex;flex-direction:column;gap:2px; }' +
            '.br-top-row { display:flex;gap:8px;align-items:baseline;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:11px; }' +
            '.br-top-n { min-width:30px;text-align:right;color:#bb9af7;font-weight:700; }' +
            '.br-top-sig { color:var(--text-secondary,#a9b1d6);white-space:nowrap;overflow:hidden;text-overflow:ellipsis; }'
        )
        .appendTo('head');

    initConnection();
    setInterval(updateStats, 5000);
    startRosterPoll();

    // ===================== ITEM TOOLTIP =====================
    // Floating tooltip that appears on hover over inventory items

    var $tooltip = $('<div id="itemTooltip"></div>').css({
        position: 'fixed',
        display: 'none',
        zIndex: 9999,
        pointerEvents: 'none',
        background: 'linear-gradient(135deg, #1a1b26 0%, #24283b 100%)',
        border: '1px solid #414868',
        borderRadius: '6px',
        padding: '10px 14px',
        maxWidth: '280px',
        fontSize: '12px',
        lineHeight: '1.5',
        color: '#c0caf5',
        boxShadow: '0 8px 24px rgba(0,0,0,0.6)'
    }).appendTo('body');

    $(document).on('mouseenter', '.bt-inv-item', function (e) {
        var $el = $(this);
        var name = $el.data('tt-name') || '';
        var quality = parseInt($el.data('tt-quality')) || 0;
        var itemClass = parseInt($el.data('tt-class')) || 0;
        var subclass = parseInt($el.data('tt-subclass')) || 0;
        var invType = parseInt($el.data('tt-invtype')) || 0;
        var iLvl = parseInt($el.data('tt-ilvl')) || 0;
        var armor = parseInt($el.data('tt-armor')) || 0;
        var sellPrice = parseInt($el.data('tt-sell')) || 0;
        var isEquipped = $el.data('tt-equipped') === true || $el.data('tt-equipped') === 'true';
        var count = parseInt($el.data('tt-count')) || 1;

        var qColor = QUALITY_COLORS[quality] || '#fff';
        var qName = QUALITY_NAMES[quality] || '';

        var html = '<div style="font-size:14px;font-weight:700;color:' + qColor + ';margin-bottom:4px;">' + esc(name) + '</div>';

        // Stack count
        if (count > 1) html += '<div style="color:#c0caf5;">Count: ' + count + '</div>';

        // Item level
        if (iLvl > 0) html += '<div style="color:#e0af68;">Item Level ' + iLvl + '</div>';

        // Slot + type
        var slotName = EQUIP_SLOT_NAMES[invType] || '';
        var className = ITEM_CLASS_NAMES[itemClass] || '';
        if (slotName || className) {
            html += '<div style="display:flex;justify-content:space-between;color:var(--text-muted);">';
            if (slotName) html += '<span>' + slotName + '</span>';
            if (className) html += '<span>' + className + '</span>';
            html += '</div>';
        }

        // Armor
        if (armor > 0) html += '<div style="color:#c0caf5;">' + armor + ' Armor</div>';

        // Quality
        html += '<div style="color:' + qColor + ';font-size:11px;margin-top:4px;">' + qName + '</div>';

        // Sell price (total for stack)
        if (sellPrice > 0) {
            var totalSell = sellPrice * count;
            html += '<div style="color:var(--text-muted);font-size:11px;">Sell: ' + formatGold(totalSell) + (count > 1 ? ' (' + formatGold(sellPrice) + ' each)' : '') + '</div>';
        }

        if (isEquipped) html += '<div style="color:#9ece6a;font-size:11px;margin-top:2px;">Equipped</div>';

        $tooltip.html(html).show();
        positionTooltip(e);
    });

    $(document).on('mousemove', '.bt-inv-item', function (e) {
        positionTooltip(e);
    });

    $(document).on('mouseleave', '.bt-inv-item', function () {
        $tooltip.hide();
    });

    function positionTooltip(e) {
        var x = e.clientX + 16;
        var y = e.clientY + 12;
        var tw = $tooltip.outerWidth();
        var th = $tooltip.outerHeight();
        if (x + tw > window.innerWidth - 10) x = e.clientX - tw - 12;
        if (y + th > window.innerHeight - 10) y = e.clientY - th - 12;
        $tooltip.css({ left: x + 'px', top: y + 'px' });
    }
});