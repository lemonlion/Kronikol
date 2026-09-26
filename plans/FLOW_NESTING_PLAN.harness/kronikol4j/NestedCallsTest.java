package check;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import io.kronikol.core.constants.TrackingHeaders;
import io.kronikol.core.context.TestIdentityScope;
import io.kronikol.core.tracking.RequestResponseLog;
import io.kronikol.core.tracking.RequestResponseLogger;
import io.kronikol.http.HttpTrackingConfig;
import io.kronikol.http.TrackingHttpClient;
import io.kronikol.junit5.KronikolExtension;
import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;

/**
 * FLOW_NESTING_PLAN §7.5: a Kronikol4J report with a call made while another was waiting. The test calls
 * "api" through a TrackingHttpClient; api's handler, inside the test identity the request carried (what
 * KronikolServletFilter does for a servlet app), calls "db" through its own TrackingHttpClient before it
 * answers. Prints the capture order the report keeps: ids and names only.
 */
@ExtendWith(KronikolExtension.class)
class NestedCallsTest {

    @Test
    void theApiCallsTheDatabaseWhileTheTestWaits() throws Exception {
        HttpServer db = HttpServer.create(new InetSocketAddress("localhost", 0), 0);
        db.createContext("/items", exchange -> answer(exchange, "{\"items\":1}"));
        db.start();

        HttpClient apiClient = new TrackingHttpClient(HttpClient.newHttpClient(),
            HttpTrackingConfig.builder().callerName("api").fixedServiceName("db").build());

        HttpServer api = HttpServer.create(new InetSocketAddress("localhost", 0), 0);
        api.createContext("/orders", exchange -> {
            String name = exchange.getRequestHeaders().getFirst(TrackingHeaders.CURRENT_TEST_NAME);
            String id = exchange.getRequestHeaders().getFirst(TrackingHeaders.CURRENT_TEST_ID);
            try (var scope = TestIdentityScope.begin(name, id)) {
                HttpResponse<String> inner = apiClient.send(
                    HttpRequest.newBuilder(URI.create("http://localhost:" + db.getAddress().getPort() + "/items")).GET().build(),
                    HttpResponse.BodyHandlers.ofString());
                answer(exchange, "{\"db\":" + inner.statusCode() + "}");
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new IOException(e);
            }
        });
        api.start();

        try {
            HttpClient testClient = new TrackingHttpClient(HttpClient.newHttpClient(),
                HttpTrackingConfig.builder().callerName("test").fixedServiceName("api").build());
            HttpResponse<String> response = testClient.send(
                HttpRequest.newBuilder(URI.create("http://localhost:" + api.getAddress().getPort() + "/orders")).GET().build(),
                HttpResponse.BodyHandlers.ofString());
            assertEquals(200, response.statusCode());
        } finally {
            api.stop(0);
            db.stop(0);
        }

        for (RequestResponseLog log : RequestResponseLogger.getAllLogs()) {
            System.out.println("CAPTURE " + log.type() + " " + log.callerName() + " -> " + log.serviceName()
                + " rr=" + log.requestResponseId() + " trace=" + log.traceId());
        }
    }

    private static void answer(HttpExchange exchange, String body) throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.sendResponseHeaders(200, bytes.length);
        try (OutputStream out = exchange.getResponseBody()) {
            out.write(bytes);
        }
    }
}
