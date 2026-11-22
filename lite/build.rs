fn main() {
    // ui/app.slint 을 Rust 코드로 바꿔서 실행 파일에 넣음
    slint_build::compile("ui/app.slint").expect("ui/app.slint");
}
