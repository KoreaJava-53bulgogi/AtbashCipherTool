//! Atbash Cipher Tool Lite 의 핵심 로직.
//!
//! 변환 결과는 Java 버전(8.x)·C# 버전(9.x)과 한 글자도 달라서는 안 된다.
//! Java·C# 은 문자열을 UTF-16 단위로 다루므로, 여기서도 모든 변환을 UTF-16 단위(`u16`)로 한다.
//! (예: 스퀴탈레는 이모지의 서로게이트 쌍을 갈라 놓을 수 있는데, 이것은 Rust 의 `String` 에 담을 수 없다.)
//! 화면과 주고받을 때만 `String` 으로 바꾼다.
//!
//! 검증: `tests/golden.rs` (Java 8.1.0 이 낸 결과 6,930개), `tests/aes.rs` (Java 가 암호화한 텍스트 풀기).

pub mod core;
pub mod i18n;
pub mod settings;
