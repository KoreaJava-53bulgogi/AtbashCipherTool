import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.Random;

/**
 * Java 로 암호화한 샘플을 만듭니다. C# 테스트가 이것을 풀 수 있는지 확인합니다. (Java → C#)
 *   aes/text.json      : 비밀번호, 원문, Java AESFileCipher.encryptText 결과
 *   aes/file_N.bin/.enc: 원본 파일과 Java AESFileCipher.encryptFile 결과
 *
 * 실행: java -cp "target\classes;csharp\tools\golden\out" AesFixtures csharp\tests\Atbash.Core.Tests\TestData\aes
 */
public class AesFixtures {
    static final String[][] TEXTS = {
            {"pw1234", "Hello, World!"},
            {"비밀번호", "안녕하세요 아트배쉬 암호 7"},
            {"p@ss w0rd!", "mixed 한글 English 日本語 😀"},
            {"😀🔑", "emoji password"},
            {"x", ""},
            {"long-" + "k".repeat(200), "line1\nline2\r\n\ttab"},
    };
    static final int[] FILE_SIZES = {0, 1, 15, 16, 4095, 4096, 4097, 65543, 100000};
    static final String FILE_PASSWORD = "파일-pw-1234";

    public static void main(String[] args) throws Exception {
        File dir = new File(args[0]);
        dir.mkdirs();

        try (PrintWriter out = new PrintWriter(new File(dir, "text.json"), StandardCharsets.UTF_8)) {
            out.println("[");
            for (int i = 0; i < TEXTS.length; i++) {
                String cipher = AESFileCipher.encryptText(TEXTS[i][1], TEXTS[i][0]);
                out.print("{\"password\":" + units(TEXTS[i][0]) + ",\"plain\":" + units(TEXTS[i][1])
                        + ",\"cipher\":\"" + cipher + "\"}");
                out.println(i < TEXTS.length - 1 ? "," : "");
            }
            out.println("]");
        }

        Random random = new Random(20261113L); // 매번 같은 내용
        for (int size : FILE_SIZES) {
            byte[] data = new byte[size];
            random.nextBytes(data);
            File plain = new File(dir, "file_" + size + ".bin");
            Files.write(plain.toPath(), data);
            AESFileCipher.encryptFile(plain, new File(dir, "file_" + size + ".bin.enc"), FILE_PASSWORD);
        }
        File korean = new File(dir, "file_korean.txt");
        Files.writeString(korean.toPath(), "한글 텍스트 파일입니다.\n두 번째 줄 😀\n", StandardCharsets.UTF_8);
        AESFileCipher.encryptFile(korean, new File(dir, "file_korean.txt.enc"), FILE_PASSWORD);

        System.out.println("written to " + dir);
    }

    static String units(String s) {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < s.length(); i++) {
            if (i > 0) sb.append(',');
            sb.append((int) s.charAt(i));
        }
        return sb.append(']').toString();
    }
}
