using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Showcase report GIF generator. Drives through a complete report capturing numbered PNG frames
/// for a showcase demo animation. Skipped in normal test runs.
/// Run: dotnet test Kronikol.Tests.EndToEnd --filter "ShowcaseReportTests"
/// Output goes to PlaywrightOutput/showcase-frames/
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class ShowcaseReportTests : PlaywrightTestBase
{
    private readonly string _framesDir;
    private int _frameNumber;

    public ShowcaseReportTests(PlaywrightFixture fixture) : base(fixture)
    {
        _framesDir = Path.Combine(OutputDir, "showcase-frames");
        Directory.CreateDirectory(_framesDir);
    }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    // ── Screenshot helpers ──

    private async Task CaptureFrame(string label)
    {
        _frameNumber++;
        var fileName = $"{_frameNumber:D3}_{label}.png";
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_framesDir, fileName) });
    }

    private async Task CaptureFrames(string label, int count, int delayMs = 150)
    {
        for (var i = 0; i < count; i++)
        {
            await CaptureFrame($"{label}_{i:D2}");
            await Task.Delay(delayMs);
        }
    }

    // ── Playwright helpers ──

    private async Task ScrollTo(string selector)
    {
        await Page.EvaluateAsync("""
            (sel) => {
                var el = document.querySelector(sel);
                if (!el) return;
                var rect = el.getBoundingClientRect();
                var targetY = window.scrollY + rect.top - (window.innerHeight / 2) + (rect.height / 2);
                var startY = window.scrollY;
                var distance = targetY - startY;
                var duration = Math.min(1200, Math.max(400, Math.abs(distance) * 0.6));
                var startTime = performance.now();
                function ease(t) { return t < 0.5 ? 4*t*t*t : 1 - Math.pow(-2*t+2, 3)/2; }
                function step(now) {
                    var elapsed = now - startTime;
                    var t = Math.min(elapsed / duration, 1);
                    window.scrollTo(0, startY + distance * ease(t));
                    if (t < 1) requestAnimationFrame(step);
                }
                requestAnimationFrame(step);
            }
        """, selector);
        await WaitForScrollIdle();
    }

    private async Task JsClick(string selector)
    {
        await MoveCursorTo(selector);
        await Page.EvaluateAsync("sel => document.querySelector(sel)?.click()", selector);
    }

    private async Task InjectFakeCursor()
    {
        await Page.EvaluateAsync("""
            (() => {
                if (document.getElementById('_fakeCursor')) return;
                var c = document.createElement('div');
                c.id = '_fakeCursor';
                c.innerHTML = `<svg width='24' height='24' viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'>
                    <path d='M5 3 L5 20 L10 15 L16 21 L19 18 L13 12 L19 12 Z'
                          fill='white' stroke='black' stroke-width='1.5' stroke-linejoin='round'/>
                </svg>`;
                c.style.cssText = 'position:fixed;top:-50px;left:-50px;z-index:99999;pointer-events:none;filter:drop-shadow(1px 2px 2px rgba(0,0,0,0.3));transition:none;';
                document.body.appendChild(c);
            })()
        """);
    }

    private async Task MoveCursorTo(string selector)
    {
        await Page.EvaluateAsync("""
            (sel) => {
                var el = document.querySelector(sel);
                var c = document.getElementById('_fakeCursor');
                if (!el || !c) return;
                var rect = el.getBoundingClientRect();
                var tx = rect.left + Math.min(rect.width * 0.3, 60);
                var ty = rect.top + rect.height / 2;
                var cx = parseFloat(c.style.left) || -50;
                var cy = parseFloat(c.style.top) || -50;
                var dist = Math.sqrt((tx-cx)*(tx-cx) + (ty-cy)*(ty-cy));
                var duration = Math.min(500, Math.max(150, dist * 0.8));
                var start = performance.now();
                function ease(t) { return t < 0.5 ? 2*t*t : 1 - Math.pow(-2*t+2, 2)/2; }
                function step(now) {
                    var t = Math.min((now - start) / duration, 1);
                    var p = ease(t);
                    c.style.left = (cx + (tx - cx) * p) + 'px';
                    c.style.top = (cy + (ty - cy) * p) + 'px';
                    if (t < 1) requestAnimationFrame(step);
                }
                requestAnimationFrame(step);
            }
        """, selector);
        await Task.Delay(350);
    }

    private async Task HideCursor()
    {
        await Page.EvaluateAsync("var c = document.getElementById('_fakeCursor'); if (c) c.style.display = 'none';");
    }

    private async Task ShowCursor()
    {
        await Page.EvaluateAsync("var c = document.getElementById('_fakeCursor'); if (c) c.style.display = '';");
    }

    private async Task ScrollToTop()
    {
        await Page.EvaluateAsync("""
            (() => {
                var startY = window.scrollY;
                var duration = Math.min(1200, Math.max(400, startY * 0.6));
                var startTime = performance.now();
                function ease(t) { return t < 0.5 ? 4*t*t*t : 1 - Math.pow(-2*t+2, 3)/2; }
                function step(now) {
                    var elapsed = now - startTime;
                    var t = Math.min(elapsed / duration, 1);
                    window.scrollTo(0, startY * (1 - ease(t)));
                    if (t < 1) requestAnimationFrame(step);
                }
                requestAnimationFrame(step);
            })()
        """);
        await WaitForScrollIdle();
    }

    private async Task WaitForScrollIdle(int maxMs = 3000)
    {
        var lastY = await Page.EvaluateAsync<double>("window.scrollY");
        var stableCount = 0;
        var elapsed = 0;
        const int poll = 25;
        while (elapsed < maxMs)
        {
            await Task.Delay(poll);
            elapsed += poll;
            var y = await Page.EvaluateAsync<double>("window.scrollY");
            if (Math.Abs(y - lastY) < 0.5) { stableCount++; if (stableCount >= 6) return; }
            else stableCount = 0;
            lastY = y;
        }
    }

    // ── Rich test data ──

    private static readonly string OrderDiagramSource = """
        @startuml
        !pragma teoz true
        skinparam wrapWidth 800
        autonumber 1

        actor "Test" as test
        participant "Orders API" as orders
        participant "Stock Service" as stock
        database "Cosmos DB" as cosmos
        queue "EventGrid" as events

        test -> orders : POST /api/orders
        note left
        Content-Type: application/json
        {
          "productId": "SKU-42",
          "quantity": 3,
          "customerId": "cust-1234"
        }
        end note

        orders -> stock : PUT /stock/reserve
        note left
        {"sku":"SKU-42","qty":3}
        end note
        stock --> orders : 200 OK

        orders -> cosmos : Create Item
        note left
        Container: orders
        {"id":"ord-7f3a","status":"created"}
        end note
        cosmos --> orders : 201

        orders -> events : OrderCreated
        note left #LightBlue
        Topic: order-events
        {"orderId":"ord-7f3a","type":"OrderCreated"}
        end note

        orders --> test : 201 Created
        note left
        {
          "orderId": "ord-7f3a",
          **"status": "Confirmed"**,
          **"totalAmount": 250.50**,
          "currency": "GBP"
        }
        end note
        @enduml
        """;

    private static readonly string PaymentDiagramSource = """
        @startuml
        !pragma teoz true
        skinparam wrapWidth 800
        autonumber 1

        actor "Test" as test
        participant "Payment API" as pay
        participant "Stripe Gateway" as stripe
        database "SQL Server" as sql

        test -> pay : POST /api/payments
        note left
        {"orderId":"ord-7f3a","amount":250.50,"currency":"GBP"}
        end note

        pay -> stripe : POST /v1/charges
        note left
        {"amount":25050,"currency":"gbp"}
        end note
        stripe --> pay : 200 OK
        note right
        {"chargeId":"ch_abc123","status":"succeeded"}
        end note

        pay -> sql : INSERT INTO Payments
        sql --> pay : OK

        pay --> test : 200 OK
        note left
        {"paymentId":"pay-001","status":"completed"}
        end note
        @enduml
        """;

    private static readonly string FailureDiagramSource = """
        @startuml
        !pragma teoz true
        skinparam wrapWidth 800
        autonumber 1

        actor "Test" as test
        participant "Orders API" as orders
        participant "Stock Service" as stock

        test -> orders : POST /api/orders
        note left
        {"productId":"SKU-99","quantity":1000}
        end note

        orders -> stock : PUT /stock/reserve
        stock --> orders : 409 Conflict
        note right #Pink
        {"error":"Insufficient stock","available":12}
        end note

        orders --> test : 409 Conflict
        note left #Pink
        {"error":"Cannot fulfil order","code":"STOCK_INSUFFICIENT"}
        end note
        @enduml
        """;

    private (Feature[] Features, DiagramAsCode[] Diagrams) CreateShowcaseData()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Management",
                Description = "End-to-end order lifecycle from creation to fulfilment",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "order-1", DisplayName = "Creating an order reserves stock and publishes event",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(342),
                        Categories = ["Smoke", "Orders"],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the Orders API is running", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(12) },
                            new ScenarioStep { Keyword = "And", Text = "Stock Service has 50 units of SKU-42", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(8) },
                            new ScenarioStep { Keyword = "When", Text = "I create an order for 3 units of SKU-42", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(287) },
                            new ScenarioStep { Keyword = "Then", Text = "the order is confirmed with status Created", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(15) },
                            new ScenarioStep { Keyword = "And", Text = "an OrderCreated event is published", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(20) }
                        ]
                    },
                    new Scenario
                    {
                        Id = "order-2", DisplayName = "Insufficient stock returns 409 Conflict",
                        IsHappyPath = false, Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromMilliseconds(1204),
                        Categories = ["Orders", "ErrorHandling"],
                        ErrorMessage = "Expected status code 409, but got 500.\nResponse body: {\"error\":\"Internal Server Error\"}",
                        ErrorStackTrace = "   at FluentAssertions.Execution.LateBoundTestFramework.Throw(String message)\n   at OrderTests.Insufficient_stock_returns_409() in /src/Tests/OrderTests.cs:line 87",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the Orders API is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "And", Text = "Stock Service has 12 units of SKU-99", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I create an order for 1000 units of SKU-99", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "Then", Text = "a 409 Conflict is returned", Status = ExecutionResult.Skipped }
                        ]
                    },
                    new Scenario
                    {
                        Id = "order-3", DisplayName = "Listing orders returns paginated results",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(189),
                        Categories = ["Smoke", "Orders"]
                    },
                    new Scenario
                    {
                        Id = "order-param-1", DisplayName = "Order validation(Amount: 100, Currency: GBP, Region: EU)",
                        OutlineId = "Order validation", IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(34),
                        Categories = ["Orders", "Validation"],
                        ExampleValues = new() { ["Amount"] = "100.00", ["Currency"] = "GBP", ["Region"] = "EU" }
                    },
                    new Scenario
                    {
                        Id = "order-param-2", DisplayName = "Order validation(Amount: 250.50, Currency: USD, Region: US)",
                        OutlineId = "Order validation", IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(28),
                        Categories = ["Orders", "Validation"],
                        ExampleValues = new() { ["Amount"] = "250.50", ["Currency"] = "USD", ["Region"] = "US" }
                    },
                    new Scenario
                    {
                        Id = "order-param-3", DisplayName = "Order validation(Amount: 999.99, Currency: EUR, Region: APAC)",
                        OutlineId = "Order validation", IsHappyPath = false, Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromMilliseconds(1102),
                        Categories = ["Orders", "Validation"],
                        ExampleValues = new() { ["Amount"] = "999.99", ["Currency"] = "EUR", ["Region"] = "APAC" },
                        ErrorMessage = "Expected 200 OK but got 502 Bad Gateway"
                    },
                    new Scenario
                    {
                        Id = "order-param-4", DisplayName = "Order validation(Amount: 0.01, Currency: JPY, Region: APAC)",
                        OutlineId = "Order validation", IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(31),
                        Categories = ["Orders", "Validation"],
                        ExampleValues = new() { ["Amount"] = "0.01", ["Currency"] = "JPY", ["Region"] = "APAC" }
                    }
                ]
            },
            new Feature
            {
                DisplayName = "Payment Processing",
                Description = "Stripe integration for payment capture and refunds",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "pay-1", DisplayName = "Processing a payment charges Stripe and records transaction",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(456),
                        Categories = ["Smoke", "Payments"],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the Payment API is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I process a payment of £250.50", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "Stripe is charged successfully", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "And", Text = "the transaction is recorded in SQL Server", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "pay-2", DisplayName = "Refund reverses the charge",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(312),
                        Categories = ["Payments"]
                    },
                    new Scenario
                    {
                        Id = "pay-3", DisplayName = "Payment timeout retries three times",
                        IsHappyPath = false, Result = ExecutionResult.Skipped,
                        Duration = TimeSpan.FromMilliseconds(50),
                        Categories = ["Payments", "ErrorHandling"]
                    }
                ]
            },
            new Feature
            {
                DisplayName = "User Authentication",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "auth-1", DisplayName = "Login with valid credentials returns JWT",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(95),
                        Categories = ["Smoke", "Auth"]
                    },
                    new Scenario
                    {
                        Id = "auth-2", DisplayName = "Login with invalid credentials returns 401",
                        IsHappyPath = false, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(78),
                        Categories = ["Auth", "ErrorHandling"]
                    },
                    new Scenario
                    {
                        Id = "auth-3", DisplayName = "Expired token is rejected with 403",
                        IsHappyPath = false, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(62),
                        Categories = ["Auth"]
                    }
                ]
            },
            new Feature
            {
                DisplayName = "Inventory Tracking",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "inv-1", DisplayName = "Stock level decreases after order",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(220),
                        Categories = ["Inventory"]
                    },
                    new Scenario
                    {
                        Id = "inv-2", DisplayName = "Restock event increases available units",
                        IsHappyPath = true, Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(145),
                        Categories = ["Inventory"]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DiagramAsCode("order-1", "", OrderDiagramSource),
            new DiagramAsCode("order-2", "", FailureDiagramSource),
            new DiagramAsCode("order-param-1", "", OrderDiagramSource),
            new DiagramAsCode("order-param-2", "", OrderDiagramSource),
            new DiagramAsCode("order-param-3", "", FailureDiagramSource),
            new DiagramAsCode("order-param-4", "", OrderDiagramSource),
            new DiagramAsCode("pay-1", "", PaymentDiagramSource),
        };

        return (features, diagrams);
    }

    private string GenerateShowcaseReport()
    {
        var (features, diagrams) = CreateShowcaseData();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow,
            null,
            Path.Combine(TempDir, "ShowcaseReport.html"),
            "E\u2011Commerce Service Specifications",
            includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true,
            maxParameterColumns: 10,
            showStepNumbers: true);

        var outPath = Path.Combine(OutputDir, "ShowcaseReport.html");
        File.Copy(path, outPath, true);
        return new Uri(path).AbsoluteUri;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  THE SHOWCASE — drives through every feature capturing PNGs
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF/screenshot generation — unskip when regenerating showcase assets, then re-skip afterwards")]
    public async Task Showcase_drives_through_report_features_capturing_frames()
    {
        foreach (var old in Directory.GetFiles(_framesDir, "*.png"))
            File.Delete(old);

        var reportUrl = GenerateShowcaseReport();
        await Page.GotoAsync(reportUrl);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("document.documentElement.style.scrollBehavior='smooth';");
        await InjectFakeCursor();
        await Task.Delay(500);

        // ── Scene 1: Full report overview (collapsed) ──
        await ScrollToTop();
        await CaptureFrames("01_overview", 48, 33);

        // ── Scene 2: Expand everything ──
        // 5.0.0: an expand button per level (plans/OPTION_C_PLAN.md S4)
        try { await ExpandAllFeaturesAsync(); await Task.Delay(300); }
        catch { }
        try
        {
            await ExpandAllScenariosAsync();
            await Task.Delay(400);
        }
        catch { }
        await CaptureFrames("02_expanded", 36, 33);

        // ── Scene 3: Test execution summary ──
        await ScrollTo(".run-band");
        await CaptureFrames("03_summary", 48, 33);

        // ── Scene 4: First scenario's BDD steps ──
        await ScrollTo("details.scenario");
        await CaptureFrames("04_scenarios_steps", 60, 33);

        // ── Scene 5: Sequence diagram ──
        try
        {
            await Page.EvaluateAsync(
                "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
            await Page.Locator("[data-diagram-type='plantuml'] svg").First.WaitForAsync(new() { Timeout = 30000 });
            await ScrollTo("[data-diagram-type='plantuml']");
            await Task.Delay(500);
            await CaptureFrames("05_diagram", 72, 33);
        }
        catch
        {
            await CaptureFrames("05_diagram_loading", 48, 33);
        }

        // ── Scene 6: Failed scenario with error ──
        var failedCount = await Page.Locator("details.scenario[data-status='Failed']").CountAsync();
        if (failedCount > 0)
        {
            await ScrollTo("details.scenario[data-status='Failed']");
            await Task.Delay(500);
            await CaptureFrames("06_failed_scenario", 84, 33);
        }

        // ── Scene 7: Parameterized test table ──
        var paramCount = await Page.Locator(".param-test-table").CountAsync();
        if (paramCount > 0)
        {
            await ScrollTo(".param-test-table");
            await Task.Delay(300);
            await CaptureFrames("07_param_table", 48, 33);

            // Click passed row
            await Page.EvaluateAsync("""
                (() => { var r = document.querySelector('.param-test-table tbody tr.row-passed'); if (r) r.click(); })()
            """);
            await Task.Delay(400);
            await CaptureFrames("07_param_row_passed", 36, 33);

            // Click failed row
            await Page.EvaluateAsync("""
                (() => { var r = document.querySelector('.param-test-table tbody tr.row-failed'); if (r) r.click(); })()
            """);
            await Task.Delay(500);
            await CaptureFrames("07_param_row_failed", 60, 33);
        }

        // ── Scene 8: Search ──
        await ScrollToTop();
        await Task.Delay(200);
        await MoveCursorTo("#searchbar");
        await Page.Locator("#searchbar").FillAsync("");
        await HideCursor();

        var query = "$failed && order";
        for (var i = 0; i < query.Length; i++)
        {
            await Page.Locator("#searchbar").PressSequentiallyAsync(query[i].ToString(), new() { Delay = 0 });
            await Task.Delay(80);
            if (_frameNumber % 2 == 0) await CaptureFrame("08_search_typing");
        }
        await Task.Delay(500);
        await CaptureFrames("08_search_result", 48, 33);
        await ShowCursor();

        // ── Scene 9: OR search ──
        await HideCursor();
        await Page.Locator("#searchbar").FillAsync("payment || auth");
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
        await Task.Delay(500);
        await CaptureFrames("09_search_or", 36, 33);
        await ShowCursor();

        // ── Scene 10: Category filter toggle ──
        await Page.Locator("#searchbar").FillAsync("");
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
        await Task.Delay(300);

        var catCount = await Page.Locator(".category-toggle").CountAsync();
        if (catCount > 0)
        {
            await ScrollTo(".category-toggle");
            await Task.Delay(200);

            var smokeCount = await Page.Locator(".category-toggle:has-text('Smoke')").CountAsync();
            if (smokeCount > 0)
            {
                await JsClick(".category-toggle[data-category='Smoke']");
                await Task.Delay(400);
                await CaptureFrames("10_category_smoke", 48, 33);
            }

            var errorCount = await Page.Locator(".category-toggle:has-text('Error')").CountAsync();
            if (errorCount > 0)
            {
                await Page.EvaluateAsync("""
                    (() => {
                        var t = document.querySelectorAll('.category-toggle');
                        for (var c of t) { if (c.textContent.includes('Error')) { c.click(); break; } }
                    })()
                """);
                await Task.Delay(400);
            }
            await CaptureFrames("10_category_multi", 36, 33);

            await Page.EvaluateAsync("""
                (() => {
                    var b = document.querySelectorAll('.export-btn');
                    for (var c of b) { if (c.textContent.includes('Clear')) { c.click(); break; } }
                })()
            """);
            await Task.Delay(300);
        }

        // ── Scene 11: Status filter — show only failed ──
        var failedToggle = Page.Locator(".status-toggle[data-status='Failed']");
        if (await failedToggle.CountAsync() > 0)
        {
            await ScrollTo(".status-toggle[data-status='Failed']");
            await JsClick(".status-toggle[data-status='Failed']");
            await Task.Delay(800);
            await CaptureFrames("11_status_failed", 72, 33);

            await Page.EvaluateAsync("""
                (() => {
                    var b = document.querySelectorAll('.export-btn');
                    for (var c of b) { if (c.textContent.includes('Clear')) { c.click(); break; } }
                })()
            """);
            await Task.Delay(500);
        }

        // ── Scene 12: Happy path toggle ──
        var hpToggle = Page.Locator(".filtering-box .happy-seg[data-hp='in']");
        if (await hpToggle.CountAsync() > 0)
        {
            await ScrollTo(".filtering-box .happy-seg[data-hp='in']");
            await JsClick(".filtering-box .happy-seg[data-hp='in']");
            await Task.Delay(500);
            await CaptureFrames("12_happy_path", 48, 33);
            await JsClick(".filtering-box .happy-seg[data-hp='off']");
            await Task.Delay(300);
        }

        // ── Scene 13: Search help panel ──
        var helpToggle = Page.Locator(".search-help-toggle");
        if (await helpToggle.CountAsync() > 0)
        {
            await ScrollTo(".search-help-toggle");
            await JsClick(".search-help-toggle");
            await Task.Delay(400);
            await CaptureFrames("13_search_help", 48, 33);
            await JsClick(".search-help-toggle");
            await Task.Delay(200);
        }

        // ── Final ──
        await ScrollToTop();
        await Task.Delay(300);
        await CaptureFrames("14_final", 36, 33);

        var frameCount = Directory.GetFiles(_framesDir, "*.png").Length;
        Assert.True(frameCount >= 50, $"Expected at least 50 frames but captured {frameCount}");
    }
}
