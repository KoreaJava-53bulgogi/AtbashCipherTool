/**
 * 비즈네르(Vigenère) 암호의 암호화 및 복호화를 처리하는 유틸리티 클래스입니다.
 */
public class VigenereCipher {

    public static String encrypt(String text, String key) {
        return transform(text, key, true);
    }

    public static String decrypt(String text, String key) {
        return transform(text, key, false);
    }

    private static String transform(String text, String key, boolean encrypt) {
        if (text == null || key == null || key.isEmpty()) return text;

        StringBuilder result = new StringBuilder();
        int keyIndex = 0;
        key = key.toUpperCase();

        for (char character : text.toCharArray()) {
            if (Character.isLetter(character)) {
                boolean isUpper = Character.isUpperCase(character);
                char base = isUpper ? 'A' : 'a';
                int keyShift = key.charAt(keyIndex % key.length()) - 'A';
                if (!encrypt) keyShift = 26 - (keyShift % 26);

                char transformed = (char) (base + (character - base + keyShift) % 26);
                result.append(transformed);
                keyIndex++;
            } else if (character >= '가' && character <= '힣') {
                // 한글 지원 (자체 알고리즘: 키워드 각 문자의 코드값을 이동값으로 사용)
                int keyShift = key.charAt(keyIndex % key.length());
                if (!encrypt) keyShift = 11172 - (keyShift % 11172);

                char transformed = (char) ('가' + (character - '가' + keyShift) % 11172);
                result.append(transformed);
                keyIndex++;
            } else {
                result.append(character);
            }
        }
        return result.toString();
    }
}