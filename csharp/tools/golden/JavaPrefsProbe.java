import java.util.prefs.Preferences;

/**
 * Java Preferences 가 레지스트리에 이름·값을 어떻게 바꿔 저장하는지 확인하는 도구.
 * 실제 앱 설정(<unnamed>)이 아니라 시험용 노드에만 쓰고, "clean" 으로 지웁니다.
 *   java JavaPrefsProbe write   → HKCU\Software\JavaSoft\Prefs\atbash_prefs_probe 에 저장
 *   java JavaPrefsProbe clean   → 그 노드를 지움
 * 저장하는 값은 PrefsSamples 목록과 같아야 함 (C# 테스트의 기대값).
 */
public class JavaPrefsProbe {
    static final String[][] SAMPLES = {
            {"theme", "LIGHT"},
            {"language", "KOREAN"},
            {"cipherMode", "18"},
            {"windowWidth", "1550"},
            {"saveSettingsEnabled", "true"},
            {"custom.themes.list", "ttt,hight 2,갈색 테마,Mixed한글"},
            {"custom.theme.갈색 테마.main.background", "-6724096"},
            {"naverClientId", "KqXpD39zL6gYUTWviEJc"},
            {"slash/and\\back", "a/b\\c"},
            {"lastUpdateCheck", "1762992000000"},
            {"emoji😀", "값 😀"},
            {"tab\there", "line\nbreak"},
    };

    public static void main(String[] args) throws Exception {
        Preferences node = Preferences.userRoot().node("atbash_prefs_probe");
        if (args[0].equals("clean")) {
            node.removeNode();
            Preferences.userRoot().flush();
            System.out.println("removed");
            return;
        }
        for (String[] kv : SAMPLES) node.put(kv[0], kv[1]);
        node.flush();
        System.out.println("written " + SAMPLES.length);
    }
}
