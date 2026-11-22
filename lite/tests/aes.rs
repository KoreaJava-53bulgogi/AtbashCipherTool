//! AES 텍스트: Java 가 암호화한 것을 풀 수 있는지, 그리고 틀린 비밀번호·잘못된 입력을 알맞게 알리는지.
//! 데이터는 C# 버전과 같은 파일: csharp/tests/Atbash.Core.Tests/TestData/aes/

use atbash_lite::core::aes::{decrypt, decrypt_text, encrypt_text, AesError};
use atbash_lite::core::engine::{transform, Mode, TransformError};
use atbash_lite::core::to_units;
use serde_json::Value;

const AES_DIR: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../csharp/tests/Atbash.Core.Tests/TestData/aes");

fn units(v: &Value) -> Vec<u16> {
    v.as_array().unwrap().iter().map(|u| u.as_u64().unwrap() as u16).collect()
}

#[test]
fn decrypts_text_encrypted_by_java() {
    let data: Vec<Value> = serde_json::from_slice(&std::fs::read(format!("{AES_DIR}/text.json")).unwrap()).unwrap();
    assert_eq!(data.len(), 6);
    for e in &data {
        let password = units(&e["password"]);
        let cipher = to_units(e["cipher"].as_str().unwrap());
        assert_eq!(decrypt_text(&cipher, &password), Ok(units(&e["plain"])));
    }
}

#[test]
fn decrypts_file_bytes_encrypted_by_java() {
    // Lite 는 파일 암호화 메뉴가 없지만, 같은 형식을 쓰는지 확인하기 위해 Java 가 만든 .enc 를 모두 풀어 봄
    let password = to_units("파일-pw-1234"); // C# AesTests.FilePassword 와 같음
    let mut count = 0;
    for entry in std::fs::read_dir(AES_DIR).unwrap() {
        let path = entry.unwrap().path();
        if path.extension().is_some_and(|e| e == "enc") {
            let original = path.with_extension("");
            assert_eq!(decrypt(&std::fs::read(&path).unwrap(), &password), Ok(std::fs::read(original).unwrap()), "{path:?}");
            count += 1;
        }
    }
    assert_eq!(count, 10);
}

#[test]
fn text_roundtrip_with_random_salt() {
    let password = to_units("pw1234");
    let plain = to_units("안녕하세요 Hello 😀\n둘째 줄");
    let a = encrypt_text(&plain, &password);
    let b = encrypt_text(&plain, &password);
    assert_ne!(a, b); // salt·iv 가 매번 다름
    assert_eq!(decrypt_text(&a, &password), Ok(plain.clone()));
    // 메신저에서 줄바꿈이 섞여 와도 풀림
    let mut wrapped = a.clone();
    wrapped.insert(10, b'\n' as u16);
    assert_eq!(decrypt_text(&wrapped, &password), Ok(plain));
}

#[test]
fn wrong_password_and_garbage_are_reported() {
    let password = to_units("right");
    let cipher = encrypt_text(&to_units("secret"), &password);
    assert_eq!(decrypt_text(&cipher, &to_units("wrong")), Err(AesError::WrongPassword));
    assert_eq!(decrypt_text(&to_units("not base64 !!"), &password), Err(AesError::NotCiphertext));
    assert_eq!(decrypt_text(&to_units("SGVsbG8="), &password), Err(AesError::NotCiphertext)); // 너무 짧음

    // 화면에 보이는 안내 종류
    assert_eq!(transform(Mode::AesTextEncrypt, &to_units("x"), 0, &[]), Err(TransformError::AesNoPassword));
    assert_eq!(transform(Mode::AesTextDecrypt, &cipher, 0, &to_units("wrong")), Err(TransformError::AesDecryptFailed));
    assert_eq!(transform(Mode::AesTextDecrypt, &[], 0, &password), Ok(vec![]));
}
