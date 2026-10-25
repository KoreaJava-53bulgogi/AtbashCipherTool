import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URLConnection;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.List;

/**
 * 새 버전 설치 파일을 내려받아 검사한 뒤, 조용한 설치(/SILENT)로 실행합니다.
 *
 * 순서: download() → verify() → launchInstaller() → (호출한 쪽에서) 앱 종료
 * 설치 프로그램(Inno Setup)이 관리자 권한 확인(UAC)을 스스로 요청하고,
 * 설치가 끝나면 새 버전 앱을 다시 실행합니다. (Atbash.iss 의 [Run] 참고)
 */
public class UpdateInstaller {

    /** 내려받기 진행 상황을 알려받고, 취소 여부를 알려주는 통로 */
    public interface Progress {
        void update(long downloaded, long total); // total 을 모르면 -1

        boolean isCancelled();
    }

    /** 지문이 다를 때 (파일이 깨졌거나 바뀜) */
    public static class VerificationException extends IOException {
        VerificationException(String message) {
            super(message);
        }
    }

    private static final int TIMEOUT_MS = 15000;

    /**
     * 설치 파일을 임시 폴더에 내려받습니다.
     * @return 내려받은 파일. 취소되면 null
     */
    public static File download(String url, Progress progress) throws IOException {
        checkAllowedUrl(url);

        String name = url.substring(url.lastIndexOf('/') + 1).replaceAll("[^A-Za-z0-9._-]", "_");
        if (!name.toLowerCase().endsWith(".exe")) {
            throw new IOException("Installer must be an .exe file: " + name);
        }
        File dir = Files.createTempDirectory("atbash-update-").toFile();
        File target = new File(dir, name);

        URLConnection con = URI.create(url).toURL().openConnection();
        con.setConnectTimeout(TIMEOUT_MS);
        con.setReadTimeout(TIMEOUT_MS);
        if (con instanceof HttpURLConnection) {
            HttpURLConnection http = (HttpURLConnection) con;
            http.setInstanceFollowRedirects(true); // GitHub 는 다른 https 주소로 넘겨줌
            int code = http.getResponseCode();
            if (code != 200) throw new IOException("HTTP " + code);
        }
        long total = con.getContentLengthLong();

        try (InputStream in = con.getInputStream(); OutputStream out = Files.newOutputStream(target.toPath())) {
            byte[] buf = new byte[64 * 1024];
            long done = 0;
            int n;
            while ((n = in.read(buf)) != -1) {
                if (progress != null && progress.isCancelled()) {
                    out.close();
                    deleteQuietly(target);
                    return null;
                }
                out.write(buf, 0, n);
                done += n;
                if (progress != null) progress.update(done, total);
            }
        } catch (IOException e) {
            deleteQuietly(target);
            throw e;
        } finally {
            if (con instanceof HttpURLConnection) ((HttpURLConnection) con).disconnect();
        }
        return target;
    }

    /** 내려받은 파일의 SHA-256 지문이 version.json 의 값과 같은지 확인. 다르면 파일을 지우고 예외 */
    public static void verify(File file, String expectedSha256) throws IOException {
        String actual = sha256(file);
        if (!actual.equalsIgnoreCase(expectedSha256)) {
            deleteQuietly(file);
            throw new VerificationException("SHA-256 mismatch: expected " + expectedSha256 + ", got " + actual);
        }
    }

    public static String sha256(File file) throws IOException {
        try {
            MessageDigest md = MessageDigest.getInstance("SHA-256");
            try (InputStream in = Files.newInputStream(file.toPath())) {
                byte[] buf = new byte[64 * 1024];
                int n;
                while ((n = in.read(buf)) != -1) md.update(buf, 0, n);
            }
            return HexFormat.of().formatHex(md.digest());
        } catch (NoSuchAlgorithmException e) {
            throw new IOException(e);
        }
    }

    /**
     * 설치 프로그램을 조용한 설치로 실행합니다. 이 메서드가 끝나면 앱을 바로 종료해야
     * 설치 프로그램이 앱 파일을 교체할 수 있습니다.
     *  /SILENT            질문 없이 진행률 창만 표시
     *  /SUPPRESSMSGBOXES  확인 창 자동 처리
     *  /NORESTART         Windows 재시작 안 함
     *  /CLOSEAPPLICATIONS 아직 켜져 있는 앱이 있으면 닫음
     */
    public static void launchInstaller(File installer) throws IOException {
        new ProcessBuilder(List.of(installer.getAbsolutePath(),
                "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS"))
                .directory(installer.getParentFile())
                .start();
    }

    /** https 만 허용. 테스트 모드(ATBASH_UPDATE_URL 설정)에서만 내 PC 파일(file:)도 허용 */
    static void checkAllowedUrl(String url) throws IOException {
        String lower = url.toLowerCase();
        if (lower.startsWith("https://")) return;
        if (lower.startsWith("file:") && UpdateChecker.isTestMode()) return;
        throw new IOException("Only https:// download links are allowed: " + url);
    }

    private static void deleteQuietly(File f) {
        try {
            Files.deleteIfExists(f.toPath());
        } catch (IOException ignored) {
            // temp file; Windows cleans the temp folder eventually
        }
    }
}
