// S7, the Kronikol4J writer: two runs of the three tests producers.py feeds to `kronikol ingest`, written through
// Kronikol4J's own report serializer (ReportData, ReportDataSerializer.toJson), so `diff --body` is measured on the
// records the Java port writes. The second run makes the health checks in another order, creates its order under a
// new id, and queries three times where the first queried four.
//
//   java -cp "<core.jar>;<diagram.jar>;<report.jar>" Kronikol4JRuns.java <out dir>
//
// Measured with the 0.1.23-SNAPSHOT jars of a local Kronikol4J build (~/.m2/repository/io/kronikol/), which have no
// dependencies of their own.
import io.kronikol.core.tracking.Method;
import io.kronikol.core.tracking.RequestResponseLog;
import io.kronikol.core.tracking.RequestResponseType;
import io.kronikol.core.tracking.StatusCode;
import io.kronikol.report.data.ReportData;
import io.kronikol.report.data.ReportDataSerializer;
import io.kronikol.report.model.ExecutionStatus;
import io.kronikol.report.model.Feature;
import io.kronikol.report.model.Scenario;

import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

public class Kronikol4JRuns {
    private static OffsetDateTime at = OffsetDateTime.of(2026, 10, 7, 10, 0, 0, 0, ZoneOffset.UTC);

    public static void main(String[] args) throws Exception {
        Path out = Path.of(args[0]);
        Files.createDirectories(out);
        for (int run = 1; run <= 2; run++) {
            boolean second = run == 2;
            Map<String, List<RequestResponseLog>> logs = new LinkedHashMap<>();
            List<String> health = second ? List.of("goat", "supplier", "kitchen") : List.of("goat", "kitchen", "supplier");
            for (String service : health) {
                String status = second && service.equals("kitchen") ? "degraded" : "ok";
                add(logs, "t-health", service, Method.Http.GET, "http://" + service + ":80/health", null,
                    "{\"status\":\"" + status + "\",\"service\":\"" + service + "\"}");
            }
            String order = UUID.randomUUID().toString();
            add(logs, "t-order", "breakfast", Method.Http.POST, "http://breakfast:80/orders", "{\"items\":2}", "{\"id\":\"" + order + "\"}");
            add(logs, "t-order", "breakfast", Method.Http.PATCH, "http://breakfast:80/orders/" + order + "/status",
                "{\"status\":\"Ready\"}", "{\"id\":\"" + order + "\",\"status\":\"Ready\",\"run\":" + run + "}");
            for (int n = 0; n < (second ? 3 : 4); n++)
                add(logs, "t-query", "CosmosDB", Method.of("QUERY"), "https://cosmos:8081/orders", "{\"page\":" + n + "}", "{\"rows\":" + n + "}");

            List<Scenario> scenarios = List.of(
                new Scenario("health › all three answer", "t-health", ExecutionStatus.PASSED, 100, null),
                new Scenario("orders › an order is marked ready", "t-order", ExecutionStatus.PASSED, 100, null),
                new Scenario("orders › the list pages", "t-query", ExecutionStatus.PASSED, 100, null));
            ReportData data = new ReportData("0.1.23-SNAPSHOT", Instant.parse("2026-10-07T10:00:00Z"), Instant.parse("2026-10-07T10:05:00Z"),
                List.of(new Feature("breakfast.spec", scenarios)), Map.of(), logs);
            Files.writeString(out.resolve("java-" + run + ".json"), ReportDataSerializer.toJson(data));
        }
    }

    private static void add(Map<String, List<RequestResponseLog>> logs, String test, String service, Method method, String uri,
                            String request, String response) {
        UUID id = UUID.randomUUID();
        UUID trace = UUID.randomUUID();
        at = at.plusNanos(50_000_000L);
        List<RequestResponseLog> calls = logs.computeIfAbsent(test, key -> new ArrayList<>());
        calls.add(RequestResponseLog.builder().testName(test).testId(test).method(method).content(request).uri(URI.create(uri))
            .headers(List.of()).serviceName(service).callerName("web").type(RequestResponseType.REQUEST).traceId(trace)
            .requestResponseId(id).timestamp(at).build());
        calls.add(RequestResponseLog.builder().testName(test).testId(test).method(method).content(response).uri(URI.create(uri))
            .headers(List.of()).serviceName(service).callerName("web").type(RequestResponseType.RESPONSE).traceId(trace)
            .requestResponseId(id).statusCode(StatusCode.of(200)).timestamp(at.plusNanos(30_000_000L)).build());
    }
}
