import java.util.regex.Matcher;
import java.util.regex.Pattern;

// java -Xss<n> RegexDepth.java [lengths...]
// Matches PlantUML's Labels patterns (as Pattern2 compiles them on the JVM: %g -> "“”U+E121, CASE_INSENSITIVE) against
// a quoted label of each length, plus a control pattern that IS recursive on the JVM (a group repeated per char),
// and reports the longest input each one survives under this -Xss.
public class RegexDepth {
    static final String G = "\"“”";
    static final Pattern BOTH = Pattern.compile("^[" + G + "]([^" + G + "]+)[" + G + "]([^" + G + "]+)[" + G + "]([^" + G + "]+)[" + G + "]$", Pattern.CASE_INSENSITIVE);
    static final Pattern FIRST = Pattern.compile("^[" + G + "]([^" + G + "]+)[" + G + "]([^" + G + "]+)$", Pattern.CASE_INSENSITIVE);
    static final Pattern SECOND = Pattern.compile("^([^" + G + "]+)[" + G + "]([^" + G + "]+)[" + G + "]$", Pattern.CASE_INSENSITIVE);
    // control: a capturing group under + recurses per iteration in OpenJDK (Loop/GroupHead)
    static final Pattern GROUPED = Pattern.compile("^[" + G + "]((?:[^" + G + "])+)[" + G + "]$", Pattern.CASE_INSENSITIVE);
    static final Pattern ALT = Pattern.compile("^[" + G + "]((?:a|[^" + G + "])+)[" + G + "]$", Pattern.CASE_INSENSITIVE);

    static String label(int n, boolean quoted) {
        StringBuilder sb = new StringBuilder();
        String unit = "INSERT INTO orders_archive_001, ";
        while (sb.length() < n) sb.append(unit);
        sb.setLength(n);
        return quoted ? "\"" + sb + "\"" : sb.toString();
    }

    static String run(Pattern p, String s) {
        try {
            Matcher m = p.matcher(s);
            return m.matches() ? "match" : "nomatch";
        } catch (StackOverflowError e) {
            return "SOE";
        }
    }

    public static void main(String[] args) {
        int[] lens = args.length > 0 ? java.util.Arrays.stream(args).mapToInt(Integer::parseInt).toArray()
                : new int[] { 1000, 2000, 10000, 100000, 1000000 };
        System.out.println("java " + System.getProperty("java.version") + ", Xss from command line");
        for (int n : lens) {
            String q = label(n, true), u = label(n, false);
            System.out.printf("len=%8d  BOTH(quoted)=%-7s FIRST(quoted)=%-7s SECOND(unquoted)=%-7s | control (?:[^g])+ =%-7s (?:a|[^g])+ =%-7s%n",
                    n, run(BOTH, q), run(FIRST, q), run(SECOND, u), run(GROUPED, q), run(ALT, q));
        }
    }
}
