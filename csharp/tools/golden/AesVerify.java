import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/**
 * C# 이 암호화한 것을 Java 로 풀어 봅니다. (C# → Java) C# 테스트가 실행합니다.
 *   java AesVerify text <비밀번호파일> <암호문파일> <결과파일>
 *   java AesVerify file <비밀번호파일> <.enc 파일> <결과파일>
 * 비밀번호와 암호문은 명령줄 글자 깨짐을 피하려고 UTF-8 파일로 주고받습니다.
 * 성공하면 "OK", 실패하면 "FAIL: 이유" 를 출력합니다.
 */
public class AesVerify {
    public static void main(String[] args) {
        try {
            String password = Files.readString(new File(args[1]).toPath(), StandardCharsets.UTF_8);
            File out = new File(args[3]);
            if (args[0].equals("text")) {
                String cipher = Files.readString(new File(args[2]).toPath(), StandardCharsets.UTF_8);
                String plain = AESFileCipher.decryptText(cipher, password);
                Files.write(out.toPath(), plain.getBytes(StandardCharsets.UTF_8));
            } else {
                AESFileCipher.decryptFile(new File(args[2]), out, password);
            }
            System.out.println("OK");
        } catch (Exception e) {
            System.out.println("FAIL: " + e);
        }
    }
}
