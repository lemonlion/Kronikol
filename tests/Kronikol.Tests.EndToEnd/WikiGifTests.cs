using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Generates animated GIFs and screenshots for the wiki pages.
/// Run: dotnet test Kronikol.Tests.EndToEnd --filter "WikiGifTests"
/// Output goes to PlaywrightOutput/wiki-gifs/
///
/// Requirements: Chromium (Playwright), ImageMagick (magick on PATH)
/// </summary>
[Collection(PlaywrightCollections.Scenarios)]
public class WikiGifTests : PlaywrightTestBase
{
    private readonly string _wikiGifDir;
    private int _frameNumber;
    private string _currentFrameDir = "";

    public WikiGifTests(PlaywrightFixture fixture) : base(fixture)
    {
        _wikiGifDir = Path.Combine(OutputDir, "wiki-gifs");
        Directory.CreateDirectory(_wikiGifDir);
    }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    // ═══════════════════════════════════════════════════════════════════
    //  FRAME CAPTURE & GIF STITCHING
    // ═══════════════════════════════════════════════════════════════════

    private string BeginFrameCapture(string name)
    {
        _frameNumber = 0;
        _currentFrameDir = Path.Combine(_wikiGifDir, $"_frames_{name}");
        if (Directory.Exists(_currentFrameDir))
            foreach (var f in Directory.GetFiles(_currentFrameDir, "*.png")) File.Delete(f);
        Directory.CreateDirectory(_currentFrameDir);
        return _currentFrameDir;
    }

