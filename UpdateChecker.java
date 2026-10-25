import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 인터넷에 올려둔 version.json 을 읽어 새 버전이 있는지 확인합니다.
 *
 * version.json 형식:
 * {
 *   "version": "8.0.0",
 *   "url": "https://github.com/.../releases/latest",
 *   "notes": "변경 내용 한 줄 요약"
 * }
 */
public class UpdateChecker {

    /** 최신 버전 정보 파일 주소 (GitHub 저장소의 main 브랜치) */
    public static final String VERSION_URL =
            "https://raw.githubusercontent.com/KoreaJava-53bulgogi/AtbashCipherTool/main/version.json";

    /** 다운로드 안내 페이지 (version.json 에 url 이 없을 때 사용) */
    public static final String DEFAULT_DOWNLOAD_URL =
            "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/latest";

    private static final int TIMEOUT_MS = 5000;

    /** 서버에서 읽은 최신 버전 정보 */
    public static class UpdateInfo {
        public final String version;
        public final String downloadUrl;
        public final String notes;

        UpdateInfo(String version, String downloadUrl, String notes) {
            this.version = version;
            this.downloadUrl = downloadUrl;
            this.notes = notes;
        }
    }

    /**
     * 최신 버전 정보를 가져옵니다. 네트워크가 느려도 앱이 멈추지 않도록
     * 반드시 백그라운드 스레드에서 호출하세요.
     */
    public static UpdateInfo fetchLatest() throws IOException {
        HttpURLConnection con = (HttpURLConnection) URI.create(VERSION_URL).toURL().openConnection();
        con.setConnectTimeout(TIMEOUT_MS);
        con.setReadTimeout(TIMEOUT_MS);
        con.setRequestProperty("Cache-Control", "no-cache");
        try {
            int code = con.getResponseCode();
            if (code != 200) {
                throw new IOException("HTTP " + code);
            }
            try (InputStream in = con.getInputStream()) {
                return parse(new String(in.readAllBytes(), StandardCharsets.UTF_8));
            }
        } finally {
            con.disconnect();
        }
    }

    static UpdateInfo parse(String json) throws IOException {
        String version = field(json, "version");
        if (version == null || version.isBlank()) {
            throw new IOException("version.json has no \"version\" field");
        }
        String url = field(json, "url");
        String notes = field(json, "notes");
        return new UpdateInfo(version.trim(),
                (url == null || url.isBlank()) ? DEFAULT_DOWNLOAD_URL : url.trim(),
                notes == null ? "" : notes.replace("\\n", "\n"));
    }

    private static String field(String json, String name) {
        Matcher m = Pattern.compile("\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").matcher(json);
        return m.find() ? m.group(1) : null;
    }

    /**
     * 버전 문자열을 숫자 단위로 비교합니다. ("8.0.10" > "8.0.9")
     * @return a 가 더 새 버전이면 양수, 같으면 0, 더 오래됐으면 음수
     */
    public static int compareVersions(String a, String b) {
        String[] pa = a.split("[.\\-]");
        String[] pb = b.split("[.\\-]");
        int n = Math.max(pa.length, pb.length);
        for (int i = 0; i < n; i++) {
            int x = i < pa.length ? toInt(pa[i]) : 0;
            int y = i < pb.length ? toInt(pb[i]) : 0;
            if (x != y) return Integer.compare(x, y);
        }
        return 0;
    }

    private static int toInt(String s) {
        try {
            return Integer.parseInt(s.replaceAll("\\D", ""));
        } catch (NumberFormatException e) {
            return 0;
        }
    }
}
