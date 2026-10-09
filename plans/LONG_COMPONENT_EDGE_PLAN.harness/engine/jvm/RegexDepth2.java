import java.util.regex.Pattern;

// java [-Xss..] RegexDepth2.java  — the other recursing PlantUML patterns on the JVM, as Pattern2 compiles them
// (CASE_INSENSITIVE, %g expanded). Reports match/nomatch/SOE per input length.
public class RegexDepth2 {
    static final String G = "\"“”";
    static final Pattern LIST = Pattern.compile("^(\\*+)([^*]+(?:[^*]|\\*\\*[^*]+\\*\\*)*)$", Pattern.CASE_INSENSITIVE);
    static final Pattern COLOR = Pattern.compile("^(\\<color[\\s:]+(#[0-9a-fA-F]{1,6}|#?\\w+)[\\s ]*\\>(.*?)\\</color\\>)", Pattern.CASE_INSENSITIVE);
    static final Pattern GROUPING = Pattern.compile("^(&[\\s ]*)?(opt|alt|loop|par|par2|break|critical|else|end|also|group|partition)((?<!else)(?<!also)(?<!end)#\\w+)?(?:[\\s ]+(#\\w+))?(?:[\\s ]+(.*?))?$", Pattern.CASE_INSENSITIVE);
    static final Pattern QUOTED_LAZY = Pattern.compile("^[" + G + "].+?[" + G + "] as x$", Pattern.CASE_INSENSITIVE);

    static String fill(int n) { StringBuilder sb = new StringBuilder(); while (sb.length() < n) sb.append("INSERT INTO orders_archive_001, "); sb.setLength(n); return sb.toString(); }

    static String run(Pattern p, String s, boolean find) {
        try { return (find ? p.matcher(s).find() : p.matcher(s).matches()) ? "match" : "nomatch"; }
        catch (StackOverflowError e) { return "SOE"; }
    }

    public static void main(String[] args) {
        System.out.println("java " + System.getProperty("java.version"));
        for (int n : new int[] { 300, 1000, 3000, 10000, 30000, 100000 }) {
            String body = fill(n);
            System.out.printf("n=%7d  creole list '**name**' = %-7s | <color> no close = %-7s | loop label = %-7s | \"name\" as x (.+?) = %-7s%n", n,
                run(LIST, "**" + body + "**", true),
                run(COLOR, "<color:white>" + body, true),
                run(GROUPING, "loop " + body, true),
                run(QUOTED_LAZY, "\"" + body + "\" as x", true));
        }
    }
}
