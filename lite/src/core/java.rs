//! Java 버전과 결과를 똑같이 맞추기 위한 도우미 (C# 의 JavaCompat.cs 를 옮김).
//! Java 의 "공백", "대문자 바꾸기", UTF-8 오류 처리, 정수·Base64 읽기 규칙을 그대로 따른다.

use std::collections::HashMap;
use std::sync::OnceLock;

/// Java 에서 뽑아 둔 글자 표 (C# 버전과 같은 파일): 글자 판별 비트 8192바이트 × 2, 그다음 대문자 표
static JAVA_CHARS: &[u8] = include_bytes!("../../../csharp/src/Atbash.Core/Resources/java-chars.bin");

fn upper_map() -> &'static HashMap<u32, Vec<u16>> {
    static MAP: OnceLock<HashMap<u32, Vec<u16>>> = OnceLock::new();
    MAP.get_or_init(|| {
        let mut map = HashMap::new();
        let mut p = 8192 * 2; // LetterBits, UpperBits 건너뜀
        let read_i32 = |p: &mut usize| {
            let v = i32::from_be_bytes(JAVA_CHARS[*p..*p + 4].try_into().unwrap());
            *p += 4;
            v
        };
        let read_u16 = |p: &mut usize| {
            let v = u16::from_be_bytes(JAVA_CHARS[*p..*p + 2].try_into().unwrap());
            *p += 2;
            v
        };
        let count = read_i32(&mut p);
        for _ in 0..count {
            let cp = read_i32(&mut p) as u32;
            let len = read_u16(&mut p);
            let chars = (0..len).map(|_| read_u16(&mut p)).collect();
            map.insert(cp, chars);
        }
        map
    })
}

pub fn is_high_surrogate(c: u16) -> bool {
    (0xD800..=0xDBFF).contains(&c)
}

pub fn is_low_surrogate(c: u16) -> bool {
    (0xDC00..=0xDFFF).contains(&c)
}

/// Java `String.toUpperCase()` (ß → SS 처럼 길이가 바뀌는 경우 포함)
pub fn to_upper(s: &[u16]) -> Vec<u16> {
    let map = upper_map();
    let mut out = Vec::with_capacity(s.len());
    let mut i = 0;
    while i < s.len() {
        let mut cp = s[i] as u32;
        let mut width = 1;
        if is_high_surrogate(s[i]) && i + 1 < s.len() && is_low_surrogate(s[i + 1]) {
            cp = 0x10000 + ((s[i] as u32 - 0xD800) << 10) + (s[i + 1] as u32 - 0xDC00);
            width = 2;
        }
        match map.get(&cp) {
            Some(upper) => out.extend_from_slice(upper),
            None => out.extend_from_slice(&s[i..i + width]),
        }
        i += width;
    }
    out
}

/// Java 정규식의 `\s` (ASCII 공백 6가지만)
pub fn is_regex_space(c: u16) -> bool {
    matches!(c, 0x20 | 0x09 | 0x0A | 0x0B | 0x0C | 0x0D)
}

/// Java `String.trim()`: 앞뒤의 U+0020 이하 글자를 지움
pub fn trim(s: &[u16]) -> &[u16] {
    let mut start = 0;
    let mut end = s.len();
    while start < end && s[start] <= 0x20 {
        start += 1;
    }
    while end > start && s[end - 1] <= 0x20 {
        end -= 1;
    }
    &s[start..end]
}

/// Java `String.split(regex)` 의 뒷처리: 하나도 안 맞으면 [원래 문자열], 맞으면 끝쪽 빈 조각을 버림
fn finish_split(input: &[u16], matched: bool, mut parts: Vec<Vec<u16>>) -> Vec<Vec<u16>> {
    if !matched {
        return vec![input.to_vec()];
    }
    while parts.last().is_some_and(|p| p.is_empty()) {
        parts.pop();
    }
    parts
}

