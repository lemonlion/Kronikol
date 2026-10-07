#:package Microsoft.Playwright@1.59.0
#:property PublishAot=false
// What the live BreakfastProvider xUnit report paints for its marked scenarios (plans/WARM_UP_PLAN.md 6.6, S7 after the
// pin move): each marked badge's text, its note's computed opacity, its tooltip, its colour class, and the shaded share
// of its timeline bar against the warm-up's share of the duration.
using Microsoft.Playwright;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
await page.GotoAsync("https://lemonlion.github.io/BreakfastProvider/reports/xunit/TestRunReport.html");
await page.Locator("details.feature").First.WaitForAsync();
await page.Locator(".timeline-toggle").First.ClickAsync();
await page.Locator("#scenario-timeline").WaitForAsync();

var rows = await page.EvaluateAsync<string[]>("""
    () => {
        const out = [];
        for (const s of document.querySelectorAll('details.scenario[data-warmup-ms]')) {
            const badge = s.querySelector(':scope > summary .duration-badge');
            const note = badge.querySelector('.warm-up-note');
            const name = s.querySelector(':scope > summary .copy-scenario-name').getAttribute('data-scenario-name');
            const label = [...document.querySelectorAll('#scenario-timeline .timeline-row')]
                .find(r => r.querySelector('.timeline-label').textContent === name);
            const bar = label ? label.querySelector('.timeline-bar') : null;
            const shade = bar ? bar.querySelector('.timeline-warm-up') : null;
            const share = shade ? (shade.getBoundingClientRect().width / bar.getBoundingClientRect().width) : -1;
            const want = Number(s.dataset.warmupMs) / Number(s.dataset.durationMs);
            out.push(`${badge.textContent} | ${badge.className} | note opacity ${getComputedStyle(note).opacity} | shade ${share.toFixed(3)} vs ${want.toFixed(3)} | ${badge.title}`);
        }
        return out;
    }
    """);
Console.WriteLine($"{rows.Length} marked scenarios painted");
foreach (var row in rows)
    Console.WriteLine(row.Length > 260 ? row[..260] + "..." : row);
