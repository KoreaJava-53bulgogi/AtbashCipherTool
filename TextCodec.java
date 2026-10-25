import java.net.URLDecoder;
import java.net.URLEncoder;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

/**
 * 간단한 인코딩: Base64, 16진수(Hex), URL 인코딩, ROT13.
 * 암호가 아니라 표기 방식을 바꾸는 것이므로 누구나 되돌릴 수 있습니다.
 * 글자는 모두 UTF-8 바이트 기준으로 변환합니다.
 *
 * 디코딩에 실패하면 IllegalArgumentException 을 던집니다.
 */
public class TextCodec {

    // --- Base64 ---------------------------------------------------------

    public static String toBase64(String text) {
        return Base64.getEncoder().encodeToString(text.getBytes(StandardCharsets.UTF_8));
    }

    /** 줄바꿈/공백, URL-safe 문자(-_), 빠진 '=' 패딩도 받아줍니다. */
    public static String fromBase64(String text) {
        String s = text.replaceAll("\\s", "").replace('-', '+').replace('_', '/');
        while (s.length() % 4 != 0) s += "=";
        return utf8(Base64.getDecoder().decode(s));
    }

    // --- Hex ------------------------------------------------------------

    /** "Hi" → "48 69" */
    public static String toHex(String text) {
        byte[] bytes = text.getBytes(StandardCharsets.UTF_8);
        StringBuilder sb = new StringBuilder(bytes.length * 3);
        for (int i = 0; i < bytes.length; i++) {
            if (i > 0) sb.append(' ');
            sb.append(String.format("%02X", bytes[i] & 0xFF));
        }
        return sb.toString();
    }

    /** "48 69", "4869", "0x48 0x69", "48:69" 모두 받아줍니다. */
    public static String fromHex(String text) {
        String s = text.replaceAll("(?i)0x", "").replaceAll("[\\s:,\\-]", "");
        if (s.length() % 2 != 0 || !s.matches("[0-9A-Fa-f]*")) {
            throw new IllegalArgumentException("not hex");
        }
        byte[] bytes = new byte[s.length() / 2];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) Integer.parseInt(s.substring(i * 2, i * 2 + 2), 16);
        }
        return utf8(bytes);
    }

    // --- URL ------------------------------------------------------------

    /** 공백은 %20 으로 바꿉니다. (주소창에 붙여넣기 좋게) */
    public static String toUrl(String text) {
        return URLEncoder.encode(text, StandardCharsets.UTF_8).replace("+", "%20");
    }

    public static String fromUrl(String text) {
        return URLDecoder.decode(text, StandardCharsets.UTF_8);
    }

    // --- ROT13 ----------------------------------------------------------

    /** 영문자만 13칸 이동. 두 번 적용하면 원래대로 돌아옵니다. */
    public static String rot13(String text) {
        StringBuilder sb = new StringBuilder(text.length());
        for (char c : text.toCharArray()) {
            if (c >= 'a' && c <= 'z') sb.append((char) ('a' + (c - 'a' + 13) % 26));
            else if (c >= 'A' && c <= 'Z') sb.append((char) ('A' + (c - 'A' + 13) % 26));
            else sb.append(c);
        }
        return sb.toString();
    }

    // --------------------------------------------------------------------

    /** 올바른 UTF-8 이 아니면(=원래 글자가 아니었으면) 실패로 처리 */
    private static String utf8(byte[] bytes) {
        try {
            return StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(bytes)).toString();
        } catch (CharacterCodingException e) {
            throw new IllegalArgumentException("decoded bytes are not UTF-8 text", e);
        }
    }
}