    private async Task CaptureFrame()
    {
        _frameNumber++;
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_currentFrameDir, $"{_frameNumber:D4}.png") });
    }

    private async Task CaptureFrames(int count, int delayMs = 33)
    {
        for (var i = 0; i < count; i++)
        {
            await CaptureFrame();
            await Task.Delay(delayMs);
        }
    }

    private async Task Hold(double seconds) => await CaptureFrames((int)(seconds * 30));

    private void StitchGif(string outputName)
    {
        var outputPath = Path.Combine(_wikiGifDir, outputName);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "magick",
            Arguments = $"-delay 3 -loop 0 \"{_currentFrameDir}\\*.png\" -resize 960x -fuzz 2% -layers optimize \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit(600_000);
        Assert.True(File.Exists(outputPath), $"GIF not created: {outputPath}");
    }

    private async Task SaveScreenshot(string name) =>
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_wikiGifDir, name) });

    // ═══════════════════════════════════════════════════════════════════
    //  PLAYWRIGHT HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private async Task TypeSlowly(string selector, string text, int charDelayMs = 60)
    {
        var locator = Page.Locator(selector);
        foreach (var ch in text)
        {
            await locator.PressSequentiallyAsync(ch.ToString(), new() { Delay = 0 });
            await CaptureFrame();
            await Task.Delay(charDelayMs);
        }
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
        await CaptureFrames(10, 33);
    }

    private async Task HideCursor()
    {
        await Page.EvaluateAsync(
            "var c = document.getElementById('_fakeCursor'); if (c) c.style.display = 'none';");
    }

    private async Task ShowCursor()
    {
        await Page.EvaluateAsync(
            "var c = document.getElementById('_fakeCursor'); if (c) c.style.display = '';");
    }

    private async Task JsClick(string selector)
    {
        await MoveCursorTo(selector);
        await Page.EvaluateAsync("sel => document.querySelector(sel)?.click()", selector);
    }

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
        await CaptureFrames(20, 33);
        await WaitForScrollIdle();
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
        await CaptureFrames(20, 33);
        await WaitForScrollIdle();
    }

    private async Task WaitForScrollIdle(int maxMs = 3000)
    {
        var lastY = await Page.EvaluateAsync<double>("window.scrollY");
        var stableCount = 0;
        var elapsed = 0;
        while (elapsed < maxMs)
        {
            await Task.Delay(25);
            elapsed += 25;
            var y = await Page.EvaluateAsync<double>("window.scrollY");
            if (Math.Abs(y - lastY) < 0.5) { stableCount++; if (stableCount >= 6) return; }
            else stableCount = 0;
            lastY = y;
        }
    }

    private async Task OpenUrl(string url)
    {
        await Page.GotoReportAsync(url);
        await Task.Delay(500);
    }

    private async Task OpenFile(string path) =>
        await OpenUrl(new Uri(Path.GetFullPath(path)).AbsoluteUri);

    private async Task ExpandAll()
    {
        await Page.EvaluateAsync("document.querySelectorAll('details').forEach(d => d.open = true);");
        await Task.Delay(300);
    }

    private new async Task WaitForDiagramSvg(int timeoutMs = 30000)
    {
        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First.WaitForAsync(
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RICH TEST DATA GENERATION
    // ═══════════════════════════════════════════════════════════════════

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

        orders --> test : 500 Internal Server Error
        note left #Pink
        {"error":"Internal Server Error"}
        end note
        @enduml
        """;

    private static readonly string MediatRDiagramSource = """
        @startuml
        !pragma teoz true
        skinparam wrapWidth 800
        autonumber 1

        actor "Test" as test
        participant "Orders API" as api
        participant "Application" as app #LightBlue
        database "Cosmos DB" as cosmos
        queue "EventGrid" as events

        test -> api : POST /api/orders
        note left
        Content-Type: application/json
        {
          "productId": "SKU-42",
          "quantity": 3
        }
        end note

        api -> app : **Send: CreateOrderCommand**
        note left #LightYellow
        {
          "productId": "SKU-42",
          "quantity": 3,
          "customerId": "cust-1234"
        }
        end note

        app -> cosmos : Create Item
        note left
        Container: orders
        {"id":"ord-7f3a","status":"created"}
        end note
        cosmos --> app : 201

        app -> events : **Publish: OrderCreatedEvent**
        note left #LightBlue
        {"orderId":"ord-7f3a","type":"OrderCreated"}
        end note

        app --> api : OrderResult
        note right #LightYellow
        {
          "orderId": "ord-7f3a",
          "status": "Confirmed"
        }
        end note

        api --> test : 201 Created
        note left
        {
          "orderId": "ord-7f3a",
          "status": "Confirmed",
          "totalAmount": 250.50
        }
        end note
        @enduml
        """;

    private (Feature[] Features, DiagramAsCode[] Diagrams) CreateRichShowcaseData()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Management",
                Description = "End-to-end order lifecycle from creation to fulfilment",
                Scenarios = [
                    Sc("order-1", "Creating an order reserves stock and publishes event", true, ExecutionResult.Passed, 342, ["Smoke","Orders"],
                        Steps("Given", "the Orders API is running", "And", "Stock Service has 50 units of SKU-42",
                              "When", "I create an order for 3 units of SKU-42",
                              "Then", "the order is confirmed with status Created",
                              "And", "an OrderCreated event is published")),
                    Sc("order-2", "Insufficient stock returns 409 Conflict", false, ExecutionResult.Failed, 1204, ["Orders","ErrorHandling"],
                        Steps("Given", "the Orders API is running", "And", "Stock Service has 12 units of SKU-99",
                              "When", "I create an order for 1000 units of SKU-99", "Then", "a 409 Conflict is returned"),
                        "System.Net.Http.HttpRequestException: Connection refused (Stock Service:5001)\nResponse body: {\"error\":\"Connection refused\"}",
                        "   at System.Net.Http.HttpClient.SendAsync()\n   at OrderTests.Insufficient_stock_returns_409() in /src/Tests/OrderTests.cs:line 87"),
                    Sc("order-3", "Listing orders returns paginated results", true, ExecutionResult.Passed, 189, ["Smoke","Orders"]),
                    Sc("order-4", "Updating order status sends notification", true, ExecutionResult.Passed, 267, ["Orders"]),
                    Sc("order-5", "Cancelling order within grace period succeeds", true, ExecutionResult.Passed, 198, ["Orders"]),
                    Sc("order-6", "Cancelling order after cut-off returns 409", false, ExecutionResult.Passed, 156, ["Orders","ErrorHandling"]),
                    ScParam("order-p1", "Order validation", "Amount: 100, Currency: GBP, Region: EU", ExecutionResult.Passed, 34, ["Orders","Validation"], new(){["Amount"]="100.00",["Currency"]="GBP",["Region"]="EU"}),
                    ScParam("order-p2", "Order validation", "Amount: 250.50, Currency: USD, Region: US", ExecutionResult.Passed, 28, ["Orders","Validation"], new(){["Amount"]="250.50",["Currency"]="USD",["Region"]="US"}),
                    ScParam("order-p3", "Order validation", "Amount: 999.99, Currency: EUR, Region: APAC", ExecutionResult.Failed, 1102, ["Orders","Validation"], new(){["Amount"]="999.99",["Currency"]="EUR",["Region"]="APAC"}, "System.Net.Http.HttpRequestException: Connection refused (Stock Service:5001)"),
                    ScParam("order-p4", "Order validation", "Amount: 0.01, Currency: JPY, Region: APAC", ExecutionResult.Passed, 31, ["Orders","Validation"], new(){["Amount"]="0.01",["Currency"]="JPY",["Region"]="APAC"}),
                    ScParam("order-p5", "Order validation", "Amount: 50, Currency: AUD, Region: APAC", ExecutionResult.Passed, 29, ["Orders","Validation"], new(){["Amount"]="50.00",["Currency"]="AUD",["Region"]="APAC"}),
                    ScParam("order-p6", "Order validation", "Amount: 1500, Currency: CAD, Region: NA", ExecutionResult.Passed, 35, ["Orders","Validation"], new(){["Amount"]="1500.00",["Currency"]="CAD",["Region"]="NA"}),
                ]
            },
            new Feature
            {
                DisplayName = "Payment Processing",
                Description = "Stripe integration for payment capture and refunds",
                Scenarios = [
                    Sc("pay-1", "Processing a payment charges Stripe and records transaction", true, ExecutionResult.Passed, 456, ["Smoke","Payments"],
                        Steps("Given", "the Payment API is running", "When", "I process a payment of £250.50",
                              "Then", "Stripe is charged successfully", "And", "the transaction is recorded in SQL Server")),
                    Sc("pay-2", "Refund reverses the charge", true, ExecutionResult.Passed, 312, ["Payments"]),
                    Sc("pay-3", "Payment timeout retries three times", false, ExecutionResult.Skipped, 50, ["Payments","ErrorHandling"]),
                    Sc("pay-4", "Partial refund calculates correct amount", true, ExecutionResult.Passed, 278, ["Payments"]),
                    Sc("pay-5", "Duplicate payment detection blocks charge", true, ExecutionResult.Passed, 189, ["Payments","ErrorHandling"]),
                    Sc("pay-6", "Currency conversion applies correct rate", true, ExecutionResult.Passed, 234, ["Payments"]),
                ]
            },
            new Feature
            {
                DisplayName = "User Authentication",
                Description = "OAuth2 and JWT-based authentication flows",
                Scenarios = [
                    Sc("auth-1", "Login with valid credentials returns JWT", true, ExecutionResult.Passed, 95, ["Smoke","Auth"]),
                    Sc("auth-2", "Login with invalid credentials returns 401", false, ExecutionResult.Passed, 78, ["Auth","ErrorHandling"]),
                    Sc("auth-3", "Expired token is rejected with 403", false, ExecutionResult.Passed, 62, ["Auth"]),
                    Sc("auth-4", "Refresh token extends session", true, ExecutionResult.Passed, 112, ["Auth"]),
                    Sc("auth-5", "Multi-factor authentication challenge", true, ExecutionResult.Passed, 245, ["Auth","Smoke"]),
                    Sc("auth-6", "Rate limiting blocks brute force attempts", false, ExecutionResult.Passed, 89, ["Auth","ErrorHandling"]),
                ]
            },
            new Feature
            {
                DisplayName = "Inventory Tracking",
                Description = "Real-time stock level management across warehouses",
                Scenarios = [
                    Sc("inv-1", "Stock level decreases after order", true, ExecutionResult.Passed, 220, ["Inventory"]),
                    Sc("inv-2", "Restock event increases available units", true, ExecutionResult.Passed, 145, ["Inventory"]),
                    Sc("inv-3", "Low stock triggers reorder alert", false, ExecutionResult.Failed, 890, ["Inventory","Alerting"],
                        error: "System.Net.Http.HttpRequestException: Connection refused (Stock Service:5001)",
                        stack: "   at System.Net.Http.HttpClient.SendAsync()\n   at InventoryTests.Low_stock_triggers_alert() in /src/Tests/InventoryTests.cs:line 134"),
                    Sc("inv-4", "Cross-warehouse transfer updates both locations", true, ExecutionResult.Passed, 334, ["Inventory"]),
                ]
            },
            new Feature
            {
                DisplayName = "Shipping & Fulfilment",
                Description = "Carrier integration and delivery tracking",
                Scenarios = [
                    Sc("ship-1", "Creating shipment allocates tracking number", true, ExecutionResult.Passed, 178, ["Smoke","Shipping"]),
                    Sc("ship-2", "Delivery confirmation updates order status", true, ExecutionResult.Passed, 267, ["Shipping"]),
                    Sc("ship-3", "Failed delivery triggers return process", false, ExecutionResult.Failed, 445, ["Shipping","ErrorHandling"],
                        error: "System.Net.Http.HttpRequestException: Connection refused (Stock Service:5001)",
                        stack: "   at System.Net.Http.HttpClient.SendAsync()\n   at ShippingTests.Failed_delivery_triggers_return_process() in /src/Tests/ShippingTests.cs:line 56"),
                    Sc("ship-4", "Express shipping selects priority carrier", true, ExecutionResult.Passed, 156, ["Shipping"]),
                    Sc("ship-5", "International shipping calculates customs duty", true, ExecutionResult.Passed, 312, ["Shipping"]),
                ]
            },
            new Feature
            {
                DisplayName = "Notification Service",
                Description = "Email, SMS, and push notification delivery",
                Scenarios = [
                    Sc("notif-1", "Order confirmation email is sent", true, ExecutionResult.Passed, 134, ["Smoke","Notifications"]),
                    Sc("notif-2", "SMS notification for delivery update", true, ExecutionResult.Passed, 89, ["Notifications"]),
                    Sc("notif-3", "Push notification for price drop alert", true, ExecutionResult.Passed, 67, ["Notifications"]),
                    Sc("notif-4", "Unsubscribed user does not receive marketing email", false, ExecutionResult.Passed, 45, ["Notifications","Privacy"]),
                ]
            },
            new Feature
            {
                DisplayName = "Analytics & Reporting",
                Description = "Business intelligence and dashboard data",
                Scenarios = [
                    Sc("analytics-1", "Daily sales summary aggregation", true, ExecutionResult.Passed, 567, ["Analytics"]),
                    Sc("analytics-2", "Customer segmentation pipeline", true, ExecutionResult.Passed, 890, ["Analytics"]),
                    Sc("analytics-3", "Revenue forecast model accuracy", true, ExecutionResult.Passed, 1234, ["Analytics"]),
                    Sc("analytics-4", "Real-time dashboard WebSocket feed", true, ExecutionResult.Passed, 445, ["Smoke","Analytics"]),
                ]
            },
            new Feature
            {
                DisplayName = "Admin Portal",
                Description = "Back-office administration tools",
                Scenarios = [
                    Sc("admin-1", "Admin can search orders by customer", true, ExecutionResult.Passed, 123, ["Smoke","Admin"]),
                    Sc("admin-2", "Admin can manually trigger refund", true, ExecutionResult.Passed, 234, ["Admin","Payments"]),
                    Sc("admin-3", "Admin can view audit trail", true, ExecutionResult.Passed, 167, ["Admin"]),
                    Sc("admin-4", "Admin can suspend user account", true, ExecutionResult.Passed, 89, ["Admin","Auth"]),
                ]
            },
        };

        var diagrams = new List<DiagramAsCode>
        {
            new("order-1", "", OrderDiagramSource),
            new("order-2", "", FailureDiagramSource),
            new("order-p1", "", OrderDiagramSource),
            new("order-p2", "", OrderDiagramSource),
            new("order-p3", "", FailureDiagramSource),
            new("order-p4", "", OrderDiagramSource),
            new("order-p5", "", OrderDiagramSource),
            new("order-p6", "", OrderDiagramSource),
            new("pay-1", "", PaymentDiagramSource),
            new("inv-3", "", FailureDiagramSource),
            new("ship-3", "", FailureDiagramSource),
        };

        return (features, diagrams.ToArray());
    }

    private static Scenario Sc(string id, string name, bool happy, ExecutionResult result, int ms, string[] cats,
        ScenarioStep[]? steps = null, string? error = null, string? stack = null)
        => new() { Id = id, DisplayName = name, IsHappyPath = happy, Result = result,
            Duration = TimeSpan.FromMilliseconds(ms), Tags = cats, Steps = steps,
            ErrorMessage = error, ErrorStackTrace = stack };

    private static Scenario ScParam(string id, string outlineId, string suffix, ExecutionResult result, int ms, string[] cats,
        Dictionary<string, string> values, string? error = null)
        => new() { Id = id, DisplayName = $"{outlineId}({suffix})", OutlineId = outlineId,
            IsHappyPath = result == ExecutionResult.Passed, Result = result,
            Duration = TimeSpan.FromMilliseconds(ms), Tags = cats, ExampleValues = values,
            ErrorMessage = error };

    private static ScenarioStep[] Steps(params string[] kwText)
    {
        var steps = new List<ScenarioStep>();
        for (var i = 0; i < kwText.Length; i += 2)
            steps.Add(new ScenarioStep { Keyword = kwText[i], Text = kwText[i + 1],
                Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 300)) });
        return steps.ToArray();
    }

    private string GenerateReport()
    {
        var (features, diagrams) = CreateRichShowcaseData();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow,
            null, Path.Combine(TempDir, "WikiReport.html"),
            "E\u2011Commerce Service Specifications",
            includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true, maxParameterColumns: 10, showStepNumbers: true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task NavigateToReport()
    {
        var url = GenerateReport();
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("document.documentElement.style.scrollBehavior='smooth';");
        await InjectFakeCursor();
        await Task.Delay(500);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 1: Interactive HTML Report GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature01_Interactive_Report_GIF()
    {
        BeginFrameCapture("feature01");
        await NavigateToReport();

        await ScrollToTop(); await Hold(2);

        await JsClick("button.fold-expand[data-fold='features']"); await Hold(1.5); // 5.0.0: OPTION_C_PLAN S4

        await ScrollTo("details.scenario"); await Hold(1.5);
        await ScrollToTop(); await Hold(0.5);

        await JsClick(".status-toggle[data-status='Failed']"); await Hold(2);
        await ScrollTo("details.feature"); await Hold(1.5);
        await ScrollToTop(); await Hold(0.5);

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(1);

        await JsClick(".status-toggle[data-status='Passed']"); await Hold(1);
        await ScrollTo("details.feature"); await Hold(1);
        await ScrollToTop(); await Hold(0.5);

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(0.5);

        await Page.EvaluateAsync("""
            (() => {
                var btn = document.querySelector('.dependency-toggle[data-dependency]');
                if (btn) btn.click();
            })()
        """);
        await Hold(1);

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(0.5);

        // P95 duration filter
        await Page.EvaluateAsync("""
            (() => {
                var btns = document.querySelectorAll('.percentile-btn[data-threshold-ms]');
                if (btns.length >= 3) btns[2].click();
                else if (btns.length > 0) btns[0].click();
            })()
        """);
        await Hold(1);

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(0.5);

        await JsClick(".filtering-box .happy-seg[data-hp='in']"); await Hold(1.5);
        await ScrollTo("details.feature"); await Hold(1);
        await ScrollToTop(); await Hold(0.5);

        await JsClick(".filtering-box .tag-toggle[data-tag='Smoke']"); await Hold(1);

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(1);
        await ScrollToTop(); await Hold(2);

        StitchGif("whats-new-feature01-report.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 2: Advanced Search GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature02_Search_GIF()
    {
        BeginFrameCapture("feature02");
        await NavigateToReport();
        await ScrollToTop(); await Hold(1);

        // the search button opens the query builder; a query typed there runs to the Search tab (plans/SEARCH_PLAN.md 4.3)
        async Task SearchAndShow(string query)
        {
            await Page.Locator("#search-button").ClickAsync();
            await Hold(0.5);
            await HideCursor();
            await Page.Locator("#search-input").FillAsync("");
            await TypeSlowly("#search-input", query);
            await Hold(1);
            await Page.Locator("#search-input").PressAsync("Enter");
            await Hold(1.5);
            await ScrollTo("#panel-search .search-result"); await Hold(1);
            await ScrollToTop(); await Hold(0.5);
            await ShowCursor();
        }

        await SearchAndShow("order");
        await SearchAndShow("tag:Smoke");
        await SearchAndShow("status:failed");
        await SearchAndShow("payment && stripe");
        await SearchAndShow("stock || inventory");

        await Page.Locator("#search-button").ClickAsync();
        await JsClick(".search-tips-toggle"); await Hold(2);
        await Page.Keyboard.PressAsync("Escape");

        await Page.Locator("#search-clear").ClickAsync();
        await Hold(1);

        StitchGif("whats-new-feature02-search.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 3: Parameterized Test Grouping GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature03_Parameterized_Tests_GIF()
    {
        BeginFrameCapture("feature03");
        await NavigateToReport();

        await ExpandAllFeaturesAsync();
        await Task.Delay(300);
        await ExpandAllScenariosAsync();
        await Task.Delay(500);

        await ScrollTo(".param-test-table"); await Hold(4);

        // Click first data row
        await Page.EvaluateAsync("""
            (() => {
                var rows = document.querySelectorAll('.param-test-table tbody tr[data-row-idx]');
                if (rows.length > 0) rows[0].click();
            })()
        """);
        await Hold(3);

        // Try scroll to detail panel
        try { await ScrollTo(".param-detail-panel[style*='display: block'], .param-detail-panel:not([style*='display: none'])"); await Hold(2.5); } catch { }

        await ScrollTo(".param-test-table"); await Hold(1.5);

        // Click failed row
        await Page.EvaluateAsync("document.querySelector('.param-test-table tbody tr.row-failed')?.click()");
        await Hold(3);
        try { await ScrollTo(".param-detail-panel[style*='display: block'], .param-detail-panel:not([style*='display: none'])"); await Hold(3); } catch { }

        // Click another row
        await Page.EvaluateAsync("""
            (() => {
                var rows = document.querySelectorAll('.param-test-table tbody tr[data-row-idx]');
                if (rows.length > 3) rows[3].click();
            })()
        """);
        await Hold(2);

        await ScrollTo(".param-test-table"); await Hold(4);

        StitchGif("whats-new-feature03-params.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 5: Sequence Diagrams GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature05_Sequence_Diagrams_GIF()
    {
        BeginFrameCapture("feature05");
        await NavigateToReport();
        await ExpandAll(); await Task.Delay(300);

        await WaitForDiagramSvg();

        await ScrollTo(".plantuml-browser"); await Hold(4);

        // Expanded
        await JsClick(".details-radio-btn[data-state='expanded']");
        await Task.Delay(500);
        try { await WaitForDiagramSvg(15000); } catch { await Task.Delay(2000); }
        await Hold(3);

        // Collapsed
        await JsClick(".details-radio-btn[data-state='collapsed']");
        await Task.Delay(500);
        try { await WaitForDiagramSvg(15000); } catch { await Task.Delay(2000); }
        await Hold(3);

        // Truncated
        await JsClick(".details-radio-btn[data-state='truncated']");
        await Task.Delay(500);
        try { await WaitForDiagramSvg(15000); } catch { await Task.Delay(2000); }
        await Hold(3);

        // Toggle Headers
        try
        {
            await JsClick(".header-toggle-btn, button[onclick*='toggle_headers']");
            await Hold(1.5);
            await JsClick(".header-toggle-btn, button[onclick*='toggle_headers']");
            await Hold(1);
        }
        catch { }

        // Double-click to zoom
        await Page.Locator(".plantuml-browser svg").First.DblClickAsync();
        await Hold(3);
        try
        {
            await JsClick(".diagram-zoom-close, .zoom-close, [onclick*='zoom']");
            await Hold(1);
        }
        catch
        {
            await Page.Locator(".plantuml-browser svg").First.DblClickAsync();
            await Hold(1);
        }

        // Context menu
        try
        {
            var svg = Page.Locator(".plantuml-browser svg").First;
            await DispatchContextMenu(svg);
            await Hold(3);
            await Page.EvaluateAsync("document.querySelectorAll('.diagram-context-menu').forEach(m => m.style.display = 'none');");
            await Hold(0.5);
        }
        catch { }

        await Hold(1);
        StitchGif("whats-new-feature05-diagrams.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 6: Database Tracking Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature06_Database_Tracking_Screenshot()
    {
        await NavigateToReport();
        await ExpandAll(); await Task.Delay(300);
        await WaitForDiagramSvg();

        await Page.EvaluateAsync("""
            (() => {
                var el = document.querySelector('.plantuml-browser');
                if (el) { el.scrollIntoView({block:'start', behavior:'instant'}); window.scrollBy(0, -20); }
            })()
        """);
        await Task.Delay(500);
        try { await WaitForDiagramSvg(10000); } catch { await Task.Delay(1000); }

        // Inject red arrow at Cosmos DB
        await Page.EvaluateAsync("""
            (() => {
                var container = document.querySelector('.plantuml-browser');
                if (!container) return;
                var svg = container.querySelector('svg');
                if (!svg) return;
                container.style.position = 'relative';
                var texts = svg.querySelectorAll('text');
                var cosmosEl = null;
                for (var i = 0; i < texts.length; i++) {
                    if (texts[i].textContent.indexOf('Cosmos') >= 0) { cosmosEl = texts[i]; break; }
                }
                var arrow = document.createElement('div');
                arrow.style.cssText = 'position:absolute;z-index:9999;pointer-events:none;';
                if (cosmosEl) {
                    var svgRect = svg.getBoundingClientRect();
                    var cosmosRect = cosmosEl.getBoundingClientRect();
                    arrow.style.left = (cosmosRect.left - svgRect.left + cosmosRect.width + 10) + 'px';
                    arrow.style.top = Math.max(10, cosmosRect.top - svgRect.top - 80) + 'px';
                } else { arrow.style.right = '30px'; arrow.style.top = '60px'; }
                arrow.innerHTML = `<svg width='200' height='120' viewBox='0 0 200 120' xmlns='http://www.w3.org/2000/svg' style='overflow:visible;'>
                    <path d='M 180 10 C 150 15, 100 20, 50 60 C 30 75, 20 85, 15 100' stroke='#e53e3e' stroke-width='3' fill='none' stroke-linecap='round' stroke-dasharray='8,4'/>
                    <polygon points='5,95 20,105 10,110' fill='#e53e3e'/>
                    <text x='100' y='10' fill='#e53e3e' font-size='13' font-weight='bold' font-family='sans-serif'>Cosmos DB</text>
                </svg>`;
                container.appendChild(arrow);
            })()
        """);
        await Task.Delay(300);

        await SaveScreenshot("whats-new-database-tracking.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 7: Component Diagram Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature07_Component_Diagram_Screenshot()
    {
        var (features, diagrams) = CreateRichShowcaseData();

        var componentDiagramPlantUml = """
            @startuml
            skinparam componentStyle rectangle
            skinparam backgroundColor white
            skinparam defaultFontSize 12
            skinparam wrapWidth 200

            actor "Caller\n[Person]" as caller #369
            rectangle "Orders API\n[Software System]" as api #369
            rectangle "Stock Service\n[Software System]" as stock #369
            rectangle "Payment API\n[Software System]" as payments #369
            rectangle "Stripe Gateway\n[Software System]" as stripe #369
            database "Cosmos DB\n[Database]" as cosmos #369
            database "SQL Server\n[Database]" as sql #369
            database "Redis Cache\n[Cache]" as redis #369
            queue "EventGrid\n[Event Broker]" as events #369
            rectangle "Notification Service\n[Software System]" as notifications #369
            rectangle "Auth Service\n[Software System]" as auth #369
            rectangle "Shipping API\n[Software System]" as shipping #369
            rectangle "Analytics Engine\n[Software System]" as analytics #369

            caller --> api : HTTP: GET, POST, PUT, DELETE\n45 calls across 12 tests
            api --> stock : HTTP: PUT, GET\n12 calls across 8 tests
            api --> payments : HTTP: POST\n6 calls across 4 tests
            payments --> stripe : HTTP: POST\n6 calls across 4 tests
            api --> cosmos : Create Item; Query; Upsert\n18 calls across 10 tests
            payments --> sql : INSERT; SELECT\n8 calls across 4 tests
            api --> redis : Get (Hit); Get (Miss); Set\n15 calls across 9 tests
            api --> events : Send (Event Protocol)\n10 calls across 7 tests
            events --> notifications : Send (Event Protocol)\n5 calls across 3 tests
            api --> auth : HTTP: POST\n8 calls across 6 tests
            api --> shipping : HTTP: POST, GET\n5 calls across 3 tests
            api --> analytics : HTTP: POST\n4 calls across 2 tests
            shipping --> notifications : Send (Event Protocol)\n3 calls across 2 tests
            @enduml
            """;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow,
            null, Path.Combine(TempDir, "ComponentDiagramReport.html"),
            "E\u2011Commerce Service \u2014 Test Run Report",
            includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: componentDiagramPlantUml);

        await OpenUrl(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });
        await Task.Delay(500);

        try { await Page.Locator("button:has-text('Component Diagram')").ClickAsync(); await Task.Delay(500); } catch { }

        try
        {
            await Page.Locator("#component-diagram .plantuml-browser svg").WaitForAsync(new() { Timeout = 30000 });
            await Task.Delay(1000);
        }
        catch { await Task.Delay(3000); }

        try
        {
            await Page.EvaluateAsync("""
                (() => {
                    var s = document.getElementById('component-diagram');
                    if (s) { s.scrollIntoView({block:'start', behavior:'instant'}); window.scrollBy(0, -10); }
                })()
            """);
            await Task.Delay(500);
        }
        catch { }

        await SaveScreenshot("whats-new-component-diagram.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 8: Internal Flow GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature08_Internal_Flow_GIF()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Service",
                Scenarios = [
                    Sc("flow-1", "Creating an order reserves stock and publishes event", true, ExecutionResult.Passed, 850, ["Smoke","Orders"],
                        Steps("Given", "the Orders API is running", "And", "Stock Service has 50 units of SKU-42",
                              "When", "I create an order for 3 units of SKU-42",
                              "Then", "the order is confirmed with status Created"))
                ]
            }
        };
        var diagrams = new[] { new DiagramAsCode("flow-1", "", OrderDiagramSource) };

        using var activitySource = new ActivitySource("Kronikol.Wiki.InternalFlow.Report");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;
        var baseTime = DateTime.UtcNow;

        var root = activitySource.StartActivity("HTTP POST /api/orders", ActivityKind.Server)!;
        root.SetStartTime(baseTime);
        root.SetEndTime(baseTime.AddMilliseconds(850));
        var rootCtx = new ActivityContext(root.TraceId, root.SpanId, ActivityTraceFlags.Recorded);

        var spans = new List<Activity> { root };
        Activity MakeSpan(string name, int startMs, int endMs)
        {
            var s = activitySource.StartActivity(name, ActivityKind.Internal, rootCtx)!;
            s.SetStartTime(baseTime.AddMilliseconds(startMs));
            s.SetEndTime(baseTime.AddMilliseconds(endMs));
            spans.Add(s);
            return s;
        }

        MakeSpan("Middleware: Authentication", 5, 25);
        MakeSpan("Middleware: Authorization", 25, 35);
        MakeSpan("MediatR: Send CreateOrderCommand", 40, 780);
        MakeSpan("FluentValidation: Validate", 45, 65);
        MakeSpan("EF Core: SELECT Products", 70, 180);
        MakeSpan("HttpClient: PUT /stock/reserve", 185, 420);
        MakeSpan("EF Core: INSERT Orders", 425, 560);
        MakeSpan("CosmosDB: Create Item (orders)", 565, 680);
        MakeSpan("EventGrid: Publish OrderCreated", 685, 750);
        MakeSpan("Serialization: JsonSerializer", 755, 775);

        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-flow-1"] = new(
                Guid.Empty, RequestResponseType.Request, "flow-1",
                root.StartTimeUtc, root.StartTimeUtc + root.Duration,
                spans.ToArray())
        };

        var boundaryLogs = new[]
        {
            ("POST: /api/orders", new DateTimeOffset(baseTime.AddMilliseconds(5), TimeSpan.Zero))
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow,
            null, Path.Combine(TempDir, "InternalFlowReport.html"),
            "Internal Flow Tracking Demo",
            includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            showStepNumbers: true,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both);

        foreach (var s in spans) s.Dispose();

        BeginFrameCapture("feature08");
        await OpenUrl(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(); await Task.Delay(500);
        await ExpandAll(); await Task.Delay(500);
        await InjectFakeCursor();

        await WaitForDiagramSvg();
        await ScrollTo(".plantuml-browser"); await Hold(5);

        await JsClick(".diagram-toggle-btn[data-dtype='activity']"); await Hold(2);
        try
        {
            await Page.Locator("[data-diagram-type='activity'] svg, .plantuml-browser svg").First.WaitForAsync(new() { Timeout = 15000 });
        }
        catch { await Task.Delay(2000); }
        await Hold(5);

        await JsClick(".diagram-toggle-btn[data-dtype='flame']"); await Hold(2);
        try
        {
            await Page.Locator(".iflow-flame, [data-diagram-type='flamechart']").First.WaitForAsync(new() { Timeout = 10000 });
        }
        catch { await Task.Delay(2000); }

        // Hover flame bars
        var barCount = await Page.Locator(".iflow-flame-bar").CountAsync();
        if (barCount > 0)
        {
            await MoveCursorTo(".iflow-flame-bar:nth-child(1)"); await Hold(2);
            if (barCount > 3) { await MoveCursorTo(".iflow-flame-bar:nth-child(4)"); await Hold(2); }
            if (barCount > 6) { await MoveCursorTo(".iflow-flame-bar:nth-child(7)"); await Hold(2); }
        }
        await Hold(3);

        await JsClick(".diagram-toggle-btn[data-dtype='seq']"); await Hold(3);

        StitchGif("whats-new-feature08-internal-flow.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 9: CI Summary Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature09_CI_Summary_Screenshot()
    {
        var (features, diagrams) = CreateRichShowcaseData();
        var markdown = CiSummaryGenerator.GenerateMarkdown(
            features, diagrams, diagrams,
            DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow,
            maxDiagrams: 5,
            diagramFormat: DiagramFormat.PlantUml);

        var escapedMd = JsonSerializer.Serialize(markdown);
        var viewerPath = Path.Combine(TempDir, "CiSummary.html");
        File.WriteAllText(viewerPath, $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <script src="https://cdn.jsdelivr.net/npm/marked/marked.min.js"></script>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Noto Sans, Helvetica, Arial, sans-serif; font-size: 14px; line-height: 1.5; color: #1f2328; background: #f6f8fa; margin: 0; padding: 0; }
                    .gh-header { background: #24292f; color: white; padding: 12px 24px; display: flex; align-items: center; gap: 12px; }
                    .gh-header strong { font-size: 16px; }
                    .gh-header .subtext { color: #8b949e; font-size: 13px; }
                    .gh-body { max-width: 960px; margin: 24px auto; background: white; border: 1px solid #d0d7de; border-radius: 6px; padding: 32px; }
                    .gh-body h1 { font-size: 24px; font-weight: 600; border-bottom: 1px solid #d0d7de; padding-bottom: 0.3em; margin-top: 0; }
                    .gh-body h2 { font-size: 20px; font-weight: 600; border-bottom: 1px solid #d0d7de; padding-bottom: 0.3em; margin-top: 24px; }
                    .gh-body table { border-collapse: collapse; width: auto; }
                    .gh-body th, .gh-body td { border: 1px solid #d0d7de; padding: 6px 13px; }
                    .gh-body th { background: #f6f8fa; font-weight: 600; }
                    .gh-body details { margin: 8px 0; }
                    .gh-body details > summary { cursor: pointer; font-weight: 600; padding: 4px 0; }
                    .gh-body pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 6px; padding: 16px; overflow: auto; font-size: 13px; }
                    .gh-body img { max-width: 100%; }
                </style>
            </head>
            <body>
                <div class="gh-header">
                    <div><strong>GitHub Actions</strong><br/><span class="subtext">acme-corp/e-commerce-api &bull; E-Commerce CI &bull; Run #847 &bull; main</span></div>
                </div>
                <div class="gh-body" id="content"></div>
                <script>
                    var md = {{escapedMd}};
                    document.getElementById('content').innerHTML = marked.parse(md);
                </script>
            </body>
            </html>
            """);

        await OpenFile(viewerPath);
        await Page.Locator(".gh-body").WaitForAsync();
        await Task.Delay(1500);

        await Page.EvaluateAsync("""
            document.querySelectorAll('.gh-body details').forEach(d => d.setAttribute('open', ''));
        """);
        await Task.Delay(2000);

        await Page.EvaluateAsync("""
            (() => { var img = document.querySelector('.gh-body img'); if (img) img.scrollIntoView({block:'center'}); })()
        """);
        await Task.Delay(500);

        await SaveScreenshot("whats-new-ci-summary.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 10: JSON Report GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature10_JSON_Report_GIF()
    {
        var (features, diagrams) = CreateRichShowcaseData();
        var jsonPath = Path.Combine(TempDir, "TestRunReport.json");
        ReportGenerator.GenerateTestRunReportData(
            features, DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow,
            jsonPath, DataFormat.Json, diagrams);

        BeginFrameCapture("feature10");

        var jsonContent = File.ReadAllText(jsonPath);
        var viewerHtml = CreateInteractiveJsonViewerHtml(jsonContent);
        var viewerPath = Path.Combine(TempDir, "JsonViewer.html");
        File.WriteAllText(viewerPath, viewerHtml);

        await OpenFile(viewerPath);
        await Page.Locator("body").WaitForAsync();
        await Task.Delay(500);
        await InjectFakeCursor();

        await Hold(2);

        async Task ExpandNode(string childrenId)
        {
            await Page.EvaluateAsync($$"""
                (() => {
                    var ch = document.getElementById('{{childrenId}}');
                    if (ch) {
                        ch.style.display = 'inline';
                        var sm = document.getElementById('{{childrenId}}'.replace('jch','jsm'));
                        if (sm) sm.style.display = 'none';
                        var toggle = ch.previousElementSibling;
                        while (toggle && !toggle.classList.contains('json-toggle')) toggle = toggle.previousElementSibling;
                        if (toggle) toggle.textContent = '\u25BC';
                    }
                })()
            """);
            await Task.Delay(300);
        }

        async Task CollapseNode(string childrenId)
        {
            await Page.EvaluateAsync($$"""
                (() => {
                    var ch = document.getElementById('{{childrenId}}');
                    if (ch) {
                        ch.style.display = 'none';
                        var sm = document.getElementById('{{childrenId}}'.replace('jch','jsm'));
                        if (sm) sm.style.display = 'inline';
                        var toggle = ch.previousElementSibling;
                        while (toggle && !toggle.classList.contains('json-toggle')) toggle = toggle.previousElementSibling;
                        if (toggle) toggle.textContent = '\u25B6';
                    }
                })()
            """);
            await Task.Delay(300);
        }

        await ExpandNode("jch0"); await Hold(2.5);
        await ExpandNode("jch1"); await Hold(2);
        await ExpandNode("jch2"); await Hold(2);
        await Page.EvaluateAsync("window.scrollBy(0, 300)"); await Task.Delay(200); await Hold(2);
        await ExpandNode("jch3"); await Hold(2.5);
        await Page.EvaluateAsync("window.scrollBy(0, 400)"); await Task.Delay(200); await Hold(2);
        await CollapseNode("jch3"); await Hold(2);
        await ScrollToTop(); await Hold(2);

        StitchGif("whats-new-feature10-json.gif");
    }

    private static string CreateInteractiveJsonViewerHtml(string jsonContent)
    {
        var escapedJson = JsonSerializer.Serialize(jsonContent);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>TestRunReport.json</title>
                <style>
                    body { font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; padding: 20px; line-height: 1.5; background: #ffffff; color: #24292f; margin: 0; }
                    .json-toolbar { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 6px; padding: 8px 16px; margin-bottom: 16px; display: flex; align-items: center; gap: 12px; font-family: -apple-system, sans-serif; font-size: 14px; }
                    .json-toolbar .filename { font-weight: 600; color: #0969da; }
                    .json-toolbar .meta { color: #57606a; font-size: 12px; }
                    .json-toggle { cursor: pointer; user-select: none; display: inline-block; width: 16px; text-align: center; color: #57606a; font-family: monospace; }
                    .json-toggle:hover { color: #0969da; }
                    .json-key { color: #0550ae; }
                    .json-string { color: #0a3069; }
                    .json-number { color: #0550ae; font-weight: 600; }
                    .json-bool { color: #cf222e; }
                    .json-null { color: #8250df; }
                    .json-bracket { color: #24292f; }
                    .json-comma { color: #57606a; }
                    .json-summary { color: #57606a; font-style: italic; cursor: pointer; background: #f6f8fa; padding: 1px 6px; border-radius: 3px; font-size: 12px; }
                    .json-summary:hover { background: #eaeef2; }
                    .json-content { border: 1px solid #d0d7de; border-radius: 6px; padding: 16px; background: #ffffff; font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; white-space: pre; overflow: auto; }
                </style>
            </head>
            <body>
                <div class="json-toolbar">
                    <span class="filename">📄 TestRunReport.json</span>
                    <span class="meta">Generated by Kronikol</span>
                </div>
                <div class="json-content" id="jsonRoot"></div>
                <script>
                    var jsonText = {{escapedJson}};
                    var obj = JSON.parse(jsonText);
                    var _nodeId = 0;
                    function renderJson(val, depth) {
                        var indent = '  '.repeat(depth);
                        var innerIndent = '  '.repeat(depth + 1);
                        if (val === null) return '<span class="json-null">null</span>';
                        if (typeof val === 'string') return '<span class="json-string">"' + escHtml(val).substring(0, 200) + (val.length > 200 ? '...' : '') + '"</span>';
                        if (typeof val === 'number') return '<span class="json-number">' + val + '</span>';
                        if (typeof val === 'boolean') return '<span class="json-bool">' + val + '</span>';
                        var isArray = Array.isArray(val);
                        var keys = isArray ? val : Object.keys(val);
                        var count = isArray ? val.length : keys.length;
                        var open = isArray ? '[' : '{';
                        var close = isArray ? ']' : '}';
                        var summary = isArray ? (count + ' items') : (count + ' keys');
                        if (count === 0) return '<span class="json-bracket">' + open + close + '</span>';
                        var nid = _nodeId++;
                        var chId = 'jch' + nid;
                        var smId = 'jsm' + nid;
                        var html = '<span class="json-toggle" data-target="' + chId + '" data-summary="' + smId + '" onclick="toggleNode(this)">\u25B6</span>';
                        html += '<span class="json-bracket">' + open + '</span>';
                        html += '<span class="json-summary" id="' + smId + '" onclick="toggleNode(this.previousElementSibling.previousElementSibling)">' + summary + '</span>';
                        html += '<span id="' + chId + '" style="display:none">';
                        if (isArray) { for (var i = 0; i < val.length; i++) { html += '\n' + innerIndent + renderJson(val[i], depth + 1) + (i < val.length - 1 ? '<span class="json-comma">,</span>' : ''); } }
                        else { for (var i = 0; i < keys.length; i++) { var k = keys[i]; html += '\n' + innerIndent + '<span class="json-key">"' + escHtml(k) + '"</span>: ' + renderJson(val[k], depth + 1) + (i < keys.length - 1 ? '<span class="json-comma">,</span>' : ''); } }
                        html += '\n' + indent + '<span class="json-bracket">' + close + '</span></span>';
                        return html;
                    }
                    function escHtml(s) { var d = document.createElement('div'); d.textContent = s; return d.innerHTML; }
                    function toggleNode(toggleEl) {
                        var ch = document.getElementById(toggleEl.getAttribute('data-target'));
                        var sm = document.getElementById(toggleEl.getAttribute('data-summary'));
                        if (!ch) return;
                        var isOpen = ch.style.display !== 'none';
                        ch.style.display = isOpen ? 'none' : 'inline';
                        if (sm) sm.style.display = isOpen ? 'inline' : 'none';
                        toggleEl.textContent = isOpen ? '\u25B6' : '\u25BC';
                    }
                    document.getElementById('jsonRoot').innerHTML = renderJson(obj, 0);
                </script>
            </body>
            </html>
            """;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 11: MediatR Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature11_MediatR_Screenshot()
    {
        var features = new[]
        {
            new Feature { DisplayName = "Order Service", Scenarios = [
                Sc("mediatr-1", "Creating an order via MediatR command pipeline", true, ExecutionResult.Passed, 456, ["Smoke","Orders"],
                    Steps("Given", "the Orders API is running",
                          "When", "I create an order via the MediatR pipeline",
                          "Then", "the CreateOrderCommand is handled",
                          "And", "the order is persisted to Cosmos DB",
                          "And", "an OrderCreatedEvent is published"))
            ]}
        };
        var diagrams = new[] { new DiagramAsCode("mediatr-1", "", MediatRDiagramSource) };
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            null, Path.Combine(TempDir, "MediatR.html"),
            "MediatR Tracking Demo", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, showStepNumbers: true);

        await OpenUrl(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll(); await Task.Delay(300);
        await WaitForDiagramSvg();

        await Page.EvaluateAsync("""
            (() => {
                var el = document.querySelector('.plantuml-browser');
                if (el) { el.scrollIntoView({block:'start', behavior:'instant'}); window.scrollBy(0, -20); }
            })()
        """);
        await Task.Delay(500);

        // Inject MediatR arrow
        await Page.EvaluateAsync("""
            (() => {
                var container = document.querySelector('.plantuml-browser');
                if (!container) return;
                var svg = container.querySelector('svg');
                if (!svg) return;
                container.style.position = 'relative';
                var texts = svg.querySelectorAll('text');
                var mediatrEl = null;
                for (var i = 0; i < texts.length; i++) {
                    if (texts[i].textContent.indexOf('Send') >= 0 || texts[i].textContent.indexOf('CreateOrder') >= 0) { mediatrEl = texts[i]; break; }
                }
                var arrow = document.createElement('div');
                arrow.style.cssText = 'position:absolute;z-index:9999;pointer-events:none;';
                if (mediatrEl) {
                    var svgRect = svg.getBoundingClientRect();
                    var elRect = mediatrEl.getBoundingClientRect();
                    arrow.style.left = Math.min(elRect.right - svgRect.left + 10, svgRect.width - 200) + 'px';
                    arrow.style.top = Math.max(10, elRect.top - svgRect.top - 60) + 'px';
                } else { arrow.style.right = '30px'; arrow.style.top = '100px'; }
                arrow.innerHTML = `<svg width='180' height='100' viewBox='0 0 180 100' xmlns='http://www.w3.org/2000/svg'>
                    <text x='80' y='15' fill='#e53e3e' font-size='13' font-weight='bold' font-family='sans-serif'>MediatR</text>
                    <path d='M 100 20 C 80 30, 40 45, 20 70 C 15 78, 10 85, 8 90' stroke='#e53e3e' stroke-width='3' fill='none' stroke-linecap='round' stroke-dasharray='8,4'/>
                    <polygon points='0,85 13,95 6,98' fill='#e53e3e'/>
                </svg>`;
                container.appendChild(arrow);
            })()
        """);
        await Task.Delay(300);

        await SaveScreenshot("whats-new-mediatr-tracking.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 12: DiagramFocus Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature12_DiagramFocus_Screenshot()
    {
        var traceId = Guid.NewGuid();
        var logs = new List<RequestResponseLog>();

        var req1Id = Guid.NewGuid();
        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            HttpMethod.Post,
            JsonSerializer.Serialize(new
            {
                productId = "SKU-42", quantity = 3, customerId = "cust-1234",
                shippingAddress = "123 High St", billingAddress = "123 High St",
                paymentMethod = "card_visa_4242", couponCode = (string?)null,
                giftWrap = false, deliveryNotes = "Leave at door"
            }, new JsonSerializerOptions { WriteIndented = true }),
            new Uri("http://orders-api/api/orders"),
            [("Content-Type", "application/json")],
            "Orders API", "Test",
            RequestResponseType.Request, traceId, req1Id, false));

        var req2Id = Guid.NewGuid();
        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            HttpMethod.Put, null,
            new Uri("http://stock-service/stock/reserve"), [],
            "Stock Service", "Orders API",
            RequestResponseType.Request, traceId, req2Id, false));

        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            HttpMethod.Put, null,
            new Uri("http://stock-service/stock/reserve"), [],
            "Stock Service", "Orders API",
            RequestResponseType.Response, traceId, req2Id, false,
            HttpStatusCode.OK));

        var req3Id = Guid.NewGuid();
        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            "Create Item",
            JsonSerializer.Serialize(new { id = "ord-7f3a", status = "created" },
                new JsonSerializerOptions { WriteIndented = true }),
            new Uri("http://cosmos-db/orders"), [],
            "Cosmos DB", "Orders API",
            RequestResponseType.Request, traceId, req3Id, false));

        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            "Create Item", null,
            new Uri("http://cosmos-db/orders"), [],
            "Cosmos DB", "Orders API",
            RequestResponseType.Response, traceId, req3Id, false,
            HttpStatusCode.Created));

        logs.Add(new RequestResponseLog(
            "Creating an order shows focused fields in diagram", "focus-1",
            HttpMethod.Post,
            JsonSerializer.Serialize(new
            {
                orderId = "ord-7f3a", status = "Confirmed", totalAmount = 250.50,
                currency = "GBP", createdAt = "2026-04-17T19:35:06Z",
                updatedAt = "2026-04-17T19:35:06Z", customerId = "cust-1234",
                shippingAddress = "123 High St", paymentRef = "pay-001",
                trackingNumber = (string?)null
            }, new JsonSerializerOptions { WriteIndented = true }),
            new Uri("http://orders-api/api/orders"), [],
            "Orders API", "Test",
            RequestResponseType.Response, traceId, req1Id, false,
            HttpStatusCode.Created)
        {
            FocusFields = ["status", "totalAmount"]
        });

        var plantUmlResults = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs,
            focusEmphasis: FocusEmphasis.Bold,
            focusDeEmphasis: FocusDeEmphasis.LightGray | FocusDeEmphasis.SmallerText,
            maxEncodedDiagramLength: 10000,
            serviceTypeOverrides: new Dictionary<string, string>
            {
                ["Cosmos DB"] = "database"
            }).ToList();

        var plantUmlSource = plantUmlResults.Single().PlantUmls.First().PlainText;

        var features = new[]
        {
            new Feature { DisplayName = "Order Service", Scenarios = [
                Sc("focus-1", "Creating an order shows focused fields in diagram", true, ExecutionResult.Passed, 342, ["Smoke"],
                    Steps("Given", "the Orders API is running",
                          "When", "I create an order with DiagramFocus on status and totalAmount",
                          "Then", "the response fields status and totalAmount are highlighted"))
            ]}
        };
        var focusDiagrams = new[] { new DiagramAsCode("focus-1", "", plantUmlSource) };
        var path = ReportGenerator.GenerateHtmlReport(
            focusDiagrams, features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            null, Path.Combine(TempDir, "Focus.html"),
            "DiagramFocus Demo", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, showStepNumbers: true);

        await OpenUrl(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll(); await Task.Delay(300);
        await WaitForDiagramSvg();

        await Page.EvaluateAsync("if (window._setReportDetails) window._setReportDetails('expanded');");
        await Task.Delay(500);

        // Scroll to response note at bottom
        await Page.EvaluateAsync("""
            (() => {
                var el = document.querySelector('.plantuml-browser');
                if (!el) return;
                var svg = el.querySelector('svg');
                if (svg) {
                    var svgRect = svg.getBoundingClientRect();
                    var targetY = window.scrollY + svgRect.top + svgRect.height - window.innerHeight + 40;
                    window.scrollTo(0, Math.max(0, targetY));
                } else { el.scrollIntoView({block:'end'}); }
            })()
        """);
        await Task.Delay(500);

        await SaveScreenshot("whats-new-diagram-focus.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 13: Failure Diagnostics GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature13_Failure_Diagnostics_GIF()
    {
        BeginFrameCapture("feature13");
        await NavigateToReport();
        await InjectFakeCursor();

        try
        {
            await ScrollTo(".failure-clusters"); await Hold(1);
            await Page.EvaluateAsync("""
                (() => {
                    var c = document.querySelector('.failure-cluster');
                    if (c) c.setAttribute('open', '');
                })()
            """);
            await Task.Delay(300);
            await Hold(3);

            try
            {
                await JsClick(".failure-cluster-scenario-link"); await Hold(3);
            }
            catch { }
        }
        catch { }

        try { await ScrollTo(".failure-result"); await Hold(3); } catch { }

        await ScrollToTop(); await Hold(1);
        await JsClick(".status-toggle[data-status='Failed']"); await Hold(3);

        try { await JsClick("#view-summary"); await Hold(2.5); } catch { } // 5.0.0: the Summary view (OPTION_C_PLAN S4)

        await JsClick(".jump-to-failure"); await Hold(3);
        await ExpandAll(); await Hold(1);
        try { await ScrollTo(".failure-result pre, .failure-result"); await Hold(3); } catch { }
        await JsClick(".jump-to-failure"); await Hold(3);
        await ScrollToTop(); await Hold(3);

        StitchGif("whats-new-feature13-failures.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 14: Category Filtering GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature14_Category_Filter_GIF()
    {
        BeginFrameCapture("feature14");
        await NavigateToReport();
        await ScrollToTop(); await Hold(1);

        await JsClick(".filtering-box .tag-toggle[data-tag='Smoke']"); await Hold(2);
        await ScrollTo("details.feature"); await Hold(1.5);
        await ScrollToTop(); await Hold(0.5);

        try { await JsClick(".filtering-box .tag-mode-toggle"); await Hold(1); } catch { }
        try { await JsClick(".filtering-box .tag-toggle[data-tag='Orders']"); await Hold(2); } catch { }

        await JsClick(".export-btn[onclick*='clear_all']"); await Hold(1);
        await ScrollToTop(); await Hold(1);

        StitchGif("whats-new-feature14-categories.gif");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 17: Framework Support (3 screenshots)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature17_Framework_xUnit_Screenshot()
    {
        var basePath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(WikiGifTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "examples", "Example.Api", "tests"));

        var xunitReport = Path.Combine(basePath,
            "Example.Api.Tests.Component.xUnit3", "bin", "Debug", "net10.0", "Reports", "Specifications.html");
        Assert.True(File.Exists(xunitReport), $"xUnit3 report not found: {xunitReport}");
        await OpenFile(xunitReport);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll(); await Task.Delay(500);
        try { await WaitForDiagramSvg(20000); } catch { await Task.Delay(2000); }
        await SaveScreenshot("whats-new-framework-xunit.png");
    }

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature17_Framework_ReqNRoll_Screenshot()
    {
        var basePath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(WikiGifTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "examples", "Example.Api", "tests"));

        var report = Path.Combine(basePath,
            "Example.Api.Tests.Component.ReqNRoll.xUnit3", "bin", "Debug", "net10.0", "Reports", "Specifications.html");
        if (!File.Exists(report))
            report = Path.Combine(basePath,
                "Example.Api.Tests.Component.ReqNRoll.xUnit2", "bin", "Debug", "net10.0", "Reports", "Specifications.html");
        Assert.True(File.Exists(report), "ReqNRoll report not found");
        await OpenFile(report);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll(); await Task.Delay(500);
        try { await WaitForDiagramSvg(20000); } catch { await Task.Delay(2000); }
        await SaveScreenshot("whats-new-framework-reqnroll.png");
    }

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature17_Framework_LightBDD_Screenshot()
    {
        var basePath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(WikiGifTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "examples", "Example.Api", "tests"));

        var report = Path.Combine(basePath,
            "Example.Api.Tests.Component.LightBDD.xUnit2", "bin", "Debug", "net10.0", "Reports", "Specifications.html");
        Assert.True(File.Exists(report), "LightBDD report not found");
        await OpenFile(report);
        await Page.Locator("body").WaitForAsync();
        await ExpandAll(); await Task.Delay(500);
        try { await WaitForDiagramSvg(20000); } catch { await Task.Delay(2000); }
        await SaveScreenshot("whats-new-framework-lightbdd.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 19: Scenario Timeline Screenshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature19_Scenario_Timeline_Screenshot()
    {
        await NavigateToReport();

        await JsClick("#tab-timeline"); // 5.0.0: a tab of the report bar (plans/OPTION_C_PLAN.md S3)
        await Task.Delay(500);

        try
        {
            await Page.EvaluateAsync("""
                (() => {
                    var b = document.querySelector("button.fold-expand[data-fold='features']");
                    if (b) {
                        b.scrollIntoView({block:'start', behavior:'instant'});
                        window.scrollBy(0, -10);
                    }
                })()
            """);
            await Task.Delay(300);
        }
        catch { }

        await SaveScreenshot("whats-new-scenario-timeline.png");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FEATURE 20: Export Filtered HTML & CSV GIF
    // ═══════════════════════════════════════════════════════════════════

    [Fact(Skip = "Long-running GIF generation for wiki demos — unskip when regenerating wiki assets, then re-skip afterwards")]
    public async Task Feature20_Export_GIF()
    {
        BeginFrameCapture("feature20");
        await NavigateToReport();

        await JsClick(".status-toggle[data-status='Failed']"); await Hold(1.5);
        await JsClick(".export-btn[onclick*='export_html']"); await Hold(2);

        await Page.EvaluateAsync("""
            (() => {
                var overlay = document.createElement('div');
                overlay.style.cssText = 'position:fixed;inset:0;background:#f8f9fa;z-index:99999;display:flex;align-items:center;justify-content:center;';
                overlay.innerHTML = '<div style="font-size:18px;color:#57606a;font-family:sans-serif;">Opening downloaded file...</div>';
                document.body.appendChild(overlay);
            })()
        """);
        await Hold(2);

        await Page.EvaluateAsync("""
            (() => { var o = document.querySelector('[style*="z-index:99999"]'); if (o) o.remove(); })()
        """);
        await Task.Delay(200);

        await Hold(2);
        await ScrollToTop(); await Hold(1);

        StitchGif("whats-new-feature20-export.gif");
    }
}
