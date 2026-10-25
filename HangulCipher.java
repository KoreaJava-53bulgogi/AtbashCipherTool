/**
 * 한글 자모 암호.
 * 한글 음절을 초성·중성·종성으로 나눈 뒤 각각을 이동 값만큼 밀고 다시 합칩니다.
 *   초성 19개, 중성 21개 안에서 돌고, 받침은 있는 글자끼리(27개)만 돌기 때문에
 *   받침 유무가 바뀌지 않습니다.
 *   예) 이동 값 1: 한 → 갡  (초성 ㅎ은 마지막이라 ㄱ으로, ㅏ→ㅐ, 받침 ㄴ→ㄵ)
 * 한글 음절이 아닌 글자(영어, 숫자, 낱자모 ㄱ/ㅏ 등)는 그대로 둡니다.
 */
public class HangulCipher {

    private static final int BASE = 0xAC00;  // '가'
    private static final int LAST = 0xD7A3;  // '힣'
    private static final int CHO_COUNT = 19;
    private static final int JUNG_COUNT = 21;
    private static final int JONG_COUNT = 28; // 0 = 받침 없음, 1~27 = 받침

    public static String encrypt(String text, int shift) {
        return transform(text, shift);
    }

    public static String decrypt(String text, int shift) {
        return transform(text, -shift);
    }

    private static String transform(String text, int shift) {
        if (text == null) return null;
        StringBuilder sb = new StringBuilder(text.length());
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (c < BASE || c > LAST) {
                sb.append(c);
                continue;
            }
            int offset = c - BASE;
            int cho = offset / (JUNG_COUNT * JONG_COUNT);
            int jung = (offset % (JUNG_COUNT * JONG_COUNT)) / JONG_COUNT;
            int jong = offset % JONG_COUNT;

            cho = Math.floorMod(cho + shift, CHO_COUNT);
            jung = Math.floorMod(jung + shift, JUNG_COUNT);
            if (jong != 0) {
                jong = Math.floorMod(jong - 1 + shift, JONG_COUNT - 1) + 1;
            }
            sb.append((char) (BASE + (cho * JUNG_COUNT + jung) * JONG_COUNT + jong));
        }
        return sb.toString();
    }
}
