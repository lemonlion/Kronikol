public class Fold {
    public static void main(String[] a) {
        String kelvin = "K";
        System.out.println("java equalsIgnoreCase Kelvin==k: " + kelvin.equalsIgnoreCase("k"));
        System.out.println("java CASE_INSENSITIVE_ORDER Kelvin vs k: " + String.CASE_INSENSITIVE_ORDER.compare(kelvin, "k"));
        System.out.println("java dotless i == i: " + "ı".equalsIgnoreCase("i"));
    }
}
