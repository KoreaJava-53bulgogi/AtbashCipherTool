import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Java 버전의 변환 결과를 "정답 데이터"로 뽑습니다. C# 테스트가 이 결과와 똑같은지 비교합니다.
 *
 * 실행 (C:\java 에서, Java 클래스가 target\classes 에 컴파일된 상태):
 *   javac -cp target\classes -d csharp\tools\golden\out csharp\tools\golden\GoldenGen.java
 *   java  -cp "target\classes;csharp\tools\golden\out" GoldenGen csharp\tests\Atbash.Core.Tests\TestData\golden.json
 *
 * 문자열은 UTF-16 코드 단위(정수 배열)로 저장합니다. 짝이 맞지 않는 서로게이트 같은
 * 특이한 글자도 그대로 비교하기 위해서입니다.
 * 디코딩 실패처럼 앱이 오류 문구를 보여주는 경우는 결과 대신 "error": "DECODE" 로 저장합니다.
 */
public class GoldenGen {

    // 앱의 변환 방식 번호 (AtbashCipherGUI 의 CIPHER_* 와 같음). AES 는 결과가 매번 달라 제외.
    static final int ATBASH = 0, MORSE = 1, BINARY = 2, CAESAR_ENC = 3, CAESAR_DEC = 4,
            SCYTALE_ENC = 5, SCYTALE_DEC = 6, VIGENERE_ENC = 7, VIGENERE_DEC = 8,
            BASE64_ENC = 11, BASE64_DEC = 12, HEX_ENC = 13, HEX_DEC = 14, URL_ENC = 15, URL_DEC = 16,
            ROT13 = 17, HANGUL_ENC = 18, HANGUL_DEC = 19;

    static final List<String> TEXTS = new ArrayList<>(List.of(
            "",
            "Hello, World!",
            "The quick brown fox jumps over the lazy dog 0123456789",
            "안녕하세요 아트배쉬 암호 7",
            "한글 Hangul 혼합 123 !?",
            "가각힣 갂 ㄱㅏ ㅎ",
            "日本語とひらがな カタカナ",
            "中文简体 繁體",
            "Émile Zoë ß ı ſ ǅ ﬁ",
            "emoji 😀👍🏽 🇰🇷 and ZWJ 👨‍👩‍👧",
            "tab\there\nnew line\r\nwindows",
            "nbsp\u00A0ideo\u3000line\u2028sep",
            "   leading and trailing   ",
            "multiple    spaces\t\t\ttabs",
            "lone high \uD800 and low \uDC00 surrogates",
            "null\u0000char",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz",
            ".,? 1234567890",
            "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~",
            "a",
            "가",
            "x".repeat(37)
    ));

    static final List<String> MORSE_INPUTS = List.of(
            "... --- ...", ".... . .-.. .-.. --- / .-- --- .-. .-.. -..", ".-.-.- --..-- ..--..",
            "-----", "... / / ---", "/ / ", " / ", "..--..--", ".-.-.-.-.-", "... ---  ...",
            "...\t---", "... --- ... /", "/... ---", "- . ... - / ..---", ". -.-. --- -.-. -.-.");

    static final List<String> BINARY_INPUTS = List.of(
            "01001000 01101001", "0101", "11111111", "100000000", "0 1", "1".repeat(33),
            "11101100 10010101 10001000", "11000000", "  01000001  ", "01000001\t01000010\n01000011",
            "11110000 10011111 10011000 10000000", "10000000 10000000", "0");

    static final List<String> DECODE_INPUTS = List.of(
            "SGVsbG8=", "SGVsbG8", "7JWI64WV", "SGVs bG8=", "SGVsbG8-_w", "!!!not base64", "====", "QQ==",
            "/w==", "48 65 6C 6C 6F", "4869", "0x48 0x69", "48:69", "4G", "FF FE", "4", "EC 95 88",
            "a%20b%26c%3D%EC%95%88", "a+b", "%E", "%zz", "100%", "%41%42", "plain text", "%ED%95%9C%EA%B8%80",
            "%C0%AF", "a b");

