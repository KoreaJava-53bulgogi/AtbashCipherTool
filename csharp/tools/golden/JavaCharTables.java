import java.io.DataOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.util.Locale;

/**
 * Java 의 글자 규칙을 표로 저장합니다. C# 이 .NET 규칙 대신 이 표를 써서 Java 와 똑같이 동작하게 합니다.
 *   - Character.isLetter(char), Character.isUpperCase(char): 65,536개 글자 각각 (비트 표)
 *   - String.toUpperCase(Locale.ROOT): 대문자가 다른 글자로 바뀌는 모든 코드 포인트 (ß → SS 등)
 *
 * 파일 형식 (big-endian, DataOutputStream):
 *   byte[8192] isLetter 비트, byte[8192] isUpperCase 비트,
 *   int 개수, 그 뒤로 [int 코드포인트, short 길이, char... 대문자 결과]
 *
 * 실행: java -cp csharp\tools\golden\out JavaCharTables csharp\src\Atbash.Core\Resources\java-chars.bin
 */
public class JavaCharTables {
    public static void main(String[] args) throws Exception {
        File file = new File(args[0]);
        file.getParentFile().mkdirs();
        try (DataOutputStream out = new DataOutputStream(new FileOutputStream(file))) {
            byte[] letter = new byte[8192];
            byte[] upper = new byte[8192];
            for (int c = 0; c < 65536; c++) {
                if (Character.isLetter((char) c)) letter[c >> 3] |= (byte) (1 << (c & 7));
                if (Character.isUpperCase((char) c)) upper[c >> 3] |= (byte) (1 << (c & 7));
            }
            out.write(letter);
            out.write(upper);

            int count = 0;
            for (int cp = 0; cp <= Character.MAX_CODE_POINT; cp++) {
                if (cp >= 0xD800 && cp <= 0xDFFF) continue;
                String s = new String(Character.toChars(cp));
                if (!s.toUpperCase(Locale.ROOT).equals(s)) count++;
            }
            out.writeInt(count);
            for (int cp = 0; cp <= Character.MAX_CODE_POINT; cp++) {
                if (cp >= 0xD800 && cp <= 0xDFFF) continue;
                String s = new String(Character.toChars(cp));
                String u = s.toUpperCase(Locale.ROOT);
                if (u.equals(s)) continue;
                out.writeInt(cp);
                out.writeShort(u.length());
                for (int i = 0; i < u.length(); i++) out.writeChar(u.charAt(i));
            }
            System.out.println("uppercase mappings: " + count + ", java " + System.getProperty("java.version")
                    + ", default locale " + Locale.getDefault());
        }
    }
}