/// Java `split("\\s+")` (ASCII 공백이 이어진 곳마다 자름)
pub fn split_spaces(input: &[u16]) -> Vec<Vec<u16>> {
    let mut parts = Vec::new();
    let mut current = Vec::new();
    let mut matched = false;
    let mut i = 0;
    while i < input.len() {
        if is_regex_space(input[i]) {
            matched = true;
            parts.push(std::mem::take(&mut current));
            while i < input.len() && is_regex_space(input[i]) {
                i += 1;
            }
        } else {
            current.push(input[i]);
            i += 1;
        }
    }
    parts.push(current);
    finish_split(input, matched, parts)
}

/// Java `split(" / ")`, `split(" ")` 처럼 고정된 문자열로 자름
pub fn split_literal(input: &[u16], separator: &[u16]) -> Vec<Vec<u16>> {
    let mut parts = Vec::new();
    let mut current = Vec::new();
    let mut matched = false;
    let mut i = 0;
    while i < input.len() {
        if input[i..].starts_with(separator) {
            matched = true;
            parts.push(std::mem::take(&mut current));
            i += separator.len();
        } else {
            current.push(input[i]);
            i += 1;
        }
    }
    parts.push(current);
    finish_split(input, matched, parts)
}

/// Java `String.getBytes(UTF_8)`: 짝이 안 맞는 서로게이트는 '?' 로 바꿈
pub fn utf8_bytes(s: &[u16]) -> Vec<u8> {
    let mut out = Vec::with_capacity(s.len());
    let mut buf = [0u8; 4];
    for r in char::decode_utf16(s.iter().copied()) {
        match r {
            Ok(c) => out.extend_from_slice(c.encode_utf8(&mut buf).as_bytes()),
            Err(_) => out.push(b'?'),
        }
    }
    out
}

/// Java `new String(bytes, UTF_8)`: 잘못된 바이트는 U+FFFD 로 바꿈
pub fn utf8_string(bytes: &[u8]) -> Vec<u16> {
    String::from_utf8_lossy(bytes).encode_utf16().collect()
}

/// 엄격한 UTF-8 해석: 잘못된 바이트가 있으면 실패 (Java `CodingErrorAction.REPORT`)
pub fn utf8_string_strict(bytes: &[u8]) -> Option<Vec<u16>> {
    std::str::from_utf8(bytes).ok().map(|s| s.encode_utf16().collect())
}

fn digit(c: u16) -> i64 {
    match c {
        0x30..=0x39 => (c - 0x30) as i64,
        0x61..=0x7A => (c - 0x61 + 10) as i64,
        0x41..=0x5A => (c - 0x41 + 10) as i64,
        _ => -1,
    }
}

/// Java `Integer.parseInt(s, radix)`: 앞에 +/- 허용, int 범위를 넘으면 실패
pub fn parse_int(s: &[u16], radix: i64) -> Option<i32> {
    if s.is_empty() {
        return None;
    }
    let mut i = 0;
    let mut negative = false;
    if s[0] == b'+' as u16 || s[0] == b'-' as u16 {
        negative = s[0] == b'-' as u16;
        i = 1;
        if s.len() == 1 {
            return None;
        }
    }
    let limit: i64 = if negative { 2_147_483_648 } else { i32::MAX as i64 };
    let mut result: i64 = 0;
    for &c in &s[i..] {
        let d = digit(c);
        if d < 0 || d >= radix {
            return None;
        }
        result = result * radix + d;
        if result > limit {
            return None;
        }
    }
    Some((if negative { -result } else { result }) as i32)
}

const BASE64_ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

/// 표준 Base64 (패딩 '=' 포함)
pub fn base64_encode(bytes: &[u8]) -> String {
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = ((b[0] as u32) << 16) | ((b[1] as u32) << 8) | b[2] as u32;
        out.push(BASE64_ALPHABET[(n >> 18) as usize & 63] as char);
        out.push(BASE64_ALPHABET[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 { BASE64_ALPHABET[(n >> 6) as usize & 63] as char } else { '=' });
        out.push(if chunk.len() > 2 { BASE64_ALPHABET[n as usize & 63] as char } else { '=' });
    }
    out
}