    static final int[] SHIFTS = {-27, -1, 0, 1, 3, 13, 25, 26, 27, 1000, -1000, 11172};
    static final int[] DIAMETERS = {1, 2, 3, 5, 7, 40, 1000};
    static final String[] KEYWORDS = {"KEY", "key", "LEMON", "가나", "A1!", "Z", "a", "zZ9", "keyword with space"};

    static PrintWriter out;
    static boolean first = true;

    public static void main(String[] args) throws Exception {
        List<String> all = new ArrayList<>(TEXTS);
        all.addAll(MORSE_INPUTS);
        all.addAll(BINARY_INPUTS);
        all.addAll(DECODE_INPUTS);

        File file = new File(args[0]);
        file.getParentFile().mkdirs();
        out = new PrintWriter(file, StandardCharsets.UTF_8);
        out.println("[");
        int count = 0;
        for (String s : all) {
            emit(ATBASH, 0, "", s);
            emit(MORSE, 0, "", s);
            emit(BINARY, 0, "", s);
            emit(BASE64_ENC, 0, "", s);
            emit(BASE64_DEC, 0, "", s);
            emit(HEX_ENC, 0, "", s);
            emit(HEX_DEC, 0, "", s);
            emit(URL_ENC, 0, "", s);
            emit(URL_DEC, 0, "", s);
            emit(ROT13, 0, "", s);
            for (int k : SHIFTS) {
                emit(CAESAR_ENC, k, "", s);
                emit(CAESAR_DEC, k, "", s);
                emit(HANGUL_ENC, k, "", s);
                emit(HANGUL_DEC, k, "", s);
            }
            for (int d : DIAMETERS) {
                emit(SCYTALE_ENC, d, "", s);
                emit(SCYTALE_DEC, d, "", s);
            }
            for (String kw : KEYWORDS) {
                emit(VIGENERE_ENC, 0, kw, s);
                emit(VIGENERE_DEC, 0, kw, s);
            }
            count++;
        }
        out.println();
        out.println("]");
        out.close();
        System.out.println("inputs: " + count + ", written: " + file);
    }

    /** AtbashCipherGUI.performTransformation 과 같은 규칙으로 변환 */
    static void emit(int mode, int key, String keyword, String input) {
        String result;
        String error = null;
        try {
            switch (mode) {
                case ATBASH: result = AtbashCipher.transform(input); break;
                case MORSE: result = MorseCode.isMorseCode(input) ? MorseCode.fromMorse(input) : MorseCode.toMorse(input); break;
                case BINARY: result = BinaryCipher.isBinary(input) ? BinaryCipher.fromBinary(input) : BinaryCipher.toBinary(input); break;
                case CAESAR_ENC: result = CaesarCipher.encrypt(input, key); break;
                case CAESAR_DEC: result = CaesarCipher.decrypt(input, key); break;
                case SCYTALE_ENC: result = ScytaleCipher.encrypt(input, key); break;
                case SCYTALE_DEC: result = ScytaleCipher.decrypt(input, key); break;
                case VIGENERE_ENC: result = VigenereCipher.encrypt(input, keyword); break;
                case VIGENERE_DEC: result = VigenereCipher.decrypt(input, keyword); break;
                case BASE64_ENC: result = TextCodec.toBase64(input); break;
                case BASE64_DEC: result = TextCodec.fromBase64(input); break;
                case HEX_ENC: result = TextCodec.toHex(input); break;
                case HEX_DEC: result = TextCodec.fromHex(input); break;
                case URL_ENC: result = TextCodec.toUrl(input); break;
                case URL_DEC: result = TextCodec.fromUrl(input); break;
                case ROT13: result = TextCodec.rot13(input); break;
                case HANGUL_ENC: result = HangulCipher.encrypt(input, key); break;
                case HANGUL_DEC: result = HangulCipher.decrypt(input, key); break;
                default: throw new IllegalStateException();
            }
        } catch (Exception e) {
            // 앱에서는 performNewTransformation 이 이 경우 DECODE_ERROR 문구를 보여줌
            result = null;
            error = "DECODE";
        }
        if (!first) out.println(",");
        first = false;
        out.print("{\"mode\":" + mode + ",\"key\":" + key + ",\"keyword\":" + units(keyword)
                + ",\"input\":" + units(input) + ",");
        out.print(error != null ? "\"error\":\"" + error + "\"}" : "\"output\":" + units(result) + "}");
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
