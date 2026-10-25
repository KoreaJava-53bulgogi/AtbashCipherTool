import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
import java.util.TreeMap;
import java.util.prefs.Preferences;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * JSON 형식의 언어팩 파일을 로드하고 관리하는 클래스입니다.
 */
public class LanguageManager {
    private final Map<String, String> strings = new HashMap<>();

    /**
     * 지정된 언어에 해당하는 JSON 파일을 로드합니다.
     * @param lang 로드할 언어
     */
    public void loadLanguage(AtbashCipherGUI.Language lang) {
        strings.clear();
        String fileName;
        switch (lang) {
            case KOREAN:
                fileName = "ko.json";
                break;
            case CHINESE:
                fileName = "zh.json";
                break;
            case JAPANESE:
                fileName = "ja.json";
                break;
            case ENGLISH:
            default:
                fileName = "en.json";
                break;
        }
        // 영어를 먼저 읽어두고 선택한 언어로 덮어씀.
        // 번역이 빠진 문구는 키 이름(!KEY!) 대신 영어로 보이게 됨.
        if (!"en.json".equals(fileName)) {
            loadResource("en.json");
        }
        loadResource(fileName);
    }

    private void loadResource(String fileName) {
        try (InputStream is = getClass().getResourceAsStream("/" + fileName)) {
            loadLanguageFromStream(is);
        } catch (Exception e) {
            e.printStackTrace();
        }
    }

    public void loadLanguageFromStream(InputStream is) {
        if (is == null) return;
        try {
            String content = new String(is.readAllBytes(), StandardCharsets.UTF_8);
            // 정규식 개선: 값에 공백이나 특수문자가 포함된 경우를 더 잘 처리하도록 수정
            Pattern pattern = Pattern.compile("\"([^\"]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            Matcher matcher = pattern.matcher(content);
            while (matcher.find()) {
                String key = matcher.group(1);
                String value = matcher.group(2).replace("\\\"", "\"").replace("\\n", "\n");
                strings.put(key, value);
            }
        } catch (Exception e) {
            System.err.println("언어팩 파싱 중 오류 발생: " + e.getMessage());
        }
    }

    public String get(String key) {
        return strings.getOrDefault(key, "!" + key + "!"); // 키가 없는 경우 눈에 띄게 표시
    }

    public TreeMap<String, String> getAllStrings() {
        return new TreeMap<>(this.strings);
    }
}