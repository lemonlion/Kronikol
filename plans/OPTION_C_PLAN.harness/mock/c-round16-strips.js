    // Round 16 (preview): a scenario's setup, taken out of its sequence diagram's source. The setup is the Setup frame
    // where the report keeps setup apart (partition ... Setup ... end, the SeparateSetup option), and otherwise
    // everything before the first When step: the Given steps and the calls they made. The participants only the setup
    // used go with it. A source with neither, or whose cut would leave a frame or a loop open, comes back as it was.
    // Note bodies are never read as statements.
    function stripSetup(source) {
        if (!source) return source;
        var lines = source.split('\n');
        var OPENS = /^(?:alt|opt|loop|par|break|critical|group|partition)\b/;
        var WHEN = /^hnote across <<stepDelimiter>>(?: #black:<color:white>|<<stepBody>>: )(?:<b>)?when\b/i;
        var CONTENT = /^(?:[A-Za-z0-9_]+\s*-|\[|note\b|hnote\b|rnote\b|==|\.\.\.|\|\|\||activate\b|deactivate\b|destroy\b|alt\b|opt\b|loop\b|par\b|break\b|critical\b|group\b|partition\b|end$|ref\b|delay\b)/;
        function noteStart(t) { return /^(?:note|hnote|rnote)\b/.test(t) && t.indexOf(':') < 0; }
        function noteEnd(t) { return /^end ?(?:note|hnote|rnote)$/.test(t) || t === 'endnote'; }
        // the statements outside note bodies and style blocks, with their line numbers
        var stmts = [], inNote = false, inStyle = false;
        for (var i = 0; i < lines.length; i++) {
            var t = lines[i].trim();
            if (inStyle) { if (/^<\/style>/.test(t)) inStyle = false; continue; }
            if (inNote) { if (noteEnd(t)) inNote = false; continue; }
            if (/^<style>/.test(t)) { inStyle = true; continue; }
            stmts.push({ i: i, t: t });
            if (noteStart(t)) inNote = true;
        }
        var drop = {}, cut = false, k, d;
        // the Setup frame, through the end that closes it
        for (k = 0; k < stmts.length; k++) {
            if (!/^partition\b.*\bSetup$/.test(stmts[k].t)) continue;
            d = 0;
            for (var j = k; j < stmts.length; j++) {
                if (OPENS.test(stmts[j].t)) d++;
                else if (stmts[j].t === 'end') d--;
                if (d === 0) break;
            }
            if (j >= stmts.length) return source;
            for (var n = stmts[k].i; n <= stmts[j].i; n++) drop[n] = true;
            cut = true;
            k = j;
        }
        if (!cut) {
            // everything from the first statement of the scenario to the first When step
            var first = -1, when = -1;
            for (k = 0; k < stmts.length; k++) {
                if (first < 0 && CONTENT.test(stmts[k].t)) first = k;
                if (WHEN.test(stmts[k].t)) { when = k; break; }
            }
            if (first < 0 || when <= first) return source;
            d = 0;
            for (k = first; k < when; k++) {
                if (OPENS.test(stmts[k].t)) d++;
                else if (stmts[k].t === 'end') { d--; if (d < 0) return source; }
            }
            if (d !== 0) return source;
            for (var m = stmts[first].i; m < stmts[when].i; m++) drop[m] = true;
        }
        var kept = lines.filter(function (l, x) { return !drop[x]; });
        // the participants nothing left calls, declared, go too, as long as one that is called stays
        var used = {}, anyCall = false, inN = false;
        kept.forEach(function (l) {
            var t = l.trim();
            if (inN) { if (noteEnd(t)) inN = false; return; }
            if (noteStart(t)) inN = true;
            var a = /^([A-Za-z0-9_]+)\s+-(?:\[[^\]]*\])?-?>+\s*([A-Za-z0-9_]+)/.exec(t);
            if (a) { used[a[1]] = used[a[2]] = true; anyCall = true; return; }
            var o = /^(?:h|r)?note\b.*?\b(?:over|of)\s+([A-Za-z0-9_]+)(?:\s*,\s*([A-Za-z0-9_]+))?/.exec(t);
            if (o) { used[o[1]] = true; if (o[2]) used[o[2]] = true; }
            var act = /^(?:activate|deactivate)\s+([A-Za-z0-9_]+)/.exec(t);
            if (act) used[act[1]] = true;
        });
        if (anyCall) {
            var DECL = /^(?:participant|actor|boundary|control|entity|database|collections|queue)\s+(?:"[^"]*"|\S+)(?:\s+as\s+([A-Za-z0-9_]+))?/;
            kept = kept.filter(function (l) {
                var dm = DECL.exec(l.trim());
                if (!dm) return true;
                var alias = dm[1] || (/^\S+\s+"?([^"\s]+)/.exec(l.trim()) || [])[1];
                return !alias || used[alias];
            });
        }
        return kept.join('\n');
    }
