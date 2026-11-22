//! Java 8.1.0 이 낸 결과(골든 벡터 6,930개)와 Rust 결과가 한 글자도 다르지 않은지 확인.
//! 데이터는 C# 버전과 같은 파일: csharp/tests/Atbash.Core.Tests/TestData/golden.json

use atbash_lite::core::engine::{transform, Mode, TransformError};
use serde_json::Value;

fn units(v: &Value) -> Vec<u16> {
    v.as_array().unwrap().iter().map(|u| u.as_u64().unwrap() as u16).collect()
}

fn show(s: &[u16]) -> String {
    s.iter()
        .map(|&c| match char::from_u32(c as u32) {
            Some(ch) if !(c < 0x20 || (0x7F..0xA0).contains(&c) || (0xD800..=0xDFFF).contains(&c)) => ch.to_string(),
            _ => format!("\\u{c:04X}"),
        })
        .collect()
}

#[test]
fn matches_java_for_every_mode() {
    let path = concat!(env!("CARGO_MANIFEST_DIR"), "/../csharp/tests/Atbash.Core.Tests/TestData/golden.json");
    let data: Vec<Value> = serde_json::from_slice(&std::fs::read(path).expect("golden.json")).unwrap();
    assert!(data.len() > 6000);

    let mut failures = Vec::new();
    let mut per_mode = std::collections::BTreeMap::new();
    for e in &data {
        let mode = Mode::from_index(e["mode"].as_i64().unwrap() as i32).unwrap();
        let key = e["key"].as_i64().unwrap() as i32;
        let keyword = units(&e["keyword"]);
        let input = units(&e["input"]);
        let result = transform(mode, &input, key, &keyword);
        let ok = match e.get("output") {
            Some(o) => result.as_ref().ok() == Some(&units(o)),
            None => result == Err(TransformError::DecodeFailed),
        };
        *per_mode.entry(mode as i32).or_insert(0) += 1;
        if !ok && failures.len() < 20 {
            failures.push(format!(
                "{mode:?} key={key} keyword=[{}] input=[{}]\n   java: {}\n   rust: {}",
                show(&keyword),
                show(&input),
                e.get("output").map_or("DECODE error".to_string(), |o| format!("[{}]", show(&units(o)))),
                match &result {
                    Ok(t) => format!("[{}]", show(t)),
                    Err(err) => format!("{err:?}"),
                }
            ));
        }
    }
    assert_eq!(
        per_mode.keys().copied().collect::<Vec<_>>(),
        vec![0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 17, 18, 19]
    );
    assert!(failures.is_empty(), "{} differ from Java:\n{}", failures.len(), failures.join("\n"));
}
