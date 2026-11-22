fn main() {
    // ui/app.slint 을 Rust 코드로 바꿔서 실행 파일에 넣음
    slint_build::compile("ui/app.slint").expect("ui/app.slint");

    // Windows: 탐색기·작업 표시줄에 보이는 아이콘과 파일 속성(제품 이름, 버전)
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let mut res = winresource::WindowsResource::new();
        res.set_icon("../csharp/src/Atbash.App/Assets/app.ico");
        res.set("ProductName", "Atbash Cipher Tool Lite");
        res.set("FileDescription", "Atbash Cipher Tool Lite");
        res.set("CompanyName", "June");
        res.compile().expect("Windows resources (icon)");
    }
    println!("cargo:rerun-if-changed=../csharp/src/Atbash.App/Assets/app.ico");
}
