package warmup;

/** Built once in the image with app/pom.xml, so Maven fetches everything the app needs. */
public final class Warmup {
    private Warmup() {}

    public static void main(String[] args) {
        System.out.println(com.anthropic.client.AnthropicClient.class.getName());
    }
}