/// Java `Base64.getDecoder().decode(String)` 를 그대로 옮김. 잘못된 입력이면 `None`
pub fn base64_decode(s: &[u16]) -> Option<Vec<u8>> {
    let mut table = [-1i32; 256];
    for (i, &c) in BASE64_ALPHABET.iter().enumerate() {
        table[c as usize] = i as i32;
    }
    table[b'=' as usize] = -2;

    // Java 는 문자열을 ISO-8859-1 로 바꿔서 읽음: 0xFF 보다 큰 글자는 '?' 가 되어 잘못된 글자로 처리됨
    let src: Vec<u8> = s.iter().map(|&c| if c <= 0xFF { c as u8 } else { b'?' }).collect();
    let sl = src.len();
    if sl == 0 {
        return Some(Vec::new());
    }
    if sl < 2 {
        return None;
    }
    let mut dst = Vec::new();
    let (mut sp, mut bits, mut shiftto) = (0usize, 0i32, 18i32);
    while sp < sl {
        let b = table[src[sp] as usize];
        sp += 1;
        if b < 0 {
            if b == -2 {
                if (shiftto == 6 && (sp == sl || {
                    let next = src[sp];
                    sp += 1;
                    next != b'='
                })) || shiftto == 18
                {
                    return None;
                }
                break;
            }
            return None;
        }
        bits |= b << shiftto;
        shiftto -= 6;
        if shiftto < 0 {
            dst.push((bits >> 16) as u8);
            dst.push((bits >> 8) as u8);
            dst.push(bits as u8);
            shiftto = 18;
            bits = 0;
        }
    }
    match shiftto {
        6 => dst.push((bits >> 16) as u8),
        0 => {
            dst.push((bits >> 16) as u8);
            dst.push((bits >> 8) as u8);
        }
        12 => return None,
        _ => {}
    }
    if sp < sl {
        return None;
    }
    Some(dst)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::to_units;

    #[test]
    fn upper_follows_java() {
        assert_eq!(to_upper(&to_units("straße ǆ")), to_units("STRASSE Ǆ"));
    }

    #[test]
    fn split_like_java() {
        let s = |x: &str| to_units(x);
        assert_eq!(split_spaces(&s("a  b\tc  ")), vec![s("a"), s("b"), s("c")]);
        assert_eq!(split_spaces(&s(" a")), vec![s(""), s("a")]); // 앞쪽 빈 조각은 남음
        assert_eq!(split_spaces(&s("abc")), vec![s("abc")]);
        assert_eq!(split_spaces(&s("   ")), Vec::<Vec<u16>>::new());
        assert_eq!(split_literal(&s(".- / -..."), &s(" / ")), vec![s(".-"), s("-...")]);
    }

    #[test]
    fn parse_int_like_java() {
        let s = |x: &str| to_units(x);
        assert_eq!(parse_int(&s("-2147483648"), 10), Some(i32::MIN));
        assert_eq!(parse_int(&s("2147483648"), 10), None);
        assert_eq!(parse_int(&s("+1F"), 16), Some(31));
        assert_eq!(parse_int(&s("-"), 10), None);
        assert_eq!(parse_int(&s("102"), 2), None);
    }

    #[test]
    fn base64_roundtrip_and_java_errors() {
        assert_eq!(base64_encode(b"Hi!"), "SGkh");
        assert_eq!(base64_encode(b"Hi"), "SGk=");
        assert_eq!(base64_decode(&to_units("SGk=")), Some(b"Hi".to_vec()));
        assert_eq!(base64_decode(&to_units("SGk")), Some(b"Hi".to_vec())); // Java 는 패딩 없이도 받음
        assert_eq!(base64_decode(&to_units("S")), None);
        assert_eq!(base64_decode(&to_units("SG!=")), None);
    }

    #[test]
    fn utf8_with_question_marks_for_lone_surrogates() {
        assert_eq!(utf8_bytes(&[0x41, 0xD800, 0x42]), b"A?B".to_vec());
    }
}
