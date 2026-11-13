/**
 * 비즈네르(Vigenère) 암호의 암호화 및 복호화를 처리하는 유틸리티 클래스입니다.
 *
 * - 영문자(A-Z, a-z): 일반 비즈네르. 키워드 글자마다 0~25칸 이동
 * - 한글 음절(가-힣): 키워드 글자의 코드값만큼 11,172자 안에서 이동
 * - 그 밖의 글자: 그대로 둠 (키워드 위치도 넘어가지 않음)
 *
 * 8.1.1 에서 고침: 예전에는 Character.isLetter 로 판별해서 한글·악센트 문자·일본어도
 * 영문 규칙으로 처리됐고, 키워드에 숫자·공백이 있으면 이동 값이 음수가 됐다.
 * 그래서 그런 경우 복호화하면 원래 글자로 돌아오지 않았다.
 * 영문 텍스트 + 영문 키워드의 결과는 예전과 같다.
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
            boolean upper = character >= 'A' && character <= 'Z';
            boolean lower = character >= 'a' && character <= 'z';
            if (upper || lower) {
                char base = upper ? 'A' : 'a';
                // 키워드에 영문자가 아닌 글자가 있어도 0~25 로 맞춤
                int keyShift = Math.floorMod(key.charAt(keyIndex % key.length()) - 'A', 26);
                if (!encrypt) keyShift = (26 - keyShift) % 26;

                result.append((char) (base + (character - base + keyShift) % 26));
                keyIndex++;
            } else if (character >= '가' && character <= '힣') {
                // 한글: 키워드 글자의 코드값을 이동 값으로 사용
                int keyShift = key.charAt(keyIndex % key.length()) % 11172;
                if (!encrypt) keyShift = (11172 - keyShift) % 11172;

                result.append((char) ('가' + (character - '가' + keyShift) % 11172));
                keyIndex++;
            } else {
                result.append(character);
            }
        }
        return result.toString();
    }
}
