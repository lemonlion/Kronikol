import java.io.IOException;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.zip.GZIPOutputStream;

/**
 * Gzips a file with GZIPOutputStream, as Kronikol4J writes its report (plans/REPORT_FORMAT_PLAN.md Q12), for the
 * reader's facts. Run by make-fixtures.py: java GzipFixture.java source target
 */
public class GzipFixture {
    public static void main(String[] args) throws IOException {
        byte[] data = Files.readAllBytes(Path.of(args[0]));
        try (OutputStream out = new GZIPOutputStream(Files.newOutputStream(Path.of(args[1])))) {
            out.write(data);
        }
    }
}
